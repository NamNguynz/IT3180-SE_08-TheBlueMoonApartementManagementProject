using System.Security.Claims;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueMoonApartementManagementConsole.BackEnd.Controllers;

[ApiController]
[Route("api/ho-so")]
[Authorize]
public class ProfileController(IAccountManagementService accounts) : ControllerBase
{
    [HttpPost("doi-mat-khau")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var r = await accounts.ChangeOwnPasswordAsync(userId, req, ct);

        return r.Status == ManageStatus.Success ? NoContent() : BadRequest(new ApiError("INVALID", r.Message ?? "Không đổi được mật khẩu."));
    }
}