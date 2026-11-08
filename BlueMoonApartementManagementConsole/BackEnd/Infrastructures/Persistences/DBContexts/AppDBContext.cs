namespace BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using Microsoft.EntityFrameworkCore;
using System.Reflection.Emit;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUserEntity> Users => Set<AppUserEntity>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ApartmentEntity> Apartments => Set<ApartmentEntity>();
    public DbSet<HouseholdEntity> Households => Set<HouseholdEntity>();
    public DbSet<ResidentEntity> Residents => Set<ResidentEntity>();
    public DbSet<FeeItemEntity> FeeItems => Set<FeeItemEntity>();
    public DbSet<FeeRateEntity> FeeRates => Set<FeeRateEntity>();

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

        modelBuilder.Entity<ApartmentEntity>(e =>
        {
            e.ToTable("apartments");
            e.HasKey(a => a.Id);
            e.Property(a => a.Code).HasMaxLength(30).IsRequired();
            e.Property(a => a.Building).HasMaxLength(100).IsRequired();
            e.Property(a => a.Area).HasPrecision(10, 2);
            e.Property(a => a.Status).HasMaxLength(30).IsRequired();
            e.Property(a => a.Notes).HasMaxLength(500);
            e.HasIndex(a => a.Code).IsUnique();
        });

        modelBuilder.Entity<HouseholdEntity>(e =>
        {
            e.ToTable("households");
            e.HasKey(h => h.Id);
            e.Property(h => h.HouseholdNumber).HasMaxLength(30).IsRequired();
            e.Property(h => h.HeadName).HasMaxLength(100).IsRequired();
            e.Property(h => h.HouseNumber).HasMaxLength(30).IsRequired();
            e.HasIndex(h => h.HouseholdNumber).IsUnique();
            e.HasIndex(h => h.ApartmentId).IsUnique();
            e.HasOne(h => h.Apartment)
                .WithOne(a => a.Household)
                .HasForeignKey<HouseholdEntity>(h => h.ApartmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ResidentEntity>(e =>
        {
            e.ToTable("residents");
            e.HasKey(r => r.Id);
            e.Property(r => r.FullName).HasMaxLength(100).IsRequired();
            e.Property(r => r.Alias).HasMaxLength(100);
            e.Property(r => r.Gender).HasMaxLength(20).IsRequired();
            e.Property(r => r.PlaceOfBirth).HasMaxLength(100);
            e.Property(r => r.Hometown).HasMaxLength(100);
            e.Property(r => r.Ethnicity).HasMaxLength(50);
            e.Property(r => r.Religion).HasMaxLength(50);
            e.Property(r => r.Occupation).HasMaxLength(100);
            e.Property(r => r.Workplace).HasMaxLength(150);
            e.Property(r => r.IdentityNumber).HasMaxLength(30);
            e.Property(r => r.IdentityIssuePlace).HasMaxLength(150);
            e.Property(r => r.PreviousResidence).HasMaxLength(500);
            e.Property(r => r.Notes).HasMaxLength(500);
            e.HasIndex(r => r.IdentityNumber).IsUnique()
                .HasFilter("\"IdentityNumber\" IS NOT NULL");
            e.HasOne(r => r.Household)
                .WithMany(h => h.Residents)
                .HasForeignKey(r => r.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FeeItemEntity>(e =>
        {
            e.ToTable("fee_items", t =>
            {
                t.HasCheckConstraint("ck_fee_items_fee_type", "\"FeeType\" IN ('BatBuoc','TuNguyen','BaoTriRieng')");
                t.HasCheckConstraint("ck_fee_items_fee_scope", "\"FeeScope\" IN ('Chung','ChiDinh')");
                t.HasCheckConstraint("ck_fee_items_fee_cycle", "\"FeeCycle\" IN ('MotLan','Thang','Nam')");
                t.HasCheckConstraint("ck_fee_items_calculation_basis", "\"CalculationBasis\" IN ('DienTich','NhanKhau','Ho','CoDinh')");
            });
            e.HasKey(f => f.Id);
            e.Property(f => f.Code).HasMaxLength(30).IsRequired();
            e.HasIndex(f => f.Code).IsUnique();
            e.Property(f => f.Name).HasMaxLength(100).IsRequired();
            e.Property(f => f.FeeType).HasConversion<string>().HasMaxLength(20);
            e.Property(f => f.FeeScope).HasConversion<string>().HasMaxLength(20);
            e.Property(f => f.FeeCycle).HasConversion<string>().HasMaxLength(20);
            e.Property(f => f.CalculationBasis).HasConversion<string>().HasMaxLength(20);
            e.Property(f => f.Unit).HasMaxLength(20).IsRequired();
            e.Property(f => f.Note).HasMaxLength(500);
            e.HasOne<AppUserEntity>().WithMany()
             .HasForeignKey(f => f.CreatedById)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FeeRateEntity>(e =>
        {
            e.ToTable("fee_rates", t =>
            {
                t.HasCheckConstraint("ck_fee_rates_rate", "\"Rate\" >= 0");
                t.HasCheckConstraint("ck_fee_rates_period", "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" > \"EffectiveFrom\"");
            });
            e.HasKey(r => r.Id);
            e.Property(r => r.Rate).HasPrecision(18, 2);
            e.Property(r => r.EffectiveFrom).HasColumnType("date").IsRequired();
            e.Property(r => r.EffectiveTo).HasColumnType("date");
            // One rate starts per fee item per day.
            e.HasIndex(r => new { r.FeeItemId, r.EffectiveFrom }).IsUnique();
            e.HasOne(r => r.FeeItem).WithMany(f => f.Rates)
             .HasForeignKey(r => r.FeeItemId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}