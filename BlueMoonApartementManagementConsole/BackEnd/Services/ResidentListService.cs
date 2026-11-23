using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;

namespace BlueMoonApartementManagementConsole.BackEnd.Services;

public interface IResidentListService
{
    Task<PagedResult<ResidentListRow>> QueryAsync(ResidentListQuery query, CancellationToken ct = default);
}

public sealed class ResidentListService(IResidentListRepository repo) : IResidentListService
{
    public Task<PagedResult<ResidentListRow>> QueryAsync(
        ResidentListQuery query, CancellationToken ct = default) =>
        repo.QueryAsync(query with
        {
            Page = Math.Max(1, query.Page),
            PageSize = ListPageSize.Normalize(query.PageSize)
        }, ct);
}
