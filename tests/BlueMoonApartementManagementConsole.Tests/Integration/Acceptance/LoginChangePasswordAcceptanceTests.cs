using System.Net;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.Tests.Integration.Acceptance;

/// <summary>
/// Tự động hóa docs/testing/acceptance_login_change_password.md: C1 – C4, C7 – C10.
/// C5 (thuộc tính cookie) đã có ở SessionTimeoutTests.S7, C6 (hết phiên) ở SessionTimeoutTests.S2 – S5.
/// </summary>
[Collection(SequentialDatabaseCollection.Name)]
public class LoginChangePasswordAcceptanceTests(BlueMoonAppFactory factory)
    : AcceptanceTestBase(factory), IClassFixture<BlueMoonAppFactory>
{
    private const string NewPassword = "NewPass@02";

    [SkippableFact]
    [Trait("Acceptance", "C1")]
    public async Task C1_AfterLogin_ReturnsToRequestedPage_OtherwiseHome()
    {
        SkipIfNoDatabase();
        var browser = NewBrowser();

        var blocked = await UiFlow.GetAsync(browser, "/quan-tri/nhat-ky");
        Assert.Equal("/login?ReturnUrl=%2Fquan-tri%2Fnhat-ky", blocked.RedirectPath);

        var login = await UiFlow.LoginAsync(browser, TestAccounts.AdminUsername, BlueMoonAppFactory.AdminPassword, UserRole.BQT, "/quan-tri/nhat-ky");
        Assert.Equal("/quan-tri/nhat-ky", login.RedirectPath);

        await UiFlow.LogoutAsync(browser);
        var plain = await UiFlow.LoginAsync(browser, TestAccounts.AdminUsername, BlueMoonAppFactory.AdminPassword, UserRole.BQT);
        Assert.Equal("/", plain.RedirectPath);
    }

    [SkippableTheory]
    [Trait("Acceptance", "C2")]
    [InlineData("https://example.com")]
    [InlineData("//example.com")]
    [InlineData("/\\example.com")]
    public async Task C2_ReturnUrlToOtherSite_StaysOnSite(string returnUrl)
    {
        // Kiểm chứng G1: với "/\example.com", server redirect tới https://localhost//example.com,
        // tức vẫn ở chính site (trang Not Found), không ra trang ngoài.
        SkipIfNoDatabase();
        var login = await UiFlow.LoginAsync(NewBrowser(), TestAccounts.AdminUsername, BlueMoonAppFactory.AdminPassword, UserRole.BQT, returnUrl);

        Assert.True(login.IsRedirect);
        var target = login.Location!.IsAbsoluteUri ? login.Location : new Uri(new Uri("https://localhost"), login.Location);
        Assert.Equal("localhost", target.Host);
    }

    [SkippableFact]
    [Trait("Acceptance", "C3")]
    public async Task C3_ErrorMessages_DoNotRevealWhatWasWrong()
    {
        SkipIfNoDatabase();
        var resident = await Accounts.SeedAsync("cudan_lg", UserRole.CU_DAN);
        var pending = await Accounts.SeedAsync("ketoan_lg", UserRole.KE_TOAN, UserStatus.PendingApproval);
        var browser = NewBrowser();

        var wrongPassword = await UiFlow.LoginAsync(browser, resident.Username, "SaiMatKhau@1", UserRole.CU_DAN);
        Assert.Equal(UiFlow.WrongCredentials, wrongPassword.Alert);
        Assert.Equal(resident.Username, wrongPassword.InputValue("username"));   // bước 7: giữ tài khoản
        Assert.Equal(string.Empty, wrongPassword.InputValue("password"));       // bước 7: xóa mật khẩu

        Assert.Equal(UiFlow.WrongCredentials, (await UiFlow.LoginAsync(browser, Accounts.Name("khongtontai"), Pw, UserRole.CU_DAN)).Alert);
        Assert.Equal(UiFlow.WrongCredentials, (await UiFlow.LoginAsync(browser, pending.Username, "SaiMatKhau@1", UserRole.KE_TOAN)).Alert);
        Assert.Equal(UiFlow.PendingMessage, (await UiFlow.LoginAsync(browser, pending.Username, Pw, UserRole.KE_TOAN)).Alert);

        Assert.Contains("Vui lòng nhập tài khoản.", (await UiFlow.LoginAsync(browser, "", Pw, UserRole.CU_DAN)).Html);
        Assert.Contains("Vui lòng nhập mật khẩu.", (await UiFlow.LoginAsync(browser, resident.Username, "", UserRole.CU_DAN)).Html);
    }

    [SkippableFact]
    [Trait("Acceptance", "C4")]
    public async Task C4_AdminUnlocksLockedAccount_UserCanLoginImmediately()
    {
        SkipIfNoDatabase();
        var resident = await Accounts.SeedAsync("cudan_lg", UserRole.CU_DAN);
        var browser = NewBrowser();
        for (var i = 0; i < 5; i++)
            await UiFlow.LoginAsync(browser, resident.Username, "SaiMatKhau@1", UserRole.CU_DAN);
        Assert.Equal(UiFlow.LockedMessage, (await UiFlow.LoginAsync(browser, resident.Username, Pw, UserRole.CU_DAN)).Alert);

        var mark = await Accounts.LastLogIdAsync();
        var admin = await AdminAsync();
        AccountDto Row(List<AccountDto> rows) => rows.Single(a => a.Id == resident.Id);

        Assert.NotNull(Row(await UiFlow.GetJsonAsync<List<AccountDto>>(admin, "/api/tai-khoan")).LockedUntil);
        Assert.Equal(HttpStatusCode.NoContent, (await UiFlow.PostAsync(admin, $"/api/tai-khoan/{resident.Id}/mo-khoa")).StatusCode);
        Assert.Null(Row(await UiFlow.GetJsonAsync<List<AccountDto>>(admin, "/api/tai-khoan")).LockedUntil);

        Assert.True((await UiFlow.LoginAsync(browser, resident.Username, Pw, UserRole.CU_DAN)).IsRedirect);

        var logs = await Accounts.LogsAfterAsync(mark);
        Assert.Contains(logs, l => l.Action == AuditAction.UnlockAccount && l.Username == TestAccounts.AdminUsername
            && l.Target == resident.Id.ToString());
    }

    [SkippableFact]
    [Trait("Acceptance", "C7")]
    public async Task C7_Logout_EndsSession_AndIsLogged()
    {
        SkipIfNoDatabase();
        var resident = await Accounts.SeedAsync("cudan_lg", UserRole.CU_DAN);
        var browser = await LoginAsync(resident.Username, UserRole.CU_DAN);

        Assert.Equal("/login", (await UiFlow.LogoutAsync(browser)).RedirectPath);
        Assert.StartsWith("/login", (await UiFlow.GetAsync(browser, "/")).RedirectPath);      // Back rồi F5
        Assert.StartsWith("/login", (await UiFlow.GetAsync(browser, "/ho-so")).RedirectPath);

        var admin = await AdminAsync();
        var logouts = await UiFlow.GetJsonAsync<PagedResult<AuditLogDto>>(admin,
            $"/api/nhat-ky?action=Logout&username={Uri.EscapeDataString(resident.Username)}");
        Assert.Equal(1, logouts.Total);
    }

    // Đã xác nhận G2 (09/11/2026): sau khi trình duyệt 1 đổi mật khẩu, trình duyệt 2 vẫn mở được /ho-so (200).
    // Bỏ Skip sau khi sửa (vd: lưu "phiên bản bảo mật" của tài khoản và so trong OnValidatePrincipal).
    [SkippableFact(Skip = "Chờ fix G2: đổi mật khẩu không đăng xuất các phiên khác của cùng tài khoản")]
    [Trait("Acceptance", "C8")]
    public async Task C8_ChangePassword_SignsOutOtherSessions()
    {
        SkipIfNoDatabase();
        var resident = await Accounts.SeedAsync("cudan_lg", UserRole.CU_DAN);
        var browser1 = await LoginAsync(resident.Username, UserRole.CU_DAN);
        var browser2 = await LoginAsync(resident.Username, UserRole.CU_DAN);

        Assert.Equal(HttpStatusCode.NoContent, (await UiFlow.ChangePasswordAsync(browser1, Pw, NewPassword)).StatusCode);

        Assert.StartsWith("/login", (await UiFlow.GetAsync(browser2, "/ho-so")).RedirectPath);
        Assert.Equal(HttpStatusCode.OK, (await UiFlow.GetAsync(browser1, "/ho-so")).Status);
    }

    [SkippableFact]
    [Trait("Acceptance", "C9")]
    public async Task C9_TemporaryPassword_BlocksEverythingUntilChanged()
    {
        SkipIfNoDatabase();
        var admin = await AdminAsync();
        var name = Accounts.Name("bqt_c");
        var created = await UiFlow.CreateAccountAsync(admin, name, UserRole.BQT);
        Assert.Equal(12, created.TempPassword.Length);

        var browser = await LoginAsync(name, UserRole.BQT, created.TempPassword);
        Assert.Equal("/ho-so", (await UiFlow.GetAsync(browser, "/")).RedirectPath);
        Assert.Contains("Mật khẩu hiện tại là mật khẩu tạm", (await UiFlow.GetAsync(browser, "/ho-so")).Html);
        Assert.Equal("/ho-so", (await UiFlow.GetAsync(browser, "/quan-tri/tai-khoan")).RedirectPath);

        var api = await browser.GetAsync("/api/tai-khoan");
        Assert.Equal(HttpStatusCode.Forbidden, api.StatusCode);
        Assert.Equal("PASSWORD_CHANGE_REQUIRED", (await UiFlow.ReadErrorAsync(api)).Code);

        Assert.Equal("/login", (await UiFlow.LogoutAsync(browser)).RedirectPath);

        await UiFlow.LoginOkAsync(browser, name, created.TempPassword, UserRole.BQT);
        Assert.Equal(HttpStatusCode.NoContent, (await UiFlow.ChangePasswordAsync(browser, created.TempPassword, Pw)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UiFlow.GetAsync(browser, "/quan-tri/tai-khoan")).Status);
    }

    [SkippableFact]
    [Trait("Acceptance", "C10")]
    public async Task C10_Username_IsTrimmed_ButCaseSensitive()
    {
        // Ghi nhận G4 (chờ PO): tên đăng nhập phân biệt chữ hoa, chữ thường
        SkipIfNoDatabase();
        var resident = await Accounts.SeedAsync("cudan_lg", UserRole.CU_DAN);

        Assert.True((await UiFlow.LoginAsync(NewBrowser(), $"  {resident.Username}  ", Pw, UserRole.CU_DAN)).IsRedirect);
        Assert.Equal(UiFlow.WrongCredentials,
            (await UiFlow.LoginAsync(NewBrowser(), resident.Username.ToUpperInvariant(), Pw, UserRole.CU_DAN)).Alert);
    }
}
