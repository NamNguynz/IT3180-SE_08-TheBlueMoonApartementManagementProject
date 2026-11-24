using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueMoonApartementManagementConsole.Tests.Integration;

/// <summary>
/// PBI-022/PBI-023/PBI-044: báo cáo thu và công nợ, báo cáo thu chi trên PostgreSQL thật.
/// Kiểm tra cùng một service cho JSON/Excel, GROUP BY ở DB, validate 400, audit ExportReport
/// và các sheet/tên file của file Excel.
/// </summary>
[Collection(SequentialDatabaseCollection.Name)]
[Trait("PBI", "PBI-044")]
public class ReportApiTests(BlueMoonAppFactory factory) : IClassFixture<BlueMoonAppFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly string _prefix = "BC" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    private readonly List<int> _periodIds = [];
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (BlueMoonAppFactory.ConnectionString is null) return;

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Payments.Where(p => _periodIds.Contains(p.BillingPeriodId)).ExecuteDeleteAsync();
        await db.BillingPeriods.Where(p => _periodIds.Contains(p.Id)).ExecuteDeleteAsync();
        await db.FeeRates.Where(r => r.FeeItem.Code.StartsWith(_prefix)).ExecuteDeleteAsync();
        await db.FeeItems.Where(f => f.Code.StartsWith(_prefix)).ExecuteDeleteAsync();
        await db.Apartments.Where(a => a.Code.StartsWith(_prefix)).ExecuteDeleteAsync();
    }

    [SkippableFact]
    public async Task DebtReport_AggregatesByHousehold_AndSharesDataWithExcel()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();
        var (periodId, householdId, householdNumber) = await BillOneHouseholdAsync(admin);
        await RecordPaymentAsync(admin, periodId, householdId, 40_000m);

        var report = await ReadAsync<DebtReportResult>(await admin.GetAsync(
            $"/api/bao-cao/thu-cong-no?kyThu={periodId}&loaiKhoan=BatBuoc&trangThai=NopMotPhan&ho={householdNumber}&page=1&pageSize=5"),
            HttpStatusCode.OK);

        Assert.Equal((100_000m, 40_000m, 60_000m, 1),
            (report.Summary.MustPay, report.Summary.Paid, report.Summary.Remaining, report.Summary.OwedHouseholds));
        var row = Assert.Single(report.Rows.Items);
        Assert.Equal(householdNumber, row.HouseholdNumber);
        Assert.Equal(PaymentStatus.NopMotPhan, row.Status);
        Assert.Equal(60_000m, row.Remaining);
        Assert.Equal(5, report.Rows.PageSize);

        var paidOnly = await ReadAsync<DebtReportResult>(await admin.GetAsync(
            $"/api/bao-cao/thu-cong-no?kyThu={periodId}&trangThai=DaNopDu"), HttpStatusCode.OK);
        Assert.Empty(paidOnly.Rows.Items);
        Assert.Equal(0m, paidOnly.Summary.MustPay);

        var invalid = await ReadAsync<ApiError>(
            await admin.GetAsync("/api/bao-cao/thu-cong-no"), HttpStatusCode.BadRequest);
        Assert.Equal("Vui chọn kỳ thu cần xem báo cáo.", invalid.Message);

        var before = await AuditCountAsync("DebtReport");
        var response = await admin.GetAsync($"/api/bao-cao/thu-cong-no/excel?kyThu={periodId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
        var disposition = response.Content.Headers.ContentDisposition?.FileName ?? "";
        Assert.Contains("bao-cao-thu-cong-no", disposition);
        Assert.EndsWith(".xlsx", disposition);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
        using (var stream = new MemoryStream(bytes))
        using (var workbook = new XLWorkbook(stream))
        {
            Assert.Equal(2, workbook.Worksheets.Count);
            Assert.Equal("Tong hop", workbook.Worksheet(1).Name);
            Assert.Equal("Chi tiet ho", workbook.Worksheet(2).Name);
            Assert.Contains("BÁO CÁO THU VÀ CÔNG NỢ", workbook.Worksheet(1).Cell(1, 1).GetString());
            Assert.Equal(householdNumber, workbook.Worksheet(2).Cell(4, 3).GetString());
        }

        Assert.Equal(before + 1, await AuditCountAsync("DebtReport"));
        var log = await AuditLastAsync("DebtReport");
        Assert.NotNull(log);
        Assert.NotNull(log.UserId);
        Assert.False(string.IsNullOrWhiteSpace(log.Username));
        Assert.Contains("KyThu", log.Detail ?? "");
        Assert.Contains("số dòng 1", log.Detail ?? "");
    }

    [SkippableFact]
    public async Task DebtReport_TotalsAllFeesOfTheSameMonth_AfterTheyAreMerged()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();
        var (periodId, _, householdNumber) = await BillOneHouseholdAsync(admin);

        var extra = await ReadAsync<FeeItemResponse>(await admin.PostAsJsonAsync("/api/khoan-thu", new CreateFeeItemRequest
        {
            Code = _prefix + "_GUI",
            Name = "Phí gửi xe test báo cáo",
            FeeType = FeeType.BatBuoc,
            FeeScope = FeeScope.Chung,
            FeeCycle = FeeCycle.Thang,
            CalculationBasis = CalculationBasis.DienTich,
            Unit = "m²",
            Rate = 5_000m,
            EffectiveFrom = Today
        }, Json), HttpStatusCode.Created);

        int countAfterFirst;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            countAfterFirst = await db.BillingPeriods.CountAsync(p => p.Year == Today.Year && p.Month == Today.Month);
        }

        // Khoản thu mới phát sinh cùng tháng phải bổ sung vào kỳ cũ, không tạo kỳ trùng tháng
        var second = await ReadAsync<BillingPeriodResponse>(await admin.PostAsJsonAsync("/api/ky-thu",
            new CreateBillingPeriodRequest
            {
                Year = Today.Year,
                Month = Today.Month,
                ChargeDate = Today,
                DueDate = Today.AddDays(15),
                FeeItemIds = [extra.Id],
                TargetedFeeItems = []
            }, Json), HttpStatusCode.Created);
        _periodIds.Add(second.Id);
        Assert.Equal(periodId, second.Id);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(countAfterFirst,
                await db.BillingPeriods.CountAsync(p => p.Year == Today.Year && p.Month == Today.Month));
        }

        var report = await ReadAsync<DebtReportResult>(await admin.GetAsync(
            $"/api/bao-cao/thu-cong-no?kyThu={periodId}&ho={householdNumber}"), HttpStatusCode.OK);
        // 10 m² × (10.000 + 5.000) = 150.000 đ của cả tháng
        Assert.Equal(150_000m, report.Summary.MustPay);
        Assert.Equal(150_000m, Assert.Single(report.Rows.Items).MustPay);
    }

    [SkippableFact]
    public async Task IncomeReport_GroupsByRecordedAt_AndValidatesRanges()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();
        var (periodId, householdId, _) = await BillOneHouseholdAsync(admin);
        await RecordPaymentAsync(admin, periodId, householdId, 40_000m);
        var today = Today.ToString("yyyy-MM-dd");

        var report = await ReadAsync<IncomeReportResult>(await admin.GetAsync(
            $"/api/bao-cao/thu-chi?ky=Thang&from={today}&to={today}&page=1&pageSize=5"), HttpStatusCode.OK);

        Assert.Equal(40_000m, report.Total);
        Assert.Equal(1, report.Rows.Total);
        var row = Assert.Single(report.Rows.Items);
        Assert.Equal(40_000m, row.ByFeeType["BatBuoc"]);
        Assert.Equal(0m, row.ByFeeType["TuNguyen"]);
        Assert.Equal("Tháng " + Today.Month.ToString("00") + "/" + Today.Year, row.Label);

        var badOrder = await ReadAsync<ApiError>(await admin.GetAsync(
            "/api/bao-cao/thu-chi?ky=Thang&from=2026-06-02&to=2026-06-01"), HttpStatusCode.BadRequest);
        Assert.Contains("Từ ngày không được sau đến ngày", badOrder.Message);

        var tooLong = await ReadAsync<ApiError>(await admin.GetAsync(
            "/api/bao-cao/thu-chi?ky=Thang&from=2025-01-01&to=2026-12-31"), HttpStatusCode.BadRequest);
        Assert.Contains("không được vượt quá", tooLong.Message);

        var missing = await ReadAsync<ApiError>(await admin.GetAsync(
            "/api/bao-cao/thu-chi?ky=Thang"), HttpStatusCode.BadRequest);
        Assert.Contains("mốc thời gian", missing.Message);

        var before = await AuditCountAsync("IncomeReport");
        var response = await admin.GetAsync($"/api/bao-cao/thu-chi/excel?ky=Thang&from={today}&to={today}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var disposition = response.Content.Headers.ContentDisposition?.FileName ?? "";
        Assert.Contains("bao-cao-thu-chi", disposition);
        Assert.EndsWith(".xlsx", disposition);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        using (var stream = new MemoryStream(bytes))
        using (var workbook = new XLWorkbook(stream))
        {
            Assert.Equal(2, workbook.Worksheets.Count);
            Assert.Equal("Ky thu", workbook.Worksheet(1).Name);
            Assert.Equal("Chi phi", workbook.Worksheet(2).Name);
            Assert.Contains("BÁO CÁO THU VÀ CÔNG NỢ", workbook.Worksheet(1).Cell(1, 1).GetString());
            Assert.Equal("Chưa có dữ liệu chi phí", workbook.Worksheet(2).Cell(3, 1).GetString());
        }

        Assert.Equal(before + 1, await AuditCountAsync("IncomeReport"));
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

    private async Task<int> AuditCountAsync(string target)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogs.CountAsync(l => l.Action == AuditAction.ExportReport && l.Target == target);
    }

    private async Task<AuditLog?> AuditLastAsync(string target)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogs
            .Where(l => l.Action == AuditAction.ExportReport && l.Target == target)
            .OrderByDescending(l => l.Id)
            .FirstOrDefaultAsync();
    }

    private static Task<HttpResponseMessage> RecordPaymentAsync(
        HttpClient admin, int periodId, int householdId, decimal amount) =>
        admin.PostAsJsonAsync("/api/thu-tien", new RecordPaymentRequest
        {
            BillingPeriodId = periodId,
            HouseholdId = householdId,
            Amount = amount,
            PaidAt = DateTimeOffset.Now.AddMinutes(-10),
            Method = PaymentMethod.TienMat,
            PayerName = "Người nộp test báo cáo"
        }, Json);

    /// <summary>Một khoản bắt buộc 10.000 đ/m², một hộ 10 m², kỳ thu tháng hiện tại.</summary>
    private async Task<(int PeriodId, int HouseholdId, string HouseholdNumber)> BillOneHouseholdAsync(HttpClient admin)
    {
        var fee = await ReadAsync<FeeItemResponse>(await admin.PostAsJsonAsync("/api/khoan-thu", new CreateFeeItemRequest
        {
            Code = _prefix + "_DV",
            Name = "Phí test báo cáo",
            FeeType = FeeType.BatBuoc,
            FeeScope = FeeScope.Chung,
            FeeCycle = FeeCycle.Thang,
            CalculationBasis = CalculationBasis.DienTich,
            Unit = "m²",
            Rate = 10_000m,
            EffectiveFrom = Today
        }, Json), HttpStatusCode.Created);

        var code = _prefix + "-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var buildingId = await TestBuilding.IdAsync(admin, "Tòa test báo cáo");
        var apartment = await ReadAsync<ApartmentProfileResponse>(await admin.PostAsJsonAsync("/api/can-ho", new CreateApartmentRequest
        {
            Code = code,
            BuildingId = buildingId,
            Type = ApartmentType.CanHo,
            Floor = 2,
            Area = 10m,
            BedroomCount = 1,
            Status = ApartmentStatus.DangChoThue
        }, Json), HttpStatusCode.Created);

        var household = await ReadAsync<HouseholdProfileResponse>(await admin.PostAsJsonAsync(
            $"/api/ho-gia-dinh/can-ho/{apartment.Id}", new CreateHouseholdRequest
            {
                HouseholdNumber = code,
                HeadName = "Nguyễn Văn Báo Cáo",
                HouseNumber = code,
                RegisteredAt = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
                Residents =
                [
                    new ResidentProfileRequest
                    {
                        FullName = "Nhân khẩu test báo cáo",
                        DateOfBirth = new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero),
                        Gender = "Nam"
                    }
                ]
            }, Json), HttpStatusCode.Created);

        var period = await ReadAsync<BillingPeriodResponse>(await admin.PostAsJsonAsync("/api/ky-thu", new CreateBillingPeriodRequest
        {
            Year = Today.Year,
            Month = Today.Month,
            ChargeDate = Today,
            DueDate = Today.AddDays(15),
            FeeItemIds = [fee.Id],
            TargetedFeeItems = []
        }, Json), HttpStatusCode.Created);
        _periodIds.Add(period.Id);

        return (period.Id, household.Id, code);
    }
}
