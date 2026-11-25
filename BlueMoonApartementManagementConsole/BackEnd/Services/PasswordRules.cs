namespace BlueMoonApartementManagementConsole.BackEnd.Services;

/// <summary>
/// Quy tắc mật khẩu dùng chung cho đăng ký, đổi mật khẩu và mật khẩu admin ban đầu (ADMIN_INIT_PWD).
/// RSK-024: bắt buộc mật khẩu đủ mạnh.
/// </summary>
public static class PasswordRules
{
    public const int MinLength = 8;
    public const int MaxLength = 100;

    public static bool HasValidLength(string? password) =>
        !string.IsNullOrEmpty(password) && password.Length is >= MinLength and <= MaxLength;
}
