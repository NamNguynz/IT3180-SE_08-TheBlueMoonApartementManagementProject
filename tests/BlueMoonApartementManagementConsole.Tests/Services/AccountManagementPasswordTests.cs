using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using BlueMoonApartementManagementConsole.Tests.Fakes;
using Microsoft.AspNetCore.Identity;

namespace BlueMoonApartementManagementConsole.Tests.Services;

/// <summary>
/// Unit test băm mật khẩu trong AccountManagementService
/// (BQT tạo tài khoản, cấp lại mật khẩu tạm, người dùng tự đổi mật khẩu).
/// </summary>
public class AccountManagementPasswordTests
{
    private const string OldPassword = "MatKhauCu@123";
    private const string NewPassword = "MatKhauMoi@456";
    private const string TempPasswordChars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";

    private readonly FakeAppUserRepo _repo = new();
    private readonly FakeAuditService _audit = new();
    private readonly PasswordHasher<AppUserEntity> _hasher = new();
    private readonly AccountManagementService _service;
    private readonly AppUserEntity _admin;

    public AccountManagementPasswordTests()
    {
        _service = new AccountManagementService(_repo, _hasher, _audit);
        _admin = SeedUser("admin", "Admin@12345", UserRole.BQT);
    }

    private AppUserEntity SeedUser(string username, string password, UserRole role, bool mustChangePassword = false)
    {
        var user = new AppUserEntity
        {
            Username = username,
            FullName = username,
            Role = role,
            Status = UserStatus.Approved,
            IsActive = true,
            MustChangePassword = mustChangePassword
        };
        user.PasswordHash = _hasher.HashPassword(user, password);
        return _repo.Seed(user);
    }

    private PasswordVerificationResult Verify(AppUserEntity user, string password) =>
        _hasher.VerifyHashedPassword(user, user.PasswordHash, password);

    // ===== U7: BQT tạo tài khoản với mật khẩu tạm =====

    [Fact]
    [Trait("TestId", "U7")]
    public async Task Create_ReturnsTwelveCharTempPassword_FromAllowedCharset()
    {
        var result = await _service.CreateAsync(_admin.Id, new CreateAccountRequest("ketoan01", "Tran Thi B", UserRole.KE_TOAN));

        Assert.Equal(ManageStatus.Success, result.Status);
        Assert.NotNull(result.TempPassword);
        Assert.Equal(12, result.TempPassword!.Length);
        Assert.All(result.TempPassword, c => Assert.Contains(c, TempPasswordChars));
    }

    [Fact]
    [Trait("TestId", "U7")]
    public async Task Create_StoresHashOfTempPassword_AndForcesPasswordChange()
    {
        var result = await _service.CreateAsync(_admin.Id, new CreateAccountRequest("ketoan01", "Tran Thi B", UserRole.KE_TOAN));
        var user = _repo.Users.Single(u => u.Id == result.Id);

        Assert.NotEqual(result.TempPassword, user.PasswordHash);
        Assert.DoesNotContain(result.TempPassword!, user.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success, Verify(user, result.TempPassword!));
        Assert.True(user.MustChangePassword);
    }

    [Fact]
    [Trait("TestId", "U7")]
    public async Task Create_TwoAccounts_GetDifferentTempPasswords()
    {
        var a = await _service.CreateAsync(_admin.Id, new CreateAccountRequest("ketoan01", "Tran Thi B", UserRole.KE_TOAN));
        var b = await _service.CreateAsync(_admin.Id, new CreateAccountRequest("ketoan02", "Le Van C", UserRole.KE_TOAN));

        Assert.NotEqual(a.TempPassword, b.TempPassword);
    }

    // ===== U7: BQT cấp lại mật khẩu =====

    [Fact]
    [Trait("TestId", "U7")]
    public async Task ResetPassword_OldPasswordStopsWorking_TempPasswordWorks()
    {
        var target = SeedUser("cudan01", OldPassword, UserRole.CU_DAN);

        var result = await _service.ResetPasswordAsync(_admin.Id, target.Id);

        Assert.Equal(ManageStatus.Success, result.Status);
        Assert.Equal(12, result.TempPassword!.Length);
        Assert.Equal(PasswordVerificationResult.Failed, Verify(target, OldPassword));
        Assert.Equal(PasswordVerificationResult.Success, Verify(target, result.TempPassword));
    }

    [Fact]
    [Trait("TestId", "U7")]
    public async Task ResetPassword_ForcesPasswordChange_AndClearsLock()
    {
        var target = SeedUser("cudan01", OldPassword, UserRole.CU_DAN);
        target.FailedAttempts = 3;
        target.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(10);

        await _service.ResetPasswordAsync(_admin.Id, target.Id);

        Assert.True(target.MustChangePassword);
        Assert.Equal(0, target.FailedAttempts);
        Assert.Null(target.LockedUntil);
    }

    // ===== U6: Người dùng tự đổi mật khẩu =====

    [Fact]
    [Trait("TestId", "U6")]
    public async Task ChangeOwnPassword_WrongCurrentPassword_IsRejected_HashUnchanged()
    {
        var user = SeedUser("cudan01", OldPassword, UserRole.CU_DAN);
        var hashBefore = user.PasswordHash;

        var result = await _service.ChangeOwnPasswordAsync(user.Id, new ChangePasswordRequest("SaiMatKhau@1", NewPassword));

        Assert.Equal(ManageStatus.Invalid, result.Status);
        Assert.Equal(hashBefore, user.PasswordHash);
    }

    [Fact]
    [Trait("TestId", "U6")]
    public async Task ChangeOwnPassword_SameAsCurrent_IsRejected()
    {
        var user = SeedUser("cudan01", OldPassword, UserRole.CU_DAN);

        var result = await _service.ChangeOwnPasswordAsync(user.Id, new ChangePasswordRequest(OldPassword, OldPassword));

        Assert.Equal(ManageStatus.Invalid, result.Status);
    }

    [Theory]
    [Trait("TestId", "U6")]
    [InlineData(7, ManageStatus.Invalid)]
    [InlineData(8, ManageStatus.Success)]
    [InlineData(100, ManageStatus.Success)]
    [InlineData(101, ManageStatus.Invalid)]
    public async Task ChangeOwnPassword_NewPasswordLengthBoundaries(int length, ManageStatus expected)
    {
        var user = SeedUser("cudan01", OldPassword, UserRole.CU_DAN);

        var result = await _service.ChangeOwnPasswordAsync(user.Id, new ChangePasswordRequest(OldPassword, new string('b', length)));

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    [Trait("TestId", "U6")]
    public async Task ChangeOwnPassword_Success_NewPasswordWorks_OldPasswordStopsWorking()
    {
        var user = SeedUser("cudan01", OldPassword, UserRole.CU_DAN, mustChangePassword: true);
        var hashBefore = user.PasswordHash;

        var result = await _service.ChangeOwnPasswordAsync(user.Id, new ChangePasswordRequest(OldPassword, NewPassword));

        Assert.Equal(ManageStatus.Success, result.Status);
        Assert.NotEqual(hashBefore, user.PasswordHash);
        Assert.DoesNotContain(NewPassword, user.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success, Verify(user, NewPassword));
        Assert.Equal(PasswordVerificationResult.Failed, Verify(user, OldPassword));
        Assert.False(user.MustChangePassword);
        Assert.NotNull(user.PasswordChangedAt);
    }

    // ===== U9b: Audit log không chứa mật khẩu tạm / mới / cũ =====

    [Fact]
    [Trait("TestId", "U9b")]
    public async Task AuditLogs_NeverContainTempOrNewPasswords()
    {
        var created = await _service.CreateAsync(_admin.Id, new CreateAccountRequest("ketoan01", "Tran Thi B", UserRole.KE_TOAN));
        var target = SeedUser("cudan01", OldPassword, UserRole.CU_DAN);
        var reset = await _service.ResetPasswordAsync(_admin.Id, target.Id);
        await _service.ChangeOwnPasswordAsync(target.Id, new ChangePasswordRequest(reset.TempPassword!, NewPassword));

        Assert.Equal(3, _audit.Entries.Count);
        Assert.False(_audit.AnyEntryContains(created.TempPassword!));
        Assert.False(_audit.AnyEntryContains(reset.TempPassword!));
        Assert.False(_audit.AnyEntryContains(NewPassword));
        Assert.False(_audit.AnyEntryContains(OldPassword));
    }
}
