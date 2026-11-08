using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.EntityFrameworkCore;

namespace BlueMoonApartementManagementConsole.BackEnd.Repositories;

public interface IApartmentProfileRepository
{
    Task<List<ApartmentEntity>> GetApartmentsAsync(CancellationToken ct = default);
    Task<ApartmentEntity?> GetApartmentByIdAsync(int id, bool tracking, CancellationToken ct = default);
    Task<HouseholdEntity?> GetHouseholdByApartmentIdAsync(int apartmentId, CancellationToken ct = default);
    Task<HouseholdEntity?> GetHouseholdByIdAsync(int id, CancellationToken ct = default);
    Task<bool> ApartmentCodeExistsAsync(string code, int? exceptId = null, CancellationToken ct = default);
    Task<bool> HouseholdNumberExistsAsync(string number, int? exceptId = null, CancellationToken ct = default);
    Task<bool> ResidentIdentityExistsAsync(string identityNumber, int? exceptId = null, CancellationToken ct = default);
    Task AddApartmentAsync(ApartmentEntity apartment, CancellationToken ct = default);
    Task AddHouseholdAsync(HouseholdEntity household, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed class ApartmentProfileRepository(AppDbContext db) : IApartmentProfileRepository
{
    public Task<List<ApartmentEntity>> GetApartmentsAsync(CancellationToken ct = default) =>
        ApartmentProfiles().AsNoTracking()
            .OrderBy(a => a.Building)
            .ThenBy(a => a.Floor)
            .ThenBy(a => a.Code)
            .ToListAsync(ct);

    public Task<ApartmentEntity?> GetApartmentByIdAsync(int id, bool tracking, CancellationToken ct = default)
    {
        var query = ApartmentProfiles();
        if (!tracking)
            query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public Task<HouseholdEntity?> GetHouseholdByApartmentIdAsync(int apartmentId, CancellationToken ct = default) =>
        HouseholdProfiles().FirstOrDefaultAsync(h => h.ApartmentId == apartmentId, ct);

    public Task<HouseholdEntity?> GetHouseholdByIdAsync(int id, CancellationToken ct = default) =>
        HouseholdProfiles().FirstOrDefaultAsync(h => h.Id == id, ct);

    public Task<bool> ApartmentCodeExistsAsync(string code, int? exceptId = null, CancellationToken ct = default) =>
        db.Apartments.AnyAsync(a => a.Code == code && (!exceptId.HasValue || a.Id != exceptId.Value), ct);

    public Task<bool> HouseholdNumberExistsAsync(string number, int? exceptId = null, CancellationToken ct = default) =>
        db.Households.AnyAsync(h => h.HouseholdNumber == number && (!exceptId.HasValue || h.Id != exceptId.Value), ct);

    public Task<bool> ResidentIdentityExistsAsync(string identityNumber, int? exceptId = null, CancellationToken ct = default) =>
        db.Residents.AnyAsync(r => r.IdentityNumber == identityNumber && (!exceptId.HasValue || r.Id != exceptId.Value), ct);

    public async Task AddApartmentAsync(ApartmentEntity apartment, CancellationToken ct = default) =>
        await db.Apartments.AddAsync(apartment, ct);

    public async Task AddHouseholdAsync(HouseholdEntity household, CancellationToken ct = default) =>
        await db.Households.AddAsync(household, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    private IQueryable<ApartmentEntity> ApartmentProfiles() =>
        db.Apartments.Include(a => a.Household).ThenInclude(h => h!.Residents);

    private IQueryable<HouseholdEntity> HouseholdProfiles() =>
        db.Households.Include(h => h.Residents);
}
