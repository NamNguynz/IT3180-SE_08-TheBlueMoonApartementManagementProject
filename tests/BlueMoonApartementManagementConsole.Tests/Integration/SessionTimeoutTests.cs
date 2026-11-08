using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BlueMoonApartementManagementConsole.Tests.Integration;

/// <summary>
/// Test tích hợp NFR-008: tự động kết thúc phiên sau 10 phút không tương tác.
/// Chạy app thật (Program.cs) với đồng hồ giả, nên "tua" 10 phút chỉ mất vài mili giây.
/// Cần PostgreSQL: đặt biến môi trường BLUEMOON_TEST_CONNECTION, nếu không các test sẽ bị skip.
/// </summary>
public class SessionTimeoutTests(BlueMoonAppFactory factory) : IClassFixture<BlueMoonAppFactory>
{
    private const string ProtectedPage = "/ho-so";
    private const string ProtectedApi = "/api/tai-khoan";

    private static void SkipIfNoDatabase() =>
        Skip.If(BlueMoonAppFactory.ConnectionString is null, BlueMoonAppFactory.SkipReason);

    private void Advance(TimeSpan by) => factory.Clock.Advance(by);

    private static TimeSpan Min(int minutes, int seconds = 0) => new(0, minutes, seconds);

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient client)
    {
        var response = await client.PostAsync(BlueMoonAppFactory.TestSignInPath, null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return response;
    }

    private static string? AuthSetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(BlueMoonAppFactory.AuthCookieName + "=", StringComparison.Ordinal))
            : null;

    private static void AssertRedirectedToLogin(HttpResponseMessage response, string returnPath)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        var uri = location.IsAbsoluteUri ? location : new Uri(new Uri("https://localhost"), location);
        Assert.Equal("/login", uri.AbsolutePath);
        Assert.Contains("ReturnUrl=" + Uri.EscapeDataString(returnPath), uri.Query);
    }

    // ===== S1: Cấu hình phiên thật trong Program.cs =====

    [SkippableFact]
    [Trait("TestId", "S1")]
    public void CookieOptions_MatchNfr008()
    {
        SkipIfNoDatabase();

        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        Assert.Equal(TimeSpan.FromMinutes(10), options.ExpireTimeSpan);
        Assert.True(options.SlidingExpiration);
        Assert.Equal("/login", options.LoginPath.Value);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Strict, options.Cookie.SameSite);
    }

    // ===== S2: Chưa tới 10 phút thì vẫn đăng nhập =====

    [SkippableFact]
    [Trait("TestId", "S2")]
    public async Task Idle_9m59s_StillSignedIn()
    {
        SkipIfNoDatabase();
        var client = factory.CreateHttpsClient();
        await SignInAsync(client);

        Advance(Min(9, 59));
        var response = await client.GetAsync(ProtectedPage);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ===== S3: Quá 10 phút thì hết phiên =====

    [SkippableFact]
    [Trait("TestId", "S3")]
    public async Task Idle_10m01s_PageRedirectsToLogin()
    {
        SkipIfNoDatabase();
        var client = factory.CreateHttpsClient();
        await SignInAsync(client);

        Advance(Min(10, 1));
        var response = await client.GetAsync(ProtectedPage);

        AssertRedirectedToLogin(response, ProtectedPage);
    }

    [SkippableFact]
    [Trait("TestId", "S3")]
    public async Task Idle_10m01s_ApiReturns401()
    {
        SkipIfNoDatabase();
        var client = factory.CreateHttpsClient();
        await SignInAsync(client);

        Advance(Min(10, 1));
        var response = await client.GetAsync(ProtectedApi);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ===== S4: Có thao tác sau nửa thời gian thì phiên được gia hạn =====

    [SkippableFact]
    [Trait("TestId", "S4")]
    public async Task ActivityAfterHalfway_RenewsCookie_SessionExtendsBeyondOriginal10m()
    {
        SkipIfNoDatabase();
        var client = factory.CreateHttpsClient();
        await SignInAsync(client);

        Advance(Min(6));
        var renew = await client.GetAsync(ProtectedPage);
        Assert.Equal(HttpStatusCode.OK, renew.StatusCode);
        Assert.NotNull(AuthSetCookie(renew)); // cookie mới được cấp

        Advance(Min(9, 59)); // phút 15:59 kể từ đăng nhập, 9:59 kể từ lần gia hạn
        var response = await client.GetAsync(ProtectedPage);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [SkippableFact]
    [Trait("TestId", "S4")]
    public async Task ActivityAfterHalfway_SessionEnds10mAfterThatActivity()
    {
        SkipIfNoDatabase();
        var client = factory.CreateHttpsClient();
        await SignInAsync(client);

        Advance(Min(6));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ProtectedPage)).StatusCode);

        Advance(Min(10, 1)); // phút 16:01 kể từ đăng nhập, 10:01 kể từ thao tác cuối
        var response = await client.GetAsync(ProtectedPage);

        AssertRedirectedToLogin(response, ProtectedPage);
    }

    // ===== S5: Kiểm chứng H1: thao tác trước nửa thời gian KHÔNG gia hạn =====

    [SkippableFact]
    [Trait("TestId", "S5")]
    public async Task ActivityBeforeHalfway_DoesNotRenew_SessionEndsOnly6mAfterThatActivity()
    {
        // Ghi nhận hành vi hiện tại (H1): SlidingExpiration chỉ cấp lại cookie khi đã quá nửa thời gian.
        // Nếu PO chốt NFR-008 là "đúng 10 phút kể từ thao tác cuối" và code được sửa, test này phải đổi theo.
        SkipIfNoDatabase();
        var client = factory.CreateHttpsClient();
        await SignInAsync(client);

        Advance(Min(4));
        var activity = await client.GetAsync(ProtectedPage);
        Assert.Equal(HttpStatusCode.OK, activity.StatusCode);
        Assert.Null(AuthSetCookie(activity)); // không được gia hạn

        Advance(Min(6, 1)); // phút 10:01 kể từ đăng nhập, mới 6:01 kể từ thao tác cuối
        var response = await client.GetAsync(ProtectedPage);

        AssertRedirectedToLogin(response, ProtectedPage);
    }

    // ===== S6: Cookie cũ đã hết hạn thì gửi lại cũng không dùng được =====

    [SkippableFact]
    [Trait("TestId", "S6")]
    public async Task ExpiredCookie_ReplayedManually_IsRejectedByServer()
    {
        SkipIfNoDatabase();
        var signInClient = factory.CreateHttpsClient(handleCookies: false);
        var setCookie = AuthSetCookie(await SignInAsync(signInClient));
        Assert.NotNull(setCookie);
        var cookieHeader = setCookie!.Split(';')[0]; // ".AspNetCore.Cookies=<giá trị>"

        async Task<HttpStatusCode> CallWithCookieAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ProtectedApi);
            request.Headers.Add("Cookie", cookieHeader);
            return (await factory.CreateHttpsClient(handleCookies: false).SendAsync(request)).StatusCode;
        }

        Advance(Min(1));
        Assert.Equal(HttpStatusCode.OK, await CallWithCookieAsync()); // đối chứng: cookie còn hạn

        Advance(Min(9, 1)); // tổng 10:01
        Assert.Equal(HttpStatusCode.Unauthorized, await CallWithCookieAsync());
    }

    // ===== S7: Cookie theo phiên trình duyệt =====

    [SkippableFact]
    [Trait("TestId", "S7")]
    public async Task AuthCookie_IsSessionCookie_WithSecureAttributes()
    {
        SkipIfNoDatabase();
        var client = factory.CreateHttpsClient(handleCookies: false);

        var setCookie = AuthSetCookie(await SignInAsync(client))!.ToLowerInvariant();

        Assert.DoesNotContain("expires=", setCookie);   // mất khi đóng trình duyệt
        Assert.DoesNotContain("max-age=", setCookie);
        Assert.Contains("path=/;", setCookie);          // dùng được cho mọi trang
        Assert.Contains("httponly", setCookie);
        Assert.Contains("secure", setCookie);
        Assert.Contains("samesite=strict", setCookie);
    }
}
