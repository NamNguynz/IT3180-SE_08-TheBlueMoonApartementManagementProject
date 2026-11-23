using System.Globalization;
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
/// Test tích hợp cho 2 API riêng của trang danh sách (không đổi GET /api/can-ho cũ):
/// GET /api/can-ho/danh-sach (lọc căn hộ, đếm nhân khẩu chưa chuyển đi, chuẩn hóa 5/10/15/20)
/// và GET /api/nhan-khau (lọc nhân khẩu, khoảng ngày sinh, cột mã hóa, CCCD theo hash).
/// Dữ liệu giả có tiền tố riêng và bị xóa khi test xong. Cần PostgreSQL: đặt BLUEMOON_TEST_CONNECTION.
/// </summary>
[Collection(SequentialDatabaseCollection.Name)]
public class ListPagesApiTests(BlueMoonAppFactory factory) : IClassFixture<BlueMoonAppFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly string _prefix = "LIST" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() + "-";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (BlueMoonAppFactory.ConnectionString is null) return;

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Apartments.Where(a => a.Code.StartsWith(_prefix)).ExecuteDeleteAsync();
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

    private static string NewCccd() =>
        "0" + Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString(CultureInfo.InvariantCulture);

    private async Task<int> CreateApartmentAsync(HttpClient admin, string suffix, int floor, decimal area, ApartmentStatus status)
    {
        var buildingId = await TestBuilding.IdAsync(admin, "Tòa test Danh sách");
        var response = await admin.PostAsJsonAsync("/api/can-ho", new CreateApartmentRequest
        {
            Code = _prefix + suffix,
            BuildingId = buildingId,
            Type = ApartmentType.CanHo,
            Floor = floor,
            Area = area,
            BedroomCount = 2,
            Status = status
        }, Json);
        return (await ReadAsync<ApartmentProfileResponse>(response, HttpStatusCode.Created)).Id;
    }

    private static ResidentProfileRequest Resident(
        string fullName, string cccd, DateOnly dob, string gender, string ethnicity, string religion,
        string placeOfBirth, string hometown, string workplace) => new()
    {
        FullName = fullName,
        DateOfBirth = new DateTimeOffset(dob.Year, dob.Month, dob.Day, 0, 0, 0, TimeSpan.Zero),
        Gender = gender,
        PlaceOfBirth = placeOfBirth,
        Hometown = hometown,
        Ethnicity = ethnicity,
        Religion = religion,
        Workplace = workplace,
        IdentityNumber = cccd
    };

    private CreateHouseholdRequest NewHousehold(string suffix, params ResidentProfileRequest[] residents) => new()
    {
        HouseholdNumber = _prefix + suffix,
        HeadName = residents[0].FullName,
        HouseNumber = "P" + suffix,
        RegisteredAt = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
        Residents = [.. residents]
    };

    private async Task<HouseholdProfileResponse> CreateHouseholdAsync(
        HttpClient admin, int apartmentId, string suffix, params ResidentProfileRequest[] residents)
    {
        var response = await admin.PostAsJsonAsync(
            $"/api/ho-gia-dinh/can-ho/{apartmentId}", NewHousehold(suffix, residents), Json);
        return await ReadAsync<HouseholdProfileResponse>(response, HttpStatusCode.Created);
    }

    // ===== Test =====

    [SkippableFact]
    [Trait("TestId", "L1")]
    public async Task ApartmentList_FiltersByCodeAreaFloorStatus_AndCountsOnlyActiveResidents()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();

        var a = await CreateApartmentAsync(admin, "A", 3, 50m, ApartmentStatus.DangChoThue);
        await CreateApartmentAsync(admin, "B", 5, 80m, ApartmentStatus.DaChoThue);
        await CreateApartmentAsync(admin, "C", 5, 120m, ApartmentStatus.DangSuaChua);

        var cccd1 = NewCccd();
        var cccd2 = NewCccd();
        var cccd3 = NewCccd();
        var dob = new DateOnly(1990, 5, 17);
        var household = await CreateHouseholdAsync(admin, a, "A",
            Resident("Nguyễn Văn Một", cccd1, dob, "Nam", "Kinh", "Không", "Hà Nội", "Hà Nội", "Công ty A"),
            Resident("Nguyễn Văn Hai", cccd2, dob, "Nam", "Kinh", "Không", "Hà Nội", "Hà Nội", "Công ty A"),
            Resident("Nguyễn Văn Ba", cccd3, dob, "Nam", "Kinh", "Không", "Hà Nội", "Hà Nội", "Công ty A"));
        var movedOut = household.Residents.Single(r => r.IdentityNumber == cccd3);

        // Chuyển đi qua luồng nghiệp vụ thật; nhân khẩu đó không được tính vào căn hộ nữa.
        var change = await admin.PostAsJsonAsync($"/api/ho-gia-dinh/{household.Id}/bien-dong",
            new RecordResidentChangeRequest
            {
                Type = ResidentChangeType.ChuyenDi,
                ResidentId = movedOut.Id,
                FromDate = DateOnly.FromDateTime(DateTime.Now),
                Place = "TP. Hồ Chí Minh"
            }, Json);
        Assert.Equal(HttpStatusCode.Created, change.StatusCode);

        var all = await ReadAsync<PagedResult<ApartmentListRow>>(
            await admin.GetAsync($"/api/can-ho/danh-sach?code={_prefix}&page=1&pageSize=10"), HttpStatusCode.OK);
        Assert.Equal(3, all.Total);
        var rowA = Assert.Single(all.Items, r => r.Code == _prefix + "A");
        Assert.Equal("Tòa test Danh sách", rowA.BuildingName);
        Assert.Equal(2, rowA.ResidentCount);

        var byArea = await ReadAsync<PagedResult<ApartmentListRow>>(
            await admin.GetAsync($"/api/can-ho/danh-sach?code={_prefix}&areaFrom=60&areaTo=100"), HttpStatusCode.OK);
        Assert.Equal(_prefix + "B", Assert.Single(byArea.Items).Code);

        var byFloor = await ReadAsync<PagedResult<ApartmentListRow>>(
            await admin.GetAsync($"/api/can-ho/danh-sach?code={_prefix}&floor=5"), HttpStatusCode.OK);
        Assert.Equal(2, byFloor.Total);

        var byStatus = await ReadAsync<PagedResult<ApartmentListRow>>(
            await admin.GetAsync($"/api/can-ho/danh-sach?code={_prefix}&status=DangSuaChua"), HttpStatusCode.OK);
        Assert.Equal(_prefix + "C", Assert.Single(byStatus.Items).Code);

        // Kích thước trang chỉ nhận 5/10/15/20, các giá trị khác bị chuẩn hóa xuống mốc gần nhất.
        foreach (var (requested, expected) in new[] { (1, 5), (8, 10), (13, 15), (999, 20) })
        {
            var sized = await ReadAsync<PagedResult<ApartmentListRow>>(
                await admin.GetAsync($"/api/can-ho/danh-sach?code={_prefix}&page=0&pageSize={requested}"), HttpStatusCode.OK);
            Assert.Equal(expected, sized.PageSize);
            Assert.Equal(1, sized.Page);
        }
    }

    [SkippableFact]
    [Trait("TestId", "L2")]
    public async Task ResidentList_FiltersByDobEncryptedFieldsAndExactCccd_AndNormalizesPageSize()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();

        var cccdA = NewCccd();
        var cccdB = NewCccd();
        var dobA = new DateOnly(1990, 5, 17);
        var dobB = new DateOnly(2000, 8, 20);
        var apartment = await CreateApartmentAsync(admin, "R", 2, 60m, ApartmentStatus.DaChoThue);
        var household = await CreateHouseholdAsync(admin, apartment, "R",
            Resident("Nguyễn Văn Alpha", cccdA, dobA, "Nam", "Kinh", "Không",
                "Hải Phòng", "Hà Nội", "Công ty A"),
            Resident("Trần Thị Beta", cccdB, dobB, "Nữ", "Tày", "Phật giáo",
                "Huế", "Đà Nẵng", "Bệnh viện B"));

        var alpha = Assert.Single(household.Residents, r => r.IdentityNumber == cccdA);
        var byName = await ReadAsync<PagedResult<ResidentListRow>>(
            await admin.GetAsync($"/api/nhan-khau?fullName=alpha&pageSize=10"), HttpStatusCode.OK);
        Assert.Equal(1, byName.Total);
        var row = Assert.Single(byName.Items);
        Assert.Equal(alpha.Id, row.Id);
        Assert.Equal("Nam", row.Gender);
        Assert.Equal("Hải Phòng", row.PlaceOfBirth);
        Assert.Equal("Hà Nội", row.Hometown);
        Assert.Equal("Kinh", row.Ethnicity);
        Assert.Equal("Không", row.Religion);
        Assert.Equal("Công ty A", row.Workplace);
        Assert.Equal(cccdA, row.IdentityNumber);
        Assert.Equal(dobA, DateOnly.FromDateTime(row.DateOfBirth.UtcDateTime));

        // Khoảng ngày sinh nhận cả hai đầu.
        var exactDob = await ReadAsync<PagedResult<ResidentListRow>>(
            await admin.GetAsync($"/api/nhan-khau?dateOfBirthFrom={dobA:yyyy-MM-dd}&dateOfBirthTo={dobA:yyyy-MM-dd}"), HttpStatusCode.OK);
        Assert.Equal(1, exactDob.Total);
        Assert.Equal(alpha.Id, Assert.Single(exactDob.Items).Id);
        var beforeDob = await ReadAsync<PagedResult<ResidentListRow>>(
            await admin.GetAsync("/api/nhan-khau?dateOfBirthTo=1990-05-16"), HttpStatusCode.OK);
        Assert.Equal(0, beforeDob.Total);

        // Dân tộc/Tôn giáo lưu mã hóa nên lọc sau khi giải mã; vẫn hỗ trợ tìm một phần.
        var byEthnicity = await ReadAsync<PagedResult<ResidentListRow>>(
            await admin.GetAsync($"/api/nhan-khau?fullName=beta&ethnicity=Tày"), HttpStatusCode.OK);
        Assert.Equal("Phật giáo", Assert.Single(byEthnicity.Items).Religion);
        var byReligion = await ReadAsync<PagedResult<ResidentListRow>>(
            await admin.GetAsync($"/api/nhan-khau?religion=phật"), HttpStatusCode.OK);
        Assert.Equal(1, byReligion.Total);
        var byWorkplace = await ReadAsync<PagedResult<ResidentListRow>>(
            await admin.GetAsync($"/api/nhan-khau?placeOfBirth=huế&hometown=đà nẵng&workplace=bệnh viện&gender=Nữ"), HttpStatusCode.OK);
        Assert.Equal(cccdB, Assert.Single(byWorkplace.Items).IdentityNumber);

        // CCCD chỉ khớp đúng số (bỏ khoảng trắng hai đầu), không khớp một phần.
        var byCccd = await ReadAsync<PagedResult<ResidentListRow>>(
            await admin.GetAsync($"/api/nhan-khau?identityNumber=%20{cccdA}%20"), HttpStatusCode.OK);
        Assert.Equal(alpha.Id, Assert.Single(byCccd.Items).Id);
        var partial = await ReadAsync<PagedResult<ResidentListRow>>(
            await admin.GetAsync($"/api/nhan-khau?identityNumber={cccdA[..4]}"), HttpStatusCode.OK);
        Assert.Equal(0, partial.Total);

        foreach (var (requested, expected) in new[] { (1, 5), (8, 10), (13, 15), (999, 20) })
        {
            var sized = await ReadAsync<PagedResult<ResidentListRow>>(
                await admin.GetAsync($"/api/nhan-khau?page=0&pageSize={requested}"), HttpStatusCode.OK);
            Assert.Equal(expected, sized.PageSize);
            Assert.Equal(1, sized.Page);
        }
    }
}
