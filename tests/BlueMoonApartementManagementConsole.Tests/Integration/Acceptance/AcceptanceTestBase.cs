using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.Tests.Integration.Acceptance;

/// <summary>
/// Cơ sở cho các test acceptance tự động: mỗi test case trong docs/testing/acceptance_*.md
/// được gắn [Trait("Acceptance", "&lt;mã case&gt;")]. Mỗi "trình duyệt" là một HttpClient riêng (cookie riêng).
/// Lớp con phải gắn [Collection(SequentialDatabaseCollection.Name)] và IClassFixture&lt;BlueMoonAppFactory&gt;.
/// Cần PostgreSQL: đặt biến môi trường BLUEMOON_TEST_CONNECTION, nếu không các test sẽ bị skip.
/// </summary>
public abstract class AcceptanceTestBase(BlueMoonAppFactory factory) : IAsyncLifetime
{
    protected const string Pw = TestAccounts.Password;

    protected BlueMoonAppFactory Factory { get; } = factory;
    protected TestAccounts Accounts { get; } = new(factory);

    protected static void SkipIfNoDatabase() =>
        Skip.If(BlueMoonAppFactory.ConnectionString is null, BlueMoonAppFactory.SkipReason);

    /// <summary>Một trình duyệt mới: cookie riêng, không tự đi theo redirect.</summary>
    protected HttpClient NewBrowser() => Factory.CreateHttpsClient();

    protected async Task<HttpClient> LoginAsync(string username, UserRole role, string password = Pw)
    {
        var browser = NewBrowser();
        await UiFlow.LoginOkAsync(browser, username, password, role);
        return browser;
    }

    protected Task<HttpClient> AdminAsync() =>
        LoginAsync(TestAccounts.AdminUsername, UserRole.BQT, BlueMoonAppFactory.AdminPassword);

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Accounts.CleanupAsync();
}
