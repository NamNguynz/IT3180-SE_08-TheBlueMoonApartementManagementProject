using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using BlueMoonApartementManagementConsole.Tests.Fakes;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlueMoonApartementManagementConsole.Tests.Services;

/// <summary>
/// Unit test đổi mật khẩu: tài khoản không tồn tại, audit log, và luồng đổi xong đăng nhập lại.
/// AuthService và AccountManagementService dùng chung một kho dữ liệu và một hasher thật.
/// Các trường hợp kiểm tra đầu vào (sai mật khẩu hiện tại, độ dài,...) đã có ở AccountManagementPasswordTests.
/// </summary>
public class ChangePasswordTests
{
    private const string OldPassword = UserFactory.DefaultPassword;
    private const string NewPassword = "MatKhauMoi@456";

    private readonly FakeAppUserRepo _repo = new();
    private readonly FakeAuditService _audit = new();
    private readonly PasswordHasher<AppUserEntity> _hasher = new();
    private readonly AccountManagementService _accounts;
    private readonly AuthService _auth;

    public ChangePasswordTests()
    {
        _accounts = new AccountManagementService(_repo, _hasher, _audit);
        _auth = new AuthService(_repo, _hasher, _audit, NullLogger<AuthService>.Instance);
    }

    private AppUserEntity SeedResident() =>
        _repo.Seed("cudan01", UserRole.CU_DAN, hasher: _hasher);

    private Task<LoginResult> LoginAsync(string password) =>
        _auth.LoginAsync(new LoginRequest("cudan01", password, UserRole.CU_DAN));

    // ===== P1: Tài khoản không tồn tại =====

    [Fact]
    [Trait("TestId", "P1")]
    public async Task ChangePassword_NonExistentUser_ReturnsNotFound()
    {
        var result = await _accounts.ChangeOwnPasswordAsync(999, new ChangePasswordRequest(OldPassword, NewPassword));

        Assert.Equal(ManageStatus.NotFound, result.Status);
        Assert.Empty(_audit.Entries);
    }

    // ===== P2: Audit log =====

    [Fact]
    [Trait("TestId", "P2")]
    public async Task ChangePassword_Success_LogsExactlyOneChangePasswordEntry()
    {
        var user = SeedResident();

        await _accounts.ChangeOwnPasswordAsync(user.Id, new ChangePasswordRequest(OldPassword, NewPassword));

        var log = Assert.Single(_audit.Entries);
        Assert.Equal(AuditAction.ChangePassword, log.Action);
        Assert.Equal("cudan01", log.Username);
        Assert.Equal(user.Id, log.UserId);
        Assert.Equal(user.Id.ToString(), log.Target);
    }

    [Theory]
    [Trait("TestId", "P2")]
    [InlineData("SaiMatKhau@1", NewPassword)]   // sai mật khẩu hiện tại
    [InlineData(OldPassword, "ngan")]           // mật khẩu mới quá ngắn
    [InlineData(OldPassword, OldPassword)]      // trùng mật khẩu cũ
    public async Task ChangePassword_Failure_WritesNoLog(string current, string next)
    {
        var user = SeedResident();

        var result = await _accounts.ChangeOwnPasswordAsync(user.Id, new ChangePasswordRequest(current, next));

        Assert.Equal(ManageStatus.Invalid, result.Status);
        Assert.Empty(_audit.Entries);
    }

    // ===== P3: Đổi xong đăng nhập bằng mật khẩu mới =====

    [Fact]
    [Trait("TestId", "P3")]
    public async Task ChangePassword_ThenLogin_NewPasswordWorks_OldPasswordFails()
    {
        var user = SeedResident();

        var change = await _accounts.ChangeOwnPasswordAsync(user.Id, new ChangePasswordRequest(OldPassword, NewPassword));
        Assert.Equal(ManageStatus.Success, change.Status);

        Assert.Equal(LoginStatus.Success, (await LoginAsync(NewPassword)).Status);
        Assert.Equal(LoginStatus.InvalidCredentials, (await LoginAsync(OldPassword)).Status);
    }

    // ===== P4: Luồng mật khẩu tạm do BQT cấp =====

    [Fact]
    [Trait("TestId", "P4")]
    public async Task TempPasswordFlow_ResetThenChange_ClearsMustChange_AndNewPasswordWorks()
    {
        var admin = _repo.Seed("admin", UserRole.BQT, hasher: _hasher);
        var user = SeedResident();

        var reset = await _accounts.ResetPasswordAsync(admin.Id, user.Id);
        Assert.Equal(ManageStatus.Success, reset.Status);
        Assert.True(user.MustChangePassword);

        var loginWithTemp = await LoginAsync(reset.TempPassword!);
        Assert.Equal(LoginStatus.Success, loginWithTemp.Status);
        Assert.True(loginWithTemp.User!.MustChangePassword);

        var change = await _accounts.ChangeOwnPasswordAsync(user.Id, new ChangePasswordRequest(reset.TempPassword!, NewPassword));
        Assert.Equal(ManageStatus.Success, change.Status);
        Assert.False(user.MustChangePassword);

        var loginWithNew = await LoginAsync(NewPassword);
        Assert.Equal(LoginStatus.Success, loginWithNew.Status);
        Assert.False(loginWithNew.User!.MustChangePassword);

        Assert.Equal(LoginStatus.InvalidCredentials, (await LoginAsync(reset.TempPassword!)).Status);
        Assert.Equal(LoginStatus.InvalidCredentials, (await LoginAsync(OldPassword)).Status);
    }

    // ===== P5: Sai mật khẩu hiện tại khi đổi không bị giới hạn số lần (G3) =====

    [Fact]
    [Trait("TestId", "P5")]
    public async Task ChangePassword_RepeatedWrongCurrentPassword_DoesNotCountOrLock()
    {
        // Ghi nhận hành vi hiện tại (G3): không có giới hạn số lần nhập sai mật khẩu hiện tại
        var user = SeedResident();

        for (var i = 0; i < 10; i++)
        {
            var result = await _accounts.ChangeOwnPasswordAsync(user.Id, new ChangePasswordRequest($"Sai@{i:D4}xx", NewPassword));
            Assert.Equal(ManageStatus.Invalid, result.Status);
        }

        Assert.Equal(0, user.FailedAttempts);
        Assert.Null(user.LockedUntil);
        Assert.Equal(PasswordVerificationResult.Success, _hasher.VerifyHashedPassword(user, user.PasswordHash, OldPassword));
    }

    // ===== P6 – P8: SecurityStamp, để các phiên đang mở bị đăng xuất (G2) =====

    [Fact]
    [Trait("TestId", "P6")]
    public async Task ChangePassword_Success_RotatesSecurityStamp()
    {
        var user = SeedResident();
        var before = user.SecurityStamp;

        var result = await _accounts.ChangeOwnPasswordAsync(user.Id, new ChangePasswordRequest(OldPassword, NewPassword));

        Assert.Equal(ManageStatus.Success, result.Status);
        Assert.False(string.IsNullOrEmpty(user.SecurityStamp));
        Assert.NotEqual(before, user.SecurityStamp);
    }

    [Theory]
    [Trait("TestId", "P7")]
    [InlineData("SaiMatKhau@1", NewPassword)]   // sai mật khẩu hiện tại
    [InlineData(OldPassword, "ngan")]           // mật khẩu mới quá ngắn
    [InlineData(OldPassword, OldPassword)]      // trùng mật khẩu cũ
    public async Task ChangePassword_Failure_KeepsSecurityStamp(string current, string next)
    {
        var user = SeedResident();
        var before = user.SecurityStamp;

        var result = await _accounts.ChangeOwnPasswordAsync(user.Id, new ChangePasswordRequest(current, next));

        Assert.Equal(ManageStatus.Invalid, result.Status);
        Assert.Equal(before, user.SecurityStamp);
    }

    [Fact]
    [Trait("TestId", "P8")]
    public async Task ResetPassword_RotatesTargetSecurityStamp_ButNotActors()
    {
        var admin = _repo.Seed("admin", UserRole.BQT, hasher: _hasher);
        var user = SeedResident();
        var userBefore = user.SecurityStamp;
        var adminBefore = admin.SecurityStamp;

        var reset = await _accounts.ResetPasswordAsync(admin.Id, user.Id);

        Assert.Equal(ManageStatus.Success, reset.Status);
        Assert.NotEqual(userBefore, user.SecurityStamp);
        Assert.Equal(adminBefore, admin.SecurityStamp);
    }
}
