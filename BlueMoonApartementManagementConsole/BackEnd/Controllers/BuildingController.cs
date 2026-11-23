using System.Security.Claims;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueMoonApartementManagementConsole.BackEnd.Controllers;

// Quản lý tòa nhà: chỉ BQT (QuanTriHeThong), cùng quyền với trang Hồ sơ căn hộ.
[ApiController]
[Route("api/toa-nha")]
[Authorize(Policy = "QuanTriHeThong")]
public sealed class BuildingController(IBuildingService service) : ControllerBase
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string ActorName => User.Identity?.Name ?? "unknown";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BuildingResponse>>> GetAll(CancellationToken ct) =>
        Ok(await service.GetAllAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBuildingRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(ActorId, ActorName, request, ct);
        return result.Status switch
        {
            ProfileStatus.Success => Ok(result.Data),
            ProfileStatus.Conflict => Conflict(new ApiError("CONFLICT", result.Message ?? "Dữ liệu đã tồn tại.")),
            _ => BadRequest(new ApiError("INVALID", result.Message ?? "Dữ liệu không hợp lệ."))
        };
    }
}