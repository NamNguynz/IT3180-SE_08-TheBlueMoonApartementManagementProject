using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using BlueMoonApartementManagementConsole.Tests.Fakes;
using Microsoft.AspNetCore.Identity;

namespace BlueMoonApartementManagementConsole.Tests.Services;

/// <summary>
/// Unit test phạm vi quản lý tài khoản theo vai trò và theo cây người cấp (GrantedById).
///
/// Cây dùng chung:
///   admin (BQT gốc)
///   ├── bqt_a (BQT)
///   │   ├── ketoan_a1 (KE_TOAN)        con của bqt_a
///   │   └── bqt_a1 (BQT)               con của bqt_a
///   │       └── ketoan_a11 (KE_TOAN)   cháu của bqt_a
///   └── bqt_b (BQT)
///   cudan_01 (CU_DAN, tự đăng ký, không có người cấp)
/// </summary>
public class AccountScopeTests
{
    private readonly FakeAppUserRepo _repo = new();
    private readonly FakeAuditService _audit = new();
    private readonly AccountManagementService _service;

    private readonly AppUserEntity _admin, _bqtA, _bqtB, _ketoanA1, _bqtA1, _ketoanA11, _cudan;

    public AccountScopeTests()
    {
        _service = new AccountManagementService(_repo, new PasswordHasher<AppUserEntity>(), _audit);

        _admin = _repo.Seed("admin", UserRole.BQT);
        _bqtA = _repo.Seed("bqt_a", UserRole.BQT, grantedById: _admin.Id);
        _bqtB = _repo.Seed("bqt_b", UserRole.BQT, grantedById: _admin.Id);
        _ketoanA1 = _repo.Seed("ketoan_a1", UserRole.KE_TOAN, grantedById: _bqtA.Id);
        _bqtA1 = _repo.Seed("bqt_a1", UserRole.BQT, grantedById: _bqtA.Id);
        _ketoanA11 = _repo.Seed("ketoan_a11", UserRole.KE_TOAN, grantedById: _bqtA1.Id);
        _cudan = _repo.Seed("cudan_01", UserRole.CU_DAN);
    }

    /// <summary>Chạy 4 thao tác trên một tài khoản đích, trả về trạng thái của từng thao tác.</summary>
    private async Task<ManageStatus[]> RunAllTargetedAsync(int actorId, int targetId) =>
    [
        (await _service.SetActiveAsync(actorId, targetId, false)).Status,
        (await _service.SetActiveAsync(actorId, targetId, true)).Status,
        (await _service.UnlockAsync(actorId, targetId)).Status,
        (await _service.ResetPasswordAsync(actorId, targetId)).Status
    ];

    // ===== R7: Người thao tác không phải BQT đang hoạt động và đã duyệt =====

    [Theory]
    [Trait("TestId", "R7")]
    [InlineData(UserRole.KE_TOAN, UserStatus.Approved, true)]
    [InlineData(UserRole.CU_DAN, UserStatus.Approved, true)]
    [InlineData(UserRole.BQT, UserStatus.Approved, false)]          // BQT bị vô hiệu hóa
    [InlineData(UserRole.BQT, UserStatus.PendingApproval, true)]    // BQT chưa được duyệt
    public async Task IneligibleActor_IsForbidden_ForAllOperations(UserRole role, UserStatus status, bool isActive)
    {
        var actor = _repo.Seed("nguoithaotac", role, status, isActive, grantedById: _admin.Id);

        var create = await _service.CreateAsync(actor.Id, new CreateAccountRequest("moi01", "Moi", UserRole.CU_DAN));
        var targeted = await RunAllTargetedAsync(actor.Id, _cudan.Id);

        Assert.Equal(ManageStatus.Forbidden, create.Status);
        Assert.All(targeted, s => Assert.Equal(ManageStatus.Forbidden, s));
        Assert.DoesNotContain(_repo.Users, u => u.Username == "moi01");
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    [Trait("TestId", "R7")]
    public async Task NonExistentActor_IsForbidden_ForAllOperations()
    {
        var create = await _service.CreateAsync(999, new CreateAccountRequest("moi01", "Moi", UserRole.CU_DAN));
        var targeted = await RunAllTargetedAsync(999, _cudan.Id);

        Assert.Equal(ManageStatus.Forbidden, create.Status);
        Assert.All(targeted, s => Assert.Equal(ManageStatus.Forbidden, s));
    }

    // ===== R8: Tài khoản đích không tồn tại / tự thao tác trên mình =====

    [Fact]
    [Trait("TestId", "R8")]
    public async Task NonExistentTarget_ReturnsNotFound()
    {
        var results = await RunAllTargetedAsync(_admin.Id, 999);

        Assert.All(results, s => Assert.Equal(ManageStatus.NotFound, s));
    }

    [Fact]
    [Trait("TestId", "R8")]
    public async Task SelfTarget_ReturnsSelf_AndAccountUnchanged()
    {
        var results = await RunAllTargetedAsync(_bqtA.Id, _bqtA.Id);

        Assert.All(results, s => Assert.Equal(ManageStatus.Self, s));
        Assert.True(_bqtA.IsActive);
        Assert.Equal("hash-gia", _bqtA.PasswordHash);
    }

    // ===== R9: Cư dân: mọi BQT đều thao tác được =====

    [Theory]
    [Trait("TestId", "R9")]
    [InlineData("admin")]
    [InlineData("bqt_a")]
    [InlineData("bqt_b")]
    [InlineData("bqt_a1")]
    public async Task AnyActiveBqt_CanManageResident(string actorName)
    {
        var actor = _repo.Users.Single(u => u.Username == actorName);

        var results = await RunAllTargetedAsync(actor.Id, _cudan.Id);

        Assert.All(results, s => Assert.Equal(ManageStatus.Success, s));
    }

    // ===== R10: Trong nhánh mình cấp (mọi độ sâu) =====

    [Theory]
    [Trait("TestId", "R10")]
    [InlineData("bqt_a", "ketoan_a1")]    // con
    [InlineData("bqt_a", "bqt_a1")]       // con là BQT
    [InlineData("bqt_a", "ketoan_a11")]   // cháu
    [InlineData("admin", "ketoan_a11")]   // chắt
    [InlineData("bqt_a1", "ketoan_a11")]  // con
    public async Task Bqt_CanManageAccountsInOwnBranch(string actorName, string targetName)
    {
        var actor = _repo.Users.Single(u => u.Username == actorName);
        var target = _repo.Users.Single(u => u.Username == targetName);

        var results = await RunAllTargetedAsync(actor.Id, target.Id);

        Assert.All(results, s => Assert.Equal(ManageStatus.Success, s));
    }

    // ===== R11: Ngoài nhánh mình cấp =====

    [Theory]
    [Trait("TestId", "R11")]
    [InlineData("bqt_a", "bqt_b")]        // anh em
    [InlineData("bqt_a", "admin")]        // cấp trên, BQT gốc
    [InlineData("bqt_a1", "bqt_a")]       // cha
    [InlineData("bqt_b", "ketoan_a1")]    // nhánh khác
    [InlineData("bqt_b", "ketoan_a11")]   // nhánh khác, sâu hơn
    public async Task Bqt_CannotManageAccountsOutsideOwnBranch(string actorName, string targetName)
    {
        var actor = _repo.Users.Single(u => u.Username == actorName);
        var target = _repo.Users.Single(u => u.Username == targetName);
        var hashBefore = target.PasswordHash;

        var results = await RunAllTargetedAsync(actor.Id, target.Id);

        Assert.All(results, s => Assert.Equal(ManageStatus.Forbidden, s));
        Assert.True(target.IsActive);
        Assert.Equal(hashBefore, target.PasswordHash);
        Assert.Empty(_audit.Entries);
    }

    // ===== R12: Dữ liệu người cấp bị vòng lặp không làm treo =====

    [Fact(Timeout = 5000)]
    [Trait("TestId", "R12")]
    public async Task CyclicGrantChain_DoesNotHang_AndIsForbidden()
    {
        var x = _repo.Seed("bqt_x", UserRole.BQT);
        var y = _repo.Seed("bqt_y", UserRole.BQT, grantedById: x.Id);
        x.GrantedById = y.Id; // x cấp y, y cấp x

        var result = await _service.ResetPasswordAsync(_admin.Id, x.Id);

        Assert.Equal(ManageStatus.Forbidden, result.Status);
    }

    // ===== R13: Tài khoản do BQT tạo thuộc nhánh của người tạo =====

    [Fact]
    [Trait("TestId", "R13")]
    public async Task Create_ByBqt_PutsNewAccountInCreatorsBranch()
    {
        var created = await _service.CreateAsync(_bqtA.Id, new CreateAccountRequest("ketoan_moi", "Ke Toan Moi", UserRole.KE_TOAN));
        var user = _repo.Users.Single(u => u.Id == created.Id);

        Assert.Equal(ManageStatus.Success, created.Status);
        Assert.Equal(_bqtA.Id, user.GrantedById);
        Assert.Equal(_bqtA.Id, user.ApprovedById);
        Assert.Equal(UserStatus.Approved, user.Status);
        Assert.True(user.IsActive);

        Assert.Equal(ManageStatus.Success, (await _service.ResetPasswordAsync(_bqtA.Id, user.Id)).Status);
        Assert.Equal(ManageStatus.Success, (await _service.ResetPasswordAsync(_admin.Id, user.Id)).Status);
        Assert.Equal(ManageStatus.Forbidden, (await _service.ResetPasswordAsync(_bqtB.Id, user.Id)).Status);
    }

    // ===== R14: Cờ CanManage trong danh sách tài khoản =====

    [Fact]
    [Trait("TestId", "R14")]
    public async Task GetAccounts_CanManageFlag_MatchesScopeRules()
    {
        var accounts = await _service.GetAccountsAsync(_bqtA.Id);
        var canManage = accounts.ToDictionary(a => a.Username, a => a.CanManage);

        Assert.Equal(_repo.Users.Count, accounts.Count);
        Assert.False(canManage["bqt_a"]);       // bản thân
        Assert.True(canManage["ketoan_a1"]);    // con
        Assert.True(canManage["bqt_a1"]);       // con
        Assert.True(canManage["ketoan_a11"]);   // cháu
        Assert.False(canManage["bqt_b"]);       // anh em
        Assert.False(canManage["admin"]);       // cấp trên
        Assert.True(canManage["cudan_01"]);     // cư dân
    }

    [Fact]
    [Trait("TestId", "R14")]
    public async Task GetAccounts_ForNonBqtActor_CanManageNothing()
    {
        var accounts = await _service.GetAccountsAsync(_ketoanA1.Id);

        Assert.All(accounts, a => Assert.False(a.CanManage));
    }

    // ===== R15: Vô hiệu hóa / kích hoạt =====

    [Fact]
    [Trait("TestId", "R15")]
    public async Task DisableThenEnable_TogglesIsActive_AndLogsEachChange()
    {
        var disable = await _service.SetActiveAsync(_bqtA.Id, _ketoanA1.Id, false);
        Assert.Equal(ManageStatus.Success, disable.Status);
        Assert.False(_ketoanA1.IsActive);

        var enable = await _service.SetActiveAsync(_bqtA.Id, _ketoanA1.Id, true);
        Assert.Equal(ManageStatus.Success, enable.Status);
        Assert.True(_ketoanA1.IsActive);

        Assert.Collection(_audit.Entries,
            e => { Assert.Equal(AuditAction.DisableAccount, e.Action); Assert.Equal(_bqtA.Id, e.UserId); Assert.Equal(_ketoanA1.Id.ToString(), e.Target); },
            e => { Assert.Equal(AuditAction.EnableAccount, e.Action); Assert.Equal(_bqtA.Id, e.UserId); Assert.Equal(_ketoanA1.Id.ToString(), e.Target); });
    }

    [Theory]
    [Trait("TestId", "R15")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetActive_ToCurrentState_ReturnsConflict_WithoutLog(bool currentState)
    {
        _ketoanA1.IsActive = currentState;

        var result = await _service.SetActiveAsync(_bqtA.Id, _ketoanA1.Id, currentState);

        Assert.Equal(ManageStatus.Conflict, result.Status);
        Assert.Equal(currentState, _ketoanA1.IsActive);
        Assert.Empty(_audit.Entries);
    }
}
