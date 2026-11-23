using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlueMoonApartementManagementConsole.BackEnd.Services;

public interface IBuildingService
{
    Task<IReadOnlyList<BuildingResponse>> GetAllAsync(CancellationToken ct = default);
    Task<ProfileOperationResult<BuildingResponse>> CreateAsync(
        int actorId, string actorName, CreateBuildingRequest request, CancellationToken ct = default);
}

public sealed class BuildingService(AppDbContext db, IAuditService audit, TimeProvider clock) : IBuildingService
{
    public async Task<IReadOnlyList<BuildingResponse>> GetAllAsync(CancellationToken ct = default) =>
        await db.Buildings.AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => new BuildingResponse(b.Id, b.Name))
            .ToListAsync(ct);

    public async Task<ProfileOperationResult<BuildingResponse>> CreateAsync(
        int actorId, string actorName, CreateBuildingRequest request, CancellationToken ct = default)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
            return new(ProfileStatus.Invalid, Message: "Tên tòa nhà không được để trống.");

        // So khớp không phân biệt hoa thường (chỉ chặn trùng ở tầng ứng dụng, index DB phân biệt)
        var key = name.ToLower();
        if (await db.Buildings.AnyAsync(b => b.Name.ToLower() == key, ct))
            return new(ProfileStatus.Conflict, Message: "Tên tòa nhà đã tồn tại.");

        var entity = new BuildingEntity { Name = name, CreatedAt = clock.GetUtcNow() };
        db.Buildings.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return new(ProfileStatus.Conflict, Message: "Tên tòa nhà đã tồn tại.");
        }

        await audit.LogAsync(AuditAction.CreateBuilding, actorName, actorId,
            entity.Id.ToString(), $"Tạo tòa nhà {entity.Name}", ct);

        return new(ProfileStatus.Success, new BuildingResponse(entity.Id, entity.Name));
    }
}