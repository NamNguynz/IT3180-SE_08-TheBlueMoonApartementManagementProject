using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using Microsoft.AspNetCore.Identity;

namespace BlueMoonApartementManagementConsole.Tests.Services;

/// <summary>
/// P9: quy tắc độ dài mật khẩu dùng chung (đăng ký, đổi mật khẩu, ADMIN_INIT_PWD).
/// P10: tạo admin ban đầu từ ADMIN_INIT_PWD (lỗi #1, issue #67; RSK-024, AC-039).
/// </summary>
public class PasswordRulesTests
{
    // ===== P9: Độ dài 8 – 100 ký tự =====

    [Theory]
    [Trait("TestId", "P9")]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("Abc@123", false)]   // 7 ký tự
    [InlineData("Abc@1234", true)]   // 8 ký tự
    public void HasValidLength_ChecksMinimum(string? password, bool expected) =>
        Assert.Equal(expected, PasswordRules.HasValidLength(password));

    [Theory]
    [Trait("TestId", "P9")]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void HasValidLength_ChecksMaximum(int length, bool expected) =>
        Assert.Equal(expected, PasswordRules.HasValidLength(new string('a', length)));

    // ===== P10: Mật khẩu admin ban đầu =====

    [Theory]
    [Trait("TestId", "P10")]
    [InlineData("")]
    [InlineData("1234567")]
    public void CreateInitialAdmin_ShortPassword_StopsWithConfigurationError(string initPassword)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            AdminSeeder.CreateInitialAdmin(initPassword, new PasswordHasher<AppUserEntity>()));

        Assert.Equal("ADMIN_INIT_PWD phải từ 8 đến 100 ký tự.", error.Message);
    }

    [Fact]
    [Trait("TestId", "P10")]
    public void CreateInitialAdmin_MissingPassword_StopsWithConfigurationError()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            AdminSeeder.CreateInitialAdmin(null, new PasswordHasher<AppUserEntity>()));

        Assert.Equal("Thiếu cấu hình ADMIN_INIT_PWD.", error.Message);
    }

    [Fact]
    [Trait("TestId", "P10")]
    public void CreateInitialAdmin_ValidPassword_CreatesApprovedBqt_WithHashedPasswordOnly()
    {
        const string initPassword = "KhoiTao@2026";
        var hasher = new PasswordHasher<AppUserEntity>();

        var admin = AdminSeeder.CreateInitialAdmin(initPassword, hasher);

        Assert.Equal("admin", admin.Username);
        Assert.Equal(UserRole.BQT, admin.Role);
        Assert.Equal(UserStatus.Approved, admin.Status);
        Assert.DoesNotContain(initPassword, admin.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(admin, admin.PasswordHash, initPassword));
    }
}
