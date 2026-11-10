using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.BackEnd.Models;

/// <summary>
/// Nhãn tiếng Việt của loại hình căn hộ, dùng chung cho ô chọn ở form căn hộ và ô lọc ở trang Hồ sơ căn hộ
/// (apartment-profiles.js đọc nhãn từ các option mà trang sinh ra từ danh sách này).
/// Thêm giá trị mới vào ApartmentType thì thêm nhãn ở đây; test K6 báo lỗi nếu quên.
/// </summary>
public static class ApartmentTypeLabels
{
    public static IReadOnlyList<(ApartmentType Type, string Label)> All { get; } =
    [
        (ApartmentType.CanHo, "Căn hộ ở"),
        (ApartmentType.Kiot, "Kiot"),
        (ApartmentType.Penthouse, "Penthouse"),
    ];
}
