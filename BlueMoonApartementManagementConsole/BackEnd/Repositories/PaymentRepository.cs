using System.Data;
using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlueMoonApartementManagementConsole.BackEnd.Repositories;

/// <summary>Số phải thu của một hộ trong kỳ: tổng các dòng phải thu của bảng kê (PBI-014).</summary>
public sealed record HouseholdDueRow(int HouseholdId, string HouseholdNumber, string ApartmentCode, string Building, decimal AmountDue);

/// <summary>Tổng đã nộp và số lần nộp của một hộ trong kỳ.</summary>
public sealed record HouseholdPaidRow(int HouseholdId, decimal AmountPaid, int PaymentCount);

public sealed record BillLineRow(string Code, string Name, decimal Amount);

public enum PaymentSaveResult
{
    Saved,

    /// <summary>Tổng đã nộp cộng lần này vượt số phải thu (thường do người khác vừa ghi), không lưu gì.</summary>
    ExceedsDue,

    /// <summary>Hai transaction cùng ghi cho một hộ, PostgreSQL hủy một bên (lỗi 40001), không lưu gì.</summary>
    Conflict
}

public interface IPaymentRepository
{
    Task<BillingPeriodEntity?> GetPeriodAsync(int periodId, CancellationToken ct = default);

    /// <summary>Số phải thu theo hộ trong kỳ; householdId khác null thì chỉ lấy một hộ.</summary>
    Task<List<HouseholdDueRow>> GetDuesAsync(int periodId, int? householdId, CancellationToken ct = default);

    Task<List<HouseholdPaidRow>> GetPaidAsync(int periodId, int? householdId, CancellationToken ct = default);
    Task<List<BillLineRow>> GetBillLinesAsync(int periodId, int householdId, CancellationToken ct = default);

    /// <summary>Các lần nộp của hộ trong kỳ, cũ trước, kèm người ghi nhận.</summary>
    Task<List<PaymentEntity>> GetPaymentsAsync(int periodId, int householdId, CancellationToken ct = default);

    /// <summary>Lưu lần nộp nếu tổng đã nộp sau khi lưu không vượt <paramref name="amountDue"/>.</summary>
    Task<PaymentSaveResult> AddAsync(PaymentEntity payment, decimal amountDue, CancellationToken ct = default);
}

public sealed class PaymentRepository(AppDbContext db) : IPaymentRepository
{
    public Task<BillingPeriodEntity?> GetPeriodAsync(int periodId, CancellationToken ct = default) =>
        db.BillingPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId, ct);

    public Task<List<HouseholdDueRow>> GetDuesAsync(int periodId, int? householdId, CancellationToken ct = default) =>
        db.Receivables.AsNoTracking()
            .Where(r => r.BillingPeriodItem.BillingPeriodId == periodId && (householdId == null || r.HouseholdId == householdId))
            .GroupBy(r => new
            {
                r.HouseholdId,
                r.Household.HouseholdNumber,
                r.Household.Apartment.Code,
                r.Household.Apartment.Building
            })
            .Select(g => new HouseholdDueRow(g.Key.HouseholdId, g.Key.HouseholdNumber, g.Key.Code, g.Key.Building,
                g.Sum(r => r.Amount)))
            .ToListAsync(ct);

    public Task<List<HouseholdPaidRow>> GetPaidAsync(int periodId, int? householdId, CancellationToken ct = default) =>
        db.Payments.AsNoTracking()
            .Where(p => p.BillingPeriodId == periodId && (householdId == null || p.HouseholdId == householdId))
            .GroupBy(p => p.HouseholdId)
            .Select(g => new HouseholdPaidRow(g.Key, g.Sum(p => p.Amount), g.Count()))
            .ToListAsync(ct);

    public Task<List<BillLineRow>> GetBillLinesAsync(int periodId, int householdId, CancellationToken ct = default) =>
        db.Receivables.AsNoTracking()
            .Where(r => r.BillingPeriodItem.BillingPeriodId == periodId && r.HouseholdId == householdId)
            .OrderBy(r => r.BillingPeriodItem.FeeItem.Code)
            .Select(r => new BillLineRow(r.BillingPeriodItem.FeeItem.Code, r.BillingPeriodItem.FeeItem.Name, r.Amount))
            .ToListAsync(ct);

    public Task<List<PaymentEntity>> GetPaymentsAsync(int periodId, int householdId, CancellationToken ct = default) =>
        db.Payments.AsNoTracking()
            .Include(p => p.RecordedBy)
            .Where(p => p.BillingPeriodId == periodId && p.HouseholdId == householdId)
            .OrderBy(p => p.PaidAt)
            .ThenBy(p => p.Id)
            .ToListAsync(ct);

    public async Task<PaymentSaveResult> AddAsync(PaymentEntity payment, decimal amountDue, CancellationToken ct = default)
    {
        // Serializable: đọc tổng đã nộp rồi mới thêm trong cùng transaction. Hai Kế toán ghi cùng lúc cho một hộ
        // thì PostgreSQL hủy một bên, nên tổng đã nộp không bao giờ vượt số phải thu.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var paid = await db.Payments
                .Where(p => p.BillingPeriodId == payment.BillingPeriodId && p.HouseholdId == payment.HouseholdId)
                .SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
            if (paid + payment.Amount > amountDue)
                return PaymentSaveResult.ExceedsDue; // transaction chưa commit, dispose sẽ rollback

            db.Payments.Add(payment);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return PaymentSaveResult.Saved;
        }
        catch (Exception exception) when (IsSerializationFailure(exception))
        {
            // Bỏ theo dõi để lần SaveChanges sau (ví dụ ghi nhật ký) không chèn lại lần nộp bị hủy
            db.Entry(payment).State = EntityState.Detached;
            return PaymentSaveResult.Conflict;
        }
    }

    private static bool IsSerializationFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
                return true;
        }
        return false;
    }
}
