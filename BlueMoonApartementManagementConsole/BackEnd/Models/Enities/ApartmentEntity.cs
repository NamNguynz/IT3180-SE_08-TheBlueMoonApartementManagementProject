namespace BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

/// <summary>
/// Loại hình căn hộ (PBI-002, LOG-008: căn hộ cơ bản / penthouse / kiot, dùng cho tra cứu và đơn giá riêng).
/// Lưu trong DB dạng chuỗi; thêm giá trị mới thì sửa cả check constraint ck_apartments_type và ApartmentTypeLabels.
/// </summary>
public enum ApartmentType
{
    CanHo,
    Kiot,
    Penthouse
}

public class ApartmentEntity
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Building { get; set; } = string.Empty;
    public ApartmentType Type { get; set; } = ApartmentType.CanHo;
    public int Floor { get; set; }
    public decimal Area { get; set; }
    public int BedroomCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public HouseholdEntity? Household { get; set; }
}

public class HouseholdEntity
{
    public int Id { get; set; }
    public int ApartmentId { get; set; }
    public string HouseholdNumber { get; set; } = string.Empty;
    public string HeadName { get; set; } = string.Empty;
    public string HouseNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
    public ApartmentEntity Apartment { get; set; } = null!;
    public List<ResidentEntity> Residents { get; set; } = [];
}

public class ResidentEntity
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Alias { get; set; }
    public DateTimeOffset DateOfBirth { get; set; }
    public string Gender { get; set; } = string.Empty;
    public string? PlaceOfBirth { get; set; }
    public string? Hometown { get; set; }
    public string? Ethnicity { get; set; }
    public string? Religion { get; set; }
    public string? Occupation { get; set; }
    public string? Workplace { get; set; }
    public string? IdentityNumber { get; set; }

    /// <summary>
    /// HMAC của số giấy tờ, do AppDbContext tự tính khi lưu. Dùng để kiểm trùng,
    /// vì cột IdentityNumber lưu bản mã, mỗi lần mã hóa một khác (PBI-030).
    /// </summary>
    public string? IdentityNumberHash { get; set; }
    public DateTimeOffset? IdentityIssuedAt { get; set; }
    public string? IdentityIssuePlace { get; set; }
    public string? PreviousResidence { get; set; }
    public string? Notes { get; set; }
    public HouseholdEntity Household { get; set; } = null!;
}
