using System.ComponentModel.DataAnnotations;

namespace BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;

/// <summary>Payload tạo tòa nhà.</summary>
public sealed record CreateBuildingRequest
{
    [Required, StringLength(100)]
    public required string Name { get; init; }
}

/// <summary>Tòa nhà trả về cho danh sách và ô chọn.</summary>
public sealed record BuildingResponse(int Id, string Name);