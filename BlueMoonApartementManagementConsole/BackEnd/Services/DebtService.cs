using BlueMoonApartementManagementConsole.BackEnd.Models;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;

namespace BlueMoonApartementManagementConsole.BackEnd.Services;

public interface IDebtService
{
    /// <summary>Danh sách công nợ; periodId khác null thì chỉ một kỳ (null nếu kỳ không tồn tại).</summary>
    Task<DebtListResponse?> GetDebtsAsync(int? periodId, CancellationToken ct = default);

    Task<IReadOnlyList<ReminderResponse>> GetRemindersAsync(int periodId, int householdId, CancellationToken ct = default);

    Task<FeeItemOperationResult<IReadOnlyList<ReminderResponse>>> RecordRemindersAsync(
        int actorId, string actorName, RecordRemindersRequest request, CancellationToken ct = default);
}

/// <summary>
/// Danh sách hộ chưa đóng và nhắc nộp (PBI-019, US-019, FEA-017, LOG-034, OBS-003).
///
/// Cách lập danh sách công nợ (GetDebtsAsync):
///   1. Với mỗi (kỳ thu, hộ): phải thu = tổng dòng phải thu của bảng kê (PBI-014), đã thu = tổng các lần nộp (PBI-016),
///      còn thiếu = phải thu − đã thu; trạng thái theo PaymentService.StatusOf (cùng quy tắc với trang Ghi nhận thu tiền).
///   2. Chỉ giữ các dòng còn thiếu &gt; 0 (OBS-003: hộ được coi là còn nợ khi số còn thiếu lớn hơn 0).
///   3. Quá hạn khi hôm nay đã sau hạn nộp của kỳ; số ngày quá hạn = hôm nay − hạn nộp.
///   4. Kèm lần nhắc gần nhất và số lần đã nhắc, để Kế toán biết hộ nào chưa được đôn đốc.
///
/// Nhắc nộp (RecordRemindersAsync): Kế toán nhắc thủ công (giấy báo, điện thoại, gặp trực tiếp, tin nhắn) rồi ghi nhận.
/// Mỗi hộ được nhắc thành một lần nhắc, lưu số còn thiếu tại lúc nhắc. Chỉ nhắc được hộ còn nợ; một hộ sai thì từ chối cả lần
/// ghi, không lưu gì. Nhắc tự động qua thông báo của hệ thống phụ thuộc kênh thông báo (FEA-028, VS-023) nên chưa làm.
/// </summary>
public sealed class DebtService(
    IDebtRepository repo,
    IAuditService audit,
    TimeProvider clock) : IDebtService
{
    private const int MaxRemindersPerRequest = 500;

    public async Task<DebtListResponse?> GetDebtsAsync(int? periodId, CancellationToken ct = default)
    {
        if (periodId is int id && !await repo.PeriodExistsAsync(id, ct))
            return null;

        var rows = await LoadDebtRowsAsync(periodId, ct);
        var overdue = rows.Where(r => r.IsOverdue).ToList();
        return new DebtListResponse(
            rows.Select(r => r.HouseholdId).Distinct().Count(),
            rows.Sum(r => r.Remaining),
            overdue.Select(r => r.HouseholdId).Distinct().Count(),
            overdue.Sum(r => r.Remaining),
            rows);
    }

    public async Task<IReadOnlyList<ReminderResponse>> GetRemindersAsync(int periodId, int householdId, CancellationToken ct = default) =>
        (await repo.GetRemindersAsync(periodId, householdId, ct))
            .Select(r => new ReminderResponse(r.Id, r.BillingPeriodId, r.BillingPeriod.Year, r.BillingPeriod.Month,
                r.HouseholdId, r.Household.HouseholdNumber, r.Remaining, r.Channel, r.Note,
                r.RemindedBy.Username, r.RemindedAt))
            .ToList();

    public async Task<FeeItemOperationResult<IReadOnlyList<ReminderResponse>>> RecordRemindersAsync(
        int actorId, string actorName, RecordRemindersRequest request, CancellationToken ct = default)
    {
        // Bước 1: dữ liệu nhập
        if (request.Channel is not ReminderChannel channel)
            return Invalid("Hãy chọn cách nhắc nộp.");
        if (request.Items.Count == 0)
            return Invalid("Hãy chọn ít nhất một hộ cần nhắc.");
        if (request.Items.Count > MaxRemindersPerRequest)
            return Invalid($"Mỗi lần ghi nhận tối đa {MaxRemindersPerRequest} hộ.");
        if (request.Items.Distinct().Count() != request.Items.Count)
            return Invalid("Danh sách hộ cần nhắc bị trùng.");

        // Bước 2: mỗi hộ phải đang còn nợ trong kỳ đó (đọc theo từng kỳ có trong yêu cầu)
        var debts = new Dictionary<(int, int), DebtRowResponse>();
        foreach (var periodId in request.Items.Select(i => i.BillingPeriodId).Distinct())
        {
            if (!await repo.PeriodExistsAsync(periodId, ct))
                return new(FeeItemStatus.NotFound, Message: $"Không tìm thấy kỳ thu #{periodId}.");
            foreach (var row in await LoadDebtRowsAsync(periodId, ct, includePaid: true))
                debts[(row.BillingPeriodId, row.HouseholdId)] = row;
        }

        foreach (var item in request.Items)
        {
            if (!debts.TryGetValue((item.BillingPeriodId, item.HouseholdId), out var debt))
            {
                // Ghi tên kỳ (tháng/năm) cho Kế toán dễ hiểu; kỳ không có dòng phải thu nào thì đành ghi mã kỳ
                var period = debts.Values.FirstOrDefault(d => d.BillingPeriodId == item.BillingPeriodId);
                var periodName = period is null ? $"#{item.BillingPeriodId}" : $"tháng {period.Month:00}/{period.Year}";
                return Invalid($"Hộ #{item.HouseholdId} không có khoản phải thu trong kỳ thu {periodName}.");
            }
            if (debt.Remaining <= 0)
                return Invalid($"Hộ {debt.HouseholdNumber} đã nộp đủ kỳ thu tháng {debt.Month:00}/{debt.Year}, không cần nhắc.");
        }

        // Bước 3: lưu các lần nhắc kèm số còn thiếu lúc nhắc, rồi ghi nhật ký từng hộ
        var now = clock.GetUtcNow();
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        var reminders = request.Items.Select(item => new DebtReminderEntity
        {
            BillingPeriodId = item.BillingPeriodId,
            HouseholdId = item.HouseholdId,
            Remaining = debts[(item.BillingPeriodId, item.HouseholdId)].Remaining,
            Channel = channel,
            Note = note,
            RemindedById = actorId,
            RemindedAt = now
        }).ToList();
        await repo.AddRemindersAsync(reminders, ct);
        await repo.SaveChangesAsync(ct);

        var responses = new List<ReminderResponse>();
        foreach (var reminder in reminders)
        {
            var debt = debts[(reminder.BillingPeriodId, reminder.HouseholdId)];
            await audit.LogAsync(AuditAction.RecordDebtReminder, actorName, actorId, reminder.Id.ToString(),
                $"Nhắc nộp kỳ thu tháng {debt.Month:00}/{debt.Year}: hộ {debt.HouseholdNumber}, " +
                $"còn thiếu {PaymentService.Money(reminder.Remaining)} đ ({DebtLabels.Label(channel)})", ct);
            responses.Add(new ReminderResponse(reminder.Id, reminder.BillingPeriodId, debt.Year, debt.Month,
                reminder.HouseholdId, debt.HouseholdNumber, reminder.Remaining, reminder.Channel, reminder.Note,
                actorName, reminder.RemindedAt));
        }

        return new(FeeItemStatus.Success, responses);
    }

    // ---- Lập danh sách ----

    /// <summary>
    /// Các bước 1–4 ở phần mô tả lớp. includePaid = true thì giữ cả dòng đã nộp đủ (để báo lỗi rõ khi nhắc nhầm hộ đã đủ).
    /// </summary>
    private async Task<List<DebtRowResponse>> LoadDebtRowsAsync(int? periodId, CancellationToken ct, bool includePaid = false)
    {
        var paid = (await repo.GetPaidAsync(periodId, ct)).ToDictionary(p => (p.BillingPeriodId, p.HouseholdId), p => p.AmountPaid);
        var reminders = (await repo.GetReminderSummariesAsync(periodId, ct)).ToDictionary(r => (r.BillingPeriodId, r.HouseholdId));
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        return (await repo.GetDuesAsync(periodId, ct))
            .Select(due =>
            {
                var amountPaid = paid.GetValueOrDefault((due.BillingPeriodId, due.HouseholdId));
                var remaining = due.AmountDue - amountPaid;
                var isOverdue = remaining > 0 && today > due.DueDate;
                var reminder = reminders.GetValueOrDefault((due.BillingPeriodId, due.HouseholdId));
                return new DebtRowResponse(due.BillingPeriodId, due.Year, due.Month, due.DueDate,
                    due.HouseholdId, due.HouseholdNumber, due.ApartmentCode, due.Building,
                    due.AmountDue, amountPaid, remaining, PaymentService.StatusOf(due.AmountDue, amountPaid),
                    isOverdue, isOverdue ? today.DayNumber - due.DueDate.DayNumber : 0,
                    reminder?.LastRemindedAt, reminder?.Count ?? 0);
            })
            .Where(row => includePaid || row.Remaining > 0)
            .OrderBy(row => row.DueDate) // nợ lâu nhất lên trước
            .ThenBy(row => row.Building)
            .ThenBy(row => row.ApartmentCode)
            .ToList();
    }

    private static FeeItemOperationResult<IReadOnlyList<ReminderResponse>> Invalid(string message) =>
        new(FeeItemStatus.Invalid, Message: message);
}
