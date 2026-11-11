using System.ComponentModel.DataAnnotations;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;

/// <summary>
/// Payload ghi nhận một biến động nhân khẩu (PBI-007). Trường nào bắt buộc tùy theo loại:
/// - ChuyenDen, TamTru: NewResident (nhân khẩu mới); TamTru cần thêm ToDate.
/// - TamVang: ResidentId, ToDate, Place (nơi đến). TroVe: ResidentId. ChuyenDi: ResidentId, Place (nơi chuyển đến).
/// Các quy tắc phụ thuộc loại được kiểm ở ResidentChangeService, thông báo lỗi chỉ rõ trường cần sửa (AC-002).
/// </summary>
public sealed record RecordResidentChangeRequest
{
    [EnumDataType(typeof(ResidentChangeType))]
    public required ResidentChangeType Type { get; init; }

    public int? ResidentId { get; init; }

    public ResidentProfileRequest? NewResident { get; init; }

    public DateOnly FromDate { get; init; }

    public DateOnly? ToDate { get; init; }

    [StringLength(300)]
    public string? Place { get; init; }

    [StringLength(500)]
    public string? Reason { get; init; }
}

/// <summary>Tình trạng cư trú của các nhân khẩu trong hộ và lịch sử biến động của hộ.</summary>
public sealed record HouseholdResidencyResponse(
    int HouseholdId,
    int ApartmentId,
    string HouseholdNumber,
    IReadOnlyList<ResidentResidencyResponse> Residents,
    IReadOnlyList<ResidentChangeResponse> Changes);

/// <summary>
/// Nhân khẩu kèm tình trạng cư trú. StatusUntil là ngày dự kiến kết thúc khi đang tạm trú/tạm vắng.
/// LastChangeDate là ngày của lần biến động gần nhất: lần sau không được ghi nhận sớm hơn ngày này.
/// </summary>
public sealed record ResidentResidencyResponse(
    int Id,
    string FullName,
    DateTimeOffset DateOfBirth,
    string Gender,
    ResidenceStatus ResidenceStatus,
    DateOnly? StatusUntil,
    DateOnly? LastChangeDate);

/// <summary>Một dòng lịch sử biến động.</summary>
public sealed record ResidentChangeResponse(
    int Id,
    int ResidentId,
    string ResidentName,
    ResidentChangeType Type,
    DateOnly FromDate,
    DateOnly? ToDate,
    string? Place,
    string? Reason,
    string RecordedBy,
    DateTimeOffset RecordedAt);
