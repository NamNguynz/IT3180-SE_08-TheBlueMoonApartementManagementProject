using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Services;

namespace BlueMoonApartementManagementConsole.Tests.Fakes;

/// <summary>
/// Thay IAuditService, ghi lại mọi log để test kiểm tra nội dung (vd: không chứa mật khẩu).
/// </summary>
public class FakeAuditService : IAuditService
{
    public record Entry(AuditAction Action, string Username, int? UserId, string? Target, string? Detail);

    public List<Entry> Entries { get; } = new();

    public Task LogAsync(AuditAction action, string username, int? userId = null, string? target = null, string? detail = null, CancellationToken ct = default)
    {
        Entries.Add(new Entry(action, username, userId, target, detail));
        return Task.CompletedTask;
    }

    /// <summary>Kiểm tra có log nào chứa chuỗi bí mật ở bất kỳ trường văn bản nào không.</summary>
    public bool AnyEntryContains(string secret) =>
        Entries.Any(e =>
            (e.Username?.Contains(secret, StringComparison.Ordinal) ?? false) ||
            (e.Target?.Contains(secret, StringComparison.Ordinal) ?? false) ||
            (e.Detail?.Contains(secret, StringComparison.Ordinal) ?? false));

    public Task<PagedResult<AuditLogDto>> QueryAsync(AuditQuery query, CancellationToken ct = default) =>
        throw new NotSupportedException("Không dùng trong unit test băm mật khẩu.");
}
