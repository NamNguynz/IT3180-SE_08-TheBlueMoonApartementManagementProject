namespace BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

/// <summary>Tòa nhà (tách riêng để căn hộ tham chiếu bằng khóa ngoại BuildingId).</summary>
public class BuildingEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public List<ApartmentEntity> Apartments { get; set; } = [];
}