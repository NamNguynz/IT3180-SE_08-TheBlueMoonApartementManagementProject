using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace BlueMoonApartementManagementConsole.Tests.Integration.Acceptance;

/// <summary>
/// Tự động hóa docs/testing/acceptance_apartment_profile.md (PBI-001, AC-001, AC-002): phần API của Y1 – Y8 và Y10.
/// Y9 (thông báo lỗi trên giao diện) phải chạy tay. Trùng CCCD (Y6) đã có test E9 trong PersonalDataEncryptionTests.
/// Căn hộ test có mã bắt đầu bằng tiền tố riêng; xóa căn hộ thì hộ và nhân khẩu bị xóa theo (cascade). Dữ liệu giả.
/// </summary>
[Collection(SequentialDatabaseCollection.Name)]
public class ApartmentProfileAcceptanceTests(BlueMoonAppFactory factory, ITestOutputHelper output)
    : AcceptanceTestBase(factory), IClassFixture<BlueMoonAppFactory>
{
    private const string BuildingName = "Tòa test Y";
    private static readonly DateTimeOffset RegisteredAt = new(2024, 1, 2, 0, 0, 0, TimeSpan.Zero);

    private readonly string _prefix = "AY" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() + "-";

    // ===== Dữ liệu giả =====

    private CreateApartmentRequest Apartment(string suffix, int buildingId) => new()
    {
        Code = _prefix + suffix,
        BuildingId = buildingId,
        Type = ApartmentType.CanHo,
        Floor = 12,
        Area = 72.5m,
        BedroomCount = 2,
        Status = ApartmentStatus.DangChoThue,
        Notes = "Căn test Y"
    };

    private static ResidentProfileRequest Resident(string fullName, int? id = null, string? occupation = null) => new()
    {
        Id = id,
        FullName = fullName,
        DateOfBirth = new DateTimeOffset(1985, 3, 14, 0, 0, 0, TimeSpan.Zero),
        Gender = "Nam",
        Occupation = occupation
    };

    private CreateHouseholdRequest Household(string suffix, params ResidentProfileRequest[] residents) => new()
    {
        HouseholdNumber = _prefix + suffix,
        HeadName = "Trần Văn Chủ Hộ",
        HouseNumber = "P" + suffix,
        RegisteredAt = RegisteredAt,
        Residents = [.. residents]
    };

    // ===== Hàm hỗ trợ =====

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} trả {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, UiFlow.Json)!;
    }

    private static Task<HttpResponseMessage> PostApartmentAsync(HttpClient client, CreateApartmentRequest request) =>
        client.PostAsJsonAsync("/api/can-ho", request, UiFlow.Json);

    private static Task<HttpResponseMessage> PostHouseholdAsync(HttpClient client, int apartmentId, CreateHouseholdRequest request) =>
        client.PostAsJsonAsync($"/api/ho-gia-dinh/can-ho/{apartmentId}", request, UiFlow.Json);

    private async Task<ApartmentProfileResponse> CreateApartmentAsync(HttpClient admin, string suffix)
    {
        var buildingId = await TestBuilding.IdAsync(admin, BuildingName);
        return await ReadAsync<ApartmentProfileResponse>(
            await PostApartmentAsync(admin, Apartment(suffix, buildingId)), HttpStatusCode.Created);
    }

    private async Task<HouseholdProfileResponse> CreateHouseholdAsync(
        HttpClient admin, int apartmentId, string suffix, params ResidentProfileRequest[] residents) =>
        await ReadAsync<HouseholdProfileResponse>(
            await PostHouseholdAsync(admin, apartmentId, Household(suffix, residents)), HttpStatusCode.Created);

    private static UpdateApartmentRequest UpdateOf(ApartmentProfileResponse a) => new()
    {
        Code = a.Code, BuildingId = a.BuildingId, Type = a.Type, Floor = a.Floor, Area = a.Area,
        BedroomCount = a.BedroomCount, Status = a.Status, Notes = a.Notes
    };

    /// <summary>
    /// Ghép toàn bộ nội dung lỗi để kiểm "chỉ rõ trường cần sửa" (AC-002): message của ApiError,
    /// hoặc tên trường và thông báo trong "errors" của lỗi kiểm tra dữ liệu mặc định của ASP.NET.
    /// Đọc qua JsonDocument để chữ tiếng Việt đã được giải mã (body có thể dùng \uXXXX).
    /// </summary>
    private async Task<string> ErrorTextAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"{(int)response.StatusCode} {body}");
        var text = new StringBuilder();
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            foreach (var name in new[] { "code", "message", "title", "detail" })
                if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    text.Append(value.GetString()).Append(' ');
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                foreach (var field in errors.EnumerateObject())
                {
                    text.Append(field.Name).Append(": ");
                    foreach (var message in field.Value.EnumerateArray())
                        text.Append(message.GetString()).Append(' ');
                }
        }
        catch (JsonException)
        {
            text.Append(body);
        }
        return text.ToString();
    }

    private async Task<(int Apartments, int Households)> CountMineAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Apartments.CountAsync(a => a.Code.StartsWith(_prefix)),
                await db.Households.CountAsync(h => h.HouseholdNumber.StartsWith(_prefix)));
    }

    private async Task CleanupAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Apartments.Where(a => a.Code.StartsWith(_prefix)).ExecuteDeleteAsync();
    }

    // ===== AC-001: lưu hồ sơ hợp lệ, gắn đúng quan hệ, tra cứu lại được =====

    [SkippableFact]
    [Trait("Acceptance", "Y1")]
    public async Task Y1_ValidApartment_IsSaved_AndCanBeLookedUp()
    {
        SkipIfNoDatabase();
        var admin = await AdminAsync();
        try
        {
            var buildingId = await TestBuilding.IdAsync(admin, BuildingName);
            // Nhập mã chữ thường, có khoảng trắng: hệ thống lưu mã chữ hoa, bỏ khoảng trắng
            var sent = Apartment("01", buildingId) with { Code = "  " + (_prefix + "01").ToLowerInvariant() + " " };
            var created = await ReadAsync<ApartmentProfileResponse>(await PostApartmentAsync(admin, sent), HttpStatusCode.Created);
            Assert.Equal(_prefix + "01", created.Code);

            var read = await UiFlow.GetJsonAsync<ApartmentProfileResponse>(admin, $"/api/can-ho/{created.Id}");
            Assert.Equal(_prefix + "01", read.Code);
            Assert.Equal(buildingId, read.BuildingId);
            Assert.Equal(BuildingName, read.BuildingName);
            Assert.Equal(ApartmentType.CanHo, read.Type);
            Assert.Equal(12, read.Floor);
            Assert.Equal(72.5m, read.Area);
            Assert.Equal(2, read.BedroomCount);
            Assert.Equal(ApartmentStatus.DangChoThue, read.Status);
            Assert.Equal("Căn test Y", read.Notes);
            Assert.Null(read.Household);

            var all = await UiFlow.GetJsonAsync<List<ApartmentProfileResponse>>(admin, "/api/can-ho");
            Assert.Contains(all, a => a.Id == created.Id && a.Code == _prefix + "01");
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [SkippableFact]
    [Trait("Acceptance", "Y2")]
    public async Task Y2_ValidHousehold_IsLinkedToApartment_HeadAndResidents_AndCanBeLookedUp()
    {
        SkipIfNoDatabase();
        var admin = await AdminAsync();
        try
        {
            var apartment = await CreateApartmentAsync(admin, "02");
            var created = await CreateHouseholdAsync(admin, apartment.Id, "02",
                Resident("Trần Văn Chủ Hộ"), Resident("Trần Thị Thành Viên") with { Gender = "Nữ" });

            var household = await UiFlow.GetJsonAsync<HouseholdProfileResponse>(admin, $"/api/ho-gia-dinh/{created.Id}");
            Assert.Equal(apartment.Id, household.ApartmentId);
            Assert.Equal(_prefix + "02", household.HouseholdNumber);
            Assert.Equal("Trần Văn Chủ Hộ", household.HeadName);
            Assert.Equal("P02", household.HouseNumber);
            Assert.Equal(RegisteredAt.Date, household.RegisteredAt.Date);
            Assert.Equal(["Trần Thị Thành Viên", "Trần Văn Chủ Hộ"], household.Residents.Select(r => r.FullName).Order());

            // Tra từ phía căn hộ cũng thấy đúng hộ và đủ nhân khẩu
            var fromApartment = await UiFlow.GetJsonAsync<ApartmentProfileResponse>(admin, $"/api/can-ho/{apartment.Id}");
            Assert.NotNull(fromApartment.Household);
            Assert.Equal(created.Id, fromApartment.Household!.Id);
            Assert.Equal(2, fromApartment.Household.Residents.Count);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [SkippableFact]
    [Trait("Acceptance", "Y3")]
    public async Task Y3_UpdatedApartmentAndHousehold_AreSaved_AndUnsubmittedResidentsAreKept()
    {
        SkipIfNoDatabase();
        var admin = await AdminAsync();
        try
        {
            var apartment = await CreateApartmentAsync(admin, "03");
            var household = await CreateHouseholdAsync(admin, apartment.Id, "03",
                Resident("Lê Văn Một"), Resident("Lê Thị Hai") with { Gender = "Nữ" });

            var apartmentUpdate = UpdateOf(apartment) with { Floor = 15, Area = 80.25m, BedroomCount = 3, Notes = "Đã sửa" };
            await ReadAsync<ApartmentProfileResponse>(
                await admin.PutAsJsonAsync($"/api/can-ho/{apartment.Id}", apartmentUpdate, UiFlow.Json), HttpStatusCode.OK);

            var first = household.Residents.Single(r => r.FullName == "Lê Văn Một");
            var householdUpdate = new UpdateHouseholdRequest
            {
                HouseholdNumber = household.HouseholdNumber,
                HeadName = "Lê Văn Một",
                HouseNumber = household.HouseNumber,
                RegisteredAt = household.RegisteredAt,
                // Sửa nhân khẩu 1, thêm nhân khẩu mới, không gửi nhân khẩu 2
                Residents = [Resident("Lê Văn Một", first.Id, "Kỹ sư"), Resident("Lê Văn Ba")]
            };
            await ReadAsync<HouseholdProfileResponse>(
                await admin.PutAsJsonAsync($"/api/ho-gia-dinh/{household.Id}", householdUpdate, UiFlow.Json), HttpStatusCode.OK);

            var read = await UiFlow.GetJsonAsync<ApartmentProfileResponse>(admin, $"/api/can-ho/{apartment.Id}");
            Assert.Equal(15, read.Floor);
            Assert.Equal(80.25m, read.Area);
            Assert.Equal(3, read.BedroomCount);
            Assert.Equal("Đã sửa", read.Notes);
            Assert.Equal("Lê Văn Một", read.Household!.HeadName);
            Assert.Equal(["Lê Thị Hai", "Lê Văn Ba", "Lê Văn Một"], read.Household.Residents.Select(r => r.FullName).Order());
            Assert.Equal("Kỹ sư", read.Household.Residents.Single(r => r.Id == first.Id).Occupation);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    // ===== AC-002: trùng dữ liệu =====

    [SkippableFact]
    [Trait("Acceptance", "Y4")]
    public async Task Y4_DuplicateApartmentCode_IsRejected_WhenCreatingOrUpdating()
    {
        SkipIfNoDatabase();
        var admin = await AdminAsync();
        try
        {
            var existing = await CreateApartmentAsync(admin, "04");

            // Khác chữ hoa/thường và có khoảng trắng vẫn là trùng
            var duplicate = await PostApartmentAsync(admin,
                Apartment("04", existing.BuildingId) with { Code = " " + existing.Code.ToLowerInvariant() + " " });
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal(("CONFLICT", "Mã căn hộ đã tồn tại."), await UiFlow.ReadErrorAsync(duplicate));

            var other = await CreateApartmentAsync(admin, "04B");
            var renamed = await admin.PutAsJsonAsync($"/api/can-ho/{other.Id}",
                UpdateOf(other) with { Code = existing.Code }, UiFlow.Json);
            Assert.Equal(HttpStatusCode.Conflict, renamed.StatusCode);
            Assert.Equal(("CONFLICT", "Mã căn hộ đã tồn tại."), await UiFlow.ReadErrorAsync(renamed));

            Assert.Equal(2, (await CountMineAsync()).Apartments);
            Assert.Equal(other.Code,
                (await UiFlow.GetJsonAsync<ApartmentProfileResponse>(admin, $"/api/can-ho/{other.Id}")).Code);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    [SkippableFact]
    [Trait("Acceptance", "Y5")]
    public async Task Y5_DuplicateHouseholdNumber_OrSecondHouseholdForApartment_IsRejected()
    {
        SkipIfNoDatabase();
        var admin = await AdminAsync();
        try
        {
            var apartmentA = await CreateApartmentAsync(admin, "05A");
            await CreateHouseholdAsync(admin, apartmentA.Id, "05", Resident("Phạm Văn Năm"));
            var apartmentB = await CreateApartmentAsync(admin, "05B");

            var sameNumber = await PostHouseholdAsync(admin, apartmentB.Id, Household("05", Resident("Phạm Thị Khác")));
            Assert.Equal(HttpStatusCode.Conflict, sameNumber.StatusCode);
            Assert.Equal(("CONFLICT", "Số hộ khẩu đã tồn tại."), await UiFlow.ReadErrorAsync(sameNumber));

            var secondHousehold = await PostHouseholdAsync(admin, apartmentA.Id, Household("05X", Resident("Phạm Văn Thêm")));
            Assert.Equal(HttpStatusCode.Conflict, secondHousehold.StatusCode);
            Assert.Equal(("CONFLICT", "Căn hộ đã có hồ sơ hộ gia đình."), await UiFlow.ReadErrorAsync(secondHousehold));

            Assert.Equal(1, (await CountMineAsync()).Households);
            Assert.Null((await UiFlow.GetJsonAsync<ApartmentProfileResponse>(admin, $"/api/can-ho/{apartmentB.Id}")).Household);
        }
        finally
        {
            await CleanupAsync();
        }
    }

    // ===== AC-002: thiếu trường bắt buộc =====

    [SkippableTheory]
    [Trait("Acceptance", "Y7")]
    [InlineData("ma-can-ho", "Code")]
    [InlineData("so-ho-khau", "HouseholdNumber")]
    [InlineData("ten-chu-ho", "HeadName")]
    [InlineData("ho-ten-nhan-khau", "FullName")]
    [InlineData("ngay-lap-ho-khau", "Ngày lập hộ khẩu")]
    [InlineData("ngay-sinh", "Ngày sinh")]
    public async Task Y7_MissingRequiredField_IsRejected_NamesTheField_AndNothingIsSaved(string kind, string field)
    {
        SkipIfNoDatabase();
        var admin = await AdminAsync();
        try
        {
            HttpResponseMessage response;
            if (kind == "ma-can-ho")
            {
                var buildingId = await TestBuilding.IdAsync(admin, BuildingName);
                response = await PostApartmentAsync(admin, Apartment("07", buildingId) with { Code = "" });
            }
            else
            {
                var apartment = await CreateApartmentAsync(admin, "07");
                var valid = Household("07", Resident("Đỗ Văn Bảy"));
                var request = kind switch
                {
                    "so-ho-khau" => valid with { HouseholdNumber = "" },
                    "ten-chu-ho" => valid with { HeadName = "" },
                    "ho-ten-nhan-khau" => valid with { Residents = [Resident("")] },
                    "ngay-lap-ho-khau" => valid with { RegisteredAt = default },
                    "ngay-sinh" => valid with { Residents = [Resident("Đỗ Văn Bảy") with { DateOfBirth = default }] },
                    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
                };
                response = await PostHouseholdAsync(admin, apartment.Id, request);
            }

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(field, await ErrorTextAsync(response), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(kind == "ma-can-ho" ? (0, 0) : (1, 0), await CountMineAsync());
        }
        finally
        {
            await CleanupAsync();
        }
    }

    // ===== AC-002: sai định dạng hoặc giá trị không hợp lệ =====

    [SkippableTheory]
    [Trait("Acceptance", "Y8")]
    [InlineData("dien-tich-bang-0", "Area")]
    [InlineData("dien-tich-am", "Area")]
    [InlineData("tang-qua-lon", "Floor")]
    [InlineData("phong-ngu-qua-nhieu", "BedroomCount")]
    [InlineData("toa-nha-khong-ton-tai", "Tòa nhà")]
    [InlineData("ngay-cap-giay-to", "Ngày cấp")]
    public async Task Y8_InvalidValue_IsRejected_NamesTheField_AndNothingIsSaved(string kind, string field)
    {
        SkipIfNoDatabase();
        var admin = await AdminAsync();
        try
        {
            HttpResponseMessage response;
            if (kind == "ngay-cap-giay-to")
            {
                var apartment = await CreateApartmentAsync(admin, "08");
                response = await PostHouseholdAsync(admin, apartment.Id,
                    Household("08", Resident("Vũ Văn Tám") with { IdentityIssuedAt = DateTimeOffset.MinValue }));
            }
            else
            {
                var buildingId = await TestBuilding.IdAsync(admin, BuildingName);
                var valid = Apartment("08", buildingId);
                var request = kind switch
                {
                    "dien-tich-bang-0" => valid with { Area = 0m },
                    "dien-tich-am" => valid with { Area = -5m },
                    "tang-qua-lon" => valid with { Floor = 201 },
                    "phong-ngu-qua-nhieu" => valid with { BedroomCount = 51 },
                    "toa-nha-khong-ton-tai" => valid with { BuildingId = int.MaxValue },
                    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
                };
                response = await PostApartmentAsync(admin, request);
            }

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(field, await ErrorTextAsync(response), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(kind == "ngay-cap-giay-to" ? (1, 0) : (0, 0), await CountMineAsync());
        }
        finally
        {
            await CleanupAsync();
        }
    }

    // ===== Ngoài quyền thì bị từ chối, dữ liệu không đổi (DoD, AC-016) =====

    [SkippableFact]
    [Trait("Acceptance", "Y10")]
    public async Task Y10_AccountantAndResident_CannotCreateOrUpdateProfiles()
    {
        SkipIfNoDatabase();
        var admin = await AdminAsync();
        try
        {
            var apartment = await CreateApartmentAsync(admin, "10");
            var household = await CreateHouseholdAsync(admin, apartment.Id, "10", Resident("Hoàng Văn Mười"));

            var adminId = await Accounts.AdminIdAsync();
            var ketoan = await Accounts.SeedAsync("y_ketoan", UserRole.KE_TOAN, grantedById: adminId);
            var cudan = await Accounts.SeedAsync("y_cudan", UserRole.CU_DAN);

            foreach (var (username, role) in new[] { (ketoan.Username, UserRole.KE_TOAN), (cudan.Username, UserRole.CU_DAN) })
            {
                var browser = await LoginAsync(username, role);
                var updateHousehold = new UpdateHouseholdRequest
                {
                    HouseholdNumber = household.HouseholdNumber,
                    HeadName = "Bị sửa trái phép",
                    HouseNumber = household.HouseNumber,
                    RegisteredAt = household.RegisteredAt,
                    Residents = []
                };

                Assert.Equal(HttpStatusCode.Forbidden,
                    (await PostApartmentAsync(browser, Apartment("10X", apartment.BuildingId))).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden,
                    (await browser.PutAsJsonAsync($"/api/can-ho/{apartment.Id}", UpdateOf(apartment) with { Area = 99m }, UiFlow.Json)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden,
                    (await browser.PutAsJsonAsync($"/api/ho-gia-dinh/{household.Id}", updateHousehold, UiFlow.Json)).StatusCode);
            }

            var read = await UiFlow.GetJsonAsync<ApartmentProfileResponse>(admin, $"/api/can-ho/{apartment.Id}");
            Assert.Equal(72.5m, read.Area);
            Assert.Equal("Trần Văn Chủ Hộ", read.Household!.HeadName);
            Assert.Equal((1, 1), await CountMineAsync());
        }
        finally
        {
            await CleanupAsync();
        }
    }
}
