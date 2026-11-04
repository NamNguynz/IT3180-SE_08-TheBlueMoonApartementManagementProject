// Auth/Controllers/AccountApprovalController.csusing Bluemoon.Auth.Dtos;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BlueMoonApartementManagementConsole.BackEnd.Controllers;

[ApiController]
[Route("api/tai-khoan")]
[Authorize(Policy = "QuanTriHeThong")]
public class AccountApprovalController(IAuthService auth) : ControllerBase
{
    [HttpGet("cho-xac-nhan")]
    public async Task<IActionResult> GetPending(CancellationToken ct)
    {
        var list = await auth.GetPendingAsync(ct);
        return Ok(list.Select(u => new PendingUserDto(
            u.Id, u.Username, u.FullName, u.Role.ToString(), u.CreatedAt)));
    }

    [HttpPost("{id:int}/xac-nhan")]
    public async Task<IActionResult> Approve(int id, CancellationToken ct)
    {
        var approverId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var status = await auth.ApproveAsync(approverId, id, ct);

        return status switch
        {
            ApproveStatus.Success => NoContent(),
            ApproveStatus.NotFound => NotFound(new ApiError("USER_NOT_FOUND", "Không tìm thấy tài khoản.")),
            ApproveStatus.NotPending => Conflict(new ApiError("NOT_PENDING", "Tài khoản này không ở trạng thái chờ xác nhận.")),
            _ => Forbid()
        };
    }
}