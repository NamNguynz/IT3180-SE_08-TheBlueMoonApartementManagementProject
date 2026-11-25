using System.Net;
using BlueMoonApartementManagementConsole.Tests.Integration.Acceptance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BlueMoonApartementManagementConsole.Tests.Integration;

/// <summary>
/// R20: trang /Error (Error.razor). App chỉ dùng trang này khi không chạy ở môi trường Development
/// (UseExceptionHandler("/Error") trong Program.cs); test chạy ở môi trường "IntegrationTest" nên trang có hoạt động.
/// Cần PostgreSQL vì app chạy migration khi khởi động: đặt BLUEMOON_TEST_CONNECTION.
/// </summary>
[Collection(SequentialDatabaseCollection.Name)]
public class ErrorPageTests(BlueMoonAppFactory factory) : IClassFixture<BlueMoonAppFactory>
{
    private const string ThrowPath = "/__test/throw";
    private const string InternalDetail = "Chi tiet noi bo R20 khong duoc lo ra ngoai";

    private static void SkipIfNoDatabase() =>
        Skip.If(BlueMoonAppFactory.ConnectionString is null, BlueMoonAppFactory.SkipReason);

    [SkippableFact]
    [Trait("TestId", "R20")]
    public async Task ErrorPage_OpensWithoutLogin_ShowsVietnameseMessageAndRequestId()
    {
        SkipIfNoDatabase();
        var client = factory.CreateHttpsClient();

        var page = await UiFlow.GetAsync(client, "/Error");

        // Trang công khai: không bị chuyển sang /login
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Null(page.Location);
        Assert.Contains("<title>Lỗi hệ thống - Bluemoon</title>", page.Html);
        Assert.Contains("Đã xảy ra lỗi", page.Html);
        Assert.Contains("Vui lòng thử lại sau", page.Html);
        // Có mã yêu cầu để người dùng gửi cho Ban quản trị
        Assert.Matches(@"gửi mã này cho Ban quản trị: <code>[^<\s]+</code>", page.Html);
    }

    [SkippableFact]
    [Trait("TestId", "R20")]
    public async Task UnhandledException_ShowsErrorPage_With500_AndNoInternalDetails()
    {
        SkipIfNoDatabase();
        await using var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ThrowingStartupFilter>()));
        var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        var page = await UiFlow.GetAsync(client, ThrowPath);

        Assert.Equal(HttpStatusCode.InternalServerError, page.Status);
        Assert.Contains("Đã xảy ra lỗi", page.Html);
        Assert.Contains("Vui lòng thử lại sau", page.Html);
        // Không lộ nội dung lỗi, tên lớp lỗi hay stack trace (NFR-013, bảo mật)
        Assert.DoesNotContain(InternalDetail, page.Html);
        Assert.DoesNotContain(nameof(InvalidOperationException), page.Html);
        Assert.DoesNotContain(nameof(ThrowingStartupFilter), page.Html);
        Assert.DoesNotContain("   at ", page.Html);
    }

    /// <summary>
    /// Thêm đường dẫn chỉ có trong test, cố tình ném lỗi. Gắn SAU pipeline của app (sau next(app)) để lỗi đi
    /// ngược qua UseExceptionHandler của Program.cs, giống một lỗi chưa xử lý thật trong controller hay trang.
    /// </summary>
    private sealed class ThrowingStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use((HttpContext ctx, RequestDelegate nextMiddleware) =>
                ctx.Request.Path == ThrowPath
                    ? throw new InvalidOperationException(InternalDetail)
                    : nextMiddleware(ctx));
        };
    }
}
