using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.EntityFrameworkCore;

namespace BlueMoonApartementManagementConsole.BackEnd.Repositories;

public interface IAuditRepository
{
    Task AddAsync(AuditLog log, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<(List<AuditLog> Items, int Total)> QueryAsync(AuditQuery query, CancellationToken ct = default);
}

public class AuditRepository(AppDbContext db) : IAuditRepository
{
    public async Task AddAsync(AuditLog log, CancellationToken ct = default) =>
        await db.AuditLogs.AddAsync(log, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);

    public async Task<(List<AuditLog> Items, int Total)> QueryAsync(AuditQuery q, CancellationToken ct = default)
    {
        var query = db.AuditLogs.AsNoTracking().AsQueryable();

        if (q.From is { } from) query = query.Where(a => a.OccurredAt >= from);
        if (q.To is { } to) query = query.Where(a => a.OccurredAt < to);
        if (q.Action is { } action) query = query.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(q.Username))
            query = query.Where(a => a.Username.Contains(q.Username));

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Skip((q.Page - 1) * q.PageSize)
            .Take(q.PageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}