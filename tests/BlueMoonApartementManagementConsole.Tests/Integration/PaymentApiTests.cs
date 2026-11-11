using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BlueMoonApartementManagementConsole.Tests.Integration;

/// <summary>
/// Test tích hợp ghi nhận thu tiền, nộp một phần (PBI-016) trên PostgreSQL thật: lập kỳ thu cho một hộ test,
/// ghi nhận qua API như trang Ghi nhận thu tiền, kiểm tra cộng dồn, trạng thái, từ chối và ràng buộc trong DB.
/// Dữ liệu giả có tiền tố riêng, xóa khi test xong (lần nộp, kỳ thu, khoản thu, căn hộ).
/// Cần PostgreSQL: đặt biến môi trường BLUEMOON_TEST_CONNECTION, nếu không các test sẽ bị skip.
/// </summary>
[Collection(SequentialDatabaseCollection.Name)]
[Trait("PBI", "PBI-016")]
public class PaymentApiTests(BlueMoonAppFactory factory) : IClassFixture<BlueMoonAppFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly string _prefix = "TT" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    private readonly List<int> _periodIds = [];

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (BlueMoonAppFactory.ConnectionString is null) return;

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Lần nộp giữ khóa tới kỳ thu và hộ (Restrict) nên xóa trước, rồi kỳ thu, khoản thu, căn hộ
        await db.Payments.Where(p => _periodIds.Contains(p.BillingPeriodId)).ExecuteDeleteAsync();
        await db.BillingPeriods.Where(p => _periodIds.Contains(p.Id)).ExecuteDeleteAsync();
        await db.FeeRates.Where(r => r.FeeItem.Code.StartsWith(_prefix)).ExecuteDeleteAsync();
        await db.FeeItems.Where(f => f.Code.StartsWith(_prefix)).ExecuteDeleteAsync();
        await db.Apartments.Where(a => a.Code.StartsWith(_prefix)).ExecuteDeleteAsync();
    }

    // ===== Test =====

    [SkippableFact]
    public async Task PartialThenFullPayment_ThroughApi_AddsUp_AndRejectsInvalidPayments()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();
        var (periodId, householdId) = await BillOneHouseholdAsync(admin, area: 72.5m); // 72,5 m² × 7.000 = 507.500 đ

        // Nộp một phần bằng tiền mặt
        var first = await ReadAsync<RecordPaymentResponse>(
            await PostPaymentAsync(admin, periodId, householdId, 300_000m, PaymentMethod.TienMat), HttpStatusCode.Created);
        Assert.Matches("^BN-\\d{6}$", first.Payment.ReceiptNumber);
        Assert.Equal((300_000m, 207_500m, PaymentStatus.NopMotPhan), (first.Bill.AmountPaid, first.Bill.Remaining, first.Bill.Status));

        var period = await ReadAsync<PeriodCollectionResponse>(
            await admin.GetAsync($"/api/thu-tien/ky-thu/{periodId}"), HttpStatusCode.OK);
        Assert.Equal(PaymentStatus.NopMotPhan, Assert.Single(period.Households, h => h.HouseholdId == householdId).Status);

        // Từ chối: nộp thừa, chuyển khoản không có mã giao dịch; số đã thu không đổi
        var overpaid = await ReadAsync<ApiError>(
            await PostPaymentAsync(admin, periodId, householdId, 207_501m, PaymentMethod.TienMat), HttpStatusCode.BadRequest);
        Assert.Equal("Số tiền nộp vượt số còn thiếu (207.500 đ). Không nhận nộp thừa.", overpaid.Message);
        var noReference = await ReadAsync<ApiError>(
            await PostPaymentAsync(admin, periodId, householdId, 1_000m, PaymentMethod.ChuyenKhoan), HttpStatusCode.BadRequest);
        Assert.Equal("Chuyển khoản phải có mã giao dịch hoặc nội dung chuyển khoản để đối chiếu.", noReference.Message);

        // Nộp nốt phần còn thiếu bằng chuyển khoản
        var second = await ReadAsync<RecordPaymentResponse>(
            await PostPaymentAsync(admin, periodId, householdId, 207_500m, PaymentMethod.ChuyenKhoan, "FT26316987654"),
            HttpStatusCode.Created);
        Assert.Equal((507_500m, 0m, PaymentStatus.DaNopDu), (second.Bill.AmountPaid, second.Bill.Remaining, second.Bill.Status));
        Assert.Equal(2, second.Bill.Payments.Count);
        Assert.Equal("admin", second.Payment.RecordedBy);

        var bill = await ReadAsync<HouseholdPaymentDetailResponse>(
            await admin.GetAsync($"/api/thu-tien/ky-thu/{periodId}/ho/{householdId}"), HttpStatusCode.OK);
        Assert.Equal(PaymentStatus.DaNopDu, bill.Status);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Payments.CountAsync(p => p.BillingPeriodId == periodId && p.HouseholdId == householdId));
        Assert.Equal(2, await db.AuditLogs.CountAsync(l => l.Action == AuditAction.RecordPayment &&
            (l.Target == first.Payment.Id.ToString() || l.Target == second.Payment.Id.ToString())));
    }

    [SkippableFact]
    public async Task Database_RejectsTransferWithoutReference_EvenOutsideTheApi()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();
        var (periodId, householdId) = await BillOneHouseholdAsync(admin, area: 40m);

        await using var connection = new NpgsqlConnection(BlueMoonAppFactory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO payments ("BillingPeriodId","HouseholdId","Amount","PaidAt","Method","RecordedById","RecordedAt")
            SELECT @period, @household, 1000, now(), 'ChuyenKhoan', "Id", now() FROM users WHERE "Username" = 'admin'
            """, connection);
        command.Parameters.AddWithValue("period", periodId);
        command.Parameters.AddWithValue("household", householdId);

        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal("ck_payments_transfer_reference", error.ConstraintName);
    }

    [SkippableFact]
    public async Task UnknownPeriodOrHousehold_Returns404()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/thu-tien/ky-thu/999999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostPaymentAsync(admin, 999999999, 1, 1_000m, PaymentMethod.TienMat)).StatusCode);
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

    private static Task<HttpResponseMessage> PostPaymentAsync(HttpClient admin, int periodId, int householdId,
        decimal amount, PaymentMethod method, string? reference = null) =>
        admin.PostAsJsonAsync("/api/thu-tien", new RecordPaymentRequest
        {
            BillingPeriodId = periodId,
            HouseholdId = householdId,
            Amount = amount,
            PaidAt = DateTimeOffset.Now.AddMinutes(-10),
            Method = method,
            Reference = reference,
            PayerName = "Người nộp test"
        }, Json);

    /// <summary>Tạo khoản thu theo diện tích 7.000 đ/m², một hộ test và kỳ thu tháng này; trả về (kỳ thu, hộ).</summary>
    private async Task<(int PeriodId, int HouseholdId)> BillOneHouseholdAsync(HttpClient admin, decimal area)
    {
        var fee = await ReadAsync<FeeItemResponse>(await admin.PostAsJsonAsync("/api/khoan-thu", new CreateFeeItemRequest
        {
            Code = _prefix + "_DV",
            Name = "Phí test thu tiền",
            FeeType = FeeType.BatBuoc,
            FeeScope = FeeScope.Chung,
            FeeCycle = FeeCycle.Thang,
            CalculationBasis = CalculationBasis.DienTich,
            Unit = "m²",
            Rate = 7_000m,
            EffectiveFrom = Today
        }, Json), HttpStatusCode.Created);

        var apartment = await ReadAsync<ApartmentProfileResponse>(await admin.PostAsJsonAsync("/api/can-ho", new CreateApartmentRequest
        {
            Code = _prefix + "-A",
            Building = "Tòa test thu tiền",
            Type = ApartmentType.CanHo,
            Floor = 6,
            Area = area,
            BedroomCount = 2,
            Status = "Đang ở"
        }, Json), HttpStatusCode.Created);

        var household = await ReadAsync<HouseholdProfileResponse>(await admin.PostAsJsonAsync(
            $"/api/ho-gia-dinh/can-ho/{apartment.Id}", new CreateHouseholdRequest
            {
                HouseholdNumber = _prefix + "-A",
                HeadName = "Nguyễn Văn Nộp",
                HouseNumber = _prefix + "-A",
                RegisteredAt = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
                Residents = [new ResidentProfileRequest
                {
                    FullName = "Nguyễn Văn Nộp",
                    DateOfBirth = new DateTimeOffset(1985, 3, 4, 0, 0, 0, TimeSpan.Zero),
                    Gender = "Nam"
                }]
            }, Json), HttpStatusCode.Created);

        var period = await ReadAsync<BillingPeriodResponse>(await admin.PostAsJsonAsync("/api/ky-thu", new CreateBillingPeriodRequest
        {
            Year = Today.Year,
            Month = Today.Month,
            ChargeDate = Today,
            DueDate = Today.AddDays(15),
            FeeItemIds = [fee.Id]
        }, Json), HttpStatusCode.Created);
        _periodIds.Add(period.Id);

        return (period.Id, household.Id);
    }
}
