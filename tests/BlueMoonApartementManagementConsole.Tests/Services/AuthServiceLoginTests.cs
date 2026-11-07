using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using BlueMoonApartementManagementConsole.Tests.Fakes;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlueMoonApartementManagementConsole.Tests.Services;

/// <summary>
/// Unit test luồng đăng nhập trong AuthService: chuẩn hóa tên đăng nhập, không lộ trạng thái tài khoản,
/// đặt lại bộ đếm sai, hết hạn khóa, audit log.
/// Các trường hợp cơ bản (đúng/sai mật khẩu, sai tư cách, khóa sau 5 lần) đã có ở AuthServicePasswordTests.
/// </summary>
public class AuthServiceLoginTests
{
    private const string WrongPassword = "SaiMatKhau@1";

    private readonly FakeAppUserRepo _repo = new();
    private readonly FakeAuditService _audit = new();
    private readonly PasswordHasher<AppUserEntity> _hasher = new();
    private readonly AuthService _service;

    public AuthServiceLoginTests()
    {
        _service = new AuthService(_repo, _hasher, _audit, NullLogger<AuthService>.Instance);
    }

    private AppUserEntity SeedResident(UserStatus status = UserStatus.Approved, bool isActive = true) =>
        _repo.Seed("cudan01", UserRole.CU_DAN, status, isActive, hasher: _hasher);

    private Task<LoginResult> LoginAsync(string username, string password, UserRole role = UserRole.CU_DAN) =>
        _service.LoginAsync(new LoginRequest(username, password, role));

    // ===== L1, L2: Chuẩn hóa tên đăng nhập =====

    [Fact]
    [Trait("TestId", "L1")]
    public async Task Login_UsernameWithSurroundingSpaces_Succeeds()
    {
        SeedResident();

        var result = await LoginAsync("  cudan01  ", UserFactory.DefaultPassword);

        Assert.Equal(LoginStatus.Success, result.Status);
    }

    [Fact]
    [Trait("TestId", "L2")]
    public async Task Login_UsernameWithDifferentCase_IsRejected()
    {
        // Ghi nhận hành vi hiện tại: so sánh phân biệt hoa/thường, giống mặc định của PostgreSQL (G4)
        SeedResident();

        var result = await LoginAsync("CUDAN01", UserFactory.DefaultPassword);

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
    }

    // ===== L3: Tài khoản không tồn tại =====

    [Fact]
    [Trait("TestId", "L3")]
    public async Task Login_NonExistentUser_ReturnsInvalidCredentials_AndLogsAttempt()
    {
        var result = await LoginAsync("khongtontai", UserFactory.DefaultPassword);

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        Assert.Null(result.User);

        var log = Assert.Single(_audit.Entries);
        Assert.Equal(AuditAction.LoginFailed, log.Action);
        Assert.Equal("khongtontai", log.Username);
        Assert.Null(log.UserId);
        Assert.Empty(_repo.Users);
    }

    // ===== L4: Không lộ trạng thái "chờ duyệt" khi sai mật khẩu =====

    [Fact]
    [Trait("TestId", "L4")]
    public async Task Login_PendingAccount_WithWrongPassword_ReturnsInvalidCredentials_NotPending()
    {
        var user = _repo.Seed("ketoan01", UserRole.KE_TOAN, UserStatus.PendingApproval, hasher: _hasher);

        var result = await LoginAsync("ketoan01", WrongPassword, UserRole.KE_TOAN);

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        Assert.Equal(1, user.FailedAttempts);
    }

    // ===== L5: Đăng nhập đúng đặt lại bộ đếm sai =====

    [Fact]
    [Trait("TestId", "L5")]
    public async Task Login_SuccessAfterFailures_ResetsFailedAttempts_AndSaves()
    {
        var user = SeedResident();
        for (var i = 0; i < 3; i++)
            await LoginAsync("cudan01", WrongPassword);
        Assert.Equal(3, user.FailedAttempts);
        var savesBefore = _repo.SaveChangesCount;

        var result = await LoginAsync("cudan01", UserFactory.DefaultPassword);

        Assert.Equal(LoginStatus.Success, result.Status);
        Assert.Equal(0, user.FailedAttempts);
        Assert.Null(user.LockedUntil);
        Assert.True(_repo.SaveChangesCount > savesBefore);
    }

    // ===== L6: Hết thời gian khóa thì đăng nhập lại được =====

    [Fact]
    [Trait("TestId", "L6")]
    public async Task Login_AfterLockExpired_WithCorrectPassword_Succeeds_AndClearsLock()
    {
        var user = SeedResident();
        user.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(-1);

        var result = await LoginAsync("cudan01", UserFactory.DefaultPassword);

        Assert.Equal(LoginStatus.Success, result.Status);
        Assert.Null(user.LockedUntil);
    }

    // ===== L7: Trong lúc khóa =====

    [Fact]
    [Trait("TestId", "L7")]
    public async Task Login_WhileLocked_WithWrongPassword_ReturnsLocked_WithoutCountingOrExtending()
    {
        var user = SeedResident();
        var lockedUntil = DateTimeOffset.UtcNow.AddMinutes(10);
        user.LockedUntil = lockedUntil;

        var result = await LoginAsync("cudan01", WrongPassword);

        Assert.Equal(LoginStatus.Locked, result.Status);
        Assert.Equal(0, user.FailedAttempts);
        Assert.Equal(lockedUntil, user.LockedUntil);

        var log = Assert.Single(_audit.Entries);
        Assert.Equal(AuditAction.LoginLocked, log.Action);
        Assert.Equal(user.Id, log.UserId);
    }

    // ===== L8: Sau khi hết khóa, bộ đếm bắt đầu lại từ đầu =====

    [Fact]
    [Trait("TestId", "L8")]
    public async Task Login_AfterLockExpired_OneMoreFailure_DoesNotRelockImmediately()
    {
        var user = SeedResident();
        for (var i = 0; i < 5; i++)
            await LoginAsync("cudan01", WrongPassword);
        Assert.NotNull(user.LockedUntil);

        user.LockedUntil = DateTimeOffset.UtcNow.AddSeconds(-1); // giả lập đã hết 15 phút

        var failed = await LoginAsync("cudan01", WrongPassword);
        Assert.Equal(LoginStatus.InvalidCredentials, failed.Status);
        Assert.Equal(1, user.FailedAttempts);

        var success = await LoginAsync("cudan01", UserFactory.DefaultPassword);
        Assert.Equal(LoginStatus.Success, success.Status);
    }

    // ===== L9: Audit log khi đăng nhập thành công =====

    [Fact]
    [Trait("TestId", "L9")]
    public async Task Login_Success_LogsLoginSuccess_WithUserIdAndTarget()
    {
        var user = SeedResident();

        await LoginAsync("cudan01", UserFactory.DefaultPassword);

        var log = Assert.Single(_audit.Entries);
        Assert.Equal(AuditAction.LoginSuccess, log.Action);
        Assert.Equal("cudan01", log.Username);
        Assert.Equal(user.Id, log.UserId);
        Assert.Equal(user.Id.ToString(), log.Target);
    }

    // ===== L10: Tài khoản bị vô hiệu hóa =====

    [Fact]
    [Trait("TestId", "L10")]
    public async Task Login_DisabledAccount_WithWrongPassword_DoesNotCountFailure()
    {
        var user = SeedResident(isActive: false);

        var result = await LoginAsync("cudan01", WrongPassword);

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        Assert.Equal(0, user.FailedAttempts);
        Assert.Null(user.LockedUntil);
    }
}
