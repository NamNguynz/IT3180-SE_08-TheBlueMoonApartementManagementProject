using System.Net;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.Tests.Integration.Acceptance;

/// <summary>
/// Tự động hóa docs/testing/acceptance_password_hashing.md (A1 – A5).
/// Các lệnh psql trong tài liệu được thay bằng đọc thẳng database test.
/// Còn chạy tay: A4 bước 4 ("Mật khẩu nhập lại không khớp" chỉ được kiểm bằng JavaScript trên trình duyệt).
/// </summary>
[Collection(SequentialDatabaseCollection.Name)]
public class PasswordHashingAcceptanceTests(BlueMoonAppFactory factory)
    : AcceptanceTestBase(factory), IClassFixture<BlueMoonAppFactory>
{
    private const string WrongPassword = "SaiMatKhau@1";
    private const string NewPassword = "NewPass@02";

    [SkippableFact]
    [Trait("Acceptance", "A1")]
    public async Task A1_Register_StoresSaltedHash_NotPlainPassword()
    {
        SkipIfNoDatabase();
        var browser = NewBrowser();
        var (name1, name2, name3) = (Accounts.Name("at01"), Accounts.Name("at02"), Accounts.Name("at03"));

        var r1 = await UiFlow.RegisterAsync(browser, name1, Pw, "Cu dan AT 01", UserRole.CU_DAN);
        var r2 = await UiFlow.RegisterAsync(browser, name2, Pw, "Cu dan AT 02", UserRole.CU_DAN);
        var r3 = await UiFlow.RegisterAsync(browser, name3, "Abc@123", "Cu dan AT 03", UserRole.CU_DAN);

        Assert.Equal("Đăng ký thành công. Bạn có thể đăng nhập ngay.", r1.Alert);
        Assert.Equal("Đăng ký thành công. Bạn có thể đăng nhập ngay.", r2.Alert);
        Assert.Contains("Mật khẩu tối thiểu 8 ký tự.", r3.Html);

        var u1 = await Accounts.FindAsync(name1);
        var u2 = await Accounts.FindAsync(name2);
        Assert.DoesNotContain(Pw, u1!.PasswordHash);
        Assert.Equal(84, u1.PasswordHash.Length);           // base64 của hash PBKDF2 (định dạng V3)
        Assert.NotEqual(u1.PasswordHash, u2!.PasswordHash);  // có salt: cùng mật khẩu, hash khác nhau
        Assert.Null(await Accounts.FindAsync(name3));        // mật khẩu 7 ký tự thì không tạo tài khoản
    }

    [SkippableFact]
    [Trait("Acceptance", "A2")]
    public async Task A2_Login_WrongRoleOrPassword_SameMessage_LocksAfterFiveFailures()
    {
        SkipIfNoDatabase();
        var user = await Accounts.SeedAsync("at01", UserRole.CU_DAN);
        var browser = NewBrowser();

        Assert.True((await UiFlow.LoginAsync(browser, user.Username, Pw, UserRole.CU_DAN)).IsRedirect);
        await UiFlow.LogoutAsync(browser);

        Assert.Equal(UiFlow.WrongCredentials, (await UiFlow.LoginAsync(browser, user.Username, Pw, UserRole.KE_TOAN)).Alert);

        for (var i = 1; i <= 4; i++)
            Assert.Equal(UiFlow.WrongCredentials, (await UiFlow.LoginAsync(browser, user.Username, WrongPassword, UserRole.CU_DAN)).Alert);
        var beforeFifth = DateTimeOffset.UtcNow;
        Assert.Equal(UiFlow.WrongCredentials, (await UiFlow.LoginAsync(browser, user.Username, WrongPassword, UserRole.CU_DAN)).Alert);
        var afterFifth = DateTimeOffset.UtcNow;

        Assert.Equal(UiFlow.LockedMessage, (await UiFlow.LoginAsync(browser, user.Username, Pw, UserRole.CU_DAN)).Alert);

        var locked = await Accounts.FindAsync(user.Username);
        Assert.NotNull(locked!.LockedUntil);
        Assert.InRange(locked.LockedUntil!.Value, beforeFifth.AddMinutes(15).AddSeconds(-1), afterFifth.AddMinutes(15).AddSeconds(1));

        var ghost = await UiFlow.LoginAsync(browser, Accounts.Name("khongtontai"), Pw, UserRole.CU_DAN);
        Assert.Equal(UiFlow.WrongCredentials, ghost.Alert);
    }

    [SkippableFact]
    [Trait("Acceptance", "A3")]
    public async Task A3_AdminResetsPassword_TempPasswordShownOnce_OldPasswordRejected_ForcedToProfile()
    {
        SkipIfNoDatabase();
        var user = await Accounts.SeedAsync("at01", UserRole.CU_DAN);
        var admin = await AdminAsync();

        var temp = await UiFlow.ResetPasswordAsync(admin, user.Id);
        Assert.Equal(12, temp.Length);

        // Tải lại trang Tài khoản: danh sách không còn chứa mật khẩu tạm
        Assert.DoesNotContain(temp, await admin.GetStringAsync("/api/tai-khoan"));

        var browser = NewBrowser();
        Assert.Equal(UiFlow.WrongCredentials, (await UiFlow.LoginAsync(browser, user.Username, Pw, UserRole.CU_DAN)).Alert);
        Assert.True((await UiFlow.LoginAsync(browser, user.Username, temp, UserRole.CU_DAN)).IsRedirect);

        Assert.Equal("/ho-so", (await UiFlow.GetAsync(browser, "/")).RedirectPath);
        Assert.Contains("Mật khẩu hiện tại là mật khẩu tạm do Ban quản trị cấp", (await UiFlow.GetAsync(browser, "/ho-so")).Html);

        var stored = await Accounts.FindAsync(user.Username);
        Assert.NotEqual(user.PasswordHash, stored!.PasswordHash);
        Assert.DoesNotContain(temp, stored.PasswordHash);
    }

    [SkippableFact]
    [Trait("Acceptance", "A4")]
    public async Task A4_ChangeOwnPassword_RejectsInvalid_ThenNewPasswordWorks()
    {
        // Bước 4 (nhập lại mật khẩu không khớp) do JavaScript kiểm, không gửi lên server: vẫn chạy tay
        SkipIfNoDatabase();
        var user = await Accounts.SeedAsync("at01", UserRole.CU_DAN);
        var browser = await LoginAsync(user.Username, UserRole.CU_DAN);

        async Task AssertRejected(string current, string next, string message)
        {
            var response = await UiFlow.ChangePasswordAsync(browser, current, next);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(message, (await UiFlow.ReadErrorAsync(response)).Message);
        }

        await AssertRejected(WrongPassword, NewPassword, "Mật khẩu hiện tại không đúng.");
        await AssertRejected(Pw, "Abc@123", "Mật khẩu mới phải từ 8 đến 100 ký tự.");
        await AssertRejected(Pw, Pw, "Mật khẩu mới phải khác mật khẩu hiện tại.");

        Assert.Equal(HttpStatusCode.NoContent, (await UiFlow.ChangePasswordAsync(browser, Pw, NewPassword)).StatusCode);

        await UiFlow.LogoutAsync(browser);
        Assert.Equal(UiFlow.WrongCredentials, (await UiFlow.LoginAsync(browser, user.Username, Pw, UserRole.CU_DAN)).Alert);
        Assert.True((await UiFlow.LoginAsync(browser, user.Username, NewPassword, UserRole.CU_DAN)).IsRedirect);
    }

    [SkippableFact]
    [Trait("Acceptance", "A5")]
    public async Task A5_AuditLog_ContainsNoPasswords()
    {
        SkipIfNoDatabase();
        var mark = await Accounts.LastLogIdAsync();
        var name = Accounts.Name("at01");
        var browser = NewBrowser();

        await UiFlow.RegisterAsync(browser, name, Pw, "Cu dan AT 01", UserRole.CU_DAN);
        for (var i = 0; i < 5; i++)
            await UiFlow.LoginAsync(browser, name, WrongPassword, UserRole.CU_DAN);
        Assert.Equal(UiFlow.LockedMessage, (await UiFlow.LoginAsync(browser, name, Pw, UserRole.CU_DAN)).Alert);

        var admin = await AdminAsync();
        var temp = await UiFlow.ResetPasswordAsync(admin, (await Accounts.FindAsync(name))!.Id);
        await UiFlow.LoginOkAsync(browser, name, temp, UserRole.CU_DAN);
        Assert.Equal(HttpStatusCode.NoContent, (await UiFlow.ChangePasswordAsync(browser, temp, NewPassword)).StatusCode);

        var logs = await Accounts.LogsAfterAsync(mark);
        foreach (var action in new[] { AuditAction.Register, AuditAction.LoginFailed, AuditAction.LoginLocked,
                                       AuditAction.ResetPassword, AuditAction.ChangePassword })
            Assert.Contains(logs, l => l.Action == action);

        foreach (var secret in new[] { Pw, WrongPassword, NewPassword, temp })
            Assert.DoesNotContain(logs, l => $"{l.Username}|{l.Target}|{l.Detail}".Contains(secret, StringComparison.Ordinal));
    }
}
