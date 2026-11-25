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
    /// Ngày tính phí: lấy đơn giá có hiệu lực vào ngày này. Nằm trong tháng của kỳ thu, để phí tháng nào tính theo
    /// đơn giá của tháng đó. Kỳ của tháng sau được lập trước, nên ngày tính phí có thể sau hôm nay.
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
/// Một khoản thu trong kỳ, kèm đơn giá đã áp dụng. Rate/FeeRateId chỉ có giá trị với chế độ Chung;
/// chế độ TheoLoai lưu ba giá đã chụp ở TypeRates và giá thực tế của từng hộ ở Receivable.Rate.
/// Đổi đơn giá về sau không làm thay đổi số đã tính, và vẫn giải thích được số tiền của kỳ cũ (AC-007, OBS-001).
/// </summary>
public class BillingPeriodItemEntity
{
    public int Id { get; set; }
    public int BillingPeriodId { get; set; }
    public BillingPeriodEntity BillingPeriod { get; set; } = null!;
    public int FeeItemId { get; set; }
    public FeeItemEntity FeeItem { get; set; } = null!;

    /// <summary>Chế độ đơn giá đã dùng khi lập kỳ (bản chụp).</summary>
    public RateMode RateMode { get; set; } = RateMode.Chung;

    /// <summary>Dòng đơn giá chung đã dùng, để biết đơn giá này có hiệu lực từ ngày nào.</summary>
    public int? FeeRateId { get; set; }
    public FeeRateEntity? FeeRate { get; set; }

    /// <summary>Đơn giá chung đã chụp; null khi dùng chế độ TheoLoai.</summary>
    public decimal? Rate { get; set; }

    /// <summary>Ba giá theo loại căn hộ đã chụp; chỉ có dòng khi dùng chế độ TheoLoai.</summary>
    public List<BillingPeriodItemTypeRateEntity> TypeRates { get; set; } = [];
    public List<ReceivableEntity> Receivables { get; set; } = [];
}

/// <summary>
/// Giá theo từng loại căn hộ đã chụp lại trong một khoản thu của kỳ (chế độ TheoLoai).
/// Giữ lại để bảng chi tiết giải thích được ba giá dù sau này danh mục có đổi.
/// </summary>
public class BillingPeriodItemTypeRateEntity
{
    public int Id { get; set; }
    public int BillingPeriodItemId { get; set; }
    public BillingPeriodItemEntity BillingPeriodItem { get; set; } = null!;
    public ApartmentType ApartmentType { get; set; }
    public decimal Rate { get; set; }
    public int FeeTypeRateId { get; set; }
    public FeeTypeRateEntity FeeTypeRate { get; set; } = null!;
    public DateOnly EffectiveFrom { get; set; }
}

/// <summary>
/// Số tiền một hộ phải nộp cho một khoản thu trong kỳ: Amount = Rate × Quantity, làm tròn đến đồng.
/// Quantity là cơ sở tính chụp lúc lập kỳ (diện tích căn hộ, số nhân khẩu, hoặc 1 với khoản theo hộ, cố định).
/// Rate là đơn giá thực tế đã áp dụng cho hộ này (giá chung, hoặc giá của loại căn hộ).
/// </summary>
public class ReceivableEntity
{
    public int Id { get; set; }
    public int BillingPeriodItemId { get; set; }
    public BillingPeriodItemEntity BillingPeriodItem { get; set; } = null!;
    public int HouseholdId { get; set; }
    public HouseholdEntity Household { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
}
