using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
namespace BlueMoonApartementManagementConsole.BackEnd.Models.DTOs
{
    public record AuditQuery(
        DateTimeOffset? From,
        DateTimeOffset? To,          // exclusive
        AuditAction? Action,
        string? Username,
        int Page = 1,
        int PageSize = 20);

    public record AuditLogDto(
        long Id,
        DateTimeOffset OccurredAt,
        string Username,
        string Action,
        string? Target,
        string? Detail);

    public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
}
