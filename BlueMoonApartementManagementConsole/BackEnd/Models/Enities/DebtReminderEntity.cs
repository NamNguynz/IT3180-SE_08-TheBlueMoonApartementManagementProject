namespace BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

/// <summary>
/// Kênh Kế toán đã dùng để nhắc nộp. Nhắc tự động qua thông báo của hệ thống phụ thuộc kênh thông báo
/// (FEA-028, v3.0) nên chưa có; ở đây chỉ ghi nhận các cách nhắc thủ công.
/// Lưu trong DB dạng chuỗi; thêm giá trị mới thì sửa cả check constraint ck_debt_reminders_channel và DebtLabels.
/// </summary>
public enum ReminderChannel
{
    GiayBao,
    DienThoai,
    TrucTiep,
    TinNhan
}

/// <summary>
/// Một lần Kế toán nhắc một hộ nộp khoản còn thiếu của một kỳ thu (PBI-019, US-019, LOG-034).
/// Giữ lại số còn thiếu tại lúc nhắc để về sau biết đã nhắc hộ nợ bao nhiêu. Chỉ thêm, không sửa, không xóa.
/// </summary>
public class DebtReminderEntity
{
    public int Id { get; set; }
    public int BillingPeriodId { get; set; }
    public BillingPeriodEntity BillingPeriod { get; set; } = null!;
    public int HouseholdId { get; set; }
    public HouseholdEntity Household { get; set; } = null!;

    /// <summary>Số còn thiếu của hộ trong kỳ tại lúc nhắc, luôn lớn hơn 0.</summary>
    public decimal Remaining { get; set; }

    public ReminderChannel Channel { get; set; }
    public string? Note { get; set; }
    public int RemindedById { get; set; }
    public AppUserEntity RemindedBy { get; set; } = null!;
    public DateTimeOffset RemindedAt { get; set; }
}
