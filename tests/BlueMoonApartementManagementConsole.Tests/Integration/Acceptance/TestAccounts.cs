using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueMoonApartementManagementConsole.Tests.Integration.Acceptance;

/// <summary>
/// Tài khoản dùng cho một test acceptance: tên có tiền tố riêng mỗi lần chạy để không đụng dữ liệu khác.
/// Tài khoản được xóa khi test xong (CleanupAsync). Nhật ký thì không xóa được (bảng audit_logs chặn DELETE),
/// nên log test nằm lại trong DB test, được nhận ra nhờ tiền tố.
/// </summary>
public sealed class TestAccounts(BlueMoonAppFactory factory)
{
    public const string Password = "TestPass@01";
    public const string AdminUsername = "admin";

    /// <summary>Tiền tố chữ thường, vd "at1a2b3c4d_". Tên tài khoản hợp lệ phải từ 3 đến 50 ký tự.</summary>
    public string Prefix { get; } = "at" + Guid.NewGuid().ToString("N")[..8] + "_";

    public string Name(string suffix) => Prefix + suffix;

    /// <summary>
    /// Thêm tài khoản thẳng vào DB với mật khẩu băm bằng hasher thật của app.
    /// Chỉ dùng để chuẩn bị dữ liệu, không phải bước đang được kiểm thử.
    /// </summary>
    public async Task<AppUserEntity> SeedAsync(
        string suffix,
        UserRole role,
        UserStatus status = UserStatus.Approved,
        int? grantedById = null,
        string password = Password,
        bool mustChangePassword = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUserEntity>>();

        var user = new AppUserEntity
        {
            Username = Name(suffix),
            FullName = "AT " + suffix,
            Role = role,
            Status = status,
            IsActive = true,
            GrantedById = grantedById,
            MustChangePassword = mustChangePassword
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public async Task<AppUserEntity?> FindAsync(string username)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == username);
    }

    public async Task<int> AdminIdAsync() => (await FindAsync(AdminUsername))!.Id;

    /// <summary>Id lớn nhất hiện có trong nhật ký, dùng làm mốc để lấy các log sinh ra sau đó.</summary>
    public async Task<long> LastLogIdAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogs.MaxAsync(a => (long?)a.Id) ?? 0;
    }

    /// <summary>Các log sinh ra sau mốc <paramref name="afterId"/>, theo thứ tự thời gian.</summary>
    public async Task<List<AuditLog>> LogsAfterAsync(long afterId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogs.AsNoTracking().Where(a => a.Id > afterId).OrderBy(a => a.Id).ToListAsync();
    }

    public async Task CleanupAsync()
    {
        if (BlueMoonAppFactory.ConnectionString is null) return;

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var mine = db.Users.Where(u => u.Username.StartsWith(Prefix));
        // GrantedById là khóa ngoại Restrict: gỡ quan hệ cha-con trước rồi mới xóa
        await mine.ExecuteUpdateAsync(s => s.SetProperty(u => u.GrantedById, (int?)null));
        await mine.ExecuteDeleteAsync();
    }
}
