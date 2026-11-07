using System.Security.Claims;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueMoonApartementManagementConsole.BackEnd.Controllers;

[ApiController]
[Route("api/tai-khoan")]
[Authorize(Policy = "QuanTriHeThong")]
public class AccountController(IAccountManagementService accounts) : ControllerBase
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await accounts.GetAccountsAsync(ActorId, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAccountRequest req, CancellationToken ct) =>
        ToResult(await accounts.CreateAsync(ActorId, req, ct));

    [HttpPost("{id:int}/vo-hieu-hoa")]
    public async Task<IActionResult> Disable(int id, CancellationToken ct) =>
        ToResult(await accounts.SetActiveAsync(ActorId, id, false, ct));

    [HttpPost("{id:int}/kich-hoat")]
    public async Task<IActionResult> Enable(int id, CancellationToken ct) =>
        ToResult(await accounts.SetActiveAsync(ActorId, id, true, ct));

    [HttpPost("{id:int}/mo-khoa")]
    public async Task<IActionResult> Unlock(int id, CancellationToken ct) =>
        ToResult(await accounts.UnlockAsync(ActorId, id, ct));

    [HttpPost("{id:int}/cap-lai-mat-khau")]
    public async Task<IActionResult> ResetPassword(int id, CancellationToken ct) =>
        ToResult(await accounts.ResetPasswordAsync(ActorId, id, ct));

    private IActionResult ToResult(ManageResult r) => r.Status switch
    {
        ManageStatus.Success when r.TempPassword is null && r.Id is null => NoContent(),
        ManageStatus.Success => Ok(new { id = r.Id, tempPassword = r.TempPassword }),
        ManageStatus.NotFound => NotFound(new ApiError("NOT_FOUND", "Không tìm thấy tài khoản.")),
        ManageStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, new ApiError("FORBIDDEN", "Bạn không có quyền thao tác trên tài khoản này.")),
        ManageStatus.Self => BadRequest(new ApiError("SELF", "Không thể tự thao tác trên tài khoản của mình.")),
        ManageStatus.Conflict => Conflict(new ApiError("CONFLICT", r.Message ?? "Dữ liệu đã thay đổi.")),
        _ => BadRequest(new ApiError("INVALID", r.Message ?? "Dữ liệu không hợp lệ."))
    };
}