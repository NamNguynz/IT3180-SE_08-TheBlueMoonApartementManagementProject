using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using BlueMoonApartementManagementConsole.Tests.Fakes;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlueMoonApartementManagementConsole.Tests.Services;

/// <summary>
/// Unit test nội dung log của các thao tác chưa được kiểm ở task trước: đăng ký, tạo tài khoản, mở khóa, cấp lại mật khẩu.
/// Đã kiểm ở chỗ khác: xác nhận (R5), vô hiệu hóa/kích hoạt (R15), đăng nhập (L3, L7, L9), đổi mật khẩu (P2),
/// không log nào chứa mật khẩu (U9). Đăng xuất nằm trong Program.cs nên được kiểm ở test tích hợp Q8.
/// </summary>
public class AuditCoverageTests
{
    private readonly FakeAppUserRepo _repo = new();
    private readonly FakeAuditService _audit = new();
    private readonly PasswordHasher<AppUserEntity> _hasher = new();
    private readonly AuthService _auth;
    private readonly AccountManagementService _accounts;
    private readonly AppUserEntity _admin;

    public AuditCoverageTests()
    {
        _auth = new AuthService(_repo, _hasher, _audit, NullLogger<AuthService>.Instance);
        _accounts = new AccountManagementService(_repo, _hasher, _audit);
        _admin = _repo.Seed("admin", UserRole.BQT);
    }

    // ===== N5: Đăng ký =====

    [Theory]
    [Trait("TestId", "N5")]
    [InlineData(UserRole.CU_DAN, "Vai trò CU_DAN, trạng thái Approved")]
    [InlineData(UserRole.KE_TOAN, "Vai trò KE_TOAN, trạng thái PendingApproval")]
    [InlineData(UserRole.BQT, "Vai trò BQT, trạng thái PendingApproval")]
    public async Task Register_LogsOneRegisterEntry_ForTheNewUser(UserRole role, string expectedDetail)
    {
        var result = await _auth.RegisterAsync(new RegisterRequest("  nguoimoi  ", "MatKhau@123", "Nguyen Van A", role));
        Assert.Equal(RegisterStatus.Success, result.Status);
        var user = result.User!;

        var log = Assert.Single(_audit.Entries);
        Assert.Equal(AuditAction.Register, log.Action);
        Assert.Equal("nguoimoi", log.Username);     // người thực hiện là chính người đăng ký, tên đã cắt khoảng trắng
        Assert.Equal(user.Id, log.UserId);
        Assert.Equal(user.Id.ToString(), log.Target);
        Assert.Equal(expectedDetail, log.Detail);
    }

    // ===== N5: Tạo tài khoản =====

    [Fact]
    [Trait("TestId", "N5")]
    public async Task CreateAccount_LogsOneEntry_ByAdmin_TargetIsNewUser()
    {
        var result = await _accounts.CreateAsync(_admin.Id, new CreateAccountRequest("ketoan01", "Tran Thi B", UserRole.KE_TOAN));
        Assert.Equal(ManageStatus.Success, result.Status);

        var log = Assert.Single(_audit.Entries);
        Assert.Equal(AuditAction.CreateAccount, log.Action);
        Assert.Equal("admin", log.Username);
        Assert.Equal(_admin.Id, log.UserId);
        Assert.Equal(result.Id.ToString(), log.Target);
        Assert.Equal("Tạo tài khoản ketoan01, vai trò KE_TOAN", log.Detail);
        Assert.False(_audit.AnyEntryContains(result.TempPassword!));
    }

    // ===== N5: Mở khóa =====

    [Fact]
    [Trait("TestId", "N5")]
    public async Task Unlock_LogsOneEntry_ByAdmin_TargetIsLockedUser()
    {
        var resident = _repo.Seed("cudan01", UserRole.CU_DAN);
        resident.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(10);

        var result = await _accounts.UnlockAsync(_admin.Id, resident.Id);
        Assert.Equal(ManageStatus.Success, result.Status);

        var log = Assert.Single(_audit.Entries);
        Assert.Equal(AuditAction.UnlockAccount, log.Action);
        Assert.Equal("admin", log.Username);
        Assert.Equal(_admin.Id, log.UserId);
        Assert.Equal(resident.Id.ToString(), log.Target);
        Assert.Equal("Mở khóa tài khoản cudan01", log.Detail);
    }

    // ===== N5: Cấp lại mật khẩu =====

    [Fact]
    [Trait("TestId", "N5")]
    public async Task ResetPassword_LogsOneEntry_ByAdmin_WithoutTempPassword()
    {
        var resident = _repo.Seed("cudan01", UserRole.CU_DAN);

        var result = await _accounts.ResetPasswordAsync(_admin.Id, resident.Id);
        Assert.Equal(ManageStatus.Success, result.Status);

        var log = Assert.Single(_audit.Entries);
        Assert.Equal(AuditAction.ResetPassword, log.Action);
        Assert.Equal("admin", log.Username);
        Assert.Equal(_admin.Id, log.UserId);
        Assert.Equal(resident.Id.ToString(), log.Target);
        Assert.Equal("Cấp lại mật khẩu cho cudan01", log.Detail);
        Assert.False(_audit.AnyEntryContains(result.TempPassword!));
    }

    // ===== N5: Thao tác bị từ chối thì không ghi log =====

    [Fact]
    [Trait("TestId", "N5")]
    public async Task RejectedOperations_WriteNoLog()
    {
        var resident = _repo.Seed("cudan01", UserRole.CU_DAN);
        var accountant = _repo.Seed("ketoan01", UserRole.KE_TOAN, grantedById: _admin.Id);

        Assert.Equal(RegisterStatus.UsernameTaken,
            (await _auth.RegisterAsync(new RegisterRequest("cudan01", "MatKhau@123", "Trung Ten", UserRole.CU_DAN))).Status);
        Assert.Equal(ManageStatus.Forbidden,
            (await _accounts.CreateAsync(accountant.Id, new CreateAccountRequest("moi01", "Moi", UserRole.CU_DAN))).Status);
        Assert.Equal(ManageStatus.Conflict,
            (await _accounts.CreateAsync(_admin.Id, new CreateAccountRequest("cudan01", "Trung Ten", UserRole.CU_DAN))).Status);
        Assert.Equal(ManageStatus.Self, (await _accounts.UnlockAsync(_admin.Id, _admin.Id)).Status);
        Assert.Equal(ManageStatus.Forbidden, (await _accounts.ResetPasswordAsync(accountant.Id, resident.Id)).Status);
        Assert.Equal(ManageStatus.NotFound, (await _accounts.ResetPasswordAsync(_admin.Id, 999)).Status);

        Assert.Empty(_audit.Entries);
    }
}
