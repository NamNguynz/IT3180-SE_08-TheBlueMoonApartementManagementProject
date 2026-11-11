using System.Security.Claims;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueMoonApartementManagementConsole.BackEnd.Controllers;

/// <summary>Biến động nhân khẩu của một hộ (PBI-007). Sprint 1 chỉ Ban quản trị được xem và ghi nhận.</summary>
[ApiController]
[Route("api/ho-gia-dinh/{householdId:int}/bien-dong")]
[Authorize(Policy = "QuanTriHeThong")]
public sealed class ResidentChangeController(IResidentChangeService residentChanges) : ControllerBase
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string ActorName => User.Identity?.Name ?? "unknown";

    [HttpGet]
    public async Task<ActionResult<HouseholdResidencyResponse>> GetByHousehold(int householdId, CancellationToken ct)
    {
        var residency = await residentChanges.GetHouseholdResidencyAsync(householdId, ct);
        return residency is null
            ? NotFound(new ApiError("NOT_FOUND", "Không tìm thấy hộ gia đình."))
            : Ok(residency);
    }

    [HttpPost]
    public async Task<IActionResult> Record(
        int householdId, [FromBody] RecordResidentChangeRequest request, CancellationToken ct)
    {
        var result = await residentChanges.RecordAsync(ActorId, ActorName, householdId, request, ct);
        if (result.Status != ProfileStatus.Success)
            return ToError(result.Status, result.Message);

        return CreatedAtAction(nameof(GetByHousehold), new { householdId }, result.Data);
    }

    private IActionResult ToError(ProfileStatus status, string? message) => status switch
    {
        ProfileStatus.NotFound => NotFound(new ApiError("NOT_FOUND", message ?? "Không tìm thấy hộ gia đình.")),
        ProfileStatus.Conflict => Conflict(new ApiError("CONFLICT", message ?? "Dữ liệu đã tồn tại.")),
        _ => BadRequest(new ApiError("INVALID", message ?? "Dữ liệu không hợp lệ."))
    };
}
