namespace BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

public enum FeeType { BatBuoc, TuNguyen, BaoTriRieng }
public enum FeeScope { Chung, ChiDinh }
public enum FeeCycle { MotLan, Thang, Nam }
public enum CalculationBasis { DienTich, NhanKhau, Ho, CoDinh }

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
    public List<FeeRateEntity> Rates { get; set; } = [];
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
