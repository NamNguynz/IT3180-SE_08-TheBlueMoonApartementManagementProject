using System.Globalization;
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
/// Test tích hợp ghi nhận biến động nhân khẩu (PBI-007) trên PostgreSQL thật: gọi API như trang
/// Biến động nhân khẩu, kiểm tra migration, khóa ngoại, check constraint và mã hóa cột Place.
/// Dữ liệu giả, căn hộ test có tiền tố riêng và bị xóa khi test xong (hộ, nhân khẩu, biến động xóa theo).
/// Cần PostgreSQL: đặt biến môi trường BLUEMOON_TEST_CONNECTION, nếu không các test sẽ bị skip.
/// </summary>
[Collection(SequentialDatabaseCollection.Name)]
[Trait("PBI", "PBI-007")]
public class ResidentChangeApiTests(BlueMoonAppFactory factory) : IClassFixture<BlueMoonAppFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly string _prefix = "BD" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() + "-";

    // App dùng TimeProvider.System cho nghiệp vụ, nên "hôm nay" là ngày thật của máy chạy test
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (BlueMoonAppFactory.ConnectionString is null) return;

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Apartments.Where(a => a.Code.StartsWith(_prefix)).ExecuteDeleteAsync();
    }

    // ===== Test =====

    [SkippableFact]
    public async Task TamVang_ThroughApi_ChangesStatus_KeepsHistory_EncryptsPlace_AndAudits()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();
        var (apartmentId, household) = await CreateHouseholdAsync(admin, "A");
        var residentId = Assert.Single(household.Residents).Id;
        const string place = "Nhà bà ngoại, xã Giả Định, Nam Định";

        var recorded = await ReadAsync<HouseholdResidencyResponse>(await PostChangeAsync(admin, household.Id,
            new RecordResidentChangeRequest
            {
                Type = ResidentChangeType.TamVang,
                ResidentId = residentId,
                FromDate = Today,
                ToDate = Today.AddDays(30),
                Place = place
            }), HttpStatusCode.Created);

        var resident = Assert.Single(recorded.Residents);
        Assert.Equal(ResidenceStatus.TamVang, resident.ResidenceStatus);
        Assert.Equal(Today.AddDays(30), resident.StatusUntil);

        // Đọc lại qua API: lịch sử có nơi đến (đã giải mã) và người ghi nhận
        var reloaded = await ReadAsync<HouseholdResidencyResponse>(
            await admin.GetAsync($"/api/ho-gia-dinh/{household.Id}/bien-dong"), HttpStatusCode.OK);
        var change = Assert.Single(reloaded.Changes);
        Assert.Equal(place, change.Place);
        Assert.Equal("admin", change.RecordedBy);

        // Trang Hồ sơ căn hộ cũng thấy tình trạng mới
        var apartment = await ReadAsync<ApartmentProfileResponse>(
            await admin.GetAsync($"/api/can-ho/{apartmentId}"), HttpStatusCode.OK);
        Assert.Equal(ResidenceStatus.TamVang, Assert.Single(apartment.Household!.Residents).ResidenceStatus);

        // Trong CSDL, cột Place là bản mã, không có chữ nào của bản rõ
        var raw = await RawPlaceAsync(change.Id);
        Assert.NotNull(raw);
        Assert.DoesNotContain("Nam Định", raw);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AuditLogs.AnyAsync(l =>
            l.Action == AuditAction.RecordResidentChange && l.Target == residentId.ToString()));
    }

    [SkippableFact]
    public async Task ChuyenDen_ThroughApi_SavesNewResidentAndHistoryTogether()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();
        var (_, household) = await CreateHouseholdAsync(admin, "B");

        var recorded = await ReadAsync<HouseholdResidencyResponse>(await PostChangeAsync(admin, household.Id,
            new RecordResidentChangeRequest
            {
                Type = ResidentChangeType.ChuyenDen,
                NewResident = Resident("Trần Thị Mới"),
                FromDate = Today,
                Place = "Số 2 phố Giả Định, Hải Phòng"
            }), HttpStatusCode.Created);

        Assert.Equal(2, recorded.Residents.Count);
        var added = Assert.Single(recorded.Residents, r => r.FullName == "Trần Thị Mới");
        Assert.Equal(ResidenceStatus.ThuongTru, added.ResidenceStatus);
        // EF gán ResidentId cho biến động từ nhân khẩu vừa thêm, trong cùng một lần lưu
        Assert.Equal(added.Id, Assert.Single(recorded.Changes).ResidentId);
    }

    [SkippableFact]
    public async Task InvalidChange_Returns400WithMessage_AndSavesNothing()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();
        var (_, household) = await CreateHouseholdAsync(admin, "C");
        var residentId = Assert.Single(household.Residents).Id;

        var error = await ReadAsync<ApiError>(await PostChangeAsync(admin, household.Id,
            new RecordResidentChangeRequest
            {
                Type = ResidentChangeType.TroVe,
                ResidentId = residentId,
                FromDate = Today
            }), HttpStatusCode.BadRequest);
        Assert.Equal("Chỉ nhân khẩu đang tạm vắng mới ghi nhận trở về.", error.Message);

        var residency = await ReadAsync<HouseholdResidencyResponse>(
            await admin.GetAsync($"/api/ho-gia-dinh/{household.Id}/bien-dong"), HttpStatusCode.OK);
        Assert.Empty(residency.Changes);
        Assert.Equal(ResidenceStatus.ThuongTru, Assert.Single(residency.Residents).ResidenceStatus);
    }

    [SkippableFact]
    public async Task UnknownHousehold_Returns404()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();

        var response = await admin.GetAsync("/api/ho-gia-dinh/999999999/bien-dong");

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

    private static Task<HttpResponseMessage> PostChangeAsync(HttpClient admin, int householdId, RecordResidentChangeRequest request) =>
        admin.PostAsJsonAsync($"/api/ho-gia-dinh/{householdId}/bien-dong", request, Json);

    /// <summary>Số CCCD giả 12 chữ số, mỗi lần một số khác để không đụng dữ liệu của lần chạy trước.</summary>
    private static string NewCccd() =>
        "0" + Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString(CultureInfo.InvariantCulture);

    private static ResidentProfileRequest Resident(string name) => new()
    {
        FullName = name,
        DateOfBirth = new DateTimeOffset(1990, 5, 17, 0, 0, 0, TimeSpan.Zero),
        Gender = "Nữ",
        IdentityNumber = NewCccd()
    };

    private async Task<(int ApartmentId, HouseholdProfileResponse Household)> CreateHouseholdAsync(HttpClient admin, string suffix)
    {
        var apartment = await ReadAsync<ApartmentProfileResponse>(await admin.PostAsJsonAsync("/api/can-ho", new CreateApartmentRequest
        {
            Code = _prefix + suffix,
            Building = "Tòa test biến động",
            Type = ApartmentType.CanHo,
            Floor = 3,
            Area = 65m,
            BedroomCount = 2,
            Status = "Đang ở"
        }, Json), HttpStatusCode.Created);

        var household = await ReadAsync<HouseholdProfileResponse>(await admin.PostAsJsonAsync(
            $"/api/ho-gia-dinh/can-ho/{apartment.Id}", new CreateHouseholdRequest
            {
                HouseholdNumber = _prefix + suffix,
                HeadName = "Nguyễn Văn Thử",
                HouseNumber = "P" + suffix,
                RegisteredAt = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
                Residents = [Resident("Nguyễn Văn Thử")]
            }, Json), HttpStatusCode.Created);

        return (apartment.Id, household);
    }

    /// <summary>Đọc thẳng cột Place bằng Npgsql, không qua EF nên không bị giải mã.</summary>
    private static async Task<string?> RawPlaceAsync(int changeId)
    {
        await using var connection = new NpgsqlConnection(BlueMoonAppFactory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""SELECT "Place" FROM resident_changes WHERE "Id" = @id""", connection);
        command.Parameters.AddWithValue("id", changeId);
        return await command.ExecuteScalarAsync() as string;
    }
}
