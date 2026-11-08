using System.Security.Claims;
using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace BlueMoonApartementManagementConsole.Tests.Integration;

/// <summary>
/// Dựng toàn bộ app (Program.cs thật) trong bộ nhớ để test tích hợp.
/// - Database: lấy từ biến môi trường BLUEMOON_TEST_CONNECTION, bắt buộc là database riêng cho test.
/// - Môi trường "IntegrationTest": không nạp user-secrets của máy dev.
/// - Đồng hồ của cookie đăng nhập được thay bằng FakeTimeProvider để "tua" thời gian.
/// - Có endpoint chỉ dành cho test (TestSignInPath) để đăng nhập nhanh tài khoản admin.
/// </summary>
public sealed class BlueMoonAppFactory : WebApplicationFactory<Program>
{
    public const string ConnectionEnvVar = "BLUEMOON_TEST_CONNECTION";
    public const string AdminPassword = "IntegrationTest@123";
    public const string TestSignInPath = "/__test/sign-in";
    public const string AuthCookieName = ".AspNetCore.Cookies";

    public static string? ConnectionString => Environment.GetEnvironmentVariable(ConnectionEnvVar);

    public static string SkipReason =>
        $"Chưa đặt biến môi trường {ConnectionEnvVar} trỏ tới PostgreSQL dùng cho test, bỏ qua test tích hợp.";

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connectionString = ConnectionString
            ?? throw new InvalidOperationException(SkipReason);
        EnsureDedicatedTestDatabase(connectionString);

        builder.UseEnvironment("IntegrationTest");
        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        builder.UseSetting("ADMIN_INIT_PWD", AdminPassword);

        builder.ConfigureServices(services =>
        {
            services.PostConfigure<CookieAuthenticationOptions>(
                CookieAuthenticationDefaults.AuthenticationScheme,
                o => o.TimeProvider = Clock);

            services.AddSingleton<IStartupFilter, TestSignInStartupFilter>();
        });
    }

    /// <summary>Client gọi qua https (cookie đặt Secure), không tự đi theo redirect để kiểm tra được 302.</summary>
    public HttpClient CreateHttpsClient(bool handleCookies = true) =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = handleCookies
        });

    /// <summary>Không cho test chạy trên database dev/thật: tên database phải chứa "IntegrationTest".</summary>
    private static void EnsureDedicatedTestDatabase(string connectionString)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database ?? string.Empty;
        if (!database.Contains("IntegrationTest", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{ConnectionEnvVar} phải trỏ tới database riêng cho test (tên chứa 'IntegrationTest'), hiện là '{database}'.");
    }

    /// <summary>
    /// Thêm endpoint đăng nhập nhanh tài khoản admin, chỉ tồn tại trong test.
    /// Claims giống hệt Login.razor để OnValidatePrincipal xử lý như đăng nhập thật.
    /// Không dùng app.Map: Map đổi PathBase, khiến cookie bị gắn path=/__test/sign-in
    /// thay vì path=/ như khi đăng nhập thật ở /login.
    /// </summary>
    private sealed class TestSignInStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (ctx, nextMiddleware) =>
            {
                if (!HttpMethods.IsPost(ctx.Request.Method) || ctx.Request.Path != TestSignInPath)
                {
                    await nextMiddleware();
                    return;
                }

                var db = ctx.RequestServices.GetRequiredService<AppDbContext>();
                var admin = await db.Users.SingleAsync(u => u.Username == "admin");

                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, admin.Id.ToString()),
                    new(ClaimTypes.Name, admin.Username),
                    new("full_name", admin.FullName),
                    new(ClaimTypes.Role, admin.Role.ToString())
                };

                await ctx.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
                    new AuthenticationProperties { IsPersistent = false });

                ctx.Response.StatusCode = StatusCodes.Status204NoContent;
            });

            next(app);
        };
    }
}
