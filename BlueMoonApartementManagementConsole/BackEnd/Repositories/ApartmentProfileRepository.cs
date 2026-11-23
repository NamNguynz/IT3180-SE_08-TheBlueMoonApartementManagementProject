using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Security;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.EntityFrameworkCore;

namespace BlueMoonApartementManagementConsole.BackEnd.Repositories;

public interface IApartmentProfileRepository
{
    Task<List<ApartmentEntity>> GetApartmentsAsync(CancellationToken ct = default);
    Task<PagedResult<ApartmentListRow>> QueryApartmentsAsync(ApartmentListQuery query, CancellationToken ct = default);
    Task<ApartmentEntity?> GetApartmentByIdAsync(int id, bool tracking, CancellationToken ct = default);
    Task<BuildingEntity?> GetBuildingAsync(int id, CancellationToken ct = default);
    Task<HouseholdEntity?> GetHouseholdByApartmentIdAsync(int apartmentId, CancellationToken ct = default);
    Task<bool> HasActiveResidentsAsync(int apartmentId, CancellationToken ct = default);
    Task<HouseholdEntity?> GetHouseholdByIdAsync(int id, CancellationToken ct = default);
    Task<bool> ApartmentCodeExistsAsync(string code, int? exceptId = null, CancellationToken ct = default);
    Task<bool> HouseholdNumberExistsAsync(string number, int? exceptId = null, CancellationToken ct = default);
    Task<bool> ResidentIdentityExistsAsync(string identityNumber, int? exceptId = null, CancellationToken ct = default);
    Task AddApartmentAsync(ApartmentEntity apartment, CancellationToken ct = default);
    Task AddHouseholdAsync(HouseholdEntity household, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed class ApartmentProfileRepository(AppDbContext db, IPersonalDataEncryptor personalData) : IApartmentProfileRepository
{
    public Task<List<ApartmentEntity>> GetApartmentsAsync(CancellationToken ct = default) =>
        ApartmentProfiles().AsNoTracking()
            .OrderBy(a => a.Building.Name)
            .ThenBy(a => a.Floor)
            .ThenBy(a => a.Code)
            .ToListAsync(ct);

    /// <summary>
    /// Tra cứu có lọc và phân trang cho trang Danh sách căn hộ. Đếm và lấy trang trên cùng một
    /// IQueryable đã lọc để tổng số bản ghi luôn khớp với bộ lọc hiện tại.
    /// </summary>
    public async Task<PagedResult<ApartmentListRow>> QueryApartmentsAsync(
        ApartmentListQuery query, CancellationToken ct = default)
    {
        var filtered = ApartmentProfiles().AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Code))
            filtered = filtered.Where(a => EF.Functions.ILike(a.Code, $"%{query.Code.Trim()}%"));
        if (query.BuildingId.HasValue)
            filtered = filtered.Where(a => a.BuildingId == query.BuildingId.Value);
        if (query.AreaFrom.HasValue)
            filtered = filtered.Where(a => a.Area >= query.AreaFrom.Value);
        if (query.AreaTo.HasValue)
            filtered = filtered.Where(a => a.Area <= query.AreaTo.Value);
        if (query.Floor.HasValue)
            filtered = filtered.Where(a => a.Floor == query.Floor.Value);
        if (query.Status.HasValue)
            filtered = filtered.Where(a => a.Status == query.Status.Value);

        var total = await filtered.CountAsync(ct);
        var items = await filtered
            .OrderBy(a => a.Building.Name)
            .ThenBy(a => a.Floor)
            .ThenBy(a => a.Code)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(a => new ApartmentListRow(
                a.Id,
                a.Code,
                a.Building.Name,
                a.Household == null
                    ? 0
                    : a.Household.Residents.Count(r => r.ResidenceStatus != ResidenceStatus.DaChuyenDi),
                a.Area,
                a.Floor,
                a.Status))
            .ToListAsync(ct);

        return new PagedResult<ApartmentListRow>(items, total, query.Page, query.PageSize);
    }

    public Task<ApartmentEntity?> GetApartmentByIdAsync(int id, bool tracking, CancellationToken ct = default)
    {
        var query = ApartmentProfiles();
        if (!tracking)
            query = query.AsNoTracking();
        return query.FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    // Tra cứu có tracking: khi gán cho căn hộ, EF tự cập nhật BuildingId lúc lưu.
    public Task<BuildingEntity?> GetBuildingAsync(int id, CancellationToken ct = default) =>
        db.Buildings.FirstOrDefaultAsync(b => b.Id == id, ct);

    public Task<HouseholdEntity?> GetHouseholdByApartmentIdAsync(int apartmentId, CancellationToken ct = default) =>
        HouseholdProfiles().FirstOrDefaultAsync(h => h.ApartmentId == apartmentId, ct);

    /// <summary>Có ít nhất một nhân khẩu chưa chuyển đi trong căn hộ này không.</summary>
    public Task<bool> HasActiveResidentsAsync(int apartmentId, CancellationToken ct = default) =>
        db.Residents.AnyAsync(r => r.Household.ApartmentId == apartmentId &&
                                   r.ResidenceStatus != ResidenceStatus.DaChuyenDi, ct);

    public Task<HouseholdEntity?> GetHouseholdByIdAsync(int id, CancellationToken ct = default) =>
        HouseholdProfiles().FirstOrDefaultAsync(h => h.Id == id, ct);

    public Task<bool> ApartmentCodeExistsAsync(string code, int? exceptId = null, CancellationToken ct = default) =>
        db.Apartments.AnyAsync(a => a.Code == code && (!exceptId.HasValue || a.Id != exceptId.Value), ct);

    public Task<bool> HouseholdNumberExistsAsync(string number, int? exceptId = null, CancellationToken ct = default) =>
        db.Households.AnyAsync(h => h.HouseholdNumber == number && (!exceptId.HasValue || h.Id != exceptId.Value), ct);

    public Task<bool> ResidentIdentityExistsAsync(string identityNumber, int? exceptId = null, CancellationToken ct = default)
    {
        // Cột IdentityNumber lưu bản mã (mỗi lần một khác), nên so sánh theo hash (PBI-030)
        var hash = personalData.HashIdentity(identityNumber);
        return db.Residents.AnyAsync(r => r.IdentityNumberHash == hash && (!exceptId.HasValue || r.Id != exceptId.Value), ct);
    }

    public async Task AddApartmentAsync(ApartmentEntity apartment, CancellationToken ct = default) =>
        await db.Apartments.AddAsync(apartment, ct);

    public async Task AddHouseholdAsync(HouseholdEntity household, CancellationToken ct = default) =>
        await db.Households.AddAsync(household, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    private IQueryable<ApartmentEntity> ApartmentProfiles() =>
        db.Apartments
            .Include(a => a.Building)
            .Include(a => a.Household).ThenInclude(h => h!.Residents);

    private IQueryable<HouseholdEntity> HouseholdProfiles() =>
        db.Households.Include(h => h.Residents);
}