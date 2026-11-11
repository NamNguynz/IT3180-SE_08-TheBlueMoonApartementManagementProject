using System.Security.Claims;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueMoonApartementManagementConsole.BackEnd.Controllers;

/// <summary>Danh sách hộ chưa đóng và nhắc nộp (PBI-019). Kế toán theo dõi công nợ (US-019); BQT cũng thuộc policy TaiChinh.</summary>
[ApiController]
[Route("api/cong-no")]
[Authorize(Policy = "TaiChinh")]
public sealed class DebtController(IDebtService debts) : ControllerBase
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string ActorName => User.Identity?.Name ?? "unknown";

    /// <summary>GET api/cong-no (mọi kỳ) hoặc api/cong-no?kyThu=3 (một kỳ).</summary>
    [HttpGet]
    public async Task<ActionResult<DebtListResponse>> GetDebts([FromQuery(Name = "kyThu")] int? periodId, CancellationToken ct)
    {
        var list = await debts.GetDebtsAsync(periodId, ct);
        return list is null
            ? NotFound(new ApiError("NOT_FOUND", "Không tìm thấy kỳ thu."))
            : Ok(list);
    }

    /// <summary>Lịch sử nhắc một hộ trong một kỳ: GET api/cong-no/nhac-nop?kyThu=3&amp;hoGiaDinh=5.</summary>
    [HttpGet("nhac-nop")]
    public async Task<IActionResult> GetReminders(
        [FromQuery(Name = "kyThu")] int? periodId, [FromQuery(Name = "hoGiaDinh")] int? householdId, CancellationToken ct)
    {
        if (periodId is null || householdId is null)
            return BadRequest(new ApiError("INVALID", "Hãy chọn kỳ thu (kyThu) và hộ gia đình (hoGiaDinh)."));
        return Ok(await debts.GetRemindersAsync(periodId.Value, householdId.Value, ct));
    }

    [HttpPost("nhac-nop")]
    public async Task<IActionResult> RecordReminders([FromBody] RecordRemindersRequest request, CancellationToken ct)
    {
        var result = await debts.RecordRemindersAsync(ActorId, ActorName, request, ct);
        return result.Status switch
        {
            FeeItemStatus.Success => StatusCode(StatusCodes.Status201Created, result.Data),
            FeeItemStatus.NotFound => NotFound(new ApiError("NOT_FOUND", result.Message ?? "Không tìm thấy kỳ thu.")),
            _ => BadRequest(new ApiError("INVALID", result.Message ?? "Dữ liệu không hợp lệ."))
        };
    }
}
