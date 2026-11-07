using System.Security.Cryptography;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;
using Microsoft.AspNetCore.Identity;

namespace BlueMoonApartementManagementConsole.BackEnd.Services;

public interface IAccountManagementService
{
    Task<IReadOnlyList<AccountDto>> GetAccountsAsync(int actorId, CancellationToken ct = default);
    Task<ManageResult> CreateAsync(int actorId, CreateAccountRequest request, CancellationToken ct = default);
    Task<ManageResult> SetActiveAsync(int actorId, int targetId, bool active, CancellationToken ct = default);
    Task<ManageResult> UnlockAsync(int actorId, int targetId, CancellationToken ct = default);
    Task<ManageResult> ResetPasswordAsync(int actorId, int targetId, CancellationToken ct = default);
    Task<ManageResult> ChangeOwnPasswordAsync(int userId, ChangePasswordRequest request, CancellationToken ct = default);
}

public class AccountManagementService(
    IAppUserRepo users,
    IPasswordHasher<AppUserEntity> hasher,
    IAuditService audit) : IAccountManagementService
{
    private const int MaxDepth = 100;

    public async Task<IReadOnlyList<AccountDto>> GetAccountsAsync(int actorId, CancellationToken ct = default)
    {
        var all = await users.GetAllAsync(ct);
        var byId = all.ToDictionary(u => u.Id);

        return all.Select(u => new AccountDto(
                u.Id, u.Username, u.FullName, u.Role.ToString(), u.Status.ToString(),
                u.IsActive, u.GrantedById, u.LockedUntil,
                CanManage: CheckManage(byId, actorId, u.Id, out _) == ManageStatus.Success)).ToList();
    }

    public async Task<ManageResult> CreateAsync(int actorId, CreateAccountRequest req, CancellationToken ct = default)
    {
        var byId = (await users.GetAllAsync(ct)).ToDictionary(u => u.Id);
        if (!byId.TryGetValue(actorId, out var actor) || !IsActiveBqt(actor))
            return new(ManageStatus.Forbidden);

        var username = req.Username?.Trim() ?? string.Empty;
        var fullName = req.FullName?.Trim() ?? string.Empty;

        if (username.Length is < 3 or > 50)
            return new(ManageStatus.Invalid, Message: "Tài khoản phải từ 3 đến 50 ký tự.");
        if (fullName.Length is 0 or > 100)
            return new(ManageStatus.Invalid, Message: "Tên người dùng phải từ 1 đến 100 ký tự.");
        if (!Enum.IsDefined(req.Role))
            return new(ManageStatus.Invalid, Message: "Vai trò không hợp lệ.");
        if (await users.UsernameExistsAsync(username, ct))
            return new(ManageStatus.Conflict, Message: "Tài khoản này đã tồn tại.");

        // BQT tạo thì không cần duyệt: người tạo là người cấp và người duyệt
        var tempPassword = GenerateTempPassword();
        var user = new AppUserEntity
        {
            Username = username,
            FullName = fullName,
            Role = req.Role,
            Status = UserStatus.Approved,
            IsActive = true,
            GrantedById = actor.Id,
            ApprovedById = actor.Id,
            ApprovedAt = DateTimeOffset.UtcNow,
            MustChangePassword = true
        };
        user.PasswordHash = hasher.HashPassword(user, tempPassword);

        await users.AddAsync(user, ct);
        await users.SaveChangesAsync(ct);

        await audit.LogAsync(AuditAction.CreateAccount, actor.Username, actor.Id, user.Id.ToString(), $"Tạo tài khoản {user.Username}, vai trò {user.Role}", ct);

        return new(ManageStatus.Success, TempPassword: tempPassword, Id: user.Id);
    }

    public async Task<ManageResult> SetActiveAsync(int actorId, int targetId, bool active, CancellationToken ct = default)
    {
        var (status, byId, target) = await CheckAsync(actorId, targetId, ct);
        if (status != ManageStatus.Success) return new(status);

        if (target!.IsActive == active)
            return new(ManageStatus.Conflict, Message: active ? "Tài khoản đang hoạt động." : "Tài khoản đã bị vô hiệu hóa.");

        target.IsActive = active;
        await users.SaveChangesAsync(ct);

        await audit.LogAsync(active ? AuditAction.EnableAccount : AuditAction.DisableAccount,
            byId[actorId].Username, actorId, target.Id.ToString(), $"Tài khoản {target.Username}", ct);

        return new(ManageStatus.Success);
    }

    public async Task<ManageResult> UnlockAsync(int actorId, int targetId, CancellationToken ct = default)
    {
        var (status, byId, target) = await CheckAsync(actorId, targetId, ct);
        if (status != ManageStatus.Success) return new(status);

        target!.FailedAttempts = 0;
        target.LockedUntil = null;
        await users.SaveChangesAsync(ct);

        await audit.LogAsync(AuditAction.UnlockAccount, byId[actorId].Username, actorId, target.Id.ToString(), $"Mở khóa tài khoản {target.Username}", ct);

        return new(ManageStatus.Success);
    }

    public async Task<ManageResult> ResetPasswordAsync(int actorId, int targetId, CancellationToken ct = default)
    {
        var (status, byId, target) = await CheckAsync(actorId, targetId, ct);
        if (status != ManageStatus.Success) return new(status);

        var tempPassword = GenerateTempPassword();
        target!.PasswordHash = hasher.HashPassword(target, tempPassword);
        target.MustChangePassword = true;
        target.FailedAttempts = 0;
        target.LockedUntil = null;
        await users.SaveChangesAsync(ct);

        await audit.LogAsync(AuditAction.ResetPassword, byId[actorId].Username, actorId, target.Id.ToString(), $"Cấp lại mật khẩu cho {target.Username}", ct);

        return new(ManageStatus.Success, TempPassword: tempPassword);
    }

    public async Task<ManageResult> ChangeOwnPasswordAsync(int userId, ChangePasswordRequest req, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(userId, ct);
        if (user is null) return new(ManageStatus.NotFound);

        if (hasher.VerifyHashedPassword(user, user.PasswordHash, req.CurrentPassword) == PasswordVerificationResult.Failed)
            return new(ManageStatus.Invalid, Message: "Mật khẩu hiện tại không đúng.");

        if (string.IsNullOrEmpty(req.NewPassword) || req.NewPassword.Length is < 8 or > 100)
            return new(ManageStatus.Invalid, Message: "Mật khẩu mới phải từ 8 đến 100 ký tự.");

        if (req.NewPassword == req.CurrentPassword)
            return new(ManageStatus.Invalid, Message: "Mật khẩu mới phải khác mật khẩu hiện tại.");

        user.PasswordHash = hasher.HashPassword(user, req.NewPassword);
        user.MustChangePassword = false;
        user.PasswordChangedAt = DateTimeOffset.UtcNow;
        await users.SaveChangesAsync(ct);

        await audit.LogAsync(AuditAction.ChangePassword, user.Username, user.Id, user.Id.ToString(), null, ct);

        return new(ManageStatus.Success);
    }

    // ===== Phạm vi thao tác =====

    private async Task<(ManageStatus Status, Dictionary<int, AppUserEntity> ById, AppUserEntity? Target)> CheckAsync(
        int actorId, int targetId, CancellationToken ct)
    {
        var byId = (await users.GetAllAsync(ct)).ToDictionary(u => u.Id);
        var status = CheckManage(byId, actorId, targetId, out var target);
        return (status, byId, target);
    }

    private static ManageStatus CheckManage(
        IReadOnlyDictionary<int, AppUserEntity> byId, int actorId, int targetId, out AppUserEntity? target)
    {
        target = null;

        if (!byId.TryGetValue(actorId, out var actor) || !IsActiveBqt(actor))
            return ManageStatus.Forbidden;

        if (!byId.TryGetValue(targetId, out target))
            return ManageStatus.NotFound;

        if (target.Id == actor.Id)
            return ManageStatus.Self;

        // Cư dân: mọi BQT đều thao tác được
        if (target.Role == UserRole.CU_DAN)
            return ManageStatus.Success;

        // BQT và Kế toán: chỉ thao tác trên nhánh do mình cấp, ở mọi độ sâu
        return IsAncestor(actor.Id, target, byId) ? ManageStatus.Success : ManageStatus.Forbidden;
    }

    private static bool IsAncestor(int ancestorId, AppUserEntity node, IReadOnlyDictionary<int, AppUserEntity> byId)
    {
        var current = node.GrantedById;
        var depth = 0;

        while (current is int id && depth++ < MaxDepth)
        {
            if (id == ancestorId) return true;
            if (!byId.TryGetValue(id, out var parent)) return false;
            current = parent.GrantedById;
        }

        return false;
    }

    private static bool IsActiveBqt(AppUserEntity u) =>
        u.Role == UserRole.BQT && u.Status == UserStatus.Approved && u.IsActive;

    private static string GenerateTempPassword(int length = 12)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
        return string.Create(length, chars, (span, set) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = set[RandomNumberGenerator.GetInt32(set.Length)];
        });
    }
}