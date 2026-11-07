using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.BackEnd.Models.DTOs
{
    public record LoginRequest(string Username, string Password, UserRole Role);
    public record LoginResponse(string Username, string FullName, string Role);
    public record RegisterRequest(string Username, string Password, string FullName, UserRole Role);
    public record PendingUserDto(int Id, string Username, string FullName, string Role, DateTimeOffset CreatedAt);
    public record ApiError(string Code, string Message);
    public enum ManageStatus { Success, NotFound, Forbidden, Self, Conflict, Invalid }
    public record ManageResult(
        ManageStatus Status,
        string? TempPassword = null,   // chỉ có khi tạo hoặc cấp lại mật khẩu; hiển thị một lần
        int? Id = null,
        string? Message = null);
    public record CreateAccountRequest(string Username, string FullName, UserRole Role);
    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    public record AccountDto(
        int Id,
        string Username,
        string FullName,
        string Role,
        string Status,
        bool IsActive,
        int? GrantedById,
        DateTimeOffset? LockedUntil,
        bool CanManage);   // để UI ẩn/hiện nút thao tác
}
