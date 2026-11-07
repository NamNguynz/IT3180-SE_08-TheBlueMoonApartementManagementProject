namespace BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUserEntity> Users => Set<AppUserEntity>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUserEntity>(e =>
        {
            e.ToTable("users");
            e.HasKey(u => u.Id);

            e.Property(u => u.Username).HasMaxLength(50).IsRequired();
            e.HasIndex(u => u.Username).IsUnique();

            e.Property(u => u.PasswordHash).HasMaxLength(255).IsRequired();

            e.Property(u => u.FullName).HasMaxLength(100).IsRequired()
             .HasDefaultValueSql("''");

            e.Property(u => u.Role)
             .HasConversion<string>()
             .HasMaxLength(20);

            e.Property(u => u.Status)
             .HasConversion<string>()
             .HasMaxLength(20);

            e.HasOne<AppUserEntity>()
             .WithMany()
             .HasForeignKey(u => u.ApprovedById)
             .OnDelete(DeleteBehavior.SetNull);

            e.HasOne<AppUserEntity>()
             .WithMany()
             .HasForeignKey(u => u.GrantedById)
             .OnDelete(DeleteBehavior.Restrict);   // không xóa cha khi còn con

            e.HasIndex(u => u.GrantedById);

            e.Property(u => u.MustChangePassword).HasDefaultValue(false);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).UseIdentityByDefaultColumn();

            e.Property(a => a.OccurredAt).IsRequired();
            e.Property(a => a.Username).HasMaxLength(50).IsRequired();
            e.Property(a => a.Action).HasConversion<string>().HasMaxLength(30);
            e.Property(a => a.Target).HasMaxLength(100);
            e.Property(a => a.Detail).HasMaxLength(500);

            e.HasIndex(a => a.OccurredAt);
            e.HasIndex(a => a.Username);
        });
    }
}