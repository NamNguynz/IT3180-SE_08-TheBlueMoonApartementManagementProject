namespace BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUserEntity> Users => Set<AppUserEntity>();

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
        });
    }
}