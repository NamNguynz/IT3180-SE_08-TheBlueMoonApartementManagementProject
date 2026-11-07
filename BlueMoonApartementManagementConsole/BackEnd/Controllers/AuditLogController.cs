using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueMoonApartementManagementConsole.BackEnd.Controllers;

[ApiController]
[Route("api/nhat-ky")]
[Authorize(Policy = "QuanTriHeThong")]
public class AuditLogController(IAuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] AuditQuery query, CancellationToken ct) =>
        Ok(await audit.QueryAsync(query, ct));
}