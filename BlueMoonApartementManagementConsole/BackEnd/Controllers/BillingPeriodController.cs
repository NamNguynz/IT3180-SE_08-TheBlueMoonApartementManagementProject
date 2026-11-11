using System.Security.Claims;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueMoonApartementManagementConsole.BackEnd.Controllers;

/// <summary>Kỳ thu và bảng kê phải thu theo hộ (PBI-014). Kế toán lập kỳ thu (US-014); BQT cũng thuộc policy TaiChinh.</summary>
[ApiController]
[Route("api/ky-thu")]
[Authorize(Policy = "TaiChinh")]
public sealed class BillingPeriodController(IBillingPeriodService billingPeriods) : ControllerBase
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string ActorName => User.Identity?.Name ?? "unknown";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BillingPeriodSummaryResponse>>> GetAll(CancellationToken ct) =>
        Ok(await billingPeriods.GetAllAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<BillingPeriodResponse>> GetById(int id, CancellationToken ct)
    {
        var period = await billingPeriods.GetByIdAsync(id, ct);
        return period is null
            ? NotFound(new ApiError("NOT_FOUND", "Không tìm thấy kỳ thu."))
            : Ok(period);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateBillingPeriodRequest request, CancellationToken ct)
    {
        var result = await billingPeriods.CreateAsync(ActorId, ActorName, request, ct);
        if (result.Status != FeeItemStatus.Success)
            return ToError(result.Status, result.Message);

        return CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data);
    }

    private IActionResult ToError(FeeItemStatus status, string? message) => status switch
    {
        FeeItemStatus.NotFound => NotFound(new ApiError("NOT_FOUND", message ?? "Không tìm thấy kỳ thu.")),
        FeeItemStatus.Conflict => Conflict(new ApiError("CONFLICT", message ?? "Dữ liệu đã tồn tại.")),
        _ => BadRequest(new ApiError("INVALID", message ?? "Dữ liệu không hợp lệ."))
    };
}
