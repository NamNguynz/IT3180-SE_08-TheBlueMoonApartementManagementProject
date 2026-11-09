using System.Net;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.Tests.Integration.Acceptance;

/// <summary>
/// Tự động hóa docs/testing/acceptance_session_timeout.md: T1, T3, phần server của T4, T7.
/// Đồng hồ của cookie là đồng hồ giả (BlueMoonAppFactory.Clock), nên "chờ 10 phút" chỉ mất vài mili giây.
/// T2 đã có ở SessionTimeoutTests.S5, T5 ở S4, T6 ở S7.
/// Còn chạy tay: T4 bước 5 (sau khi đăng nhập lại, app về trang nào: do JavaScript quyết định, lỗi H3).
/// </summary>
[Collection(SequentialDatabaseCollection.Name)]
public class SessionTimeoutAcceptanceTests(BlueMoonAppFactory factory)
    : AcceptanceTestBase(factory), IClassFixture<BlueMoonAppFactory>
{
    private const string LogPage = "/quan-tri/nhat-ky";

    private void Wait(int minutes, int seconds = 0) => Factory.Clock.Advance(new TimeSpan(0, minutes, seconds));

    [SkippableFact]
    [Trait("Acceptance", "T1")]
    public async Task T1_Idle10Minutes_SessionEnds_AndLoginReturnsToSamePage()
    {
        SkipIfNoDatabase();
        var browser = await AdminAsync();
        Assert.Equal(HttpStatusCode.OK, (await UiFlow.GetAsync(browser, LogPage)).Status);

        Wait(9, 30);
        Assert.Equal(HttpStatusCode.OK, (await UiFlow.GetAsync(browser, LogPage)).Status);

        Wait(10, 30); // t0 + 20:00, tức 10:30 sau lần F5 ở trên
        var expired = await UiFlow.GetAsync(browser, LogPage);
        Assert.Equal("/login?ReturnUrl=%2Fquan-tri%2Fnhat-ky", expired.RedirectPath);

        var relogin = await UiFlow.LoginAsync(browser, TestAccounts.AdminUsername, BlueMoonAppFactory.AdminPassword, UserRole.BQT, LogPage);
        Assert.Equal(LogPage, relogin.RedirectPath);
    }

    [SkippableFact]
    [Trait("Acceptance", "T3")]
    public async Task T3_ActionAfterSessionExpired_IsNotPerformed_NorLogged()
    {
        SkipIfNoDatabase();
        var resident = await Accounts.SeedAsync("cudan_tg", UserRole.CU_DAN);
        var browser = await AdminAsync();
        Assert.Equal(HttpStatusCode.OK, (await UiFlow.GetAsync(browser, "/quan-tri/tai-khoan")).Status);
        var mark = await Accounts.LastLogIdAsync();

        Wait(11);
        var disable = await UiFlow.PostAsync(browser, $"/api/tai-khoan/{resident.Id}/vo-hieu-hoa");

        Assert.Equal(HttpStatusCode.Unauthorized, disable.StatusCode);
        Assert.True((await Accounts.FindAsync(resident.Username))!.IsActive);
        Assert.DoesNotContain(await Accounts.LogsAfterAsync(mark), l => l.Action == AuditAction.DisableAccount);
    }

    [SkippableFact]
    [Trait("Acceptance", "T4")]
    public async Task T4_ChangePasswordAfterSessionExpired_IsRejected_PasswordUnchanged()
    {
        // Gõ phím trên form không gửi gì lên server, nên phiên vẫn hết hạn sau 10 phút (H2)
        SkipIfNoDatabase();
        var resident = await Accounts.SeedAsync("cudan_tg", UserRole.CU_DAN);
        var browser = await LoginAsync(resident.Username, UserRole.CU_DAN);
        Assert.Equal(HttpStatusCode.OK, (await UiFlow.GetAsync(browser, "/ho-so")).Status);

        Wait(11);
        var change = await UiFlow.ChangePasswordAsync(browser, Pw, "NewPass@02");

        Assert.Equal(HttpStatusCode.Unauthorized, change.StatusCode);
        Assert.True((await UiFlow.LoginAsync(NewBrowser(), resident.Username, Pw, UserRole.CU_DAN)).IsRedirect);
    }

    [SkippableFact]
    [Trait("Acceptance", "T7")]
    public async Task T7_SessionExpiry_IsNotWrittenToAuditLog()
    {
        // Ghi nhận H4 (chờ PO): hết phiên không có dòng nhật ký nào, chỉ có "Đăng xuất" khi bấm nút
        SkipIfNoDatabase();
        var resident = await Accounts.SeedAsync("cudan_tg", UserRole.CU_DAN);
        var browser = await LoginAsync(resident.Username, UserRole.CU_DAN);
        var mark = await Accounts.LastLogIdAsync();

        Wait(11);
        Assert.StartsWith("/login", (await UiFlow.GetAsync(browser, "/")).RedirectPath);

        Assert.DoesNotContain(await Accounts.LogsAfterAsync(mark), l => l.Username == resident.Username);
    }
}
