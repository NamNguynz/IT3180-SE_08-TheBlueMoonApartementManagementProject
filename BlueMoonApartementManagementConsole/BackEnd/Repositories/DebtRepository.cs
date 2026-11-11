using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.EntityFrameworkCore;

namespace BlueMoonApartementManagementConsole.BackEnd.Repositories;

/// <summary>Số phải thu của một hộ trong một kỳ (tổng các dòng phải thu của bảng kê), kèm thông tin kỳ và hộ.</summary>
public sealed record DebtDueRow(
    int BillingPeriodId, int Year, int Month, DateOnly DueDate,
    int HouseholdId, string HouseholdNumber, string ApartmentCode, string Building, decimal AmountDue);

public sealed record DebtPaidRow(int BillingPeriodId, int HouseholdId, decimal AmountPaid);

public sealed record ReminderSummaryRow(int BillingPeriodId, int HouseholdId, DateTimeOffset LastRemindedAt, int Count);

public interface IDebtRepository
{
    Task<bool> PeriodExistsAsync(int periodId, CancellationToken ct = default);

    /// <summary>Số phải thu theo (kỳ, hộ); periodId null thì lấy mọi kỳ.</summary>
    Task<List<DebtDueRow>> GetDuesAsync(int? periodId, CancellationToken ct = default);

    Task<List<DebtPaidRow>> GetPaidAsync(int? periodId, CancellationToken ct = default);
    Task<List<ReminderSummaryRow>> GetReminderSummariesAsync(int? periodId, CancellationToken ct = default);

    /// <summary>Các lần nhắc một hộ trong một kỳ, mới nhất trước, kèm người nhắc.</summary>
    Task<List<DebtReminderEntity>> GetRemindersAsync(int periodId, int householdId, CancellationToken ct = default);

    Task AddRemindersAsync(IEnumerable<DebtReminderEntity> reminders, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed class DebtRepository(AppDbContext db) : IDebtRepository
{
    public Task<bool> PeriodExistsAsync(int periodId, CancellationToken ct = default) =>
        db.BillingPeriods.AnyAsync(p => p.Id == periodId, ct);

    public Task<List<DebtDueRow>> GetDuesAsync(int? periodId, CancellationToken ct = default) =>
        db.Receivables.AsNoTracking()
            .Where(r => periodId == null || r.BillingPeriodItem.BillingPeriodId == periodId)
            .GroupBy(r => new
            {
                r.BillingPeriodItem.BillingPeriodId,
                r.BillingPeriodItem.BillingPeriod.Year,
                r.BillingPeriodItem.BillingPeriod.Month,
                r.BillingPeriodItem.BillingPeriod.DueDate,
                r.HouseholdId,
                r.Household.HouseholdNumber,
                r.Household.Apartment.Code,
                r.Household.Apartment.Building
            })
            .Select(g => new DebtDueRow(g.Key.BillingPeriodId, g.Key.Year, g.Key.Month, g.Key.DueDate,
                g.Key.HouseholdId, g.Key.HouseholdNumber, g.Key.Code, g.Key.Building, g.Sum(r => r.Amount)))
            .ToListAsync(ct);

    public Task<List<DebtPaidRow>> GetPaidAsync(int? periodId, CancellationToken ct = default) =>
        db.Payments.AsNoTracking()
            .Where(p => periodId == null || p.BillingPeriodId == periodId)
            .GroupBy(p => new { p.BillingPeriodId, p.HouseholdId })
            .Select(g => new DebtPaidRow(g.Key.BillingPeriodId, g.Key.HouseholdId, g.Sum(p => p.Amount)))
            .ToListAsync(ct);

    public Task<List<ReminderSummaryRow>> GetReminderSummariesAsync(int? periodId, CancellationToken ct = default) =>
        db.DebtReminders.AsNoTracking()
            .Where(r => periodId == null || r.BillingPeriodId == periodId)
            .GroupBy(r => new { r.BillingPeriodId, r.HouseholdId })
            .Select(g => new ReminderSummaryRow(g.Key.BillingPeriodId, g.Key.HouseholdId, g.Max(r => r.RemindedAt), g.Count()))
            .ToListAsync(ct);

    public Task<List<DebtReminderEntity>> GetRemindersAsync(int periodId, int householdId, CancellationToken ct = default) =>
        db.DebtReminders.AsNoTracking()
            .Include(r => r.RemindedBy)
            .Include(r => r.BillingPeriod)
            .Include(r => r.Household)
            .Where(r => r.BillingPeriodId == periodId && r.HouseholdId == householdId)
            .OrderByDescending(r => r.RemindedAt)
            .ThenByDescending(r => r.Id)
            .ToListAsync(ct);

    public async Task AddRemindersAsync(IEnumerable<DebtReminderEntity> reminders, CancellationToken ct = default) =>
        await db.DebtReminders.AddRangeAsync(reminders, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
