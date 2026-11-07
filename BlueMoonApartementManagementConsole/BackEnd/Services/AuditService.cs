using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;
using Microsoft.AspNetCore.Identity;

namespace BlueMoonApartementManagementConsole.BackEnd.Services;

public interface IAuditService
{
    Task LogAsync(AuditAction action, string username, int? userId = null, string? target = null, string? detail = null, CancellationToken ct = default);

    Task<PagedResult<AuditLogDto>> QueryAsync(AuditQuery query, CancellationToken ct = default);
}

public class AuditService(IAuditRepository repo) : IAuditService
{
    public async Task LogAsync(AuditAction action, string username, int? userId = null, string? target = null, string? detail = null, CancellationToken ct = default)
    {
        var name = (username ?? string.Empty).Trim();

        await repo.AddAsync(new AuditLog
        {
            OccurredAt = DateTimeOffset.UtcNow,
            UserId = userId,
            Username = name.Length > 50 ? name[..50] : name,
            Action = action,
            Target = target,
            Detail = detail
        }, ct);

        await repo.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<AuditLogDto>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 10, 100);

        var (items, total) = await repo.QueryAsync(query with { Page = page, PageSize = size }, ct);

        var dtos = items.Select(a => new AuditLogDto(a.Id, a.OccurredAt, a.Username, a.Action.ToString(), a.Target, a.Detail)).ToList();

        return new PagedResult<AuditLogDto>(dtos, total, page, size);
    }
}
