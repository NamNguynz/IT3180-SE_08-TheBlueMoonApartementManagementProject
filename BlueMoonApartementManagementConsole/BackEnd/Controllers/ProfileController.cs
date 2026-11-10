using System.Security.Claims;
using BlueMoonApartementManagementConsole.BackEnd.Models;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlueMoonApartementManagementConsole.BackEnd.Controllers;

[ApiController]
[Route("api/ho-so")]
[Authorize]
public class ProfileController(IAccountManagementService accounts, IAppUserRepo users) : ControllerBase
{
    [HttpPost("doi-mat-khau")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var r = await accounts.ChangeOwnPasswordAsync(userId, req, ct);
        if (r.Status != ManageStatus.Success)
            return BadRequest(new ApiError("INVALID", r.Message ?? "Không đổi được mật khẩu."));

        // Đổi mật khẩu đã đổi SecurityStamp, nên mọi phiên khác bị đăng xuất (G2).
        // Cấp lại cookie với mã mới cho chính phiên đang đổi để người dùng không bị đăng xuất theo.
        var user = await users.GetByIdAsync(userId, ct);
        if (user is not null)
        {
            var claims = User.Claims
                .Where(c => c.Type is not (AppClaimTypes.SecurityStamp or "must_change_password"))
                .Append(new Claim(AppClaimTypes.SecurityStamp, user.SecurityStamp))
                .Append(new Claim("must_change_password", "0"));
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
                new AuthenticationProperties { IsPersistent = false });
        }

        return NoContent();
    }
}
