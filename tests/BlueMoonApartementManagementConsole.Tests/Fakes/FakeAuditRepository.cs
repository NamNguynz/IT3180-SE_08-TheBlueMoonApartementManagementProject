using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;

namespace BlueMoonApartementManagementConsole.Tests.Fakes;

/// <summary>
/// Thay IAuditRepository để unit test AuditService mà không cần database.
/// Ghi lại log được thêm, số lần SaveChanges và truy vấn nhận được. Kết quả truy vấn do test đặt sẵn.
/// Không tự lọc hay phân trang: phần đó nằm trong AuditRepository, được kiểm bằng test tích hợp.
/// </summary>
public class FakeAuditRepository : IAuditRepository
{
    public List<AuditLog> Added { get; } = new();
    public int SaveChangesCount { get; private set; }

    /// <summary>Truy vấn cuối cùng mà AuditService gửi xuống (sau khi đã giới hạn Page/PageSize).</summary>
    public AuditQuery? LastQuery { get; private set; }

    public List<AuditLog> ItemsToReturn { get; set; } = new();
    public int TotalToReturn { get; set; }

    public Task AddAsync(AuditLog log, CancellationToken ct = default)
    {
        Added.Add(log);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        SaveChangesCount++;
        return Task.CompletedTask;
    }

    public Task<(List<AuditLog> Items, int Total)> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        LastQuery = query;
        return Task.FromResult((ItemsToReturn, TotalToReturn));
    }
}
