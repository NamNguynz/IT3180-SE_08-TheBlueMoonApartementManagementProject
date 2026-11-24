using System.Security.Claims;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueMoonApartementManagementConsole.BackEnd.Controllers;

/// <summary>Ghi nhận thu tiền và theo dõi số đã thu theo kỳ (PBI-016). Kế toán thực hiện (US-016); BQT cũng thuộc policy TaiChinh.</summary>
[ApiController]
[Route("api/thu-tien")]
[Authorize(Policy = "TaiChinh")]
public sealed class PaymentController(IPaymentService payments) : ControllerBase
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string ActorName => User.Identity?.Name ?? "unknown";

    [HttpGet("ky-thu/{periodId:int}")]
    public async Task<ActionResult<PeriodCollectionResponse>> GetPeriod(int periodId, CancellationToken ct)
    {
        var period = await payments.GetPeriodAsync(periodId, ct);
        return period is null
            ? NotFound(new ApiError("NOT_FOUND", "Không tìm thấy kỳ thu."))
            : Ok(period);
    }

    [HttpGet("ky-thu/{periodId:int}/ho/{householdId:int}")]
    public async Task<ActionResult<HouseholdPaymentDetailResponse>> GetHousehold(int periodId, int householdId, CancellationToken ct)
    {
        var bill = await payments.GetHouseholdAsync(periodId, householdId, ct);
        return bill is null
            ? NotFound(new ApiError("NOT_FOUND", "Không tìm thấy khoản phải thu của hộ trong kỳ thu này."))
            : Ok(bill);
    }

    /// <summary>Phục vụ ảnh minh chứng cho người có quyền tài chính. Không lộ đường dẫn file vật lý.</summary>
    [HttpGet("{paymentId:int}/anh-minh-chung")]
    public async Task<IActionResult> GetProofImage(int paymentId, CancellationToken ct)
    {
        var proof = await payments.GetProofAsync(paymentId, ct);
        if (proof is null)
            return NotFound(new ApiError("NOT_FOUND", "Không tìm thấy ảnh minh chứng."));

        Response.Headers.CacheControl = "private, no-store";
        // FileStreamResult sẽ đóng stream sau khi ghi xong, không đóng ở đây.
        return File(proof.Value.Stream, proof.Value.ContentType, enableRangeProcessing: true);
    }

    [HttpPost]
    [RequestSizeLimit(6 * 1024 * 1024)] // ảnh tối đa 5 MB + phần form, chặn request quá sớm
    public async Task<IActionResult> Record([FromForm] RecordPaymentRequest request, IFormFile? proofImage, CancellationToken ct)
    {
        await using var stream = proofImage?.OpenReadStream() ?? Stream.Null;
        var proof = proofImage is null
            ? null
            : new PaymentProofUpload(proofImage.FileName, proofImage.ContentType, proofImage.Length, stream);

        var result = await payments.RecordAsync(ActorId, ActorName, request, proof, ct);
        if (result.Status != FeeItemStatus.Success)
            return result.Status switch
            {
                FeeItemStatus.NotFound => NotFound(new ApiError("NOT_FOUND", result.Message ?? "Không tìm thấy kỳ thu.")),
                FeeItemStatus.Conflict => Conflict(new ApiError("CONFLICT", result.Message ?? "Dữ liệu vừa thay đổi.")),
                _ => BadRequest(new ApiError("INVALID", result.Message ?? "Dữ liệu không hợp lệ."))
            };

        var bill = result.Data!.Bill;
        return CreatedAtAction(nameof(GetHousehold),
            new { periodId = bill.BillingPeriodId, householdId = bill.HouseholdId }, result.Data);
    }
}
