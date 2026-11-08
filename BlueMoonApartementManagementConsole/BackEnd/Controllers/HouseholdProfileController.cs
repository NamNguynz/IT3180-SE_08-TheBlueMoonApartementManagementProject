using System.Security.Claims;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueMoonApartementManagementConsole.BackEnd.Controllers;

[ApiController]
[Route("api/ho-gia-dinh")]
[Authorize(Policy = "QuanTriHeThong")]
public sealed class HouseholdProfileController(IApartmentProfileService profiles) : ControllerBase
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string ActorName => User.Identity?.Name ?? "unknown";

    [HttpGet("{id:int}")]
    public async Task<ActionResult<HouseholdProfileResponse>> GetById(int id, CancellationToken ct)
    {
        var household = await profiles.GetHouseholdByIdAsync(id, ct);
        return household is null ? NotFound(new ApiError("NOT_FOUND", "Không tìm thấy hộ gia đình.")) : Ok(household);
    }

    [HttpPost("can-ho/{apartmentId:int}")]
    public async Task<IActionResult> Create(
        int apartmentId, [FromBody] CreateHouseholdRequest request, CancellationToken ct)
    {
        var result = await profiles.CreateHouseholdAsync(ActorId, ActorName, apartmentId, request, ct);
        if (result.Status != ProfileStatus.Success)
            return ToError(result.Status, result.Message);

        return CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateHouseholdRequest request, CancellationToken ct)
    {
        var result = await profiles.UpdateHouseholdAsync(ActorId, ActorName, id, request, ct);
        return result.Status == ProfileStatus.Success
            ? Ok(result.Data)
            : ToError(result.Status, result.Message);
    }

    private IActionResult ToError(ProfileStatus status, string? message) => status switch
    {
        ProfileStatus.NotFound => NotFound(new ApiError("NOT_FOUND", message ?? "Không tìm thấy hộ gia đình.")),
        ProfileStatus.Conflict => Conflict(new ApiError("CONFLICT", message ?? "Dữ liệu đã tồn tại.")),
        _ => BadRequest(new ApiError("INVALID", message ?? "Dữ liệu không hợp lệ."))
    };
}
