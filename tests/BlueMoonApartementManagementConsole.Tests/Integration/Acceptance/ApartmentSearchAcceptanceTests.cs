using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BlueMoonApartementManagementConsole.Tests.Integration.Acceptance;

/// <summary>
/// Tự động hóa docs/testing/acceptance_apartment_search.md (PBI-002): phần API của K2, K5, K7.
/// Việc lọc danh sách (mã, loại hình, diện tích) chạy bằng JavaScript trên trình duyệt (K1 – K4), nên kiểm bằng tay.
/// Căn hộ test có mã bắt đầu bằng tiền tố riêng và bị xóa khi test xong. Dữ liệu giả.
/// </summary>
[Collection(SequentialDatabaseCollection.Name)]
public class ApartmentSearchAcceptanceTests(BlueMoonAppFactory factory)
    : AcceptanceTestBase(factory), IClassFixture<BlueMoonAppFactory>
{
    private readonly string _prefix = "AK" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() + "-";

    private CreateApartmentRequest Request(string suffix, ApartmentType type) => new()
    {
        Code = _prefix + suffix, Building = "Tòa AK", Type = type, Floor = 5, Area = 70m, BedroomCount = 2, Status = "Đang sử dụng"
    };

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} trả {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, UiFlow.Json)!;
    }

    private async Task CleanupApartmentsAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Apartments.Where(a => a.Code.StartsWith(_prefix)).ExecuteDeleteAsync();
    }

    private async Task<List<ApartmentProfileResponse>> MineAsync(HttpClient admin) =>
        (await UiFlow.GetJsonAsync<List<ApartmentProfileResponse>>(admin, "/api/can-ho"))
            .Where(a => a.Code.StartsWith(_prefix)).ToList();

    [SkippableFact]
    [Trait("Acceptance", "K2")]
    public async Task K2_ApartmentType_IsSaved_Returned_Updated_AndOfferedOnPage()
    {
        SkipIfNoDatabase();
        var admin = await AdminAsync();
        try
        {
            var created = new Dictionary<ApartmentType, ApartmentProfileResponse>();
            foreach (var (type, suffix) in new[] { (ApartmentType.CanHo, "01"), (ApartmentType.Kiot, "02"), (ApartmentType.Penthouse, "03") })
            {
                var apartment = await ReadAsync<ApartmentProfileResponse>(
                    await admin.PostAsJsonAsync("/api/can-ho", Request(suffix, type), UiFlow.Json), HttpStatusCode.Created);
                Assert.Equal(type, apartment.Type);
                created[type] = apartment;
            }

            // Danh sách và chi tiết trả đúng loại hình (trang lọc theo trường này)
            var listed = (await MineAsync(admin)).ToDictionary(a => a.Code, a => a.Type);
            Assert.Equal(3, listed.Count);
            foreach (var (type, apartment) in created)
            {
                Assert.Equal(type, listed[apartment.Code]);
                Assert.Equal(type, (await UiFlow.GetJsonAsync<ApartmentProfileResponse>(admin, $"/api/can-ho/{apartment.Id}")).Type);
            }

            // Sửa loại hình
            var kiot = created[ApartmentType.Kiot];
            var updateRequest = new UpdateApartmentRequest
            {
                Code = kiot.Code, Building = kiot.Building, Type = ApartmentType.Penthouse, Floor = kiot.Floor,
                Area = kiot.Area, BedroomCount = kiot.BedroomCount, Status = kiot.Status
            };
            var updated = await ReadAsync<ApartmentProfileResponse>(
                await admin.PutAsJsonAsync($"/api/can-ho/{kiot.Id}", updateRequest, UiFlow.Json), HttpStatusCode.OK);
            Assert.Equal(ApartmentType.Penthouse, updated.Type);

            // Trang Hồ sơ căn hộ có đủ lựa chọn loại hình ở ô lọc và ở form căn hộ (2 lần mỗi loại)
            var page = await UiFlow.GetAsync(admin, "/can-ho/ho-so");
            // Blazor viết thuộc tính rỗng thành "<option value>" (giá trị là chuỗi rỗng)
            Assert.Contains("<option value>Tất cả loại hình</option>", page.Html);
            Assert.Contains("<option value>Chọn loại hình</option>", page.Html);
            foreach (var (type, label) in ApartmentTypeLabels.All)
            {
                var option = $"<option value=\"{type}\">{label}</option>";
                Assert.Equal(2, page.Html.Split(option).Length - 1);
            }
        }
        finally
        {
            await CleanupApartmentsAsync();
        }
    }

    [SkippableTheory]
    [Trait("Acceptance", "K5")]
    [InlineData("missing")]
    [InlineData("Villa")]
    [InlineData("number")]
    public async Task K5_MissingOrInvalidType_IsRejected_AndNothingIsSaved(string kind)
    {
        SkipIfNoDatabase();
        var admin = await AdminAsync();
        var code = _prefix + "X1";
        try
        {
            object body = kind switch
            {
                "missing" => new { code, building = "Tòa AK", floor = 1, area = 50m, bedroomCount = 1, status = "Trống" },
                "Villa" => new { code, building = "Tòa AK", type = "Villa", floor = 1, area = 50m, bedroomCount = 1, status = "Trống" },
                _ => new { code, building = "Tòa AK", type = 5, floor = 1, area = 50m, bedroomCount = 1, status = "Trống" }
            };

            var response = await admin.PostAsJsonAsync("/api/can-ho", body, UiFlow.Json);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await MineAsync(admin));
        }
        finally
        {
            await CleanupApartmentsAsync();
        }
    }

    [SkippableFact]
    [Trait("Acceptance", "K7")]
    public async Task K7_ApartmentWithoutType_DefaultsToCanHo()
    {
        // Như căn hộ tạo trước khi có cột Type: migration AddApartmentType gán mặc định 'CanHo'
        SkipIfNoDatabase();
        var admin = await AdminAsync();
        try
        {
            int id;
            await using (var connection = new NpgsqlConnection(BlueMoonAppFactory.ConnectionString))
            {
                await connection.OpenAsync();
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO apartments ("Code", "Building", "Floor", "Area", "BedroomCount", "Status")
                    VALUES (@code, 'Tòa AK', 1, 45, 1, 'Đang sử dụng') RETURNING "Id"
                    """, connection);
                insert.Parameters.AddWithValue("code", _prefix + "OLD");
                id = (int)(await insert.ExecuteScalarAsync())!;
            }

            Assert.Equal(ApartmentType.CanHo, (await UiFlow.GetJsonAsync<ApartmentProfileResponse>(admin, $"/api/can-ho/{id}")).Type);
        }
        finally
        {
            await CleanupApartmentsAsync();
        }
    }
}
