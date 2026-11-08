using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.EntityFrameworkCore;

namespace BlueMoonApartementManagementConsole.BackEnd.Repositories;

public interface IFeeItemRepository
{
    Task<List<FeeItemEntity>> GetAllAsync(CancellationToken ct = default);
    Task<FeeItemEntity?> GetByIdAsync(int id, bool tracking, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, int? exceptId = null, CancellationToken ct = default);
    Task AddAsync(FeeItemEntity entity, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed class FeeItemRepository(AppDbContext db) : IFeeItemRepository
{
    public Task<List<FeeItemEntity>> GetAllAsync(CancellationToken ct = default) =>
        db.FeeItems.AsNoTracking()
            .Include(f => f.Rates)
            .OrderBy(f => f.FeeType)
            .ThenBy(f => f.Code)
            .ToListAsync(ct);

    public Task<FeeItemEntity?> GetByIdAsync(int id, bool tracking, CancellationToken ct = default)
    {
        IQueryable<FeeItemEntity> query = db.FeeItems.Include(f => f.Rates);
        if (!tracking)
            query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(f => f.Id == id, ct);
    }

    public Task<bool> CodeExistsAsync(string code, int? exceptId = null, CancellationToken ct = default) =>
        db.FeeItems.AnyAsync(f => f.Code == code && (!exceptId.HasValue || f.Id != exceptId.Value), ct);

    public async Task AddAsync(FeeItemEntity entity, CancellationToken ct = default)
    {
        await db.FeeItems.AddAsync(entity, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
