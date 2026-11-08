using System.Reflection;
using BlueMoonApartementManagementConsole.BackEnd.Controllers;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace BlueMoonApartementManagementConsole.Tests.Security;

/// <summary>
/// Nhật ký chỉ đọc ở tầng ứng dụng: API chỉ có GET, repository và service không có hàm sửa/xóa.
/// Kiểm bằng reflection. Nếu sau này có người thêm hàm sửa/xóa, test sẽ đỏ để nhóm xem lại có chủ ý hay không.
/// Ở tầng database, bảng audit_logs có trigger chặn UPDATE/DELETE: kiểm ở test tích hợp Q12 (AuditLogQueryTests).
/// </summary>
public class AuditImmutabilityTests
{
    private static readonly string[] ForbiddenPrefixes = ["Update", "Delete", "Remove", "Edit", "Modify", "Clear", "Purge", "Truncate"];

    private static MethodInfo[] PublicActions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                  .Where(m => !m.IsSpecialName)
                  .ToArray();

    // ===== N6: API nhật ký chỉ có GET =====

    [Fact]
    [Trait("TestId", "N6")]
    public void AuditLogController_HasOnlyGetActions()
    {
        var actions = PublicActions(typeof(AuditLogController));
        Assert.NotEmpty(actions);

        foreach (var action in actions)
        {
            var methods = action.GetCustomAttributes<HttpMethodAttribute>(inherit: true)
                                .SelectMany(a => a.HttpMethods)
                                .ToList();

            Assert.True(methods.Count > 0, $"Action {action.Name} phải khai báo rõ HTTP method.");
            Assert.All(methods, m => Assert.Equal("GET", m));
        }
    }

    [Fact]
    [Trait("TestId", "N6")]
    public void AuditLogController_DoesNotAcceptArbitraryMethods()
    {
        // [AcceptVerbs]/[Route] không kèm method sẽ nhận mọi method, kể cả DELETE
        var loose = PublicActions(typeof(AuditLogController))
            .Where(m => m.GetCustomAttribute<AcceptVerbsAttribute>() is not null
                     || (m.GetCustomAttribute<RouteAttribute>() is not null && m.GetCustomAttribute<HttpMethodAttribute>() is null))
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(loose);
    }

    // ===== N6: Repository và service không có hàm sửa/xóa =====

    [Theory]
    [Trait("TestId", "N6")]
    [InlineData(typeof(IAuditRepository))]
    [InlineData(typeof(AuditRepository))]
    [InlineData(typeof(IAuditService))]
    [InlineData(typeof(AuditService))]
    public void AuditTypes_HaveNoUpdateOrDeleteMethods(Type type)
    {
        var offending = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .Where(name => ForbiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Empty(offending);
    }

    [Fact]
    [Trait("TestId", "N6")]
    public void AuditRepository_ExposesOnlyAddSaveAndQuery()
    {
        var names = typeof(IAuditRepository).GetMethods().Select(m => m.Name).OrderBy(n => n).ToArray();

        Assert.Equal(["AddAsync", "QueryAsync", "SaveChangesAsync"], names);
    }
}
