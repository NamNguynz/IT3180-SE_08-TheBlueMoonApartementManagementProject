using System.Security.Claims;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueMoonApartementManagementConsole.BackEnd.Controllers;

[ApiController]
[Route("api/can-ho")]
[Authorize(Policy = "QuanTriHeThong")]
public sealed class ApartmentProfileController(IApartmentProfileService profiles) : ControllerBase
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string ActorName => User.Identity?.Name ?? "unknown";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApartmentProfileResponse>>> GetAll(CancellationToken ct) =>
        Ok(await profiles.GetApartmentsAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ApartmentProfileResponse>> GetById(int id, CancellationToken ct)
    {
        var apartment = await profiles.GetApartmentByIdAsync(id, ct);
        return apartment is null ? NotFound(new ApiError("NOT_FOUND", "Không tìm thấy căn hộ.")) : Ok(apartment);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateApartmentRequest request, CancellationToken ct)
    {
        var result = await profiles.CreateApartmentAsync(ActorId, ActorName, request, ct);
        if (result.Status != ProfileStatus.Success)
            return ToError(result.Status, result.Message);

        return CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateApartmentRequest request, CancellationToken ct)
    {
        var result = await profiles.UpdateApartmentAsync(ActorId, ActorName, id, request, ct);
        return result.Status == ProfileStatus.Success
            ? Ok(result.Data)
            : ToError(result.Status, result.Message);
    }

    private IActionResult ToError(ProfileStatus status, string? message) => status switch
    {
        ProfileStatus.NotFound => NotFound(new ApiError("NOT_FOUND", message ?? "Không tìm thấy căn hộ.")),
        ProfileStatus.Conflict => Conflict(new ApiError("CONFLICT", message ?? "Dữ liệu đã tồn tại.")),
        _ => BadRequest(new ApiError("INVALID", message ?? "Dữ liệu không hợp lệ."))
    };
}
