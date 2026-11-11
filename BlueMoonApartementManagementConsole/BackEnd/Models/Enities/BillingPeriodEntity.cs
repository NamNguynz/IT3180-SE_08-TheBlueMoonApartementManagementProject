namespace BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

/// <summary>
/// Kỳ thu theo tháng (PBI-014, US-014): Kế toán chọn tháng, ngày tính phí, hạn nộp và các khoản thu bắt buộc;
/// hệ thống lập ngay bảng kê số tiền phải thu của từng hộ. Kỳ thu đã lập không sửa, không xóa,
/// vì là căn cứ để thu tiền và đối chiếu công nợ (PBI-016, PBI-022).
/// </summary>
public class BillingPeriodEntity
{
    public int Id { get; set; }
    public int Year { get; set; }

    /// <summary>Tháng của kỳ thu, 1–12.</summary>
    public int Month { get; set; }

    /// <summary>
    /// Ngày tính phí: lấy đơn giá có hiệu lực vào ngày này. Nằm trong tháng của kỳ thu và không sau hôm nay,
    /// tức chỉ lập được bảng kê khi đã đến ngày tính phí, và phí tháng nào tính theo đơn giá của tháng đó.
    /// </summary>
    public DateOnly ChargeDate { get; set; }

    /// <summary>Hạn nộp, không trước ngày tính phí.</summary>
    public DateOnly DueDate { get; set; }

    public string? Note { get; set; }
    public int CreatedById { get; set; }
    public AppUserEntity CreatedBy { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public List<BillingPeriodItemEntity> Items { get; set; } = [];
}

/// <summary>
/// Một khoản thu trong kỳ, kèm đơn giá đã áp dụng. Rate là bản chụp lúc lập kỳ: đổi đơn giá về sau
/// không làm thay đổi số đã tính, và vẫn giải thích được số tiền của kỳ cũ (AC-007, OBS-001).
/// </summary>
public class BillingPeriodItemEntity
{
    public int Id { get; set; }
    public int BillingPeriodId { get; set; }
    public BillingPeriodEntity BillingPeriod { get; set; } = null!;
    public int FeeItemId { get; set; }
    public FeeItemEntity FeeItem { get; set; } = null!;

    /// <summary>Dòng đơn giá đã dùng, để biết đơn giá này có hiệu lực từ ngày nào.</summary>
    public int FeeRateId { get; set; }
    public FeeRateEntity FeeRate { get; set; } = null!;

    public decimal Rate { get; set; }
    public List<ReceivableEntity> Receivables { get; set; } = [];
}

/// <summary>
/// Số tiền một hộ phải nộp cho một khoản thu trong kỳ: Amount = Rate × Quantity, làm tròn đến đồng.
/// Quantity là cơ sở tính chụp lúc lập kỳ (diện tích căn hộ, số nhân khẩu, hoặc 1 với khoản theo hộ, cố định).
/// </summary>
public class ReceivableEntity
{
    public int Id { get; set; }
    public int BillingPeriodItemId { get; set; }
    public BillingPeriodItemEntity BillingPeriodItem { get; set; } = null!;
    public int HouseholdId { get; set; }
    public HouseholdEntity Household { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
}
