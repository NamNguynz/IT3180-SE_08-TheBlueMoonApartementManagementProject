using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.BackEnd.Models;

/// <summary>
/// Nhãn tiếng Việt của trạng thái căn hộ, dùng chung cho ô chọn trong form, các cột bảng kéo thả
/// và badge ở trang Hồ sơ căn hộ (apartment-profiles.js đọc nhãn từ danh sách này).
/// Thêm giá trị mới vào ApartmentStatus thì thêm nhãn ở đây; test K6 báo lỗi nếu quên.
/// </summary>
public static class ApartmentStatusLabels
{
    public static IReadOnlyList<(ApartmentStatus Status, string Label)> All { get; } =
    [
        (ApartmentStatus.DangChoThue, "Đang cho thuê"),
        (ApartmentStatus.DaChoThue, "Đã cho thuê"),
        (ApartmentStatus.DangSuaChua, "Đang sửa chữa"),
    ];

    /// <summary>Trả nhãn tiếng Việt, fallback sang tên enum nếu có giá trị mới chưa kịp thêm nhãn.</summary>
    public static string LabelOf(ApartmentStatus status) =>
        All.FirstOrDefault(x => x.Status == status).Label ?? status.ToString();

    /// <summary>Class CSS của badge theo trạng thái.</summary>
    public static string BadgeClassOf(ApartmentStatus status) => status switch
    {
        ApartmentStatus.DaChoThue => "badge badge-ok",
        ApartmentStatus.DangSuaChua => "badge badge-warn",
        _ => "badge badge-muted"
    };
}
