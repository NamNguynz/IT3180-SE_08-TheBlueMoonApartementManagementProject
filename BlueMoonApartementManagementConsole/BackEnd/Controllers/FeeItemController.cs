using System.Security.Claims;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueMoonApartementManagementConsole.BackEnd.Controllers;

// Read: BQT and accountant (TaiChinh). Create fee item and change rate: BQT only (QuanTriHeThong).
[ApiController]
[Route("api/khoan-thu")]
[Authorize(Policy = "TaiChinh")]
public sealed class FeeItemController(IFeeItemService service) : ControllerBase
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string ActorName => User.Identity?.Name ?? "unknown";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FeeItemResponse>>> GetAll(CancellationToken ct) =>
        Ok(await service.GetAllAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<FeeItemResponse>> GetById(int id, CancellationToken ct)
    {
        var entity = await service.GetByIdAsync(id, ct);
        return entity is null
            ? NotFound(new ApiError("NOT_FOUND", "Không tìm thấy khoản thu."))
            : Ok(entity);
    }

    [HttpGet("{id:int}/don-gia")]
    public async Task<ActionResult<FeeRateLookupResponse>> GetRateOnDate(
    int id, [FromQuery(Name = "ngay")] DateOnly? date, CancellationToken ct)
    {
        var result = await service.GetRateOnDateAsync(id, date, ct);
        return result is null
            ? NotFound(new ApiError("NOT_FOUND", "Không có đơn giá hiệu lực vào ngày này."))
            : Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "QuanTriHeThong")]
    public async Task<IActionResult> Create([FromBody] CreateFeeItemRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(ActorId, ActorName, request, ct);
        if (result.Status != FeeItemStatus.Success)
            return ToError(result.Status, result.Message);

        return CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPost("{id:int}/don-gia")]
    [Authorize(Policy = "QuanTriHeThong")]
    public async Task<IActionResult> ChangeRate(
        int id, [FromBody] ChangeRateRequest request, CancellationToken ct)
    {
        var result = await service.ChangeRateAsync(ActorId, ActorName, id, request, ct);
        return result.Status == FeeItemStatus.Success
            ? Ok(result.Data)
            : ToError(result.Status, result.Message);
    }

    private IActionResult ToError(FeeItemStatus status, string? message) => status switch
    {
        FeeItemStatus.NotFound => NotFound(new ApiError("NOT_FOUND", message ?? "Không tìm thấy khoản thu.")),
        FeeItemStatus.Conflict => Conflict(new ApiError("CONFLICT", message ?? "Dữ liệu đã tồn tại.")),
        FeeItemStatus.FeatureUnavailable => BadRequest(new ApiError("FEATURE_UNAVAILABLE", message ?? "Tính năng không khả dụng.")),
        _ => BadRequest(new ApiError("INVALID", message ?? "Dữ liệu không hợp lệ."))
    };
}
