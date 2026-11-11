using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using BlueMoonApartementManagementConsole.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace BlueMoonApartementManagementConsole.Tests.Services;

/// <summary>
/// Unit test lập kỳ thu và bảng kê phải thu (PBI-014): cách tính theo cơ sở tính, làm tròn, gom theo hộ,
/// chụp đơn giá theo ngày tính phí, chặn thu trùng theo chu kỳ và các trường hợp bị từ chối.
/// Test chấp nhận trên app thật (WBS 4.14.03) do Tester làm riêng.
/// </summary>
[Trait("PBI", "PBI-014")]
public sealed class BillingPeriodServiceTests
{
    private static readonly DateOnly Today = new(2026, 11, 12);
    private static readonly DateOnly MonthStart = new(2026, 11, 1);

    private readonly FakeBillingPeriodRepository _repo = new();
    private readonly FakeAuditService _audit = new();
    private readonly BillingPeriodService _service;

    public BillingPeriodServiceTests()
    {
        // FakeTimeProvider mặc định múi giờ UTC, nên "hôm nay" của service là 12/11/2026
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero));
        _service = new BillingPeriodService(_repo, _audit, clock);
    }

    // ===== Tính số phải thu =====

    [Fact]
    public async Task Create_ComputesEachHouseholdByBasis_GroupsMandatoryItems_AndAudits()
    {
        var service = _repo.SeedFeeItem("PHI_DV", CalculationBasis.DienTich, "m²", 7_000m);
        var hygiene = _repo.SeedFeeItem("PHI_VS", CalculationBasis.NhanKhau, "người", 6_000m);
        var parking = _repo.SeedFeeItem("PHI_QL", CalculationBasis.Ho, "hộ", 50_000m);
        _repo.SeedHousehold("HK-0501", "A-501", area: 72.5m, residents: 3);
        _repo.SeedHousehold("HK-1203", "B-1203", area: 95m, residents: 2);

        var result = await CreateAsync(service.Id, hygiene.Id, parking.Id);

        Assert.Equal(FeeItemStatus.Success, result.Status);
        var period = result.Data!;
        var a501 = Assert.Single(period.Households, h => h.ApartmentCode == "A-501");
        // 72,5 m² × 7.000 = 507.500; 3 người × 6.000 = 18.000; 1 hộ × 50.000 = 50.000
        Assert.Equal(507_500m, AmountOf(period, a501, "PHI_DV"));
        Assert.Equal(18_000m, AmountOf(period, a501, "PHI_VS"));
        Assert.Equal(50_000m, AmountOf(period, a501, "PHI_QL"));
        Assert.Equal(575_500m, a501.TotalAmount);
        var b1203 = Assert.Single(period.Households, h => h.ApartmentCode == "B-1203");
        Assert.Equal(665_000m + 12_000m + 50_000m, b1203.TotalAmount);
        Assert.Equal(a501.TotalAmount + b1203.TotalAmount, period.TotalAmount);

        // Đơn giá đã áp dụng được giữ cùng kỳ (AC-007)
        Assert.All(period.Items, i => Assert.Equal(MonthStart, i.RateEffectiveFrom));
        Assert.Equal(7_000m, Assert.Single(period.Items, i => i.Code == "PHI_DV").Rate);

        var log = Assert.Single(_audit.Entries);
        Assert.Equal(AuditAction.CreateBillingPeriod, log.Action);
        Assert.Equal(period.Id.ToString(), log.Target);
        Assert.Contains("tháng 11/2026", log.Detail);
        Assert.Contains("2 hộ", log.Detail);
    }

    [Theory]
    [InlineData(10.05, 25_175)] // 10,05 × 2.505 = 25.175,25 → làm tròn xuống
    [InlineData(10.5, 26_303)]  // 10,5 × 2.505 = 26.302,5 → .5 làm tròn lên
    public async Task Amount_IsRoundedToWholeDong(decimal area, decimal expected)
    {
        var fee = _repo.SeedFeeItem("PHI_DV", CalculationBasis.DienTich, "m²", 2_505m);
        _repo.SeedHousehold("HK-1", "A-101", area, residents: 1);

        var result = await CreateAsync(fee.Id);

        Assert.Equal(expected, Assert.Single(result.Data!.Households).TotalAmount);
    }

    [Fact]
    public async Task HouseholdWithoutResidents_HasNoLine_ForPerPersonFee()
    {
        var hygiene = _repo.SeedFeeItem("PHI_VS", CalculationBasis.NhanKhau, "người", 6_000m);
        var service = _repo.SeedFeeItem("PHI_DV", CalculationBasis.DienTich, "m²", 7_000m);
        _repo.SeedHousehold("HK-1", "A-101", area: 50m, residents: 0);

        var result = await CreateAsync(hygiene.Id, service.Id);

        var line = Assert.Single(Assert.Single(result.Data!.Households).Lines);
        Assert.Equal(service.Id, line.FeeItemId);
    }

    [Fact]
    public async Task UsesRateEffectiveOnChargeDate_NotTheCurrentOne()
    {
        var fee = _repo.SeedFeeItem("PHI_DV", CalculationBasis.DienTich, "m²",
            (5_000m, new DateOnly(2026, 1, 1), new DateOnly(2026, 11, 10)),
            (8_000m, new DateOnly(2026, 11, 10), null));
        _repo.SeedHousehold("HK-1", "A-101", area: 10m, residents: 1);

        var result = await CreateAsync(new DateOnly(2026, 11, 5), fee.Id);

        var item = Assert.Single(result.Data!.Items);
        Assert.Equal(5_000m, item.Rate);
        Assert.Equal(new DateOnly(2026, 1, 1), item.RateEffectiveFrom);
        Assert.Equal(50_000m, result.Data.TotalAmount);
    }

    // ===== Chặn thu trùng theo chu kỳ =====

    [Theory]
    [InlineData(FeeCycle.Thang, 2026, 11, true)]
    [InlineData(FeeCycle.Thang, 2026, 10, false)]
    [InlineData(FeeCycle.Nam, 2026, 3, true)]
    [InlineData(FeeCycle.Nam, 2025, 11, false)]
    [InlineData(FeeCycle.MotLan, 2024, 1, true)]
    public async Task SameFeeItemInSameCycle_IsRejected(FeeCycle cycle, int billedYear, int billedMonth, bool rejected)
    {
        var fee = _repo.SeedFeeItem("PHI_DV", CalculationBasis.DienTich, "m²", 7_000m, cycle: cycle);
        _repo.SeedHousehold("HK-1", "A-101", area: 50m, residents: 1);
        _repo.Billed.Add(new BilledFeeItemRow(fee.Id, 99, billedYear, billedMonth));

        var result = await CreateAsync(fee.Id);

        if (rejected)
        {
            Assert.Equal(FeeItemStatus.Conflict, result.Status);
            Assert.Equal($"Khoản thu PHI_DV đã được lập trong kỳ thu tháng {billedMonth:00}/{billedYear}.", result.Message);
            Assert.Empty(_repo.Periods);
            Assert.Empty(_audit.Entries);
        }
        else
        {
            Assert.Equal(FeeItemStatus.Success, result.Status);
        }
    }

    // ===== Từ chối dữ liệu không hợp lệ =====

    public static TheoryData<int, int, DateOnly, DateOnly, string> InvalidPeriods => new()
    {
        { 2026, 13, MonthStart, MonthStart, "Tháng của kỳ thu không hợp lệ." },
        { 2026, 12, Today, new DateOnly(2026, 12, 31), "Chưa đến kỳ thu tháng 12/2026: chỉ lập được từ ngày 01/12/2026." },
        { 2026, 11, default, new DateOnly(2026, 11, 30), "Ngày tính phí là bắt buộc." },
        { 2026, 11, new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 30), "Ngày tính phí phải nằm trong tháng của kỳ thu (01/11/2026 – 30/11/2026)." },
        // Lập bù kỳ tháng 10 thì phải lấy đơn giá của tháng 10, không lấy ngày hôm nay
        { 2026, 10, Today, new DateOnly(2026, 11, 30), "Ngày tính phí phải nằm trong tháng của kỳ thu (01/10/2026 – 31/10/2026)." },
        { 2026, 11, new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 30), "Chưa đến ngày tính phí 13/11/2026, chưa lập được bảng kê." },
        { 2026, 11, Today, default, "Hạn nộp là bắt buộc." },
        { 2026, 11, Today, new DateOnly(2026, 11, 11), "Hạn nộp không được trước ngày tính phí." },
    };

    [Theory]
    [MemberData(nameof(InvalidPeriods))]
    public async Task InvalidPeriod_IsRejected_WithMessage(int year, int month, DateOnly chargeDate, DateOnly dueDate, string message)
    {
        var fee = _repo.SeedFeeItem("PHI_DV", CalculationBasis.DienTich, "m²", 7_000m);
        _repo.SeedHousehold("HK-1", "A-101", area: 50m, residents: 1);

        var result = await _service.CreateAsync(1, "ketoan", new CreateBillingPeriodRequest
        {
            Year = year, Month = month, ChargeDate = chargeDate, DueDate = dueDate, FeeItemIds = [fee.Id]
        });

        AssertInvalid(result, message);
    }

    [Fact]
    public async Task FeeItemSelection_IsValidated()
    {
        var fee = _repo.SeedFeeItem("PHI_DV", CalculationBasis.DienTich, "m²", 7_000m);
        var donation = _repo.SeedFeeItem("QUY_TU_THIEN", CalculationBasis.CoDinh, "hộ", 0m, type: FeeType.TuNguyen);
        var assigned = _repo.SeedFeeItem("PHI_RIENG", CalculationBasis.Ho, "hộ", 10_000m, scope: FeeScope.ChiDinh);
        var future = _repo.SeedFeeItem("PHI_MOI", CalculationBasis.Ho, "hộ",
            (20_000m, new DateOnly(2026, 12, 1), null));
        _repo.SeedHousehold("HK-1", "A-101", area: 50m, residents: 1);

        AssertInvalid(await CreateAsync(), "Hãy chọn ít nhất một khoản thu.");
        AssertInvalid(await CreateAsync(fee.Id, fee.Id), "Danh sách khoản thu bị trùng.");
        AssertInvalid(await CreateAsync(fee.Id, 404), "Không tìm thấy khoản thu #404.");
        AssertInvalid(await CreateAsync(donation.Id),
            "Khoản thu QUY_TU_THIEN là đóng góp tự nguyện, thu theo đợt riêng, không đưa vào kỳ thu.");
        AssertInvalid(await CreateAsync(assigned.Id),
            "Khoản thu PHI_RIENG áp dụng cho hộ chỉ định, chưa có danh sách hộ nên chưa đưa vào kỳ thu được.");
        AssertInvalid(await CreateAsync(future.Id),
            "Khoản thu PHI_MOI chưa có đơn giá hiệu lực vào ngày 12/11/2026.");
        Assert.Empty(_repo.Periods);
    }

    [Fact]
    public async Task NoHousehold_IsRejected()
    {
        var fee = _repo.SeedFeeItem("PHI_DV", CalculationBasis.DienTich, "m²", 7_000m);

        AssertInvalid(await CreateAsync(fee.Id), "Chưa có hộ gia đình nào để lập bảng kê.");
    }

    [Fact]
    public async Task UnknownPeriod_IsNull()
    {
        Assert.Null(await _service.GetByIdAsync(404));
    }

    // ===== Hàm hỗ trợ =====

    private Task<FeeItemOperationResult<BillingPeriodResponse>> CreateAsync(params int[] feeItemIds) =>
        CreateAsync(Today, feeItemIds);

    private Task<FeeItemOperationResult<BillingPeriodResponse>> CreateAsync(DateOnly chargeDate, params int[] feeItemIds) =>
        _service.CreateAsync(1, "ketoan", new CreateBillingPeriodRequest
        {
            Year = 2026,
            Month = 11,
            ChargeDate = chargeDate,
            DueDate = new DateOnly(2026, 11, 30),
            FeeItemIds = feeItemIds
        });

    private static decimal AmountOf(BillingPeriodResponse period, HouseholdBillResponse household, string code)
    {
        var feeItemId = period.Items.Single(i => i.Code == code).FeeItemId;
        return household.Lines.Single(l => l.FeeItemId == feeItemId).Amount;
    }

    private void AssertInvalid(FeeItemOperationResult<BillingPeriodResponse> result, string message)
    {
        Assert.Equal(FeeItemStatus.Invalid, result.Status);
        Assert.Equal(message, result.Message);
        Assert.Empty(_audit.Entries);
    }

    /// <summary>Kho dữ liệu trong bộ nhớ; SaveChangesAsync cấp Id và gắn navigation như EF khi đọc lại.</summary>
    private sealed class FakeBillingPeriodRepository : IBillingPeriodRepository
    {
        private readonly List<FeeItemEntity> _feeItems = [];
        private readonly List<HouseholdEntity> _households = [];
        private readonly Dictionary<int, int> _residentCounts = [];

        public List<BillingPeriodEntity> Periods { get; } = [];
        public List<BilledFeeItemRow> Billed { get; } = [];

        public FeeItemEntity SeedFeeItem(string code, CalculationBasis basis, string unit, decimal rate,
            FeeType type = FeeType.BatBuoc, FeeScope scope = FeeScope.Chung, FeeCycle cycle = FeeCycle.Thang) =>
            SeedFeeItem(code, basis, unit, [(rate, MonthStart, null)], type, scope, cycle);

        public FeeItemEntity SeedFeeItem(string code, CalculationBasis basis, string unit,
            params (decimal Rate, DateOnly From, DateOnly? To)[] rates) =>
            SeedFeeItem(code, basis, unit, rates, FeeType.BatBuoc, FeeScope.Chung, FeeCycle.Thang);

        private FeeItemEntity SeedFeeItem(string code, CalculationBasis basis, string unit,
            (decimal Rate, DateOnly From, DateOnly? To)[] rates, FeeType type, FeeScope scope, FeeCycle cycle)
        {
            var feeItem = new FeeItemEntity
            {
                Id = _feeItems.Count + 1,
                Code = code,
                Name = code,
                FeeType = type,
                FeeScope = scope,
                FeeCycle = cycle,
                CalculationBasis = basis,
                Unit = unit
            };
            foreach (var (rate, from, to) in rates)
            {
                feeItem.Rates.Add(new FeeRateEntity
                {
                    Id = _feeItems.Count * 10 + feeItem.Rates.Count + 1,
                    FeeItemId = feeItem.Id,
                    Rate = rate,
                    EffectiveFrom = from,
                    EffectiveTo = to,
                    FeeItem = feeItem
                });
            }
            _feeItems.Add(feeItem);
            return feeItem;
        }

        public void SeedHousehold(string number, string apartmentCode, decimal area, int residents)
        {
            var id = _households.Count + 1;
            _households.Add(new HouseholdEntity
            {
                Id = id,
                HouseholdNumber = number,
                Apartment = new ApartmentEntity { Code = apartmentCode, Building = "Tòa " + apartmentCode[0], Area = area }
            });
            _residentCounts[id] = residents;
        }

        public Task<List<BillingPeriodSummaryRow>> GetSummariesAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<BillingPeriodEntity?> GetDetailAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(Periods.SingleOrDefault(p => p.Id == id));

        public Task<List<FeeItemEntity>> GetFeeItemsAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default) =>
            Task.FromResult(_feeItems.Where(f => ids.Contains(f.Id)).ToList());

        public Task<List<BilledFeeItemRow>> GetBilledItemsAsync(IReadOnlyCollection<int> feeItemIds, CancellationToken ct = default) =>
            Task.FromResult(Billed.Where(b => feeItemIds.Contains(b.FeeItemId)).ToList());

        public Task<List<HouseholdBillingBasis>> GetHouseholdBasesAsync(CancellationToken ct = default) =>
            Task.FromResult(_households
                .Select(h => new HouseholdBillingBasis(h.Id, h.Apartment.Area, _residentCounts[h.Id]))
                .ToList());

        public Task AddAsync(BillingPeriodEntity period, CancellationToken ct = default)
        {
            Periods.Add(period);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            foreach (var period in Periods.Where(p => p.Id == 0))
            {
                period.Id = Periods.Count;
                period.CreatedBy = new AppUserEntity { Id = period.CreatedById, Username = "ketoan" };
                foreach (var item in period.Items)
                {
                    item.FeeItem = _feeItems.Single(f => f.Id == item.FeeItemId);
                    item.FeeRate = item.FeeItem.Rates.Single(r => r.Id == item.FeeRateId);
                    foreach (var receivable in item.Receivables)
                        receivable.Household = _households.Single(h => h.Id == receivable.HouseholdId);
                }
            }
            return Task.CompletedTask;
        }
    }
}
