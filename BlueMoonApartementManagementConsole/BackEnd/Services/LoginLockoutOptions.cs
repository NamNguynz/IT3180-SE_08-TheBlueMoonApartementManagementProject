namespace BlueMoonApartementManagementConsole.BackEnd.Services;

/// <summary>
/// Khóa tạm tài khoản khi nhập sai mật khẩu nhiều lần, dùng chung cho đăng nhập và đổi mật khẩu (G3, issue #69).
/// Mặc định 5 lần, khóa 15 phút: giá trị đề xuất, PO cần xác nhận (RSK-024).
/// Dev chỉnh được bằng cấu hình mục "Lockout", ví dụ biến môi trường Lockout__Minutes=1 khi test bằng tay.
/// </summary>
public sealed class LoginLockoutOptions
{
    public const string Section = "Lockout";

    public int MaxFailedAttempts { get; set; } = 5;

    public int Minutes { get; set; } = 15;

    public TimeSpan Duration => TimeSpan.FromMinutes(Minutes);
}
