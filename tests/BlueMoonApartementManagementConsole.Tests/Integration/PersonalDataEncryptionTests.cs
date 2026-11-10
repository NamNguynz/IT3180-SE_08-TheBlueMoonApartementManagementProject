using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Security;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BlueMoonApartementManagementConsole.Tests.Integration;

/// <summary>
/// Test tích hợp mã hóa dữ liệu cá nhân (PBI-030, AC-038, NFR-007) trên PostgreSQL thật:
/// gọi API hồ sơ hộ như trang Hồ sơ căn hộ, rồi đọc thẳng bảng residents bằng Npgsql (không qua EF)
/// để thấy giá trị thật sự nằm trong CSDL.
/// Dữ liệu là dữ liệu giả (RSK-008). Căn hộ test có mã bắt đầu bằng tiền tố riêng và bị xóa khi test xong
/// (hộ, nhân khẩu xóa theo). Nhật ký không xóa được (bảng audit_logs chỉ cho thêm).
/// Cần PostgreSQL: đặt biến môi trường BLUEMOON_TEST_CONNECTION, nếu không các test sẽ bị skip.
/// </summary>
[Collection(SequentialDatabaseCollection.Name)]
public class PersonalDataEncryptionTests(BlueMoonAppFactory factory) : IClassFixture<BlueMoonAppFactory>, IAsyncLifetime
{
    private const string DuplicateIdentity = "Số CMND/CCCD đã được sử dụng.";
    // Cùng cấu hình với app: enum (vd ApartmentType) đọc/ghi dạng chuỗi
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <summary>Tiền tố mã căn hộ và số hộ khẩu (service viết hoa mã căn hộ).</summary>
    private readonly string _prefix = "PII" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant() + "-";

    /// <summary>5 cột được mã hóa, theo thứ tự đọc ở RawResidentAsync.</summary>
    private static readonly string[] EncryptedColumns =
        ["IdentityNumber", "IdentityIssuePlace", "Ethnicity", "Religion", "PreviousResidence"];

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

    /// <summary>Số CCCD giả 12 chữ số, mỗi lần một số khác để không đụng dữ liệu của lần chạy trước.</summary>
    private static string NewCccd() =>
        "0" + Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString(CultureInfo.InvariantCulture);

    private static ResidentProfileRequest Resident(string cccd, int? id = null, string religion = "Không") => new()
    {
        Id = id,
        FullName = "Nguyễn Văn Thử",
        DateOfBirth = new DateTimeOffset(1990, 5, 17, 0, 0, 0, TimeSpan.Zero),
        Gender = "Nam",
        Ethnicity = "Kinh",
        Religion = religion,
        IdentityNumber = cccd,
        IdentityIssuedAt = new DateTimeOffset(2021, 4, 1, 0, 0, 0, TimeSpan.Zero),
        IdentityIssuePlace = "Cục Cảnh sát QLHC về TTXH",
        PreviousResidence = "Số 1 phố Giả Định, Hà Nội"
    };

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

    private async Task<int> CreateApartmentAsync(HttpClient admin, string suffix)
    {
        var response = await admin.PostAsJsonAsync("/api/can-ho", new CreateApartmentRequest
        {
            Code = _prefix + suffix,
            Building = "Tòa test PII",
            Type = ApartmentType.CanHo,
            Floor = 5,
            Area = 70m,
            BedroomCount = 2,
            Status = "Đang ở"
        }, Json);
        return (await ReadAsync<ApartmentProfileResponse>(response, HttpStatusCode.Created)).Id;
    }

    private CreateHouseholdRequest NewHousehold(string suffix, params ResidentProfileRequest[] residents) => new()
    {
        HouseholdNumber = _prefix + suffix,
        HeadName = "Nguyễn Văn Thử",
        HouseNumber = "P" + suffix,
        RegisteredAt = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero),
        Residents = [.. residents]
    };

    private Task<HttpResponseMessage> PostHouseholdAsync(HttpClient admin, int apartmentId, CreateHouseholdRequest request) =>
        admin.PostAsJsonAsync($"/api/ho-gia-dinh/can-ho/{apartmentId}", request, Json);

    private async Task<HouseholdProfileResponse> CreateHouseholdAsync(HttpClient admin, string suffix, params ResidentProfileRequest[] residents)
    {
        var apartmentId = await CreateApartmentAsync(admin, suffix);
        var response = await PostHouseholdAsync(admin, apartmentId, NewHousehold(suffix, residents));
        return await ReadAsync<HouseholdProfileResponse>(response, HttpStatusCode.Created);
    }

    /// <summary>Đọc thẳng một dòng residents bằng Npgsql, không qua EF nên không bị giải mã.</summary>
    private static async Task<Dictionary<string, string?>> RawResidentAsync(int residentId)
    {
        await using var connection = new NpgsqlConnection(BlueMoonAppFactory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT "FullName", "IdentityNumber", "IdentityIssuePlace", "Ethnicity", "Religion",
                   "PreviousResidence", "IdentityNumberHash"
            FROM residents WHERE "Id" = @id
            """, connection);
        command.Parameters.AddWithValue("id", residentId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"Không thấy nhân khẩu {residentId} trong bảng residents");
        var row = new Dictionary<string, string?>();
        for (var i = 0; i < reader.FieldCount; i++)
            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetString(i);
        return row;
    }

    private string HashOf(string cccd)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IPersonalDataEncryptor>().HashIdentity(cccd);
    }

    // ===== Test =====

    [SkippableFact]
    [Trait("TestId", "E8")]
    public async Task CreatedResident_IsStoredEncrypted_ButApiReturnsPlaintext()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();
        var cccd = NewCccd();
        var sent = Resident(cccd);

        var household = await CreateHouseholdAsync(admin, "A", sent);
        var residentId = Assert.Single(household.Residents).Id;

        var raw = await RawResidentAsync(residentId);
        var plaintext = new[] { cccd, sent.IdentityIssuePlace, sent.Ethnicity, sent.Religion, sent.PreviousResidence };
        for (var i = 0; i < EncryptedColumns.Length; i++)
        {
            var stored = raw[EncryptedColumns[i]];
            Assert.NotNull(stored);
            Assert.StartsWith(PersonalDataEncryptor.Prefix, stored);
            Assert.DoesNotContain(plaintext[i]!, stored);
        }
        Assert.Equal(HashOf(cccd), raw["IdentityNumberHash"]);
        // Họ tên không mã hóa (cần tìm kiếm, sắp xếp)
        Assert.Equal(sent.FullName, raw["FullName"]);

        var read = Assert.Single((await ReadAsync<HouseholdProfileResponse>(
            await admin.GetAsync($"/api/ho-gia-dinh/{household.Id}"), HttpStatusCode.OK)).Residents);
        Assert.Equal(cccd, read.IdentityNumber);
        Assert.Equal(sent.IdentityIssuePlace, read.IdentityIssuePlace);
        Assert.Equal(sent.Ethnicity, read.Ethnicity);
        Assert.Equal(sent.Religion, read.Religion);
        Assert.Equal(sent.PreviousResidence, read.PreviousResidence);
    }

    [SkippableFact]
    [Trait("TestId", "E9")]
    public async Task SameCccdInAnotherHousehold_IsRejected()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();
        var cccd = NewCccd();
        await CreateHouseholdAsync(admin, "A", Resident(cccd));

        var otherApartment = await CreateApartmentAsync(admin, "B");
        var response = await PostHouseholdAsync(admin, otherApartment, NewHousehold("B", Resident(" " + cccd + " ")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(DuplicateIdentity, (await ReadAsync<ApiError>(response, HttpStatusCode.BadRequest)).Message);
    }

    [SkippableFact]
    [Trait("TestId", "E10")]
    public async Task UpdateResident_KeepingSameCccd_IsSaved_AndReencrypted()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();
        var cccd = NewCccd();
        var household = await CreateHouseholdAsync(admin, "A", Resident(cccd));
        var residentId = household.Residents.Single().Id;
        var before = await RawResidentAsync(residentId);

        var update = new UpdateHouseholdRequest
        {
            HouseholdNumber = household.HouseholdNumber,
            HeadName = household.HeadName,
            HouseNumber = household.HouseNumber,
            RegisteredAt = household.RegisteredAt,
            Residents = [Resident(cccd, residentId, religion: "Phật giáo")]
        };
        var updated = await ReadAsync<HouseholdProfileResponse>(
            await admin.PutAsJsonAsync($"/api/ho-gia-dinh/{household.Id}", update, Json), HttpStatusCode.OK);

        var resident = Assert.Single(updated.Residents);
        Assert.Equal(cccd, resident.IdentityNumber);
        Assert.Equal("Phật giáo", resident.Religion);

        var after = await RawResidentAsync(residentId);
        Assert.StartsWith(PersonalDataEncryptor.Prefix, after["Religion"]);
        Assert.NotEqual(before["Religion"], after["Religion"]);
        Assert.Equal(before["IdentityNumberHash"], after["IdentityNumberHash"]);
    }

    [SkippableFact]
    [Trait("TestId", "E11")]
    public async Task LegacyPlaintextRow_IsEncryptedByBackfill_AndStillReadable()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();
        var household = await CreateHouseholdAsync(admin, "A", Resident(NewCccd()));
        var legacyCccd = NewCccd();

        // Dòng lưu bản rõ như dữ liệu tạo trước PBI-030
        int legacyId;
        await using (var connection = new NpgsqlConnection(BlueMoonAppFactory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = new NpgsqlCommand("""
                INSERT INTO residents ("HouseholdId", "FullName", "DateOfBirth", "Gender", "IdentityNumber", "Ethnicity", "Religion")
                VALUES (@household, 'Trần Thị Cũ', @dob, 'Nữ', @cccd, 'Kinh', 'Phật giáo')
                RETURNING "Id"
                """, connection);
            insert.Parameters.AddWithValue("household", household.Id);
            insert.Parameters.AddWithValue("dob", new DateTimeOffset(1985, 3, 4, 0, 0, 0, TimeSpan.Zero));
            insert.Parameters.AddWithValue("cccd", legacyCccd);
            legacyId = (int)(await insert.ExecuteScalarAsync())!;
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await PersonalDataBackfill.EncryptLegacyRowsAsync(db) >= 1);
        }

        var raw = await RawResidentAsync(legacyId);
        Assert.StartsWith(PersonalDataEncryptor.Prefix, raw["IdentityNumber"]);
        Assert.StartsWith(PersonalDataEncryptor.Prefix, raw["Ethnicity"]);
        Assert.StartsWith(PersonalDataEncryptor.Prefix, raw["Religion"]);
        Assert.Null(raw["IdentityIssuePlace"]);
        Assert.Equal(HashOf(legacyCccd), raw["IdentityNumberHash"]);

        // Chạy lại thì không còn gì để mã hóa
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await PersonalDataBackfill.EncryptLegacyRowsAsync(db));
        }

        var legacy = (await ReadAsync<HouseholdProfileResponse>(
            await admin.GetAsync($"/api/ho-gia-dinh/{household.Id}"), HttpStatusCode.OK)).Residents.Single(r => r.Id == legacyId);
        Assert.Equal(legacyCccd, legacy.IdentityNumber);
        Assert.Equal("Phật giáo", legacy.Religion);

        // Số CCCD của dòng cũ vẫn được kiểm trùng
        var otherApartment = await CreateApartmentAsync(admin, "B");
        var duplicate = await PostHouseholdAsync(admin, otherApartment, NewHousehold("B", Resident(legacyCccd)));
        Assert.Equal(DuplicateIdentity, (await ReadAsync<ApiError>(duplicate, HttpStatusCode.BadRequest)).Message);
    }

    [SkippableFact]
    [Trait("TestId", "E12")]
    public async Task HouseholdAuditLogs_DoNotContainPersonalData()
    {
        SkipIfNoDatabase();
        var admin = await AdminClientAsync();
        var cccd = NewCccd();
        var household = await CreateHouseholdAsync(admin, "A", Resident(cccd));
        var update = new UpdateHouseholdRequest
        {
            HouseholdNumber = household.HouseholdNumber,
            HeadName = household.HeadName,
            HouseNumber = household.HouseNumber,
            RegisteredAt = household.RegisteredAt,
            Residents = [Resident(cccd, household.Residents.Single().Id, religion: "Phật giáo")]
        };
        await ReadAsync<HouseholdProfileResponse>(
            await admin.PutAsJsonAsync($"/api/ho-gia-dinh/{household.Id}", update, Json), HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var target = household.Id.ToString(CultureInfo.InvariantCulture);
        var logs = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Target == target
                && (a.Action == AuditAction.CreateHouseholdProfile || a.Action == AuditAction.UpdateHouseholdProfile))
            .ToListAsync();

        Assert.Contains(logs, l => l.Action == AuditAction.CreateHouseholdProfile);
        Assert.Contains(logs, l => l.Action == AuditAction.UpdateHouseholdProfile);
        Assert.All(logs, l =>
        {
            Assert.DoesNotContain(cccd, l.Detail ?? string.Empty);
            Assert.DoesNotContain("Phật giáo", l.Detail ?? string.Empty);
        });
    }
}
