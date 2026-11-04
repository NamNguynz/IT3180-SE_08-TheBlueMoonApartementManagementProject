using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BlueMoonApartementManagementConsole.BackEnd.Services
{
    public enum LoginStatus { Success, InvalidCredentials, Locked, PendingApproval }
    public enum RegisterStatus 
    {
        Success,
        UsernameTaken,
        InvalidUsername,
        InvalidFullName,
        InvalidPassword,
        InvalidRole
    }
    public enum ApproveStatus { Success, NotAuthorized, NotFound, NotPending }

    public record LoginResult(LoginStatus Status, AppUserEntity? User = null);
    public record RegisterResult(RegisterStatus Status, AppUserEntity? User = null);

    public interface IAuthService
    {
        Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken ct = default);
        Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
        Task<IReadOnlyList<AppUserEntity>> GetPendingAsync(CancellationToken ct = default);
        Task<ApproveStatus> ApproveAsync(int approverId, int userId, CancellationToken ct = default);

        string HashPassword(AppUserEntity user, string password);
    }

    public class AuthService(
        IAppUserRepo users,
        IPasswordHasher<AppUserEntity> hasher,
        ILogger<AuthService> log) : IAuthService
    {
        private const int MaxFailedAttempts = 5;
        private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15); // đề xuất, PO cần xác nhận

        public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
        {
            var user = await users.GetByUsernameAsync(request.Username.Trim(), ct);

            // Không phân biệt "sai tài khoản" và "sai mật khẩu" ra ngoài
            if (user is null || !user.IsActive)
            {
                log.LogWarning("Đăng nhập thất bại: tài khoản không hợp lệ hoặc bị vô hiệu");
                return new(LoginStatus.InvalidCredentials);
            }

            var now = DateTimeOffset.UtcNow;
            if (user.LockedUntil is { } until && until > now)
            {
                log.LogWarning("Đăng nhập bị từ chối: tài khoản {UserId} đang bị khóa", user.Id);
                return new(LoginStatus.Locked);
            }

            var verify = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

            if (verify == PasswordVerificationResult.Failed)
            {
                user.FailedAttempts++;
                if (user.FailedAttempts >= MaxFailedAttempts)
                {
                    user.LockedUntil = now.Add(LockDuration);
                    user.FailedAttempts = 0;
                }
                await users.SaveChangesAsync(ct);
                log.LogWarning("Sai mật khẩu: tài khoản {UserId}, số lần sai {Count}", user.Id, user.FailedAttempts);
                return new(LoginStatus.InvalidCredentials);
            }

            // Thuật toán băm được nâng cấp thì tự băm lại
            if (verify == PasswordVerificationResult.SuccessRehashNeeded)
                user.PasswordHash = hasher.HashPassword(user, request.Password);

            if (user.Role != request.Role)
            {
                log.LogWarning("Đăng nhập sai tư cách: tài khoản {UserId}", user.Id);
                return new(LoginStatus.InvalidCredentials);
            }

            user.FailedAttempts = 0;
            user.LockedUntil = null;
            await users.SaveChangesAsync(ct);

            // Mật khẩu đúng rồi mới báo trạng thái chờ duyệt, tránh lộ thông tin khi chưa xác thực
            if (user.Status == UserStatus.PendingApproval)
                return new(LoginStatus.PendingApproval);

            log.LogInformation("Đăng nhập thành công: tài khoản {UserId}", user.Id);
            return new(LoginStatus.Success, user);
        }

        public string HashPassword(AppUserEntity user, string password) =>
            hasher.HashPassword(user, password);

        public async Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
        {
            var username = request.Username?.Trim() ?? string.Empty;
            var fullName = request.FullName?.Trim() ?? string.Empty;

            if (username.Length is < 3 or > 50)
                return new(RegisterStatus.InvalidUsername);

            if (fullName.Length is 0 or > 100)
                return new(RegisterStatus.InvalidFullName);

            if (string.IsNullOrEmpty(request.Password) || request.Password.Length is < 8 or > 100)
                return new(RegisterStatus.InvalidPassword);

            if (!Enum.IsDefined(request.Role))
                return new(RegisterStatus.InvalidRole);

            if (await users.UsernameExistsAsync(username, ct))
                return new(RegisterStatus.UsernameTaken);

            var user = new AppUserEntity
            {
                Username = username,
                FullName = fullName,
                Role = request.Role,
                // Cư dân được xác nhận ngay; BQT và kế toán chờ BQT khác duyệt
                Status = request.Role == UserRole.CU_DAN
                    ? UserStatus.Approved
                    : UserStatus.PendingApproval
            };
            user.PasswordHash = hasher.HashPassword(user, request.Password);

            try
            {
                await users.AddAsync(user, ct);
                await users.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // Hai yêu cầu cùng tên được gửi gần như đồng thời: unique index chặn ở DB
                log.LogWarning(ex, "Không lưu được tài khoản {Username}", username);
                if (await users.UsernameExistsAsync(username, ct))
                    return new(RegisterStatus.UsernameTaken);
                throw;
            }

            log.LogInformation("Đăng ký tài khoản {UserId}, vai trò {Role}, trạng thái {Status}",
                user.Id, user.Role, user.Status);

            return new(RegisterStatus.Success, user);
        }

        public async Task<IReadOnlyList<AppUserEntity>> GetPendingAsync(CancellationToken ct = default) =>
        await users.GetPendingAsync(ct);

        public async Task<ApproveStatus> ApproveAsync(int approverId, int userId, CancellationToken ct = default)
        {
            // Kiểm tra quyền trên dữ liệu thật, không chỉ dựa vào claim trong cookie
            var approver = await users.GetByIdAsync(approverId, ct);
            if (approver is null || !approver.IsActive
                || approver.Role != UserRole.BQT || approver.Status != UserStatus.Approved)
                return ApproveStatus.NotAuthorized;

            if (approverId == userId)
                return ApproveStatus.NotAuthorized;

            var target = await users.GetByIdAsync(userId, ct);
            if (target is null)
                return ApproveStatus.NotFound;

            if (target.Status != UserStatus.PendingApproval)
                return ApproveStatus.NotPending;

            target.Status = UserStatus.Approved;
            target.ApprovedById = approverId;
            target.ApprovedAt = DateTimeOffset.UtcNow;
            await users.SaveChangesAsync(ct);

            log.LogInformation("Tài khoản {UserId} được xác nhận bởi {ApproverId}", userId, approverId);
            return ApproveStatus.Success;
        }
    }
}
