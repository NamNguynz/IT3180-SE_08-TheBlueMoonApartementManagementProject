using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueMoonApartementManagementConsole.Tests.Integration;

/// <summary>
/// Test tích hợp lập kỳ thu và bảng kê phải thu (PBI-014) trên PostgreSQL thật: gọi API như trang Kỳ thu,
/// kiểm tra migration, cách tính theo diện tích và nhân khẩu, đơn giá được chụp lại khi lập kỳ (AC-007).
/// Dữ liệu giả có tiền tố riêng, xóa khi test xong (kỳ thu trước, rồi khoản thu, căn hộ).
/// Cần PostgreSQL: đặt biến môi trường BLUEMOON_TEST_CONNECTION, nếu không các test sẽ bị skip.
/// </summary>
[Collection(SequentialDatabaseCollection.Name)]
[Trait("PBI", "PBI-014")]
public class BillingPeriodApiTests(BlueMoonAppFactory factory) : IClassFixture<BlueMoonAppFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly string _prefix = "KT" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    private readonly List<int> _periodIds = [];

    // App dùng TimeProvider.System cho nghiệp vụ, nên "hôm nay" là ngày thật của máy chạy test
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (BlueMoonAppFactory.ConnectionString is null) return;

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Kỳ thu giữ khóa tới khoản thu và hộ (Restrict), nên xóa trước; khoản và dòng phải thu xóa theo
        await db.BillingPeriods.Where(p => _periodIds.Contains(p.Id)).ExecuteDeleteAsync();
        await db.FeeRates.Where(r => r.FeeItem.Code.StartsWith(_prefix)).ExecuteDeleteAsync();
        await db.FeeItems.Where(f => f.Code.StartsWith(_prefix)).ExecuteDeleteAsync();
        await db.Apartments.Where(a => a.Code.StartsWith(_prefix)).ExecuteDeleteAsync();
    }

    // ===== Test =====

    [SkippableFact]
    public async Task CreatePeriod_BillsEachHouseholdByBasis_KeepsRateWhenRateChanges_AndAudits()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();
        var serviceFee = await CreateFeeItemAsync(admin, "_DV", CalculationBasis.DienTich, "m²", 7_000m);
        var hygieneFee = await CreateFeeItemAsync(admin, "_VS", CalculationBasis.NhanKhau, "người", 6_000m);
        var householdId = await CreateHouseholdAsync(admin, area: 72.5m, residents: 2);

        var created = await ReadAsync<BillingPeriodResponse>(
            await PostPeriodAsync(admin, serviceFee.Id, hygieneFee.Id), HttpStatusCode.Created);
        _periodIds.Add(created.Id);

        // Hộ test: 72,5 m² × 7.000 = 507.500 và 2 người × 6.000 = 12.000, gom thành 519.500
        var bill = Assert.Single(created.Households, h => h.HouseholdId == householdId);
        Assert.Equal(519_500m, bill.TotalAmount);
        Assert.Equal(507_500m, Assert.Single(bill.Lines, l => l.FeeItemId == serviceFee.Id).Amount);

        // BQT đổi đơn giá sau khi lập kỳ: bảng kê đã lập không đổi (AC-007)
        var rateChange = await admin.PostAsJsonAsync($"/api/khoan-thu/{serviceFee.Id}/don-gia",
            new ChangeRateRequest { Rate = 9_000m, EffectiveFrom = Today.AddDays(1) }, Json);
        Assert.Equal(HttpStatusCode.OK, rateChange.StatusCode);

        var reloaded = await ReadAsync<BillingPeriodResponse>(
            await admin.GetAsync($"/api/ky-thu/{created.Id}"), HttpStatusCode.OK);
        Assert.Equal(created.TotalAmount, reloaded.TotalAmount);
        Assert.Equal(7_000m, Assert.Single(reloaded.Items, i => i.FeeItemId == serviceFee.Id).Rate);

        // API tra đơn giá theo ngày (dùng chung FeeCalculator.RateOn, PBI-015): hôm nay vẫn giá cũ, ngày mai giá mới
        foreach (var (day, expected) in new[] { (Today, 7_000m), (Today.AddDays(1), 9_000m) })
        {
            var lookup = await ReadAsync<FeeRateLookupResponse>(
                await admin.GetAsync($"/api/khoan-thu/{serviceFee.Id}/don-gia?ngay={day:yyyy-MM-dd}"), HttpStatusCode.OK);
            Assert.Equal(expected, lookup.Rate);
        }

        var list = await ReadAsync<List<BillingPeriodSummaryResponse>>(await admin.GetAsync("/api/ky-thu"), HttpStatusCode.OK);
        var summary = Assert.Single(list, p => p.Id == created.Id);
        Assert.Equal(2, summary.ItemCount);
        Assert.Equal(created.TotalAmount, summary.TotalAmount);
        Assert.Equal(created.Households.Count, summary.HouseholdCount);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(l =>
            l.Action == AuditAction.CreateBillingPeriod && l.Target == created.Id.ToString()));
    }

    [SkippableFact]
    public async Task SameMonthlyFeeTwiceInOneMonth_Returns409_NextMonthIsAllowed_LaterMonthReturns400()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();
        var fee = await CreateFeeItemAsync(admin, "_QL", CalculationBasis.Ho, "hộ", 50_000m);
        await CreateHouseholdAsync(admin, area: 60m, residents: 1);

        var first = await ReadAsync<BillingPeriodResponse>(await PostPeriodAsync(admin, fee.Id), HttpStatusCode.Created);
        _periodIds.Add(first.Id);

        var duplicate = await ReadAsync<ApiError>(await PostPeriodAsync(admin, fee.Id), HttpStatusCode.Conflict);
        Assert.Equal($"Khoản thu {fee.Code} đã được lập trong kỳ thu tháng {Today.Month:00}/{Today.Year}.", duplicate.Message);

        // Lập trước được kỳ của tháng sau (ngày tính phí sau hôm nay), xa hơn thì bị từ chối
        var nextMonth = new DateOnly(Today.Year, Today.Month, 1).AddMonths(1);
        var advance = await ReadAsync<BillingPeriodResponse>(
            await PostPeriodAsync(admin, nextMonth, [fee.Id]), HttpStatusCode.Created);
        _periodIds.Add(advance.Id);
        Assert.Equal((nextMonth.Year, nextMonth.Month), (advance.Year, advance.Month));

        var later = nextMonth.AddMonths(1);
        var tooEarly = await ReadAsync<ApiError>(await PostPeriodAsync(admin, later, [fee.Id]), HttpStatusCode.BadRequest);
        Assert.Equal($"Chỉ lập trước được kỳ thu của tháng sau ({nextMonth.Month:00}/{nextMonth.Year}), " +
                     $"chưa lập được kỳ tháng {later.Month:00}/{later.Year}.", tooEarly.Message);
    }

    [SkippableFact]
    public async Task TargetedFee_BillsOnlyChosenHouseholds_AndCanRepeatInTheSameMonth()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();
        var repair = await CreateFeeItemAsync(admin, "_SUA", CalculationBasis.Ho, "hộ", 300_000m, FeeScope.ChiDinh);
        var chosen = await CreateHouseholdAsync(admin, area: 50m, residents: 1);
        var other = await CreateHouseholdAsync(admin, area: 60m, residents: 1);

        // Pop-up chọn hộ lấy danh sách từ api/ky-thu/ho-gia-dinh
        var options = await ReadAsync<List<HouseholdOptionResponse>>(
            await admin.GetAsync("/api/ky-thu/ho-gia-dinh"), HttpStatusCode.OK);
        Assert.Contains(options, o => o.HouseholdId == chosen);
        Assert.Contains(options, o => o.HouseholdId == other);

        // Không gửi danh sách hộ thì bị từ chối
        var missing = await ReadAsync<ApiError>(await PostPeriodAsync(admin, repair.Id), HttpStatusCode.BadRequest);
        Assert.Equal($"Khoản thu {repair.Code} áp dụng cho hộ chỉ định: hãy chọn ít nhất một hộ.", missing.Message);

        // Hai kỳ trong cùng tháng cho khoản chỉ định: mỗi kỳ chỉ có hộ được chọn, trang thu tiền chỉ hiện hộ đó
        for (var i = 0; i < 2; i++)
        {
            var period = await ReadAsync<BillingPeriodResponse>(await PostPeriodAsync(admin, Today, [repair.Id],
                new TargetedFeeItemRequest { FeeItemId = repair.Id, HouseholdIds = [chosen] }), HttpStatusCode.Created);
            _periodIds.Add(period.Id);
            var bill = Assert.Single(period.Households);
            Assert.Equal((chosen, 300_000m), (bill.HouseholdId, bill.TotalAmount));

            var collection = await ReadAsync<PeriodCollectionResponse>(
                await admin.GetAsync($"/api/thu-tien/ky-thu/{period.Id}"), HttpStatusCode.OK);
            var row = Assert.Single(collection.Households);
            Assert.Equal((chosen, PaymentStatus.ChuaNop), (row.HouseholdId, row.Status));
        }
    }

    [SkippableFact]
    public async Task ZeroAmountLine_IsNotShown_SoTheHouseholdIsNotMarkedAsPaid()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();
        var fee = await CreateFeeItemAsync(admin, "_KO", CalculationBasis.Ho, "hộ", 300_000m, FeeScope.ChiDinh);
        var payer = await CreateHouseholdAsync(admin, area: 50m, residents: 1);
        var notPaying = await CreateHouseholdAsync(admin, area: 60m, residents: 1);

        var period = await ReadAsync<BillingPeriodResponse>(await PostPeriodAsync(admin, Today, [fee.Id],
            new TargetedFeeItemRequest { FeeItemId = fee.Id, HouseholdIds = [payer] }), HttpStatusCode.Created);
        _periodIds.Add(period.Id);

        // Dòng 0 đ như ở các kỳ lập trước bản sửa này (hộ A-101 hiện "Đã nộp đủ" dù chưa nộp)
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = await db.BillingPeriodItems.SingleAsync(i => i.BillingPeriodId == period.Id);
            db.Receivables.Add(new ReceivableEntity { BillingPeriodItemId = item.Id, HouseholdId = notPaying, Quantity = 1, Amount = 0 });
            await db.SaveChangesAsync();
        }

        var collection = await ReadAsync<PeriodCollectionResponse>(
            await admin.GetAsync($"/api/thu-tien/ky-thu/{period.Id}"), HttpStatusCode.OK);
        Assert.Equal(payer, Assert.Single(collection.Households).HouseholdId);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/thu-tien/ky-thu/{period.Id}/ho/{notPaying}")).StatusCode);

        var detail = await ReadAsync<BillingPeriodResponse>(await admin.GetAsync($"/api/ky-thu/{period.Id}"), HttpStatusCode.OK);
        Assert.Equal(payer, Assert.Single(detail.Households).HouseholdId);
        var summary = Assert.Single(
            await ReadAsync<List<BillingPeriodSummaryResponse>>(await admin.GetAsync("/api/ky-thu"), HttpStatusCode.OK),
            p => p.Id == period.Id);
        Assert.Equal(1, summary.HouseholdCount);
    }

    [SkippableFact]
    [Trait("PBI", "PBI-015")]
    public async Task CalculateApi_MatchesTheBillingPeriodLine_AndSavesNothing()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();
        var fee = await CreateFeeItemAsync(admin, "_TP", CalculationBasis.DienTich, "m²", 2_505m);
        var householdId = await CreateHouseholdAsync(admin, area: 72.5m, residents: 1);
        var url = $"/api/ky-thu/tinh-phi?khoanThu={fee.Id}&hoGiaDinh={householdId}&ngay={Today:yyyy-MM-dd}";

        // Tính thử trước khi lập kỳ: 72,5 m² × 2.505 = 181.612,5 → 181.613 đ, chưa có kỳ thu nào được lưu
        var calculated = await ReadAsync<FeeCalculationResponse>(await admin.GetAsync(url), HttpStatusCode.OK);
        Assert.Equal((72.5m, 2_505m, 181_613m), (calculated.Quantity, calculated.Rate, calculated.Amount));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.BillingPeriodItems.AnyAsync(i => i.FeeItemId == fee.Id));
        }

        // Lập kỳ thật: dòng của hộ đúng bằng số đã tính thử
        var period = await ReadAsync<BillingPeriodResponse>(await PostPeriodAsync(admin, fee.Id), HttpStatusCode.Created);
        _periodIds.Add(period.Id);
        var line = Assert.Single(Assert.Single(period.Households, h => h.HouseholdId == householdId).Lines);
        Assert.Equal(calculated.Amount, line.Amount);

        var missingHousehold = await ReadAsync<ApiError>(
            await admin.GetAsync($"/api/ky-thu/tinh-phi?khoanThu={fee.Id}"), HttpStatusCode.BadRequest);
        Assert.Equal("Hãy chọn khoản thu (khoanThu) và hộ gia đình (hoGiaDinh) cần tính phí.", missingHousehold.Message);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/ky-thu/tinh-phi?khoanThu={fee.Id}&hoGiaDinh=999999999")).StatusCode);
    }

    [SkippableFact]
    public async Task UnknownPeriod_Returns404()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();

        var response = await admin.GetAsync("/api/ky-thu/999999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ===== Hàm hỗ trợ =====

    private static void SkipIfNoDatabase() =>
        Skip.If(BlueMoonAppFactory.ConnectionString is null, BlueMoonAppFactory.SkipReason);

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = factory.CreateHttpsClient();
        var response = await client.PostAsync(BlueMoonAppFactory.TestSignInPath, null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} trả {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    private static Task<HttpResponseMessage> PostPeriodAsync(HttpClient admin, params int[] feeItemIds) =>
        PostPeriodAsync(admin, Today, feeItemIds);

    /// <summary>Lập kỳ của tháng chứa chargeDate, tính phí vào chargeDate; khoản chỉ định hộ kèm danh sách hộ.</summary>
    private static Task<HttpResponseMessage> PostPeriodAsync(
        HttpClient admin, DateOnly chargeDate, int[] feeItemIds, params TargetedFeeItemRequest[] targets) =>
        admin.PostAsJsonAsync("/api/ky-thu", new CreateBillingPeriodRequest
        {
            Year = chargeDate.Year,
            Month = chargeDate.Month,
            ChargeDate = chargeDate,
            DueDate = chargeDate.AddDays(15),
            FeeItemIds = feeItemIds,
            TargetedFeeItems = targets
        }, Json);

    private async Task<FeeItemResponse> CreateFeeItemAsync(
        HttpClient admin, string suffix, CalculationBasis basis, string unit, decimal rate, FeeScope scope = FeeScope.Chung) =>
        await ReadAsync<FeeItemResponse>(await admin.PostAsJsonAsync("/api/khoan-thu", new CreateFeeItemRequest
        {
            Code = _prefix + suffix,
            Name = "Phí test kỳ thu " + suffix,
            FeeType = FeeType.BatBuoc,
            FeeScope = scope,
            FeeCycle = FeeCycle.Thang,
            CalculationBasis = basis,
            Unit = unit,
            Rate = rate,
            EffectiveFrom = Today
        }, Json), HttpStatusCode.Created);

    private async Task<int> CreateHouseholdAsync(HttpClient admin, decimal area, int residents)
    {
        var code = _prefix + "-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var apartment = await ReadAsync<ApartmentProfileResponse>(await admin.PostAsJsonAsync("/api/can-ho", new CreateApartmentRequest
        {
            Code = code,
            Building = "Tòa test kỳ thu",
            Type = ApartmentType.CanHo,
            Floor = 4,
            Area = area,
            BedroomCount = 2,
            Status = ApartmentStatus.DangChoThue
        }, Json), HttpStatusCode.Created);

        var household = await ReadAsync<HouseholdProfileResponse>(await admin.PostAsJsonAsync(
            $"/api/ho-gia-dinh/can-ho/{apartment.Id}", new CreateHouseholdRequest
            {
                HouseholdNumber = code,
                HeadName = "Nguyễn Văn Kỳ",
                HouseNumber = code,
                RegisteredAt = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
                Residents = Enumerable.Range(1, residents).Select(i => new ResidentProfileRequest
                {
                    FullName = $"Nhân khẩu test {i}",
                    DateOfBirth = new DateTimeOffset(1990, 1, i, 0, 0, 0, TimeSpan.Zero),
                    Gender = "Nam"
                }).ToList()
            }, Json), HttpStatusCode.Created);

        return household.Id;
    }
}
