using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using BlueMoonApartementManagementConsole.Tests.Fakes;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlueMoonApartementManagementConsole.Tests.Services;

/// <summary>
/// Unit test băm mật khẩu trong AuthService (đăng ký, đăng nhập, khóa tài khoản, băm lại).
/// Dùng PasswordHasher thật để kiểm tra đúng thuật toán app đang dùng.
/// </summary>
public class AuthServicePasswordTests
{
    private const string Password = "MatKhau@123";

    private readonly FakeAppUserRepo _repo = new();
    private readonly FakeAuditService _audit = new();
    private readonly PasswordHasher<AppUserEntity> _hasher = new();

    private AuthService CreateService(IPasswordHasher<AppUserEntity>? hasher = null) =>
        new(_repo, hasher ?? _hasher, _audit, NullLogger<AuthService>.Instance);

    private async Task<AppUserEntity> RegisterResidentAsync(string username = "cudan01", string password = Password)
    {
        var result = await CreateService().RegisterAsync(
            new RegisterRequest(username, password, "Nguyen Van A", UserRole.CU_DAN));
        Assert.Equal(RegisterStatus.Success, result.Status);
        return result.User!;
    }

    // ===== U1: Đăng ký lưu hash, không lưu mật khẩu gốc =====

    [Fact]
    [Trait("TestId", "U1")]
    public async Task Register_StoresHash_NotPlainPassword()
    {
        var user = await RegisterResidentAsync();

        Assert.False(string.IsNullOrEmpty(user.PasswordHash));
        Assert.NotEqual(Password, user.PasswordHash);
        Assert.DoesNotContain(Password, user.PasswordHash);
    }

    [Fact]
    [Trait("TestId", "U1")]
    public async Task Register_StoredHash_VerifiesCorrectPassword_RejectsWrongPassword()
    {
        var user = await RegisterResidentAsync();

        Assert.Equal(PasswordVerificationResult.Success,
            _hasher.VerifyHashedPassword(user, user.PasswordHash, Password));
        Assert.Equal(PasswordVerificationResult.Failed,
            _hasher.VerifyHashedPassword(user, user.PasswordHash, "SaiMatKhau@1"));
    }

    // ===== U2: Có salt: cùng mật khẩu ra hash khác nhau =====

    [Fact]
    [Trait("TestId", "U2")]
    public async Task Register_SamePasswordForTwoUsers_ProducesDifferentHashes()
    {
        var a = await RegisterResidentAsync("cudan01");
        var b = await RegisterResidentAsync("cudan02");

        Assert.NotEqual(a.PasswordHash, b.PasswordHash);
    }

    // ===== U3: Đăng nhập đúng / sai mật khẩu =====

    [Fact]
    [Trait("TestId", "U3")]
    public async Task Login_CorrectPassword_ReturnsSuccess()
    {
        await RegisterResidentAsync();

        var result = await CreateService().LoginAsync(new LoginRequest("cudan01", Password, UserRole.CU_DAN));

        Assert.Equal(LoginStatus.Success, result.Status);
        Assert.Equal(0, result.User!.FailedAttempts);
    }

    [Fact]
    [Trait("TestId", "U3")]
    public async Task Login_WrongPassword_ReturnsInvalidCredentials_AndIncrementsFailedAttempts()
    {
        var user = await RegisterResidentAsync();

        var result = await CreateService().LoginAsync(new LoginRequest("cudan01", "SaiMatKhau@1", UserRole.CU_DAN));

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        Assert.Null(result.User);
        Assert.Equal(1, user.FailedAttempts);
    }

    [Fact]
    [Trait("TestId", "U3b")]
    public async Task Login_CorrectPasswordButWrongRole_ReturnsInvalidCredentials_WithoutCountingFailure()
    {
        var user = await RegisterResidentAsync();

        var result = await CreateService().LoginAsync(new LoginRequest("cudan01", Password, UserRole.KE_TOAN));

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        Assert.Equal(0, user.FailedAttempts);
    }

    // ===== U4: Sai 5 lần thì khóa 15 phút =====

    [Fact]
    [Trait("TestId", "U4")]
    public async Task Login_FourWrongPasswords_DoesNotLock()
    {
        var user = await RegisterResidentAsync();
        var service = CreateService();

        for (var i = 0; i < 4; i++)
            await service.LoginAsync(new LoginRequest("cudan01", "SaiMatKhau@1", UserRole.CU_DAN));

        Assert.Null(user.LockedUntil);
        Assert.Equal(4, user.FailedAttempts);
    }

    [Fact]
    [Trait("TestId", "U4")]
    public async Task Login_FiveWrongPasswords_LocksAccountForFifteenMinutes()
    {
        var user = await RegisterResidentAsync();
        var service = CreateService();

        var before = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
            await service.LoginAsync(new LoginRequest("cudan01", "SaiMatKhau@1", UserRole.CU_DAN));
        var after = DateTimeOffset.UtcNow;

        Assert.NotNull(user.LockedUntil);
        Assert.InRange(user.LockedUntil!.Value, before.AddMinutes(15), after.AddMinutes(15));
    }

    [Fact]
    [Trait("TestId", "U4")]
    public async Task Login_WhileLocked_ReturnsLocked_EvenWithCorrectPassword()
    {
        await RegisterResidentAsync();
        var service = CreateService();

        for (var i = 0; i < 5; i++)
            await service.LoginAsync(new LoginRequest("cudan01", "SaiMatKhau@1", UserRole.CU_DAN));

        var result = await service.LoginAsync(new LoginRequest("cudan01", Password, UserRole.CU_DAN));

        Assert.Equal(LoginStatus.Locked, result.Status);
    }

    // ===== U5: Thuật toán băm được nâng cấp thì tự băm lại =====

    [Fact]
    [Trait("TestId", "U5")]
    public async Task Login_WhenRehashNeeded_UpdatesStoredHash()
    {
        var user = _repo.Seed(new AppUserEntity
        {
            Username = "cudan01",
            PasswordHash = "hash-cu",
            Role = UserRole.CU_DAN,
            Status = UserStatus.Approved
        });
        var stub = new StubPasswordHasher(PasswordVerificationResult.SuccessRehashNeeded, "hash-moi");

        var result = await CreateService(stub).LoginAsync(new LoginRequest("cudan01", Password, UserRole.CU_DAN));

        Assert.Equal(LoginStatus.Success, result.Status);
        Assert.Equal("hash-moi", user.PasswordHash);
        Assert.Equal(1, stub.HashCalls);
    }

    // ===== U8a: Định dạng hash là PBKDF2 V3 của ASP.NET Core Identity =====

    [Fact]
    [Trait("TestId", "U8a")]
    public async Task Register_HashUsesIdentityV3Format()
    {
        var user = await RegisterResidentAsync();

        var bytes = Convert.FromBase64String(user.PasswordHash);

        Assert.Equal(0x01, bytes[0]); // 0x01 = định dạng V3 (PBKDF2 + salt ngẫu nhiên)
    }

    // ===== U8b: Biên độ dài mật khẩu khi đăng ký (8 đến 100 ký tự) =====

    [Theory]
    [Trait("TestId", "U8b")]
    [InlineData(7, RegisterStatus.InvalidPassword)]
    [InlineData(8, RegisterStatus.Success)]
    [InlineData(100, RegisterStatus.Success)]
    [InlineData(101, RegisterStatus.InvalidPassword)]
    public async Task Register_PasswordLengthBoundaries(int length, RegisterStatus expected)
    {
        var result = await CreateService().RegisterAsync(
            new RegisterRequest("cudan01", new string('a', length), "Nguyen Van A", UserRole.CU_DAN));

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    [Trait("TestId", "U8b")]
    public async Task Register_InvalidPassword_DoesNotCreateUser()
    {
        await CreateService().RegisterAsync(
            new RegisterRequest("cudan01", "ngan", "Nguyen Van A", UserRole.CU_DAN));

        Assert.Empty(_repo.Users);
    }

    // ===== U9a: Audit log không chứa mật khẩu =====

    [Fact]
    [Trait("TestId", "U9a")]
    public async Task AuditLogs_NeverContainPasswords()
    {
        const string wrongPassword = "SaiMatKhau@1";
        await RegisterResidentAsync();
        var service = CreateService();

        await service.LoginAsync(new LoginRequest("cudan01", wrongPassword, UserRole.CU_DAN));
        await service.LoginAsync(new LoginRequest("cudan01", Password, UserRole.CU_DAN));
        await service.LoginAsync(new LoginRequest("khongtontai", wrongPassword, UserRole.CU_DAN));

        Assert.NotEmpty(_audit.Entries);
        Assert.False(_audit.AnyEntryContains(Password));
        Assert.False(_audit.AnyEntryContains(wrongPassword));
    }
}
