using System.Reflection;
using BlueMoonApartementManagementConsole.BackEnd.Controllers;
using BlueMoonApartementManagementConsole.Components.Pages;
using Microsoft.AspNetCore.Authorization;
using ComponentRoute = Microsoft.AspNetCore.Components.RouteAttribute;

namespace BlueMoonApartementManagementConsole.Tests.Security;

/// <summary>
/// Kiểm tra controller và trang có gắn đúng [Authorize] / policy, bằng reflection, không cần chạy app.
/// Việc policy nào ứng với vai trò nào nằm trong Program.cs, được kiểm bằng acceptance test.
/// </summary>
public class AuthorizationMetadataTests
{
    private const string AdminPolicy = "QuanTriHeThong";

    /// <summary>Các trang được phép truy cập khi chưa đăng nhập.</summary>
    private static readonly Type[] PublicPages = [typeof(Login), typeof(NotFound), typeof(Error)];

    private static AuthorizeAttribute[] AuthorizeOf(Type type) =>
        type.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToArray();

    // ===== R16: API controller =====

    [Theory]
    [Trait("TestId", "R16")]
    [InlineData(typeof(AccountController))]
    [InlineData(typeof(AccountApprovalController))]
    [InlineData(typeof(AuditLogController))]
    public void AdminControllers_RequireAdminPolicy(Type controller)
    {
        var attrs = AuthorizeOf(controller);

        Assert.Contains(attrs, a => a.Policy == AdminPolicy);
    }

    [Fact]
    [Trait("TestId", "R16")]
    public void ProfileController_RequiresAuthenticatedUser()
    {
        Assert.NotEmpty(AuthorizeOf(typeof(ProfileController)));
    }

    [Theory]
    [Trait("TestId", "R16")]
    [InlineData(typeof(AccountController))]
    [InlineData(typeof(AccountApprovalController))]
    [InlineData(typeof(AuditLogController))]
    [InlineData(typeof(ProfileController))]
    public void ControllerActions_DoNotBypassAuthorization(Type controller)
    {
        var anonymousActions = controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(anonymousActions);
    }

    // ===== R17: Trang =====

    [Theory]
    [Trait("TestId", "R17")]
    [InlineData(typeof(AccountManagementPage))]
    [InlineData(typeof(AccountApprovalPage))]
    [InlineData(typeof(AuditLogPage))]
    public void AdminPages_RequireAdminPolicy(Type page)
    {
        var attrs = AuthorizeOf(page);

        Assert.Contains(attrs, a => a.Policy == AdminPolicy);
    }

    [Theory]
    [Trait("TestId", "R17")]
    [InlineData(typeof(ProfilePage))]
    [InlineData(typeof(WelcomePage))]
    public void UserPages_RequireAuthenticatedUser(Type page)
    {
        Assert.NotEmpty(AuthorizeOf(page));
    }

    // ===== R18: Chống quên gắn quyền khi thêm trang mới =====

    [Fact]
    [Trait("TestId", "R18")]
    public void EveryRoutablePage_ExceptPublicOnes_RequiresAuthorization()
    {
        var routablePages = typeof(Login).Assembly.GetTypes()
            .Where(t => t.GetCustomAttributes<ComponentRoute>(inherit: false).Any())
            .ToList();

        // Đảm bảo test thực sự tìm thấy các trang, không pass vì danh sách rỗng
        Assert.True(routablePages.Count >= PublicPages.Length + 5,
            $"Chỉ tìm thấy {routablePages.Count} trang có @page.");

        var unprotected = routablePages
            .Where(t => !PublicPages.Contains(t))
            .Where(t => AuthorizeOf(t).Length == 0)
            .Select(t => t.FullName)
            .ToList();

        Assert.True(unprotected.Count == 0,
            "Các trang sau có @page nhưng thiếu [Authorize]: " + string.Join(", ", unprotected));
    }

    [Fact]
    [Trait("TestId", "R18")]
    public void PublicPages_AreReallyPublic()
    {
        // Nếu sau này trang Login bị gắn [Authorize] thì không ai đăng nhập được
        Assert.All(PublicPages, p => Assert.Empty(AuthorizeOf(p)));
    }
}
