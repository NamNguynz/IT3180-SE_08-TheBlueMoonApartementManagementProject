using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.EntityFrameworkCore;

namespace BlueMoonApartementManagementConsole.BackEnd.Repositories
{
    public interface IAppUserRepo
    {
        Task<AppUserEntity?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default);
        Task<AppUserEntity?> GetByUsernameAsync(string username, CancellationToken ct = default);
        Task<List<AppUserEntity>> GetPendingAsync(CancellationToken ct = default);
        Task AddAsync(AppUserEntity user, CancellationToken ct = default);
        Task SaveChangesAsync(CancellationToken ct = default);
    }

    public class AppUserRepo(AppDbContext db) : IAppUserRepo
    {
        public Task<AppUserEntity?> GetByIdAsync(int id, CancellationToken ct = default) =>
            db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        public Task<AppUserEntity?> GetByUsernameAsync(string username, CancellationToken ct = default) =>
            db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);
        public Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default) =>
            db.Users.AnyAsync(u => u.Username == username, ct);
        public async Task AddAsync(AppUserEntity user, CancellationToken ct = default) =>
            await db.Users.AddAsync(user, ct);
        public Task<List<AppUserEntity>> GetPendingAsync(CancellationToken ct = default) =>
            db.Users
              .Where(u => u.Status == UserStatus.PendingApproval)
              .OrderBy(u => u.CreatedAt)
              .ToListAsync(ct);
        public Task SaveChangesAsync(CancellationToken ct = default) =>
            db.SaveChangesAsync(ct);
    }
}
