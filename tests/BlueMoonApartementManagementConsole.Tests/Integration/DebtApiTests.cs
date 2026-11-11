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
/// Test tích hợp danh sách hộ chưa đóng và nhắc nộp (PBI-019) trên PostgreSQL thật: lập kỳ thu, ghi nhận thu tiền
/// qua API, rồi kiểm tra danh sách công nợ (còn thiếu, trạng thái, quá hạn) và ghi nhận nhắc nộp.
/// Dữ liệu giả có tiền tố riêng, xóa khi test xong (lần nhắc, lần nộp, kỳ thu, khoản thu, căn hộ).
/// Cần PostgreSQL: đặt biến môi trường BLUEMOON_TEST_CONNECTION, nếu không các test sẽ bị skip.
/// </summary>
[Collection(SequentialDatabaseCollection.Name)]
[Trait("PBI", "PBI-019")]
public class DebtApiTests(BlueMoonAppFactory factory) : IClassFixture<BlueMoonAppFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly string _prefix = "CN" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    private readonly List<int> _periodIds = [];

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (BlueMoonAppFactory.ConnectionString is null) return;

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Lần nhắc, lần nộp giữ khóa tới kỳ thu và hộ (Restrict) nên xóa trước
        await db.DebtReminders.Where(r => _periodIds.Contains(r.BillingPeriodId)).ExecuteDeleteAsync();
        await db.Payments.Where(p => _periodIds.Contains(p.BillingPeriodId)).ExecuteDeleteAsync();
        await db.BillingPeriods.Where(p => _periodIds.Contains(p.Id)).ExecuteDeleteAsync();
        await db.FeeRates.Where(r => r.FeeItem.Code.StartsWith(_prefix)).ExecuteDeleteAsync();
        await db.FeeItems.Where(f => f.Code.StartsWith(_prefix)).ExecuteDeleteAsync();
        await db.Apartments.Where(a => a.Code.StartsWith(_prefix)).ExecuteDeleteAsync();
    }

    // ===== Test =====

    [SkippableFact]
    public async Task DebtList_ShowsOnlyHouseholdsStillOwing_AndRemindersAreRecorded()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();
        await CreateFeeItemAsync(admin);
        var partial = await CreateHouseholdAsync(admin, "A", area: 50m); // 50 m² × 7.000 = 350.000 đ
        var paidUp = await CreateHouseholdAsync(admin, "B", area: 40m);  // 40 m² × 7.000 = 280.000 đ
        var periodId = await BillAsync(admin);

        // Hạn nộp đã qua 5 ngày (sửa thẳng trong DB, vì API chỉ cho lập kỳ có hạn nộp từ hôm nay)
        await MoveDueDateAsync(periodId, Today.AddDays(-5));

        await PayAsync(admin, periodId, partial, 100_000m);
        await PayAsync(admin, periodId, paidUp, 280_000m);

        var debts = await ReadAsync<DebtListResponse>(await admin.GetAsync($"/api/cong-no?kyThu={periodId}"), HttpStatusCode.OK);
        var row = Assert.Single(debts.Rows, r => r.HouseholdId == partial);
        Assert.Equal((250_000m, PaymentStatus.NopMotPhan, true, 5), (row.Remaining, row.Status, row.IsOverdue, row.DaysOverdue));
        Assert.DoesNotContain(debts.Rows, r => r.HouseholdId == paidUp);
        Assert.Equal(0, row.ReminderCount);

        // Nhắc hộ còn nợ: lưu số còn thiếu lúc nhắc
        var reminders = await ReadAsync<List<ReminderResponse>>(await PostRemindersAsync(admin, periodId, partial), HttpStatusCode.Created);
        Assert.Equal(250_000m, Assert.Single(reminders).Remaining);

        // Nhắc hộ đã nộp đủ: từ chối
        var paidUpError = await ReadAsync<ApiError>(await PostRemindersAsync(admin, periodId, paidUp), HttpStatusCode.BadRequest);
        Assert.EndsWith("không cần nhắc.", paidUpError.Message);

        var history = await ReadAsync<List<ReminderResponse>>(
            await admin.GetAsync($"/api/cong-no/nhac-nop?kyThu={periodId}&hoGiaDinh={partial}"), HttpStatusCode.OK);
        Assert.Equal(ReminderChannel.GiayBao, Assert.Single(history).Channel);

        var afterReminder = await ReadAsync<DebtListResponse>(await admin.GetAsync("/api/cong-no"), HttpStatusCode.OK);
        var remindedRow = Assert.Single(afterReminder.Rows, r => r.BillingPeriodId == periodId && r.HouseholdId == partial);
        Assert.Equal(1, remindedRow.ReminderCount);
        Assert.NotNull(remindedRow.LastRemindedAt);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(l =>
            l.Action == AuditAction.RecordDebtReminder && l.Target == reminders[0].Id.ToString()));
    }

    [SkippableFact]
    public async Task UnknownPeriod_Returns404_AndMissingQueryReturns400()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/cong-no?kyThu=999999999")).StatusCode);
        var missing = await ReadAsync<ApiError>(await admin.GetAsync("/api/cong-no/nhac-nop?kyThu=1"), HttpStatusCode.BadRequest);
        Assert.Equal("Hãy chọn kỳ thu (kyThu) và hộ gia đình (hoGiaDinh).", missing.Message);
    }

    // ===== Hàm hỗ trợ =====

    private int _feeItemId;

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

    /// <summary>Tạo khoản thu theo diện tích 7.000 đ/m²; kỳ thu lập sau khi đã có hộ (BillAsync).</summary>
    private async Task CreateFeeItemAsync(HttpClient admin)
    {
        var fee = await ReadAsync<FeeItemResponse>(await admin.PostAsJsonAsync("/api/khoan-thu", new CreateFeeItemRequest
        {
            Code = _prefix + "_DV",
            Name = "Phí test công nợ",
            FeeType = FeeType.BatBuoc,
            FeeScope = FeeScope.Chung,
            FeeCycle = FeeCycle.Thang,
            CalculationBasis = CalculationBasis.DienTich,
            Unit = "m²",
            Rate = 7_000m,
            EffectiveFrom = Today
        }, Json), HttpStatusCode.Created);
        _feeItemId = fee.Id;
    }

    private async Task<int> BillAsync(HttpClient admin)
    {
        var period = await ReadAsync<BillingPeriodResponse>(await admin.PostAsJsonAsync("/api/ky-thu", new CreateBillingPeriodRequest
        {
            Year = Today.Year,
            Month = Today.Month,
            ChargeDate = Today,
            DueDate = Today,
            FeeItemIds = [_feeItemId]
        }, Json), HttpStatusCode.Created);
        _periodIds.Add(period.Id);
        return period.Id;
    }

    private async Task<int> CreateHouseholdAsync(HttpClient admin, string suffix, decimal area)
    {
        var code = _prefix + "-" + suffix;
        var apartment = await ReadAsync<ApartmentProfileResponse>(await admin.PostAsJsonAsync("/api/can-ho", new CreateApartmentRequest
        {
            Code = code,
            Building = "Tòa test công nợ",
            Type = ApartmentType.CanHo,
            Floor = 7,
            Area = area,
            BedroomCount = 2,
            Status = "Đang ở"
        }, Json), HttpStatusCode.Created);

        var household = await ReadAsync<HouseholdProfileResponse>(await admin.PostAsJsonAsync(
            $"/api/ho-gia-dinh/can-ho/{apartment.Id}", new CreateHouseholdRequest
            {
                HouseholdNumber = code,
                HeadName = "Nguyễn Văn Nợ",
                HouseNumber = code,
                RegisteredAt = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
                Residents = [new ResidentProfileRequest
                {
                    FullName = "Nguyễn Văn Nợ",
                    DateOfBirth = new DateTimeOffset(1980, 2, 3, 0, 0, 0, TimeSpan.Zero),
                    Gender = "Nam"
                }]
            }, Json), HttpStatusCode.Created);
        return household.Id;
    }

    private async Task MoveDueDateAsync(int periodId, DateOnly dueDate)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Ràng buộc DB: hạn nộp không trước ngày tính phí, nên lùi cả ngày tính phí
        await db.BillingPeriods.Where(p => p.Id == periodId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ChargeDate, dueDate.AddDays(-5)).SetProperty(p => p.DueDate, dueDate));
    }

    private static async Task PayAsync(HttpClient admin, int periodId, int householdId, decimal amount) =>
        await ReadAsync<RecordPaymentResponse>(await admin.PostAsJsonAsync("/api/thu-tien", new RecordPaymentRequest
        {
            BillingPeriodId = periodId,
            HouseholdId = householdId,
            Amount = amount,
            PaidAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Method = PaymentMethod.TienMat
        }, Json), HttpStatusCode.Created);

    private static Task<HttpResponseMessage> PostRemindersAsync(HttpClient admin, int periodId, int householdId) =>
        admin.PostAsJsonAsync("/api/cong-no/nhac-nop", new RecordRemindersRequest
        {
            Items = [new DebtReminderTarget(periodId, householdId)],
            Channel = ReminderChannel.GiayBao,
            Note = "Gửi giấy báo test"
        }, Json);
}
