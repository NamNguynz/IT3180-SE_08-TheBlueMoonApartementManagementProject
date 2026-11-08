namespace BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

/// <summary>
/// Loại khoản thu: Bắt buộc, Đóng góp, Bảo trì riêng
/// </summary>
public enum FeeType
{
    Mandatory = 1,      // Bắt buộc
    Voluntary = 2,      // Đóng góp/Tự nguyện
    Maintenance = 3     // Bảo trì riêng
}

/// <summary>
/// Chu kỳ tính phí
/// </summary>
public enum BillingFrequency
{
    Monthly = 1,        // Tháng
    Quarterly = 2,      // Quý
    Yearly = 3,         // Năm
    Once = 4            // Lần duy nhất
}

/// <summary>
/// Cơ sở tính phí
/// </summary>
public enum CalculationBasis
{
    FixedAmount = 1,    // Số tiền cố định
    PerSquareMeter = 2, // Theo m²
    PerPersonMonth = 3, // Theo nhân khẩu/tháng
    Invoice = 4         // Theo hóa đơn nhà cung cấp
}

/// <summary>
/// Phạm vi áp dụng phí
/// </summary>
public enum FeeScope
{
    AllUnits = 1,           // Tất cả căn hộ
    SpecificUnits = 2,      // Căn hộ chỉ định
    SpecificResidents = 3   // Cư dân chỉ định
}


/// <summary>
/// Định nghĩa khoản thu (BQT/Kế toán tự khai báo)
/// </summary>
public class FeeDefinition
{
    public int Id { get; set; }

    /// <summary>
    /// Tên khoản thu (ví dụ: "Phí dịch vụ chung cư", "Phí vệ sinh")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả chi tiết
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Loại khoản thu: Bắt buộc/Đóng góp/Bảo trì
    /// </summary>
    public FeeType Type { get; set; }

    /// <summary>
    /// Chu kỳ: Tháng/Năm/Lần
    /// </summary>
    public BillingFrequency Frequency { get; set; }

    /// <summary>
    /// Cơ sở tính phí: Cố định/m²/Nhân khẩu/Hóa đơn
    /// </summary>
    public CalculationBasis CalculationBasis { get; set; }

    /// <summary>
    /// Phạm vi áp dụng: Tất cả/Chỉ định căn hộ/Chỉ định cư dân
    /// </summary>
    public FeeScope Scope { get; set; }

    /// <summary>
    /// Số tiền cố định (khi CalculationBasis = FixedAmount)
    /// </summary>
    public decimal? Amount { get; set; }

    /// <summary>
    /// Đơn vị tính (ví dụ: "đ/m²", "đ/nhân khẩu", "đ")
    /// </summary>
    public string UnitOfMeasure { get; set; } = string.Empty;

    /// <summary>
    /// Công thức tính (ví dụ: "Diện tích m² × 2.500 đ/m²")
    /// </summary>
    public string? Formula { get; set; }

    /// <summary>
    /// Ngày bắt đầu hiệu lực
    /// </summary>
    public DateOnly EffectiveFromDate { get; set; }

    /// <summary>
    /// Ngày kết thúc hiệu lực (null = vô hạn)
    /// </summary>
    public DateOnly? EffectiveToDate { get; set; }

    /// <summary>
    /// Trạng thái: Hoạt động/Ngưng sử dụng
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Vị trí sắp xếp trên hóa đơn
    /// </summary>
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Người tạo (ID)
    /// </summary>
    public int CreatedBy { get; set; }

    /// <summary>
    /// Ngày tạo
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Người cập nhật cuối (ID)
    /// </summary>
    public int? UpdatedBy { get; set; }

    /// <summary>
    /// Ngày cập nhật cuối
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    // Navigation
    public AppUserEntity? Creator { get; set; }
    public AppUserEntity? Updater { get; set; }
}