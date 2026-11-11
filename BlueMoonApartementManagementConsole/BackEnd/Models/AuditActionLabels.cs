using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.BackEnd.Models;

/// <summary>
/// Nhãn tiếng Việt của từng loại thao tác trong nhật ký, dùng chung cho ô lọc Hành động và bảng ở trang Nhật ký
/// (audit-log.js đọc nhãn từ các option mà trang sinh ra từ danh sách này).
/// Thêm giá trị mới vào AuditAction thì thêm nhãn ở đây; test N7 báo lỗi nếu quên.
/// </summary>
public static class AuditActionLabels
{
    /// <summary>Thứ tự hiển thị ở ô lọc: tài khoản, hồ sơ căn hộ/hộ khẩu, khoản thu.</summary>
    public static IReadOnlyList<(AuditAction Action, string Label)> All { get; } =
    [
        (AuditAction.LoginSuccess, "Đăng nhập thành công"),
        (AuditAction.LoginFailed, "Đăng nhập thất bại"),
        (AuditAction.LoginLocked, "Bị khóa tạm thời"),
        (AuditAction.Logout, "Đăng xuất"),
        (AuditAction.Register, "Đăng ký"),
        (AuditAction.ApproveAccount, "Xác nhận tài khoản"),
        (AuditAction.CreateAccount, "Tạo tài khoản mới"),
        (AuditAction.DisableAccount, "Vô hiệu hóa tài khoản"),
        (AuditAction.EnableAccount, "Kích hoạt tài khoản"),
        (AuditAction.UnlockAccount, "Mở khóa tài khoản"),
        (AuditAction.ResetPassword, "Reset mật khẩu"),
        (AuditAction.ChangePassword, "Thay đổi mật khẩu"),
        (AuditAction.CreateApartmentProfile, "Tạo hồ sơ căn hộ"),
        (AuditAction.UpdateApartmentProfile, "Cập nhật hồ sơ căn hộ"),
        (AuditAction.CreateHouseholdProfile, "Tạo hồ sơ hộ khẩu"),
        (AuditAction.UpdateHouseholdProfile, "Cập nhật hồ sơ hộ khẩu"),
        (AuditAction.RecordResidentChange, "Ghi nhận biến động nhân khẩu"),
        (AuditAction.CreateFeeItem, "Tạo khoản thu"),
        (AuditAction.ChangeRate, "Điều chỉnh giá khoản thu"),
        (AuditAction.CreateBillingPeriod, "Lập kỳ thu"),
        (AuditAction.RecordPayment, "Ghi nhận thu tiền"),
        (AuditAction.RecordDebtReminder, "Ghi nhận nhắc nộp"),
    ];
}
