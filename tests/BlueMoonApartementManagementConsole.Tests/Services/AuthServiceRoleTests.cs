using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using BlueMoonApartementManagementConsole.Tests.Fakes;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlueMoonApartementManagementConsole.Tests.Services;

/// <summary>
/// Unit test tài khoản riêng và phân quyền trong AuthService:
/// đăng ký theo vai trò, tên đăng nhập duy nhất, đăng nhập theo trạng thái, duyệt tài khoản.
/// </summary>
public class AuthServiceRoleTests
{
    private readonly FakeAppUserRepo _repo = new();
    private readonly FakeAuditService _audit = new();
    private readonly PasswordHasher<AppUserEntity> _hasher = new();
    private readonly AuthService _service;

    public AuthServiceRoleTests()
    {
        _service = new AuthService(_repo, _hasher, _audit, NullLogger<AuthService>.Instance);
    }

    private Task<RegisterResult> RegisterAsync(string username, UserRole role) =>
        _service.RegisterAsync(new RegisterRequest(username, UserFactory.DefaultPassword, "Nguoi Dung", role));

    // ===== R1: Đăng ký theo vai trò =====

    [Theory]
    [Trait("TestId", "R1")]
    [InlineData(UserRole.CU_DAN, UserStatus.Approved)]
    [InlineData(UserRole.KE_TOAN, UserStatus.PendingApproval)]
    [InlineData(UserRole.BQT, UserStatus.PendingApproval)]
    public async Task Register_StatusDependsOnRole(UserRole role, UserStatus expected)
    {
        var result = await RegisterAsync("nguoidung01", role);

        Assert.Equal(RegisterStatus.Success, result.Status);
        Assert.Equal(role, result.User!.Role);
        Assert.Equal(expected, result.User.Status);
    }

    // ===== R2: Vai trò không hợp lệ =====

    [Fact]
    [Trait("TestId", "R2")]
    public async Task Register_UndefinedRole_IsRejected_AndNoUserCreated()
    {
        var result = await RegisterAsync("nguoidung01", (UserRole)99);

        Assert.Equal(RegisterStatus.InvalidRole, result.Status);
        Assert.Empty(_repo.Users);
    }

    // ===== R3: Tài khoản riêng: tên đăng nhập là duy nhất =====

    [Theory]
    [Trait("TestId", "R3")]
    [InlineData("cudan01")]
    [InlineData("  cudan01  ")]
    public async Task Register_DuplicateUsername_IsRejected(string username)
    {
        _repo.Seed("cudan01", UserRole.CU_DAN);

        var result = await RegisterAsync(username, UserRole.CU_DAN);

        Assert.Equal(RegisterStatus.UsernameTaken, result.Status);
        Assert.Single(_repo.Users);
    }

    [Fact]
    [Trait("TestId", "R3")]
    public async Task Register_DuplicateUsername_IsRejected_EvenWithDifferentRole()
    {
        _repo.Seed("nguoidung01", UserRole.CU_DAN);

        var result = await RegisterAsync("nguoidung01", UserRole.BQT);

        Assert.Equal(RegisterStatus.UsernameTaken, result.Status);
        Assert.Single(_repo.Users);
    }

    // ===== R4: Đăng nhập theo trạng thái tài khoản =====

    [Fact]
    [Trait("TestId", "R4")]
    public async Task Login_PendingAccount_WithCorrectPassword_ReturnsPendingApproval()
    {
        _repo.Seed("ketoan01", UserRole.KE_TOAN, UserStatus.PendingApproval, hasher: _hasher);

        var result = await _service.LoginAsync(new LoginRequest("ketoan01", UserFactory.DefaultPassword, UserRole.KE_TOAN));

        Assert.Equal(LoginStatus.PendingApproval, result.Status);
        Assert.Null(result.User);
    }

    [Fact]
    [Trait("TestId", "R4")]
    public async Task Login_DisabledAccount_WithCorrectPassword_ReturnsInvalidCredentials()
    {
        _repo.Seed("cudan01", UserRole.CU_DAN, isActive: false, hasher: _hasher);

        var result = await _service.LoginAsync(new LoginRequest("cudan01", UserFactory.DefaultPassword, UserRole.CU_DAN));

        Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        Assert.Null(result.User);
    }

    // ===== R5: Duyệt tài khoản thành công =====

    [Fact]
    [Trait("TestId", "R5")]
    public async Task Approve_ByActiveBqt_ApprovesAndRecordsApprover()
    {
        var admin = _repo.Seed("admin", UserRole.BQT);
        var pending = _repo.Seed("ketoan01", UserRole.KE_TOAN, UserStatus.PendingApproval);

        var before = DateTimeOffset.UtcNow;
        var status = await _service.ApproveAsync(admin.Id, pending.Id);

        Assert.Equal(ApproveStatus.Success, status);
        Assert.Equal(UserStatus.Approved, pending.Status);
        Assert.Equal(admin.Id, pending.ApprovedById);
        Assert.Equal(admin.Id, pending.GrantedById);
        Assert.NotNull(pending.ApprovedAt);
        Assert.True(pending.ApprovedAt >= before);

        var log = Assert.Single(_audit.Entries, e => e.Action == AuditAction.ApproveAccount);
        Assert.Equal(admin.Id, log.UserId);
        Assert.Equal(pending.Id.ToString(), log.Target);
    }

    // ===== R6: Duyệt tài khoản bị từ chối =====

    [Theory]
    [Trait("TestId", "R6")]
    [InlineData(UserRole.KE_TOAN, UserStatus.Approved, true)]
    [InlineData(UserRole.CU_DAN, UserStatus.Approved, true)]
    [InlineData(UserRole.BQT, UserStatus.Approved, false)]          // BQT bị vô hiệu hóa
    [InlineData(UserRole.BQT, UserStatus.PendingApproval, true)]    // BQT chưa được duyệt
    public async Task Approve_ByIneligibleApprover_IsNotAuthorized(UserRole role, UserStatus status, bool isActive)
    {
        var approver = _repo.Seed("nguoiduyet", role, status, isActive);
        var pending = _repo.Seed("ketoan01", UserRole.KE_TOAN, UserStatus.PendingApproval);

        var result = await _service.ApproveAsync(approver.Id, pending.Id);

        Assert.Equal(ApproveStatus.NotAuthorized, result);
        Assert.Equal(UserStatus.PendingApproval, pending.Status);
        Assert.DoesNotContain(_audit.Entries, e => e.Action == AuditAction.ApproveAccount);
    }

    [Fact]
    [Trait("TestId", "R6")]
    public async Task Approve_ByNonExistentApprover_IsNotAuthorized()
    {
        var pending = _repo.Seed("ketoan01", UserRole.KE_TOAN, UserStatus.PendingApproval);

        var result = await _service.ApproveAsync(999, pending.Id);

        Assert.Equal(ApproveStatus.NotAuthorized, result);
        Assert.Equal(UserStatus.PendingApproval, pending.Status);
    }

    [Fact]
    [Trait("TestId", "R6")]
    public async Task Approve_Self_IsNotAuthorized()
    {
        var admin = _repo.Seed("admin", UserRole.BQT);

        var result = await _service.ApproveAsync(admin.Id, admin.Id);

        Assert.Equal(ApproveStatus.NotAuthorized, result);
    }

    [Fact]
    [Trait("TestId", "R6")]
    public async Task Approve_NonExistentTarget_ReturnsNotFound()
    {
        var admin = _repo.Seed("admin", UserRole.BQT);

        var result = await _service.ApproveAsync(admin.Id, 999);

        Assert.Equal(ApproveStatus.NotFound, result);
    }

    [Fact]
    [Trait("TestId", "R6")]
    public async Task Approve_AlreadyApprovedTarget_ReturnsNotPending()
    {
        var admin = _repo.Seed("admin", UserRole.BQT);
        var approved = _repo.Seed("ketoan01", UserRole.KE_TOAN);

        var result = await _service.ApproveAsync(admin.Id, approved.Id);

        Assert.Equal(ApproveStatus.NotPending, result);
        Assert.DoesNotContain(_audit.Entries, e => e.Action == AuditAction.ApproveAccount);
    }
}
