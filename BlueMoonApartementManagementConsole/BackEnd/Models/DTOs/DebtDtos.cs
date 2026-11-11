using System.ComponentModel.DataAnnotations;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;

/// <summary>
/// Danh sách công nợ (PBI-019): mỗi dòng là một hộ trong một kỳ thu còn thiếu tiền (OBS-003: còn nợ khi số còn thiếu &gt; 0).
/// HouseholdCount, OverdueHouseholdCount đếm số hộ khác nhau (một hộ có thể nợ nhiều kỳ).
/// </summary>
public sealed record DebtListResponse(
    int HouseholdCount,
    decimal TotalRemaining,
    int OverdueHouseholdCount,
    decimal OverdueRemaining,
    IReadOnlyList<DebtRowResponse> Rows);

/// <summary>Một hộ còn nợ trong một kỳ: số còn thiếu, trạng thái, quá hạn bao nhiêu ngày, đã nhắc chưa.</summary>
public sealed record DebtRowResponse(
    int BillingPeriodId,
    int Year,
    int Month,
    DateOnly DueDate,
    int HouseholdId,
    string HouseholdNumber,
    string ApartmentCode,
    string Building,
    decimal AmountDue,
    decimal AmountPaid,
    decimal Remaining,
    PaymentStatus Status,
    bool IsOverdue,
    int DaysOverdue,
    DateTimeOffset? LastRemindedAt,
    int ReminderCount);

/// <summary>Một hộ cần nhắc trong một kỳ thu.</summary>
public sealed record DebtReminderTarget(int BillingPeriodId, int HouseholdId);

/// <summary>
/// Ghi nhận Kế toán đã nhắc nộp các hộ (PBI-019). Mỗi phần tử của Items thành một lần nhắc,
/// lưu kèm số còn thiếu tại lúc nhắc. Các quy tắc phụ thuộc dữ liệu kiểm ở DebtService.
/// </summary>
public sealed record RecordRemindersRequest
{
    [Required]
    public IReadOnlyList<DebtReminderTarget> Items { get; init; } = [];

    // Nullable để thiếu kênh nhắc thì bị từ chối, không lặng lẽ nhận giá trị enum đầu tiên
    public ReminderChannel? Channel { get; init; }

    [StringLength(500)]
    public string? Note { get; init; }
}

/// <summary>Một lần nhắc nộp đã ghi nhận.</summary>
public sealed record ReminderResponse(
    int Id,
    int BillingPeriodId,
    int Year,
    int Month,
    int HouseholdId,
    string HouseholdNumber,
    decimal Remaining,
    ReminderChannel Channel,
    string? Note,
    string RemindedBy,
    DateTimeOffset RemindedAt);
