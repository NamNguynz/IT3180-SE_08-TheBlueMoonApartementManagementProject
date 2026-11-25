namespace BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

public enum FeeType { BatBuoc, TuNguyen, BaoTriRieng }
public enum FeeScope { Chung, ChiDinh }
public enum FeeCycle { MotLan, Thang, Nam }
public enum CalculationBasis { DienTich, NhanKhau, Ho, CoDinh }

/// <summary>
/// Cách đặt đơn giá cho một khoản thu: Chung là một giá cho mọi loại căn hộ,
/// TheoLoai là ba giá riêng theo ApartmentType (chỉ dùng với cơ sở tính DienTich).
/// </summary>
public enum RateMode { Chung, TheoLoai }

public class FeeItemEntity
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public FeeType FeeType { get; set; }
    public FeeScope FeeScope { get; set; }
    public FeeCycle FeeCycle { get; set; }
    public CalculationBasis CalculationBasis { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string? Note { get; set; }
    public int CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public RateMode RateMode { get; set; } = RateMode.Chung;
    public List<FeeRateEntity> Rates { get; set; } = [];
    public List<FeeTypeRateEntity> TypeRates { get; set; } = [];
}

// Ko xóa, giữ lịch sử (AC-007)
public class FeeRateEntity
{
    public int Id { get; set; }
    public int FeeItemId { get; set; }
    public decimal Rate { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public FeeItemEntity FeeItem { get; set; } = null!;
}

/// <summary>
/// Đơn giá riêng theo loại căn hộ, dùng khi khoản thu tính theo diện tích và chủ chế độ TheoLoai.
/// Cùng quy tắc lịch sử với FeeRateEntity: không xóa dòng cũ, chỉ đóng EffectiveTo.
/// </summary>
public class FeeTypeRateEntity
{
    public int Id { get; set; }
    public int FeeItemId { get; set; }
    public ApartmentType ApartmentType { get; set; }
    public decimal Rate { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public FeeItemEntity FeeItem { get; set; } = null!;
}
