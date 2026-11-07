using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;

namespace BlueMoonApartementManagementConsole.Tests.Fakes;

/// <summary>
/// Thay IAppUserRepo bằng danh sách trong bộ nhớ, không cần database.
/// Trả về chính các object đang lưu, giống EF Core tracking: sửa object rồi SaveChanges là "đã lưu".
/// </summary>
public class FakeAppUserRepo : IAppUserRepo
{
    private int _nextId = 1;

    public List<AppUserEntity> Users { get; } = new();
    public int SaveChangesCount { get; private set; }

    /// <summary>Thêm sẵn user vào kho để chuẩn bị dữ liệu test.</summary>
    public AppUserEntity Seed(AppUserEntity user)
    {
        user.Id = _nextId++;
        Users.Add(user);
        return user;
    }

    public Task<AppUserEntity?> GetByIdAsync(int id, CancellationToken ct = default) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

    public Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default) =>
        Task.FromResult(Users.Any(u => u.Username == username));

    public Task<AppUserEntity?> GetByUsernameAsync(string username, CancellationToken ct = default) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Username == username));

    public Task<List<AppUserEntity>> GetPendingAsync(CancellationToken ct = default) =>
        Task.FromResult(Users.Where(u => u.Status == UserStatus.PendingApproval)
                             .OrderBy(u => u.CreatedAt)
                             .ToList());

    public Task AddAsync(AppUserEntity user, CancellationToken ct = default)
    {
        Seed(user);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        SaveChangesCount++;
        return Task.CompletedTask;
    }

    public Task<List<AppUserEntity>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult(Users.ToList());
}
