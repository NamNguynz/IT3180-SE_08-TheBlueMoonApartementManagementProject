using System.Linq.Expressions;
using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.EntityFrameworkCore;

namespace BlueMoonApartementManagementConsole.BackEnd.Repositories;

/// <summary>Số liệu tổng của một kỳ thu, tính trong CSDL để danh sách kỳ không phải tải từng dòng phải thu.</summary>
public sealed record BillingPeriodSummaryRow(
    int Id, int Year, int Month, DateOnly ChargeDate, DateOnly DueDate,
    int ItemCount, int HouseholdCount, decimal TotalAmount, string CreatedBy, DateTimeOffset CreatedAt);

/// <summary>Khoản thu đã nằm trong một kỳ nào đó, dùng để chặn thu trùng theo chu kỳ.</summary>
public sealed record BilledFeeItemRow(int FeeItemId, int BillingPeriodId, int Year, int Month);

/// <summary>
/// Cơ sở tính của một hộ tại thời điểm tính phí: diện tích căn hộ và số nhân khẩu chưa chuyển đi.
/// Số hộ khẩu, mã căn chỉ để hiển thị và báo lỗi.
/// </summary>
public sealed record HouseholdBillingBasis(
    int HouseholdId, string HouseholdNumber, string ApartmentCode, decimal Area, int ResidentCount);

public interface IBillingPeriodRepository
{
    Task<List<BillingPeriodSummaryRow>> GetSummariesAsync(CancellationToken ct = default);

    /// <summary>Kỳ thu kèm khoản thu, đơn giá đã dùng và các dòng phải thu (có hộ, căn hộ).</summary>
    Task<BillingPeriodEntity?> GetDetailAsync(int id, CancellationToken ct = default);

    Task<List<FeeItemEntity>> GetFeeItemsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);
    Task<List<BilledFeeItemRow>> GetBilledItemsAsync(IReadOnlyCollection<int> feeItemIds, CancellationToken ct = default);
    Task<List<HouseholdBillingBasis>> GetHouseholdBasesAsync(CancellationToken ct = default);
    Task<HouseholdBillingBasis?> GetHouseholdBasisAsync(int householdId, CancellationToken ct = default);
    Task AddAsync(BillingPeriodEntity period, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed class BillingPeriodRepository(AppDbContext db) : IBillingPeriodRepository
{
    public Task<List<BillingPeriodSummaryRow>> GetSummariesAsync(CancellationToken ct = default) =>
        db.BillingPeriods.AsNoTracking()
            .OrderByDescending(p => p.Year)
            .ThenByDescending(p => p.Month)
            .ThenByDescending(p => p.Id)
            .Select(p => new BillingPeriodSummaryRow(
                p.Id, p.Year, p.Month, p.ChargeDate, p.DueDate,
                p.Items.Count,
                p.Items.SelectMany(i => i.Receivables).Select(r => r.HouseholdId).Distinct().Count(),
                p.Items.SelectMany(i => i.Receivables).Sum(r => (decimal?)r.Amount) ?? 0,
                p.CreatedBy.Username,
                p.CreatedAt))
            .ToListAsync(ct);

    public Task<BillingPeriodEntity?> GetDetailAsync(int id, CancellationToken ct = default) =>
        db.BillingPeriods.AsNoTracking()
            .Include(p => p.CreatedBy)
            .Include(p => p.Items).ThenInclude(i => i.FeeItem)
            .Include(p => p.Items).ThenInclude(i => i.FeeRate)
            .Include(p => p.Items).ThenInclude(i => i.Receivables).ThenInclude(r => r.Household).ThenInclude(h => h.Apartment)
            .AsSplitQuery() // tránh nhân bản dòng khi Include nhiều nhánh
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<List<FeeItemEntity>> GetFeeItemsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default) =>
        db.FeeItems.AsNoTracking()
            .Include(f => f.Rates)
            .Where(f => ids.Contains(f.Id))
            .ToListAsync(ct);

    public Task<List<BilledFeeItemRow>> GetBilledItemsAsync(IReadOnlyCollection<int> feeItemIds, CancellationToken ct = default) =>
        db.BillingPeriodItems.AsNoTracking()
            .Where(i => feeItemIds.Contains(i.FeeItemId))
            .Select(i => new BilledFeeItemRow(i.FeeItemId, i.BillingPeriodId, i.BillingPeriod.Year, i.BillingPeriod.Month))
            .ToListAsync(ct);

    // Một định nghĩa cơ sở tính cho cả bảng kê và API tính thử (PBI-015), để hai nơi không lệch nhau
    private static readonly Expression<Func<HouseholdEntity, HouseholdBillingBasis>> ToBillingBasis = h =>
        new HouseholdBillingBasis(
            h.Id,
            h.HouseholdNumber,
            h.Apartment.Code,
            h.Apartment.Area,
            h.Residents.Count(r => r.ResidenceStatus != ResidenceStatus.DaChuyenDi));

    public Task<List<HouseholdBillingBasis>> GetHouseholdBasesAsync(CancellationToken ct = default) =>
        db.Households.AsNoTracking()
            .OrderBy(h => h.Id)
            .Select(ToBillingBasis)
            .ToListAsync(ct);

    public Task<HouseholdBillingBasis?> GetHouseholdBasisAsync(int householdId, CancellationToken ct = default) =>
        db.Households.AsNoTracking()
            .Where(h => h.Id == householdId)
            .Select(ToBillingBasis)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(BillingPeriodEntity period, CancellationToken ct = default) =>
        await db.BillingPeriods.AddAsync(period, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
