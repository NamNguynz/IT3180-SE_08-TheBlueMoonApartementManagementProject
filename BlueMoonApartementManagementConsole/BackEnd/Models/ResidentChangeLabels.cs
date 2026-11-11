using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.BackEnd.Models;

/// <summary>
/// Nhãn tiếng Việt của loại biến động và tình trạng cư trú (PBI-007), dùng chung cho trang Biến động nhân khẩu
/// và trang Hồ sơ căn hộ (resident-changes.js, apartment-profiles.js đọc nhãn từ các option mà trang sinh ra
/// từ danh sách này). Thêm giá trị mới vào ResidentChangeType hoặc ResidenceStatus thì thêm nhãn ở đây;
/// test trong ResidentChangeServiceTests báo lỗi nếu quên.
/// </summary>
public static class ResidentChangeLabels
{
    public static IReadOnlyList<(ResidentChangeType Type, string Label)> Types { get; } =
    [
        (ResidentChangeType.ChuyenDen, "Chuyển đến"),
        (ResidentChangeType.TamTru, "Đăng ký tạm trú"),
        (ResidentChangeType.TamVang, "Khai báo tạm vắng"),
        (ResidentChangeType.TroVe, "Trở về (kết thúc tạm vắng)"),
        (ResidentChangeType.ChuyenDi, "Chuyển đi"),
    ];

    public static IReadOnlyList<(ResidenceStatus Status, string Label)> Statuses { get; } =
    [
        (ResidenceStatus.ThuongTru, "Thường trú"),
        (ResidenceStatus.TamTru, "Tạm trú"),
        (ResidenceStatus.TamVang, "Tạm vắng"),
        (ResidenceStatus.DaChuyenDi, "Đã chuyển đi"),
    ];
}
