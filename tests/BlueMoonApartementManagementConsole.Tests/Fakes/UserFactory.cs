using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.AspNetCore.Identity;

namespace BlueMoonApartementManagementConsole.Tests.Fakes;

/// <summary>
/// Tạo nhanh user theo vai trò / trạng thái / người cấp và thêm vào FakeAppUserRepo,
/// dùng để dựng cây tài khoản cho test phân quyền.
/// </summary>
public static class UserFactory
{
    public const string DefaultPassword = "MatKhau@123";

    /// <param name="hasher">Truyền vào khi test cần đăng nhập bằng mật khẩu thật; bỏ trống thì hash là chuỗi giả.</param>
    public static AppUserEntity Seed(
        this FakeAppUserRepo repo,
        string username,
        UserRole role,
        UserStatus status = UserStatus.Approved,
        bool isActive = true,
        int? grantedById = null,
        IPasswordHasher<AppUserEntity>? hasher = null,
        string password = DefaultPassword)
    {
        var user = new AppUserEntity
        {
            Username = username,
            FullName = username,
            Role = role,
            Status = status,
            IsActive = isActive,
            GrantedById = grantedById,
            PasswordHash = "hash-gia"
        };
        if (hasher is not null)
            user.PasswordHash = hasher.HashPassword(user, password);

        return repo.Seed(user);
    }
}
