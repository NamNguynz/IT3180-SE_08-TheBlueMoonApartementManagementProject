using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueMoonApartementManagementConsole.BackEnd.Controllers;

/// <summary>API cho trang Hộ &amp; nhân khẩu (PBI-008): lọc và phân trang nhân khẩu.</summary>
[ApiController]
[Route("api/nhan-khau")]
[Authorize(Policy = "QuanTriHeThong")]
public sealed class ResidentController(IResidentListService residents) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ResidentListRow>>> List(
        [FromQuery] ResidentListQuery query, CancellationToken ct) =>
        Ok(await residents.QueryAsync(query, ct));
}
