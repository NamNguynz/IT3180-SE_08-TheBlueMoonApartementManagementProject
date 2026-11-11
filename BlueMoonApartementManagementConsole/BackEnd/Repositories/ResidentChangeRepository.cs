using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.EntityFrameworkCore;

namespace BlueMoonApartementManagementConsole.BackEnd.Repositories;

public interface IResidentChangeRepository
{
    /// <summary>Lịch sử biến động của một hộ, mới nhất trước.</summary>
    Task<List<ResidentChangeEntity>> GetByHouseholdAsync(int householdId, CancellationToken ct = default);
    Task AddAsync(ResidentChangeEntity change, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed class ResidentChangeRepository(AppDbContext db) : IResidentChangeRepository
{
    public Task<List<ResidentChangeEntity>> GetByHouseholdAsync(int householdId, CancellationToken ct = default) =>
        db.ResidentChanges.AsNoTracking()
            .Include(c => c.RecordedBy)
            .Where(c => c.HouseholdId == householdId)
            .OrderByDescending(c => c.FromDate)
            .ThenByDescending(c => c.Id)
            .ToListAsync(ct);

    public async Task AddAsync(ResidentChangeEntity change, CancellationToken ct = default) =>
        await db.ResidentChanges.AddAsync(change, ct);

    // Dùng chung AppDbContext (scoped) với ApartmentProfileRepository, nên một lần lưu ghi cả nhân khẩu
    // vừa thêm/đổi tình trạng lẫn dòng lịch sử: không có trường hợp đổi tình trạng mà thiếu lịch sử.
    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
