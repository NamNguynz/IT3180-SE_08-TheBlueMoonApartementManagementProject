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
        // Hôm nay 12/11/2026: lập trước được kỳ tháng 12, chưa lập được tháng 01/2027
        { 2027, 1, new DateOnly(2027, 1, 5), new DateOnly(2027, 1, 31),
            "Chỉ lập trước được kỳ thu của tháng sau (12/2026), chưa lập được kỳ tháng 01/2027." },
        { 2026, 11, default, new DateOnly(2026, 11, 30), "Ngày tính phí là bắt buộc." },
        { 2026, 11, new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 30), "Ngày tính phí phải nằm trong tháng của kỳ thu (01/11/2026 – 30/11/2026)." },
        // Lập bù kỳ tháng 10 thì phải lấy đơn giá của tháng 10, không lấy ngày hôm nay
        { 2026, 10, Today, new DateOnly(2026, 11, 30), "Ngày tính phí phải nằm trong tháng của kỳ thu (01/10/2026 – 31/10/2026)." },
        // Lập trước kỳ tháng 12 thì ngày tính phí cũng phải trong tháng 12
        { 2026, 12, Today, new DateOnly(2026, 12, 31), "Ngày tính phí phải nằm trong tháng của kỳ thu (01/12/2026 – 31/12/2026)." },
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
        // Khoản chỉ định hộ đưa vào kỳ được, nhưng phải kèm danh sách hộ
        AssertInvalid(await CreateAsync(assigned.Id),
            "Khoản thu PHI_RIENG áp dụng cho hộ chỉ định: hãy chọn ít nhất một hộ.");
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

    // ===== PBI-015: cơ sở tính đã cấu hình, tính nhất quán =====

    [Fact]
    [Trait("PBI", "PBI-015")]
    public async Task ApartmentWithoutArea_RejectsAreaBasedPeriod_InsteadOfSkippingTheHousehold()
    {
        var fee = _repo.SeedFeeItem("PHI_DV", CalculationBasis.DienTich, "m²", 7_000m);
        _repo.SeedHousehold("HK-1", "A-101", area: 50m, residents: 1);
        _repo.SeedHousehold("HK-2", "A-102", area: 0m, residents: 1);

        AssertInvalid(await CreateAsync(fee.Id),
            "Căn hộ A-102 chưa có diện tích hợp lệ, chưa tính được khoản thu PHI_DV theo diện tích.");
        Assert.Empty(_repo.Periods);
    }

    [Fact]
    [Trait("PBI", "PBI-015")]
    public async Task Calculate_GivesTheSameAmountAsTheBillingPeriod()
    {
        var service = _repo.SeedFeeItem("PHI_DV", CalculationBasis.DienTich, "m²", 2_505m);
        var hygiene = _repo.SeedFeeItem("PHI_VS", CalculationBasis.NhanKhau, "người", 6_000m);
        _repo.SeedHousehold("HK-0501", "A-501", area: 72.5m, residents: 3);
        var household = Assert.Single(await _repo.GetHouseholdBasesAsync()).HouseholdId;

        var period = (await CreateAsync(service.Id, hygiene.Id)).Data!;
        var bill = Assert.Single(period.Households);

        foreach (var fee in new[] { service, hygiene })
        {
            var calculated = await _service.CalculateAsync(fee.Id, household, Today);
            Assert.Equal(FeeItemStatus.Success, calculated.Status);
            Assert.Equal(bill.Lines.Single(l => l.FeeItemId == fee.Id).Amount, calculated.Data!.Amount);
        }

        // 72,5 m² × 2.505 = 181.612,5 → 181.613; giải thích được số tiền (AC-007)
        var detail = (await _service.CalculateAsync(service.Id, household, Today)).Data!;
        Assert.Equal((72.5m, 2_505m, MonthStart, 181_613m), (detail.Quantity, detail.Rate, detail.RateEffectiveFrom, detail.Amount));
        Assert.Equal(("HK-0501", "A-501", Today), (detail.HouseholdNumber, detail.ApartmentCode, detail.Date));
    }

    [Fact]
    [Trait("PBI", "PBI-015")]
    public async Task Calculate_DefaultsToToday_AndReportsWhatIsMissing()
    {
        var fee = _repo.SeedFeeItem("PHI_DV", CalculationBasis.DienTich, "m²", 7_000m);
        _repo.SeedHousehold("HK-1", "A-101", area: 50m, residents: 1);
        _repo.SeedHousehold("HK-2", "A-102", area: 0m, residents: 1);

        var today = await _service.CalculateAsync(fee.Id, 1, null);
        Assert.Equal((Today, 350_000m), (today.Data!.Date, today.Data.Amount));

        Assert.Equal(FeeItemStatus.NotFound, (await _service.CalculateAsync(404, 1, null)).Status);
        Assert.Equal(FeeItemStatus.NotFound, (await _service.CalculateAsync(fee.Id, 404, null)).Status);

        var beforeRate = await _service.CalculateAsync(fee.Id, 1, new DateOnly(2026, 10, 31));
        Assert.Equal(FeeItemStatus.Invalid, beforeRate.Status);
        Assert.Equal("Khoản thu PHI_DV chưa có đơn giá hiệu lực vào ngày 31/10/2026.", beforeRate.Message);

        var noArea = await _service.CalculateAsync(fee.Id, 2, null);
        Assert.Equal(FeeItemStatus.Invalid, noArea.Status);
        Assert.StartsWith("Căn hộ A-102 chưa có diện tích hợp lệ", noArea.Message);
        Assert.Empty(_repo.Periods); // tính thử không lưu gì
    }

    [Fact]
    [Trait("PBI", "PBI-015")]
    public async Task Calculate_RejectsTheSameFeeItemsAsTheBillingPeriod_WithTheSameMessage()
    {
        var donation = _repo.SeedFeeItem("QUY_TU_THIEN", CalculationBasis.CoDinh, "hộ", 0m, type: FeeType.TuNguyen);
        var maintenance = _repo.SeedFeeItem("BAO_TRI", CalculationBasis.Ho, "hộ", 10_000m, type: FeeType.BaoTriRieng);
        _repo.SeedHousehold("HK-1", "A-101", area: 50m, residents: 1);

        foreach (var fee in new[] { donation, maintenance })
        {
            var calculated = await _service.CalculateAsync(fee.Id, 1, Today);
            var billed = await CreateAsync(fee.Id);

            Assert.Equal(FeeItemStatus.Invalid, calculated.Status);
            Assert.Equal(billed.Message, calculated.Message);
        }
    }

    // ===== Lập trước kỳ của tháng sau =====

    [Fact]
    public async Task NextMonth_CanBeCreatedInAdvance_WithTheRateOfThatMonth()
    {
        var fee = _repo.SeedFeeItem("PHI_DV", CalculationBasis.DienTich, "m²",
            (7_000m, MonthStart, new DateOnly(2026, 12, 1)),
            (8_000m, new DateOnly(2026, 12, 1), null));
        _repo.SeedHousehold("HK-1", "A-101", area: 10m, residents: 1);

        // Hôm nay 12/11: lập kỳ tháng 12, ngày tính phí 01/12 (sau hôm nay) lấy đơn giá mới của tháng 12
        var result = await _service.CreateAsync(1, "ketoan", Request(fee.Id) with
        {
            Month = 12,
            ChargeDate = new DateOnly(2026, 12, 1),
            DueDate = new DateOnly(2026, 12, 31)
        });

        Assert.Equal(FeeItemStatus.Success, result.Status);
        Assert.Equal((12, 8_000m, 80_000m), (result.Data!.Month, Assert.Single(result.Data.Items).Rate, result.Data.TotalAmount));
    }

    [Fact]
    public async Task ChargeDateLaterThisMonth_IsAllowed()
    {
        var fee = _repo.SeedFeeItem("PHI_DV", CalculationBasis.DienTich, "m²", 7_000m);
        _repo.SeedHousehold("HK-1", "A-101", area: 10m, residents: 1);

        var result = await CreateAsync(new DateOnly(2026, 11, 20), fee.Id);

        Assert.Equal((FeeItemStatus.Success, 70_000m), (result.Status, result.Data!.TotalAmount));
    }

    // ===== Khoản chỉ định hộ: chỉ thu của các hộ được chọn =====

    [Fact]
    public async Task TargetedFee_BillsOnlySelectedHouseholds_WhileGeneralFeeBillsEveryone()
    {
        var service = _repo.SeedFeeItem("PHI_DV", CalculationBasis.DienTich, "m²", 7_000m);
        var repair = _repo.SeedFeeItem("PHI_SUA", CalculationBasis.Ho, "hộ", 300_000m, scope: FeeScope.ChiDinh);
        _repo.SeedHousehold("HK-1", "A-101", area: 50m, residents: 1);
        _repo.SeedHousehold("HK-2", "A-102", area: 60m, residents: 1);
        _repo.SeedHousehold("HK-3", "A-103", area: 70m, residents: 1);

        var result = await CreateTargetedAsync([service.Id, repair.Id], (repair.Id, [1, 3]));

        Assert.Equal(FeeItemStatus.Success, result.Status);
        var period = result.Data!;
        Assert.Equal(3, period.Households.Count); // khoản toàn chung cư vẫn thu của mọi hộ
        Assert.Equal(new[] { "A-101", "A-103" }, period.Households
            .Where(h => h.Lines.Any(l => l.FeeItemId == repair.Id))
            .Select(h => h.ApartmentCode));
        Assert.Equal(600_000m, period.Items.Single(i => i.Code == "PHI_SUA").TotalAmount);
        // A-102 không được chọn: chỉ có phí dịch vụ 60 m² × 7.000
        Assert.Equal(420_000m, period.Households.Single(h => h.ApartmentCode == "A-102").TotalAmount);

        // Tính thử khoản chỉ định cho một hộ ra đúng số trên bảng kê
        var calculated = await _service.CalculateAsync(repair.Id, 1, Today);
        Assert.Equal((FeeItemStatus.Success, 300_000m), (calculated.Status, calculated.Data!.Amount));
    }

    [Fact]
    public async Task TargetedFee_HouseholdSelection_IsValidated()
    {
        var service = _repo.SeedFeeItem("PHI_DV", CalculationBasis.DienTich, "m²", 7_000m);
        var repair = _repo.SeedFeeItem("PHI_SUA", CalculationBasis.Ho, "hộ", 300_000m, scope: FeeScope.ChiDinh);
        _repo.SeedHousehold("HK-1", "A-101", area: 50m, residents: 1);

        const string noHousehold = "Khoản thu PHI_SUA áp dụng cho hộ chỉ định: hãy chọn ít nhất một hộ.";
        AssertInvalid(await CreateTargetedAsync([repair.Id]), noHousehold);
        AssertInvalid(await CreateTargetedAsync([repair.Id], (repair.Id, [])), noHousehold);
        AssertInvalid(await CreateTargetedAsync([repair.Id], (repair.Id, [1, 1])),
            "Danh sách hộ của khoản thu PHI_SUA bị trùng.");
        AssertInvalid(await CreateTargetedAsync([repair.Id], (repair.Id, [404])),
            "Không tìm thấy hộ gia đình #404 trong danh sách hộ của khoản thu PHI_SUA.");
        AssertInvalid(await CreateTargetedAsync([repair.Id], (repair.Id, [1]), (repair.Id, [1])),
            "Khoản thu PHI_SUA có nhiều hơn một danh sách hộ.");
        AssertInvalid(await CreateTargetedAsync([service.Id], (service.Id, [1])),
            "Khoản thu PHI_DV áp dụng toàn chung cư, không chọn hộ riêng.");
        AssertInvalid(await CreateTargetedAsync([service.Id], (repair.Id, [1])),
            $"Có danh sách hộ cho khoản thu #{repair.Id} nhưng khoản này không được chọn vào kỳ.");
        // JSON "targetedFeeItems": [null] bị từ chối có thông báo, không gây lỗi 500
        AssertInvalid(await _service.CreateAsync(1, "ketoan", Request(repair.Id) with { TargetedFeeItems = [null!] }),
            "Danh sách hộ của khoản thu chỉ định không hợp lệ.");
        Assert.Empty(_repo.Periods);
    }

    [Theory]
    [InlineData(FeeCycle.Thang)]
    [InlineData(FeeCycle.Nam)]
    [InlineData(FeeCycle.MotLan)]
    public async Task TargetedFee_CanBeBilledSeveralTimesInTheSameMonth(FeeCycle cycle)
    {
        var repair = _repo.SeedFeeItem("PHI_SUA", CalculationBasis.Ho, "hộ", 300_000m, scope: FeeScope.ChiDinh, cycle: cycle);
        _repo.SeedHousehold("HK-1", "A-101", area: 50m, residents: 1);
        _repo.Billed.Add(new BilledFeeItemRow(repair.Id, 99, 2026, 11)); // đã có một kỳ tháng 11 thu khoản này

        var result = await CreateTargetedAsync([repair.Id], (repair.Id, [1]));

        Assert.Equal(FeeItemStatus.Success, result.Status);
    }

    [Fact]
    public async Task HouseholdOptions_AreSortedByApartmentCode()
    {
        _repo.SeedHousehold("HK-2", "B-201", area: 50m, residents: 1);
        _repo.SeedHousehold("HK-1", "A-101", area: 50m, residents: 1);

        var options = await _service.GetHouseholdOptionsAsync();

        Assert.Equal(new[] { "A-101", "B-201" }, options.Select(o => o.ApartmentCode));
        Assert.Equal((2, "HK-1"), (options[0].HouseholdId, options[0].HouseholdNumber));
    }

    // ===== Hộ không phải nộp gì thì không có trong bảng kê =====

    [Fact]
    public async Task HouseholdWhoseAmountRoundsToZero_IsLeftOutOfTheBill()
    {
        var fee = _repo.SeedFeeItem("PHI_NHO", CalculationBasis.DienTich, "m²", 0.01m);
        _repo.SeedHousehold("HK-1", "A-101", area: 10m, residents: 1);  // 10 × 0,01 = 0,1 → 0 đ
        _repo.SeedHousehold("HK-2", "A-102", area: 100m, residents: 1); // 100 × 0,01 = 1 đ

        var result = await CreateAsync(fee.Id);

        var household = Assert.Single(result.Data!.Households);
        Assert.Equal(("A-102", 1m), (household.ApartmentCode, household.TotalAmount));
    }

    [Fact]
    public async Task BillWithNothingToCollect_IsRejected()
    {
        var hygiene = _repo.SeedFeeItem("PHI_VS", CalculationBasis.NhanKhau, "người", 6_000m);
        _repo.SeedHousehold("HK-1", "A-101", area: 50m, residents: 0);

        AssertInvalid(await CreateAsync(hygiene.Id),
            "Bảng kê trống: không hộ nào phải nộp số tiền lớn hơn 0 đ, chưa lập kỳ thu.");
        Assert.Empty(_repo.Periods);
    }

    // ===== Hàm hỗ trợ =====

    /// <summary>Yêu cầu lập kỳ tháng 11/2026, tính phí hôm nay, hạn nộp cuối tháng.</summary>
    private static CreateBillingPeriodRequest Request(params int[] feeItemIds) => new()
    {
        Year = 2026,
        Month = 11,
        ChargeDate = Today,
        DueDate = new DateOnly(2026, 11, 30),
        FeeItemIds = feeItemIds
    };

    private Task<FeeItemOperationResult<BillingPeriodResponse>> CreateTargetedAsync(
        int[] feeItemIds, params (int FeeItemId, int[] HouseholdIds)[] targets) =>
        _service.CreateAsync(1, "ketoan", Request(feeItemIds) with
        {
            TargetedFeeItems = targets
                .Select(t => new TargetedFeeItemRequest { FeeItemId = t.FeeItemId, HouseholdIds = t.HouseholdIds })
                .ToList()
        });

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
                Apartment = new ApartmentEntity
                {
                    Code = apartmentCode,
                    BuildingId = 1,
                    Building = new BuildingEntity { Id = 1, Name = "Tòa " + apartmentCode[0] },
                    Area = area
                }
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
            Task.FromResult(_households.Select(ToBasis).ToList());

        public Task<HouseholdBillingBasis?> GetHouseholdBasisAsync(int householdId, CancellationToken ct = default) =>
            Task.FromResult(_households.Where(h => h.Id == householdId).Select(ToBasis).FirstOrDefault());

        private HouseholdBillingBasis ToBasis(HouseholdEntity h) =>
            new(h.Id, h.HouseholdNumber, h.Apartment.Code, h.Apartment.Area, _residentCounts[h.Id]);

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
