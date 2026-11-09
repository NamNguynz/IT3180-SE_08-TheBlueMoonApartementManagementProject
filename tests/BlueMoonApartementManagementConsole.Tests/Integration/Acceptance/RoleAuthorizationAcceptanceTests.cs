using System.Net;
using System.Text.RegularExpressions;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.Tests.Integration.Acceptance;

/// <summary>
/// Tự động hóa docs/testing/acceptance_role_authorization.md (B1 – B8).
/// Cây tài khoản ở mục 3.1 của tài liệu được tạo thẳng trong DB (trừ B2, B8: đi qua luồng thật).
/// </summary>
[Collection(SequentialDatabaseCollection.Name)]
public class RoleAuthorizationAcceptanceTests(BlueMoonAppFactory factory)
    : AcceptanceTestBase(factory), IClassFixture<BlueMoonAppFactory>
{
    private const string RegisterOk = "Đăng ký thành công. Bạn có thể đăng nhập ngay.";
    private const string RegisterPending = "Đăng ký thành công. Tài khoản cần được Ban quản trị khác xác nhận trước khi đăng nhập.";
    private const string UsernameTaken = "Tài khoản này đã tồn tại.";

    private static readonly (string Path, string Title)[] AdminPages =
    [
        ("/quan-tri/tai-khoan", "Tài khoản & phân quyền"),
        ("/quan-tri/duyet-tai-khoan", "Duyệt tài khoản"),
        ("/quan-tri/nhat-ky", "Nhật ký thao tác")
    ];

    private static readonly string[] AdminApis = ["/api/tai-khoan", "/api/tai-khoan/cho-xac-nhan", "/api/nhat-ky"];

    private static string RoleLine(string label) => $"Bạn đang đăng nhập với vai trò <strong>{label}</strong>.";

    [SkippableFact]
    [Trait("Acceptance", "B1")]
    public async Task B1_OwnAccounts_DuplicateRejected_RoleShownAfterLogin()
    {
        SkipIfNoDatabase();
        var adminId = await Accounts.AdminIdAsync();
        var bqtA = await Accounts.SeedAsync("bqt_a", UserRole.BQT, grantedById: adminId);
        var ketoanA1 = await Accounts.SeedAsync("ketoan_a1", UserRole.KE_TOAN, grantedById: bqtA.Id);
        var cudan = Accounts.Name("cudan_01");
        var browser = NewBrowser();

        Assert.Equal(RegisterOk, (await UiFlow.RegisterAsync(browser, cudan, Pw, "Cu Dan 01", UserRole.CU_DAN)).Alert);
        Assert.Equal(UsernameTaken, (await UiFlow.RegisterAsync(browser, cudan, "Khac@1234", "Trung ten", UserRole.KE_TOAN)).Alert);
        Assert.Equal(UsernameTaken, (await UiFlow.RegisterAsync(browser, bqtA.Username, Pw, "Trung ten", UserRole.CU_DAN)).Alert);

        Assert.Equal(UiFlow.WrongCredentials, (await UiFlow.LoginAsync(browser, cudan, Pw, UserRole.KE_TOAN)).Alert);

        (string Username, UserRole Role, string Label, string FullName)[] cases =
        [
            (cudan, UserRole.CU_DAN, "Cư Dân", "Cu Dan 01"),
            (ketoanA1.Username, UserRole.KE_TOAN, "Kế Toán", ketoanA1.FullName),
            (bqtA.Username, UserRole.BQT, "Ban Quản Trị", bqtA.FullName)
        ];
        foreach (var c in cases)
        {
            var home = await UiFlow.GetAsync(await LoginAsync(c.Username, c.Role), "/");
            Assert.Contains(RoleLine(c.Label), home.Html);
            Assert.Contains(c.FullName, home.Html);
        }
    }

    [SkippableFact]
    [Trait("Acceptance", "B2")]
    public async Task B2_SelfRegisteredAccountant_WaitsForApproval_ThenCanLogin()
    {
        SkipIfNoDatabase();
        var name = Accounts.Name("ketoan_tudk");
        var browser = NewBrowser();

        Assert.Equal(RegisterPending, (await UiFlow.RegisterAsync(browser, name, Pw, "Ke toan tu dang ky", UserRole.KE_TOAN)).Alert);
        Assert.Equal(UiFlow.PendingMessage, (await UiFlow.LoginAsync(browser, name, Pw, UserRole.KE_TOAN)).Alert);

        var admin = await AdminAsync();
        var pending = await UiFlow.GetJsonAsync<List<PendingUserDto>>(admin, "/api/tai-khoan/cho-xac-nhan");
        var row = Assert.Single(pending, p => p.Username == name);

        Assert.Equal(HttpStatusCode.NoContent, (await UiFlow.PostAsync(admin, $"/api/tai-khoan/{row.Id}/xac-nhan")).StatusCode);
        Assert.DoesNotContain(await UiFlow.GetJsonAsync<List<PendingUserDto>>(admin, "/api/tai-khoan/cho-xac-nhan"), p => p.Username == name);

        await UiFlow.LoginOkAsync(browser, name, Pw, UserRole.KE_TOAN);
        Assert.Contains(RoleLine("Kế Toán"), (await UiFlow.GetAsync(browser, "/")).Html);
    }

    [SkippableFact]
    [Trait("Acceptance", "B3")]
    public async Task B3_MenuGroups_DependOnRole()
    {
        SkipIfNoDatabase();
        var adminId = await Accounts.AdminIdAsync();
        var bqtA = await Accounts.SeedAsync("bqt_a", UserRole.BQT, grantedById: adminId);
        var ketoan = await Accounts.SeedAsync("ketoan_a1", UserRole.KE_TOAN, grantedById: bqtA.Id);
        var cudan = await Accounts.SeedAsync("cudan_01", UserRole.CU_DAN);

        var expected = new[]
        {
            (bqtA.Username, UserRole.BQT, new[] { "Căn hộ & cư dân", "Tài chính", "Báo cáo", "Quản trị" }),
            (ketoan.Username, UserRole.KE_TOAN, new[] { "Tài chính", "Báo cáo" }),
            (cudan.Username, UserRole.CU_DAN, Array.Empty<string>())
        };
        foreach (var (username, role, groups) in expected)
        {
            var html = (await UiFlow.GetAsync(await LoginAsync(username, role), "/")).Html;
            var shown = Regex.Matches(html, "<div class=\"nav-title\">(.*?)</div>").Select(m => m.Groups[1].Value).ToArray();

            Assert.Equal(groups, shown);
            Assert.Contains(">Trang chủ</a>", html);
        }
    }

    [SkippableFact]
    [Trait("Acceptance", "B4")]
    public async Task B4_AdminPages_BlockedByUrl_ForNonAdmin_AndAnonymous()
    {
        SkipIfNoDatabase();
        var adminId = await Accounts.AdminIdAsync();
        var bqtA = await Accounts.SeedAsync("bqt_a", UserRole.BQT, grantedById: adminId);
        var ketoan = await Accounts.SeedAsync("ketoan_a1", UserRole.KE_TOAN, grantedById: bqtA.Id);
        var cudan = await Accounts.SeedAsync("cudan_01", UserRole.CU_DAN);

        foreach (var (username, role) in new[] { (ketoan.Username, UserRole.KE_TOAN), (cudan.Username, UserRole.CU_DAN) })
        {
            var browser = await LoginAsync(username, role);
            foreach (var (path, title) in AdminPages)
            {
                var page = await UiFlow.GetAsync(browser, path);
                Assert.True(page.IsRedirect, $"{username} mở được {path}");
                Assert.DoesNotContain($"<h1>{title}</h1>", page.Html);
                // Ghi nhận F1: bị đưa tới /403, nhưng app chưa có trang /403 nên hiện "Not Found"
                Assert.Equal("/403", page.RedirectPath);
            }
            Assert.Contains("Not Found", (await UiFlow.GetAsync(browser, "/403")).Html);
        }

        var anonymous = NewBrowser();
        foreach (var (path, _) in AdminPages)
            Assert.Equal("/login?ReturnUrl=" + Uri.EscapeDataString(path), (await UiFlow.GetAsync(anonymous, path)).RedirectPath);

        var bqt = await LoginAsync(bqtA.Username, UserRole.BQT);
        foreach (var (path, title) in AdminPages)
        {
            var page = await UiFlow.GetAsync(bqt, path);
            Assert.Equal(HttpStatusCode.OK, page.Status);
            Assert.Contains($"<h1>{title}</h1>", page.Html);
        }
    }

    [SkippableFact]
    [Trait("Acceptance", "B5")]
    public async Task B5_AdminApis_Return403ForNonAdmin_401ForAnonymous_200ForAdmin()
    {
        SkipIfNoDatabase();
        var adminId = await Accounts.AdminIdAsync();
        var bqtA = await Accounts.SeedAsync("bqt_a", UserRole.BQT, grantedById: adminId);
        var ketoan = await Accounts.SeedAsync("ketoan_a1", UserRole.KE_TOAN, grantedById: bqtA.Id);
        var cudan = await Accounts.SeedAsync("cudan_01", UserRole.CU_DAN);

        (HttpClient Browser, HttpStatusCode Expected)[] callers =
        [
            (await LoginAsync(ketoan.Username, UserRole.KE_TOAN), HttpStatusCode.Forbidden),
            (await LoginAsync(cudan.Username, UserRole.CU_DAN), HttpStatusCode.Forbidden),
            (NewBrowser(), HttpStatusCode.Unauthorized),
            (await LoginAsync(bqtA.Username, UserRole.BQT), HttpStatusCode.OK)
        ];
        foreach (var (browser, expected) in callers)
            foreach (var api in AdminApis)
            {
                var response = await browser.GetAsync(api);
                Assert.Equal(expected, response.StatusCode);
                if (expected == HttpStatusCode.OK)
                    Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            }
    }

    [SkippableFact]
    [Trait("Acceptance", "B6")]
    public async Task B6_ManageScope_FollowsGrantTree()
    {
        SkipIfNoDatabase();
        var adminId = await Accounts.AdminIdAsync();
        var bqtA = await Accounts.SeedAsync("bqt_a", UserRole.BQT, grantedById: adminId);
        var bqtB = await Accounts.SeedAsync("bqt_b", UserRole.BQT, grantedById: adminId);
        var ketoanA1 = await Accounts.SeedAsync("ketoan_a1", UserRole.KE_TOAN, grantedById: bqtA.Id);
        var ketoanTudk = await Accounts.SeedAsync("ketoan_tudk", UserRole.KE_TOAN, grantedById: adminId);
        var cudan = await Accounts.SeedAsync("cudan_01", UserRole.CU_DAN);

        async Task<Dictionary<string, AccountDto>> RowsSeenBy(HttpClient browser) =>
            (await UiFlow.GetJsonAsync<List<AccountDto>>(browser, "/api/tai-khoan"))
                .Where(a => a.Username == TestAccounts.AdminUsername || a.Username.StartsWith(Accounts.Prefix))
                .ToDictionary(a => a.Username);

        var byBqtA = await RowsSeenBy(await LoginAsync(bqtA.Username, UserRole.BQT));
        var byBqtB = await RowsSeenBy(await LoginAsync(bqtB.Username, UserRole.BQT));
        var byAdmin = await RowsSeenBy(await AdminAsync());

        // Bảng ở B6: dòng tài khoản | bqt_a | bqt_b | admin (true = có nút thao tác)
        var expected = new[]
        {
            (TestAccounts.AdminUsername, false, false, false),
            (bqtA.Username, false, false, true),
            (bqtB.Username, false, false, true),
            (ketoanA1.Username, true, false, true),
            (ketoanTudk.Username, false, false, true),
            (cudan.Username, true, true, true)
        };
        foreach (var (row, a, b, admin) in expected)
        {
            Assert.True(byBqtA[row].CanManage == a, $"bqt_a với dòng {row}");
            Assert.True(byBqtB[row].CanManage == b, $"bqt_b với dòng {row}");
            Assert.True(byAdmin[row].CanManage == admin, $"admin với dòng {row}");
        }

        Assert.Equal(bqtA.Id, byAdmin[ketoanA1.Username].GrantedById);
        Assert.Equal(adminId, byAdmin[ketoanTudk.Username].GrantedById);
    }

    [SkippableFact]
    [Trait("Acceptance", "B7")]
    public async Task B7_DisableTakesEffectImmediately_EnableRestoresAccess()
    {
        SkipIfNoDatabase();
        var adminId = await Accounts.AdminIdAsync();
        var bqtA = await Accounts.SeedAsync("bqt_a", UserRole.BQT, grantedById: adminId);
        var ketoan = await Accounts.SeedAsync("ketoan_a1", UserRole.KE_TOAN, grantedById: bqtA.Id);

        var browser2 = await LoginAsync(ketoan.Username, UserRole.KE_TOAN);
        var browser1 = await LoginAsync(bqtA.Username, UserRole.BQT);

        Assert.Equal(HttpStatusCode.NoContent, (await UiFlow.PostAsync(browser1, $"/api/tai-khoan/{ketoan.Id}/vo-hieu-hoa")).StatusCode);
        var row = (await UiFlow.GetJsonAsync<List<AccountDto>>(browser1, "/api/tai-khoan")).Single(a => a.Id == ketoan.Id);
        Assert.False(row.IsActive);

        var refreshed = await UiFlow.GetAsync(browser2, "/");
        Assert.StartsWith("/login", refreshed.RedirectPath);
        Assert.Equal(UiFlow.WrongCredentials, (await UiFlow.LoginAsync(browser2, ketoan.Username, Pw, UserRole.KE_TOAN)).Alert);

        Assert.Equal(HttpStatusCode.NoContent, (await UiFlow.PostAsync(browser1, $"/api/tai-khoan/{ketoan.Id}/kich-hoat")).StatusCode);
        Assert.True((await UiFlow.LoginAsync(browser2, ketoan.Username, Pw, UserRole.KE_TOAN)).IsRedirect);
    }

    [SkippableFact]
    [Trait("Acceptance", "B8")]
    public async Task B8_AuditLog_RecordsTheRealActor()
    {
        SkipIfNoDatabase();
        var mark = await Accounts.LastLogIdAsync();
        var admin = await AdminAsync();

        // admin tạo bqt_a, bqt_b. bqt_a đổi mật khẩu tạm rồi tạo ketoan_a1
        var bqtA = await UiFlow.CreateAccountAsync(admin, Accounts.Name("bqt_a"), UserRole.BQT);
        var bqtB = await UiFlow.CreateAccountAsync(admin, Accounts.Name("bqt_b"), UserRole.BQT);
        var browserA = await LoginAsync(Accounts.Name("bqt_a"), UserRole.BQT, bqtA.TempPassword);
        Assert.Equal(HttpStatusCode.NoContent, (await UiFlow.ChangePasswordAsync(browserA, bqtA.TempPassword, Pw)).StatusCode);
        var ketoanA1 = await UiFlow.CreateAccountAsync(browserA, Accounts.Name("ketoan_a1"), UserRole.KE_TOAN);

        // admin xác nhận ketoan_tudk tự đăng ký
        var tudk = Accounts.Name("ketoan_tudk");
        await UiFlow.RegisterAsync(NewBrowser(), tudk, Pw, "Ke toan tu dang ky", UserRole.KE_TOAN);
        var tudkId = (await Accounts.FindAsync(tudk))!.Id;
        Assert.Equal(HttpStatusCode.NoContent, (await UiFlow.PostAsync(admin, $"/api/tai-khoan/{tudkId}/xac-nhan")).StatusCode);

        // bqt_a vô hiệu hóa rồi kích hoạt ketoan_a1
        Assert.Equal(HttpStatusCode.NoContent, (await UiFlow.PostAsync(browserA, $"/api/tai-khoan/{ketoanA1.Id}/vo-hieu-hoa")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await UiFlow.PostAsync(browserA, $"/api/tai-khoan/{ketoanA1.Id}/kich-hoat")).StatusCode);

        // cudan_01 đăng nhập sai tư cách
        var cudan = Accounts.Name("cudan_01");
        await UiFlow.RegisterAsync(NewBrowser(), cudan, Pw, "Cu Dan 01", UserRole.CU_DAN);
        await UiFlow.LoginAsync(NewBrowser(), cudan, Pw, UserRole.KE_TOAN);

        var logs = await Accounts.LogsAfterAsync(mark);
        void AssertLogged(AuditAction action, string actor, int? target)
        {
            Assert.Contains(logs, l => l.Action == action && l.Username == actor
                && (target is null || l.Target == target.Value.ToString()));
        }

        AssertLogged(AuditAction.CreateAccount, TestAccounts.AdminUsername, bqtA.Id);
        AssertLogged(AuditAction.CreateAccount, TestAccounts.AdminUsername, bqtB.Id);
        AssertLogged(AuditAction.CreateAccount, Accounts.Name("bqt_a"), ketoanA1.Id);
        AssertLogged(AuditAction.ApproveAccount, TestAccounts.AdminUsername, tudkId);
        AssertLogged(AuditAction.DisableAccount, Accounts.Name("bqt_a"), ketoanA1.Id);
        AssertLogged(AuditAction.EnableAccount, Accounts.Name("bqt_a"), ketoanA1.Id);
        Assert.Contains(logs, l => l.Action == AuditAction.LoginFailed && l.Username == cudan
            && l.Detail == "Sai tư cách đăng nhập: chọn KE_TOAN");
    }
}
