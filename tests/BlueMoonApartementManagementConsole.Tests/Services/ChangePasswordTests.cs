using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using BlueMoonApartementManagementConsole.Tests.Fakes;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
    [InlineData(OldPassword, "ngan")]           // mật khẩu mới quá ngắn
    [InlineData(OldPassword, OldPassword)]      // trùng mật khẩu cũ
    public async Task ChangePassword_InvalidNewPassword_WritesNoLog(string current, string next)
    {
        var user = SeedResident();

        var result = await _accounts.ChangeOwnPasswordAsync(user.Id, new ChangePasswordRequest(current, next));

        Assert.Equal(ManageStatus.Invalid, result.Status);
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    [Trait("TestId", "P2")]
    public async Task ChangePassword_WrongCurrentPassword_WritesOneFailedLog_WithoutPassword()
    {
        // Từ G3 (issue #69): sai mật khẩu hiện tại được đếm như đăng nhập sai, nên ghi nhật ký để truy vết
        var user = SeedResident();

        var result = await _accounts.ChangeOwnPasswordAsync(user.Id, new ChangePasswordRequest("SaiMatKhau@1", NewPassword));

        Assert.Equal(ManageStatus.Invalid, result.Status);
        var log = Assert.Single(_audit.Entries);
        Assert.Equal(AuditAction.LoginFailed, log.Action);
        Assert.Equal(user.Id, log.UserId);
        Assert.Equal("Sai mật khẩu hiện tại khi đổi mật khẩu, lần 1", log.Detail);
        Assert.False(_audit.AnyEntryContains("SaiMatKhau@1"));
        Assert.False(_audit.AnyEntryContains(NewPassword));
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

    // ===== P5, P11 – P15: Sai mật khẩu hiện tại khi đổi thì đếm và khóa như đăng nhập (G3, issue #69) =====

    private Task<ManageResult> ChangeWithWrongCurrentAsync(AppUserEntity user, int i) =>
        _accounts.ChangeOwnPasswordAsync(user.Id, new ChangePasswordRequest($"Sai@{i:D4}xx", NewPassword));

    [Fact]
    [Trait("TestId", "P5")]
    public async Task ChangePassword_FiveWrongCurrentPasswords_LocksAccount_AndEndsAllSessions()
    {
        var user = SeedResident();
        var stamp = user.SecurityStamp;

        for (var i = 1; i <= 4; i++)
        {
            var result = await ChangeWithWrongCurrentAsync(user, i);
            Assert.Equal(ManageStatus.Invalid, result.Status);
            Assert.Equal("Mật khẩu hiện tại không đúng.", result.Message);
        }
        Assert.Equal(4, user.FailedAttempts);
        Assert.Null(user.LockedUntil);
        Assert.Equal(stamp, user.SecurityStamp);

        var fifth = await ChangeWithWrongCurrentAsync(user, 5);

        Assert.Equal(ManageStatus.Locked, fifth.Status);
        Assert.Equal("Tài khoản tạm bị khóa do nhập sai mật khẩu nhiều lần. Vui lòng đăng nhập lại sau 15 phút.", fifth.Message);
        Assert.InRange(user.LockedUntil!.Value, DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(15));
        Assert.Equal(0, user.FailedAttempts);
        Assert.NotEqual(stamp, user.SecurityStamp);   // mọi phiên đang mở bị đăng xuất
        Assert.Equal(PasswordVerificationResult.Success, _hasher.VerifyHashedPassword(user, user.PasswordHash, OldPassword));

        Assert.Equal(
            Enumerable.Range(1, 5).Select(i => $"Sai mật khẩu hiện tại khi đổi mật khẩu, lần {i}"),
            _audit.Entries.Where(e => e.Action == AuditAction.LoginFailed).Select(e => e.Detail));
        var locked = Assert.Single(_audit.Entries, e => e.Action == AuditAction.LoginLocked);
        Assert.EndsWith("đăng xuất mọi phiên", locked.Detail);
        Assert.False(_audit.AnyEntryContains("Sai@0005xx"), "Nhật ký không được chứa mật khẩu đã nhập");
    }

    [Fact]
    [Trait("TestId", "P11")]
    public async Task ChangePassword_WhileLocked_EvenWithCorrectCurrentPassword_IsRejected_PasswordUnchanged()
    {
        var user = SeedResident();
        user.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(10);

        var result = await _accounts.ChangeOwnPasswordAsync(user.Id, new ChangePasswordRequest(OldPassword, NewPassword));

        Assert.Equal(ManageStatus.Locked, result.Status);
        Assert.Equal(PasswordVerificationResult.Success, _hasher.VerifyHashedPassword(user, user.PasswordHash, OldPassword));
        Assert.Equal(AuditAction.LoginLocked, Assert.Single(_audit.Entries).Action);
    }

    [Fact]
    [Trait("TestId", "P12")]
    public async Task Login_AfterLockFromChangePassword_IsLockedToo()
    {
        var user = SeedResident();
        for (var i = 1; i <= 5; i++)
            await ChangeWithWrongCurrentAsync(user, i);

        var login = await LoginAsync(OldPassword);

        Assert.Equal(LoginStatus.Locked, login.Status);
    }

    [Fact]
    [Trait("TestId", "P13")]
    public async Task WrongLogins_AndWrongCurrentPasswords_ShareOneCounter()
    {
        var user = SeedResident();
        for (var i = 0; i < 3; i++)
            await LoginAsync("SaiMatKhau@1");

        await ChangeWithWrongCurrentAsync(user, 1);
        var fifth = await ChangeWithWrongCurrentAsync(user, 2);

        Assert.Equal(ManageStatus.Locked, fifth.Status);
        Assert.NotNull(user.LockedUntil);
    }

    [Fact]
    [Trait("TestId", "P14")]
    public async Task ChangePassword_AfterLockExpired_Succeeds_AndResetsCounter()
    {
        var user = SeedResident();
        user.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(-1);
        user.FailedAttempts = 2;

        var result = await _accounts.ChangeOwnPasswordAsync(user.Id, new ChangePasswordRequest(OldPassword, NewPassword));

        Assert.Equal(ManageStatus.Success, result.Status);
        Assert.Equal(0, user.FailedAttempts);
        Assert.Null(user.LockedUntil);
        Assert.Equal(PasswordVerificationResult.Success, _hasher.VerifyHashedPassword(user, user.PasswordHash, NewPassword));
    }

    [Fact]
    [Trait("TestId", "P15")]
    public async Task Lockout_UsesConfiguredAttemptsAndMinutes()
    {
        // Dev chỉnh được bằng cấu hình "Lockout" (vd Lockout__Minutes=1) để test bằng tay không phải chờ 15 phút
        var accounts = new AccountManagementService(_repo, _hasher, _audit,
            Options.Create(new LoginLockoutOptions { MaxFailedAttempts = 2, Minutes = 1 }));
        var user = SeedResident();

        await accounts.ChangeOwnPasswordAsync(user.Id, new ChangePasswordRequest("Sai@0001xx", NewPassword));
        var second = await accounts.ChangeOwnPasswordAsync(user.Id, new ChangePasswordRequest("Sai@0002xx", NewPassword));

        Assert.Equal(ManageStatus.Locked, second.Status);
        Assert.EndsWith("sau 1 phút.", second.Message);
        Assert.InRange(user.LockedUntil!.Value, DateTimeOffset.UtcNow.AddSeconds(50), DateTimeOffset.UtcNow.AddMinutes(1));
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
