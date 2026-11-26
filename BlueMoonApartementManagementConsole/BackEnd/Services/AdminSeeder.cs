using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.AspNetCore.Identity;

namespace BlueMoonApartementManagementConsole.BackEnd.Services;

/// <summary>
/// Tạo tài khoản admin ban đầu khi database chưa có Ban quản trị nào (Program.cs gọi lúc khởi động).
/// Mật khẩu lấy từ ADMIN_INIT_PWD, phải theo PasswordRules (lỗi #1, issue #67); sai thì app dừng và báo lỗi cấu hình.
/// </summary>
public static class AdminSeeder
{
    public const string Username = "admin";

    public static AppUserEntity CreateInitialAdmin(string? initPassword, IPasswordHasher<AppUserEntity> hasher)
    {
        if (initPassword is null)
            throw new InvalidOperationException("Thiếu cấu hình ADMIN_INIT_PWD.");
        if (!PasswordRules.HasValidLength(initPassword))
            throw new InvalidOperationException(
                $"ADMIN_INIT_PWD phải từ {PasswordRules.MinLength} đến {PasswordRules.MaxLength} ký tự.");

        var admin = new AppUserEntity { Username = Username, Role = UserRole.BQT, Status = UserStatus.Approved };
        admin.PasswordHash = hasher.HashPassword(admin, initPassword);
        return admin;
    }
}
