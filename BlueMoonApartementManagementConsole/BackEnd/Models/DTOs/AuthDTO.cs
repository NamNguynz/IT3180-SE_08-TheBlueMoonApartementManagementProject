using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.BackEnd.Models.DTOs
{
    public record LoginRequest(string Username, string Password, UserRole Role);
    public record LoginResponse(string Username, string FullName, string Role);
    public record RegisterRequest(string Username, string Password, string FullName, UserRole Role);
    public record PendingUserDto(int Id, string Username, string FullName, string Role, DateTimeOffset CreatedAt);
    public record ApiError(string Code, string Message);
}
