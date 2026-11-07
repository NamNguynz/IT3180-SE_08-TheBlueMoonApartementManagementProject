using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using BlueMoonApartementManagementConsole.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IPasswordHasher<AppUserEntity>, PasswordHasher<AppUserEntity>>();
builder.Services.AddScoped<IAppUserRepo, AppUserRepo>();
builder.Services.AddScoped<IAuditRepository, AuditRepository>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IAccountManagementService, AccountManagementService>();
builder.Services.AddScoped<IAuthService, AuthService>();

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(o =>
        {
            o.LoginPath = "/login";
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.ExpireTimeSpan = TimeSpan.FromMinutes(10); // NFR-008: 10 phút không tương tác
            o.SlidingExpiration = true;
            o.Events.OnRedirectToLogin = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api"))
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                else
                    ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            };
            o.Events.OnRedirectToAccessDenied = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api"))
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                else
                    ctx.Response.Redirect("/403");
                return Task.CompletedTask;
            };
            o.Events.OnValidatePrincipal = async ctx =>
            {
                var users = ctx.HttpContext.RequestServices.GetRequiredService<IAppUserRepo>();

                var userId = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var user = int.TryParse(userId, out var id) ? await users.GetByIdAsync(id, ctx.HttpContext.RequestAborted) : null;

                // Bị vô hiệu hóa, bị xóa hoặc chưa được duyệt: đăng xuất ngay
                if (user is null || !user.IsActive || user.Status != UserStatus.Approved)
                {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return;
                }

                // Vai trò và cờ đổi mật khẩu lấy từ DB, nên thay đổi có hiệu lực ở request kế tiếp
                var claims = ctx.Principal!.Claims
                    .Where(c => c.Type is not (ClaimTypes.Role or "must_change_password"))
                    .ToList();
                claims.Add(new Claim(ClaimTypes.Role, user.Role.ToString()));
                claims.Add(new Claim("must_change_password", user.MustChangePassword ? "1" : "0"));

                ctx.ReplacePrincipal(new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
            };
        });

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("QuanTriHeThong", p => p.RequireRole(UserRole.BQT.ToString()));
    o.AddPolicy("TaiChinh", p => p.RequireRole(UserRole.BQT.ToString(), UserRole.KE_TOAN.ToString()));
    o.AddPolicy("CuDan", p => p.RequireRole(UserRole.CU_DAN.ToString()));
});

var app = builder.Build();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapControllers();

app.Use(async (ctx, next) =>
{
    var mustChange = ctx.User.FindFirst("must_change_password")?.Value == "1";
    var path = ctx.Request.Path.Value ?? string.Empty;

    var allowed = Path.HasExtension(path)
               || path.StartsWith("/ho-so", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("/api/ho-so", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("/logout", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("/403", StringComparison.OrdinalIgnoreCase)
               || path.StartsWith("/_", StringComparison.OrdinalIgnoreCase);

    if (mustChange && !allowed)
    {
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new ApiError("PASSWORD_CHANGE_REQUIRED", "Vui lòng đổi mật khẩu tạm trước khi tiếp tục."));
            return;
        }

        ctx.Response.Redirect("/ho-so");
        return;
    }

    await next();
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/logout", async (HttpContext ctx, IAuditService audit) =>
{
    var username = ctx.User.Identity?.Name ?? "unknown";
    int? userId = int.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    await audit.LogAsync(AuditAction.Logout, username, userId, userId?.ToString());
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).RequireAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUserEntity>>();

    await db.Database.MigrateAsync();

    if (!await db.Users.AnyAsync(u => u.Role == UserRole.BQT))
    {
        var initPwd = app.Configuration["ADMIN_INIT_PWD"]
            ?? throw new InvalidOperationException("Thiếu cấu hình ADMIN_INIT_PWD.");

        var admin = new AppUserEntity { Username = "admin", Role = UserRole.BQT, Status = UserStatus.Approved};
        admin.PasswordHash = hasher.HashPassword(admin, initPwd);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
    }
}


app.Run();
