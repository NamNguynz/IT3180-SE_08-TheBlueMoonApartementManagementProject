namespace BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Reflection.Emit;
using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Security;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

public class AppDbContext(DbContextOptions<AppDbContext> options, IPersonalDataEncryptor personalData) : DbContext(options)
{
    public DbSet<AppUserEntity> Users => Set<AppUserEntity>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ApartmentEntity> Apartments => Set<ApartmentEntity>();
    public DbSet<HouseholdEntity> Households => Set<HouseholdEntity>();
    public DbSet<ResidentEntity> Residents => Set<ResidentEntity>();
    public DbSet<ResidentChangeEntity> ResidentChanges => Set<ResidentChangeEntity>();
    public DbSet<FeeItemEntity> FeeItems => Set<FeeItemEntity>();
    public DbSet<FeeRateEntity> FeeRates => Set<FeeRateEntity>();
    public DbSet<BillingPeriodEntity> BillingPeriods => Set<BillingPeriodEntity>();
    public DbSet<BillingPeriodItemEntity> BillingPeriodItems => Set<BillingPeriodItemEntity>();
    public DbSet<ReceivableEntity> Receivables => Set<ReceivableEntity>();

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

            // Tài khoản đã có trước migration được DB tự cấp mã ngẫu nhiên (PostgreSQL 13+)
            e.Property(u => u.SecurityStamp).HasMaxLength(64).IsRequired()
             .HasDefaultValueSql("gen_random_uuid()::text");
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
            e.ToTable("apartments", t =>
                t.HasCheckConstraint("ck_apartments_type", "\"Type\" IN ('CanHo','Kiot','Penthouse')"));
            e.HasKey(a => a.Id);
            e.Property(a => a.Code).HasMaxLength(30).IsRequired();
            e.Property(a => a.Building).HasMaxLength(100).IsRequired();
            // Căn hộ có trước khi thêm loại hình nhận mặc định CanHo (căn hộ ở)
            e.Property(a => a.Type).HasConversion<string>().HasMaxLength(20)
             .HasDefaultValue(ApartmentType.CanHo);
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

        // PBI-030 (AC-038, NFR-007): dữ liệu cá nhân được mã hóa khi ghi, giải mã khi đọc.
        // Trường nhạy cảm thêm sau này (vd số điện thoại, số tài khoản) cũng gắn converter này.
        // Cột mã hóa để kiểu text vì bản mã dài hơn bản rõ; độ dài bản rõ do [StringLength] ở DTO kiểm.
        // Lưu ý: EF Core dựng model một lần cho mỗi loại DbContext rồi dùng lại, nên converter giữ encryptor
        // của lần dựng đầu. Mọi AppDbContext trong cùng tiến trình phải dùng cùng khóa (test dùng chung một khóa).
        var encryptor = personalData;
        // EF không đưa null qua converter (null vẫn lưu là NULL), nên dùng ! ở đây là an toàn
        var encrypted = new ValueConverter<string?, string?>(v => encryptor.Protect(v!), v => encryptor.Unprotect(v!));

        modelBuilder.Entity<ResidentEntity>(e =>
        {
            e.ToTable("residents", t =>
                t.HasCheckConstraint("ck_residents_residence_status",
                    "\"ResidenceStatus\" IN ('ThuongTru','TamTru','TamVang','DaChuyenDi')"));
            e.HasKey(r => r.Id);
            e.Property(r => r.FullName).HasMaxLength(100).IsRequired();
            e.Property(r => r.Alias).HasMaxLength(100);
            e.Property(r => r.Gender).HasMaxLength(20).IsRequired();
            e.Property(r => r.PlaceOfBirth).HasMaxLength(100);
            e.Property(r => r.Hometown).HasMaxLength(100);
            e.Property(r => r.Ethnicity).HasColumnType("text").HasConversion(encrypted);
            e.Property(r => r.Religion).HasColumnType("text").HasConversion(encrypted);
            e.Property(r => r.Occupation).HasMaxLength(100);
            e.Property(r => r.Workplace).HasMaxLength(150);
            e.Property(r => r.IdentityNumber).HasColumnType("text").HasConversion(encrypted);
            e.Property(r => r.IdentityIssuePlace).HasColumnType("text").HasConversion(encrypted);
            e.Property(r => r.PreviousResidence).HasColumnType("text").HasConversion(encrypted);
            e.Property(r => r.Notes).HasMaxLength(500);
            // PBI-007: nhân khẩu có trước khi thêm tình trạng cư trú nhận mặc định ThuongTru
            e.Property(r => r.ResidenceStatus).HasConversion<string>().HasMaxLength(20)
             .HasDefaultValue(ResidenceStatus.ThuongTru);
            // Bản mã của cùng một số CCCD mỗi lần một khác, nên unique đặt trên hash (SaveChanges tự tính)
            e.Property(r => r.IdentityNumberHash).HasMaxLength(64);
            e.HasIndex(r => r.IdentityNumberHash).IsUnique()
                .HasFilter("\"IdentityNumberHash\" IS NOT NULL");
            e.HasOne(r => r.Household)
                .WithMany(h => h.Residents)
                .HasForeignKey(r => r.HouseholdId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // PBI-007: lịch sử biến động nhân khẩu, chỉ thêm dòng mới, không sửa/xóa
        modelBuilder.Entity<ResidentChangeEntity>(e =>
        {
            e.ToTable("resident_changes", t =>
            {
                t.HasCheckConstraint("ck_resident_changes_type",
                    "\"Type\" IN ('ChuyenDen','TamTru','TamVang','TroVe','ChuyenDi')");
                t.HasCheckConstraint("ck_resident_changes_period", "\"ToDate\" IS NULL OR \"ToDate\" > \"FromDate\"");
            });
            e.HasKey(c => c.Id);
            e.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(c => c.FromDate).HasColumnType("date").IsRequired();
            e.Property(c => c.ToDate).HasColumnType("date");
            // Nơi đi/đến là dữ liệu cá nhân, mã hóa như PreviousResidence (PBI-030)
            e.Property(c => c.Place).HasColumnType("text").HasConversion(encrypted);
            e.Property(c => c.Reason).HasMaxLength(500);
            e.Property(c => c.RecordedAt).IsRequired();
            e.HasIndex(c => new { c.HouseholdId, c.FromDate });
            e.HasIndex(c => c.ResidentId);
            e.HasOne<HouseholdEntity>().WithMany()
             .HasForeignKey(c => c.HouseholdId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Resident).WithMany()
             .HasForeignKey(c => c.ResidentId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.RecordedBy).WithMany()
             .HasForeignKey(c => c.RecordedById)
             .OnDelete(DeleteBehavior.Restrict);
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

        // PBI-014: kỳ thu và bảng kê phải thu. Đã lập thì không sửa, không xóa (căn cứ thu tiền, công nợ),
        // nên khóa ngoại tới khoản thu, đơn giá, hộ, người lập đều Restrict.
        modelBuilder.Entity<BillingPeriodEntity>(e =>
        {
            e.ToTable("billing_periods", t =>
            {
                t.HasCheckConstraint("ck_billing_periods_month", "\"Month\" BETWEEN 1 AND 12");
                t.HasCheckConstraint("ck_billing_periods_due_date", "\"DueDate\" >= \"ChargeDate\"");
            });
            e.HasKey(p => p.Id);
            e.Property(p => p.ChargeDate).HasColumnType("date").IsRequired();
            e.Property(p => p.DueDate).HasColumnType("date").IsRequired();
            e.Property(p => p.Note).HasMaxLength(500);
            e.HasIndex(p => new { p.Year, p.Month });
            e.HasOne(p => p.CreatedBy).WithMany()
             .HasForeignKey(p => p.CreatedById)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillingPeriodItemEntity>(e =>
        {
            e.ToTable("billing_period_items", t =>
                t.HasCheckConstraint("ck_billing_period_items_rate", "\"Rate\" >= 0"));
            e.HasKey(i => i.Id);
            e.Property(i => i.Rate).HasPrecision(18, 2);
            // Một khoản thu chỉ xuất hiện một lần trong một kỳ
            e.HasIndex(i => new { i.BillingPeriodId, i.FeeItemId }).IsUnique();
            e.HasOne(i => i.BillingPeriod).WithMany(p => p.Items)
             .HasForeignKey(i => i.BillingPeriodId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.FeeItem).WithMany()
             .HasForeignKey(i => i.FeeItemId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.FeeRate).WithMany()
             .HasForeignKey(i => i.FeeRateId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReceivableEntity>(e =>
        {
            e.ToTable("receivables", t =>
            {
                t.HasCheckConstraint("ck_receivables_quantity", "\"Quantity\" > 0");
                t.HasCheckConstraint("ck_receivables_amount", "\"Amount\" >= 0");
            });
            e.HasKey(r => r.Id);
            e.Property(r => r.Quantity).HasPrecision(10, 2);
            e.Property(r => r.Amount).HasPrecision(18, 2);
            // Một hộ chỉ có một dòng phải thu cho mỗi khoản trong kỳ
            e.HasIndex(r => new { r.BillingPeriodItemId, r.HouseholdId }).IsUnique();
            e.HasIndex(r => r.HouseholdId);
            e.HasOne(r => r.BillingPeriodItem).WithMany(i => i.Receivables)
             .HasForeignKey(r => r.BillingPeriodItemId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Household).WithMany()
             .HasForeignKey(r => r.HouseholdId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        UpdateIdentityNumberHashes();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        UpdateIdentityNumberHashes();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Mọi chỗ thêm/sửa nhân khẩu đều được tính lại hash số giấy tờ, không phải nhớ gọi riêng.</summary>
    private void UpdateIdentityNumberHashes()
    {
        foreach (var entry in ChangeTracker.Entries<ResidentEntity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            var resident = entry.Entity;
            resident.IdentityNumberHash = resident.IdentityNumber is null
                ? null
                : personalData.HashIdentity(resident.IdentityNumber);
        }
    }
}