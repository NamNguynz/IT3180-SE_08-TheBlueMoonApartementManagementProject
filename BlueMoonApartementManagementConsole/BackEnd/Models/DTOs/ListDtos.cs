using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;

/// <summary>
/// Bộ lọc trang Danh sách căn hộ. Số nhân khẩu chỉ là cột hiển thị, không có ô lọc
/// (theo yêu cầu nghiệp vụ). Khoảng diện tích nhận cả hai đầu.
/// </summary>
public sealed record ApartmentListQuery(
    string? Code,
    int? BuildingId,
    decimal? AreaFrom,
    decimal? AreaTo,
    int? Floor,
    ApartmentStatus? Status,
    int Page = 1,
    int PageSize = 10);

/// <summary>Dòng hiển thị của bảng Danh sách căn hộ.</summary>
public sealed record ApartmentListRow(
    int Id,
    string Code,
    string BuildingName,
    int ResidentCount,
    decimal Area,
    int Floor,
    ApartmentStatus Status);

/// <summary>
/// Bộ lọc trang Hộ &amp; nhân khẩu. Ngày sinh là khoảng theo ngày (DateOnly),
/// số CCCD chỉ khớp khi nhập đủ số vì cột này được mã hóa (PBI-030).
/// </summary>
public sealed record ResidentListQuery(
    string? FullName,
    DateOnly? DateOfBirthFrom,
    DateOnly? DateOfBirthTo,
    string? Gender,
    string? PlaceOfBirth,
    string? Hometown,
    string? Ethnicity,
    string? Religion,
    string? Workplace,
    string? IdentityNumber,
    int Page = 1,
    int PageSize = 10);

/// <summary>Dòng hiển thị của bảng Hộ &amp; nhân khẩu.</summary>
public sealed record ResidentListRow(
    int Id,
    string FullName,
    DateTimeOffset DateOfBirth,
    string Gender,
    string? PlaceOfBirth,
    string? Hometown,
    string? Ethnicity,
    string? Religion,
    string? Workplace,
    string? IdentityNumber);

/// <summary>Chuẩn hóa kích thước trang cho phép: 5 / 10 / 15 / 20.</summary>
public static class ListPageSize
{
    public static readonly int[] Allowed = [5, 10, 15, 20];
    public const int Default = 10;

    public static int Normalize(int value) => value switch
    {
        <= 5 => 5,
        <= 10 => 10,
        <= 15 => 15,
        _ => 20
    };
}
