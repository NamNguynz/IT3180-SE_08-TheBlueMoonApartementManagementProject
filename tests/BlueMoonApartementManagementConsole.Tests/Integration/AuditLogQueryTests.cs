using System.Globalization;
using System.Net;
using System.Text.Json;
using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BlueMoonApartementManagementConsole.Tests.Integration;

/// <summary>
/// Test tích hợp tra cứu nhật ký: gọi API /api/nhat-ky thật (đăng nhập admin) trên PostgreSQL thật,
/// để kiểm tra phần lọc, sắp xếp, phân trang nằm trong câu SQL của AuditRepository.
///
/// Cách cô lập dữ liệu: mỗi test thêm log trực tiếp vào DB với tên tài khoản có tiền tố riêng (_prefix)
/// và thời điểm năm 2001, và luôn lọc theo tiền tố đó.
/// Log thêm vào KHÔNG xóa được: bảng audit_logs có trigger chặn UPDATE/DELETE (migration AddAuditLogs, xem Q12).
/// Vì vậy log test nằm lại trong DB test (trên CI, DB được tạo mới mỗi lần chạy). Chỉ user test là được xóa.
/// Cần PostgreSQL: đặt biến môi trường BLUEMOON_TEST_CONNECTION, nếu không các test sẽ bị skip.
/// </summary>
[Collection(SequentialDatabaseCollection.Name)]
public class AuditLogQueryTests(BlueMoonAppFactory factory) : IClassFixture<BlueMoonAppFactory>, IAsyncLifetime
{
    private const string Api = "/api/nhat-ky";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Mốc thời gian giả, cách xa hiện tại để không lẫn với log thật trong DB test.</summary>
    private static readonly DateTimeOffset T0 = new(2001, 2, 3, 0, 0, 0, TimeSpan.Zero);

    private readonly string _prefix = "it" + Guid.NewGuid().ToString("N")[..10] + "_";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (BlueMoonAppFactory.ConnectionString is null) return;

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Users.Where(u => u.Username.StartsWith(_prefix)).ExecuteDeleteAsync();
    }

    // ===== Hàm hỗ trợ =====

    private static void SkipIfNoDatabase() =>
        Skip.If(BlueMoonAppFactory.ConnectionString is null, BlueMoonAppFactory.SkipReason);

    private AuditLog Log(string user, AuditAction action, DateTimeOffset at, string? target = null, string? detail = null) =>
        new() { Username = _prefix + user, Action = action, OccurredAt = at.ToUniversalTime(), Target = target, Detail = detail };

    private async Task<AuditLog[]> SeedAsync(params AuditLog[] logs)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AuditLogs.AddRange(logs);
        await db.SaveChangesAsync();
        return logs; // EF đã gán Id thật
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = factory.CreateHttpsClient();
        var response = await client.PostAsync(BlueMoonAppFactory.TestSignInPath, null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return client;
    }

    /// <summary>Cùng định dạng với Date.toISOString() mà audit-log.js gửi lên.</summary>
    private static string Iso(DateTimeOffset t) =>
        t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>0 giờ ngày d theo giờ Việt Nam, đổi sang UTC (giống buildQuery trong audit-log.js).</summary>
    private static DateTimeOffset VnMidnight(int year, int month, int day) =>
        new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.FromHours(7)).ToUniversalTime();

    private static string Url(DateTimeOffset? from = null, DateTimeOffset? to = null, AuditAction? action = null,
        string? username = null, int? page = null, int? pageSize = null)
    {
        var parts = new List<string>();
        if (from is { } f) parts.Add("from=" + Uri.EscapeDataString(Iso(f)));
        if (to is { } t) parts.Add("to=" + Uri.EscapeDataString(Iso(t)));
        if (action is { } a) parts.Add("action=" + a);
        if (username is not null) parts.Add("username=" + Uri.EscapeDataString(username));
        if (page is { } p) parts.Add("page=" + p);
        if (pageSize is { } s) parts.Add("pageSize=" + s);
        return Api + "?" + string.Join("&", parts);
    }

    private static async Task<PagedResult<AuditLogDto>> GetPageAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {url} trả {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<PagedResult<AuditLogDto>>(body, Json)!;
    }

    /// <summary>Thứ tự API phải trả: mới nhất trước, cùng thời điểm thì Id lớn hơn trước.</summary>
    private static long[] NewestFirst(IEnumerable<AuditLog> logs) =>
        logs.OrderByDescending(l => l.OccurredAt).ThenByDescending(l => l.Id).Select(l => l.Id).ToArray();

    private static long[] Ids(PagedResult<AuditLogDto> page) => page.Items.Select(i => i.Id).ToArray();

    // ===== Q1: Lọc theo hành động =====

    [SkippableFact]
    [Trait("TestId", "Q1")]
    public async Task FilterByAction_ReturnsOnlyThatAction()
    {
        SkipIfNoDatabase();
        var logs = await SeedAsync(
            Log("u", AuditAction.LoginSuccess, T0.AddMinutes(1)),
            Log("u", AuditAction.LoginFailed, T0.AddMinutes(2)),
            Log("u", AuditAction.Logout, T0.AddMinutes(3)),
            Log("u", AuditAction.LoginFailed, T0.AddMinutes(4)));
        var client = await AdminClientAsync();

        var all = await GetPageAsync(client, Url(username: _prefix));
        var failed = await GetPageAsync(client, Url(action: AuditAction.LoginFailed, username: _prefix));

        Assert.Equal(4, all.Total);
        Assert.Equal(2, failed.Total);
        Assert.All(failed.Items, i => Assert.Equal("LoginFailed", i.Action));
        Assert.Equal(NewestFirst(logs.Where(l => l.Action == AuditAction.LoginFailed)), Ids(failed));
    }

    // ===== Q2: Lọc theo tài khoản =====

    [SkippableFact]
    [Trait("TestId", "Q2")]
    public async Task FilterByUsername_MatchesPartOfName()
    {
        SkipIfNoDatabase();
        var logs = await SeedAsync(
            Log("alice", AuditAction.LoginSuccess, T0),
            Log("alice2", AuditAction.LoginSuccess, T0.AddMinutes(1)),
            Log("bob", AuditAction.LoginSuccess, T0.AddMinutes(2)));
        var client = await AdminClientAsync();

        var exact = await GetPageAsync(client, Url(username: _prefix + "alice2"));
        var middle = await GetPageAsync(client, Url(username: _prefix[2..] + "ali")); // khớp ở giữa chuỗi, không chỉ đầu chuỗi

        Assert.Equal([logs[1].Id], Ids(exact));
        Assert.Equal(NewestFirst(logs.Take(2)), Ids(middle));
    }

    [SkippableFact]
    [Trait("TestId", "Q2")]
    public async Task FilterByUsername_CurrentLimitations_J2()
    {
        // Ghi nhận hành vi hiện tại (J2). Nếu nhóm quyết định sửa, test này phải đổi theo.
        SkipIfNoDatabase();
        await SeedAsync(
            Log("alice", AuditAction.LoginSuccess, T0),
            Log("admin", AuditAction.UnlockAccount, T0.AddMinutes(1), target: "99", detail: $"Mở khóa tài khoản {_prefix}victim"));
        var client = await AdminClientAsync();

        var upper = await GetPageAsync(client, Url(username: (_prefix + "alice").ToUpperInvariant()));
        var spaced = await GetPageAsync(client, Url(username: " " + _prefix + "alice"));
        var victim = await GetPageAsync(client, Url(username: _prefix + "victim"));

        Assert.Equal(0, upper.Total);   // phân biệt chữ hoa, chữ thường
        Assert.Equal(0, spaced.Total);  // API không cắt khoảng trắng (chỉ audit-log.js cắt)
        Assert.Equal(0, victim.Total);  // chỉ lọc theo người thực hiện, không theo tài khoản bị tác động
    }

    // ===== Q3: Ranh giới thời gian =====

    [SkippableFact]
    [Trait("TestId", "Q3")]
    public async Task FilterByVietnamDay_FromInclusive_ToExclusive()
    {
        // Giống audit-log.js khi chọn Từ ngày = Đến ngày = 03/02/2001
        SkipIfNoDatabase();
        var from = VnMidnight(2001, 2, 3); // 2001-02-02T17:00:00Z
        var to = from.AddDays(1);          // 0 giờ ngày 04/02 giờ VN, không tính
        var logs = await SeedAsync(
            Log("u", AuditAction.LoginSuccess, from.AddMilliseconds(-1)), // 23:59:59.999 ngày 02/02: không có
            Log("u", AuditAction.LoginSuccess, from),                     // 00:00:00.000 ngày 03/02: có
            Log("u", AuditAction.LoginSuccess, from.AddHours(12)),        // giữa ngày: có
            Log("u", AuditAction.LoginSuccess, to.AddMilliseconds(-1)),   // 23:59:59.999 ngày 03/02: có
            Log("u", AuditAction.LoginSuccess, to));                      // 00:00:00.000 ngày 04/02: không có
        var client = await AdminClientAsync();

        var day = await GetPageAsync(client, Url(from, to, username: _prefix));
        var fromOnly = await GetPageAsync(client, Url(from: from, username: _prefix));
        var toOnly = await GetPageAsync(client, Url(to: to, username: _prefix));

        Assert.Equal(NewestFirst(logs[1..4]), Ids(day));
        Assert.Equal(NewestFirst(logs[1..]), Ids(fromOnly));
        Assert.Equal(NewestFirst(logs[..4]), Ids(toOnly));
    }

    // ===== Q4: Thứ tự =====

    [SkippableFact]
    [Trait("TestId", "Q4")]
    public async Task Results_NewestFirst_TieBrokenByIdDescending()
    {
        SkipIfNoDatabase();
        var logs = await SeedAsync(
            Log("u", AuditAction.LoginSuccess, T0.AddMinutes(1)),
            Log("u", AuditAction.LoginFailed, T0.AddMinutes(3)),
            Log("u", AuditAction.Logout, T0.AddMinutes(2)),
            Log("u", AuditAction.Register, T0.AddMinutes(3))); // cùng thời điểm với dòng thứ hai
        var client = await AdminClientAsync();

        var page = await GetPageAsync(client, Url(username: _prefix));

        Assert.Equal(NewestFirst(logs), Ids(page));
        var (first, second) = (page.Items[0], page.Items[1]);
        Assert.Equal(first.OccurredAt, second.OccurredAt);
        Assert.True(first.Id > second.Id);
    }

    // ===== Q5: Phân trang =====

    [SkippableFact]
    [Trait("TestId", "Q5")]
    public async Task Paging_ReturnsCorrectSlices_AndTotal()
    {
        SkipIfNoDatabase();
        var logs = await SeedAsync(Enumerable.Range(0, 25)
            .Select(i => Log("u", AuditAction.LoginFailed, T0.AddSeconds(i)))
            .ToArray());
        var expected = NewestFirst(logs);
        var client = await AdminClientAsync();

        var p1 = await GetPageAsync(client, Url(username: _prefix, page: 1, pageSize: 10));
        var p2 = await GetPageAsync(client, Url(username: _prefix, page: 2, pageSize: 10));
        var p3 = await GetPageAsync(client, Url(username: _prefix, page: 3, pageSize: 10));
        var p4 = await GetPageAsync(client, Url(username: _prefix, page: 4, pageSize: 10));

        Assert.Equal(expected[..10], Ids(p1));
        Assert.Equal(expected[10..20], Ids(p2));
        Assert.Equal(expected[20..], Ids(p3));
        Assert.Empty(p4.Items); // trang vượt quá: không có dòng nhưng vẫn biết tổng
        Assert.All([p1, p2, p3, p4], p => Assert.Equal(25, p.Total));
    }

    [SkippableFact]
    [Trait("TestId", "Q5")]
    public async Task Paging_OutOfRangeParameters_AreClamped()
    {
        SkipIfNoDatabase();
        var logs = await SeedAsync(Enumerable.Range(0, 25)
            .Select(i => Log("u", AuditAction.LoginFailed, T0.AddSeconds(i)))
            .ToArray());
        var expected = NewestFirst(logs);
        var client = await AdminClientAsync();

        var big = await GetPageAsync(client, Url(username: _prefix, pageSize: 1000));
        var small = await GetPageAsync(client, Url(username: _prefix, pageSize: 5));
        var zero = await GetPageAsync(client, Url(username: _prefix, page: 0, pageSize: 10));

        Assert.Equal(100, big.PageSize);
        Assert.Equal(expected, Ids(big));
        Assert.Equal(10, small.PageSize);
        Assert.Equal(expected[..10], Ids(small));
        Assert.Equal(1, zero.Page);
        Assert.Equal(expected[..10], Ids(zero));
    }

    // ===== Q6: Kết hợp nhiều bộ lọc =====

    [SkippableFact]
    [Trait("TestId", "Q6")]
    public async Task CombinedFilters_AllMustMatch()
    {
        SkipIfNoDatabase();
        var logs = await SeedAsync(
            Log("x", AuditAction.LoginFailed, T0.AddMinutes(10)),  // khớp tất cả
            Log("x", AuditAction.LoginFailed, T0.AddHours(2)),     // ngoài khoảng thời gian
            Log("x", AuditAction.Logout, T0.AddMinutes(20)),       // khác hành động
            Log("y", AuditAction.LoginFailed, T0.AddMinutes(30))); // khác tài khoản
        var client = await AdminClientAsync();

        var page = await GetPageAsync(client, Url(T0, T0.AddHours(1), AuditAction.LoginFailed, _prefix + "x"));

        Assert.Equal([logs[0].Id], Ids(page));
        Assert.Equal(1, page.Total);
    }

    // ===== Q7: Kiểm chứng J1: thời gian không theo UTC =====

    // Đã xác nhận J1 (2026-11-08): API trả 500, log của app ghi
    // "Cannot write DateTimeOffset with Offset=07:00:00 to PostgreSQL type 'timestamp with time zone', only offset 0 (UTC) is supported".
    // Giao diện không bị ảnh hưởng vì audit-log.js luôn gửi UTC. Bỏ Skip sau khi sửa (vd: đổi From/To sang UTC trong AuditService).
    [SkippableFact(Skip = "Chờ fix J1: API trả 500 khi from/to không theo UTC (vd +07:00)")]
    [Trait("TestId", "Q7")]
    public async Task FilterWithNonUtcOffset_GivesSameResultAsUtc()
    {
        SkipIfNoDatabase();
        var logs = await SeedAsync(
            Log("u", AuditAction.LoginSuccess, T0.AddMinutes(-1)),
            Log("u", AuditAction.LoginSuccess, T0),
            Log("u", AuditAction.LoginSuccess, T0.AddMinutes(1)));
        var client = await AdminClientAsync();

        // Cùng thời điểm T0 nhưng viết theo giờ Việt Nam: 2001-02-03T07:00:00+07:00
        var fromVn = T0.ToOffset(TimeSpan.FromHours(7)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        var page = await GetPageAsync(client, $"{Api}?from={Uri.EscapeDataString(fromVn)}&username={Uri.EscapeDataString(_prefix)}");

        Assert.Equal(NewestFirst(logs[1..]), Ids(page));
    }

    // ===== Q8: Đăng xuất được ghi log =====

    [SkippableFact]
    [Trait("TestId", "Q8")]
    public async Task Logout_WritesOneLogoutEntryForAdmin()
    {
        SkipIfNoDatabase();
        var client = await AdminClientAsync();

        int adminId;
        long lastId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            adminId = await db.Users.Where(u => u.Username == "admin").Select(u => u.Id).SingleAsync();
            lastId = await db.AuditLogs.MaxAsync(a => (long?)a.Id) ?? 0;
        }

        var response = await client.PostAsync("/logout", null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login", response.Headers.Location!.OriginalString);

        List<AuditLog> written;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            written = await db.AuditLogs.AsNoTracking().Where(a => a.Id > lastId).ToListAsync();
        }

        var log = Assert.Single(written);
        Assert.Equal(AuditAction.Logout, log.Action);
        Assert.Equal("admin", log.Username);
        Assert.Equal(adminId, log.UserId);
        Assert.Equal(adminId.ToString(), log.Target);

        // Đăng xuất xong thì phiên cũ không gọi được API nữa
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Api)).StatusCode);
    }

    // ===== Q9: Định dạng JSON mà audit-log.js đọc =====

    [SkippableFact]
    [Trait("TestId", "Q9")]
    public async Task Json_HasFieldsUsedByAuditLogJs()
    {
        SkipIfNoDatabase();
        var logs = await SeedAsync(
            Log("json", AuditAction.LoginFailed, T0),
            Log("json", AuditAction.ResetPassword, T0.AddMilliseconds(123), target: "42", detail: "Cấp lại mật khẩu cho <b>x</b>"));
        var client = await AdminClientAsync();

        var response = await client.GetAsync(Url(username: _prefix + "json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal(["items", "page", "pageSize", "total"], root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(2, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(20, root.GetProperty("pageSize").GetInt32()); // mặc định khi không truyền

        var items = root.GetProperty("items").EnumerateArray().ToList();
        var withValues = items[0];
        var withNulls = items[1];

        Assert.Equal(["action", "detail", "id", "occurredAt", "target", "username"],
            withValues.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(logs[1].Id, withValues.GetProperty("id").GetInt64());
        Assert.Equal("ResetPassword", withValues.GetProperty("action").GetString()); // khóa của ACTION_LABEL
        Assert.Equal(_prefix + "json", withValues.GetProperty("username").GetString());
        Assert.Equal("42", withValues.GetProperty("target").GetString());
        Assert.Equal("Cấp lại mật khẩu cho <b>x</b>", withValues.GetProperty("detail").GetString());
        Assert.Equal(T0.AddMilliseconds(123), DateTimeOffset.Parse(withValues.GetProperty("occurredAt").GetString()!, CultureInfo.InvariantCulture));

        // audit-log.js dùng "?? '—'" nên trường trống phải là null, không được thiếu
        Assert.Equal(JsonValueKind.Null, withNulls.GetProperty("target").ValueKind);
        Assert.Equal(JsonValueKind.Null, withNulls.GetProperty("detail").ValueKind);
    }

    // ===== Q10: Kiểm chứng lại lỗi #2 trong acceptance_password_hashing.md =====

    [SkippableFact]
    [Trait("TestId", "Q10")]
    public async Task WrongRoleLogin_RehashedPasswordIsStillSaved()
    {
        // Lỗi #2 cho rằng: hash được tạo lại khi đăng nhập sẽ không được lưu nếu chọn sai tư cách,
        // vì nhánh đó không gọi users.SaveChangesAsync. Nhưng AuditRepository và AppUserRepo dùng chung
        // một AppDbContext trong mỗi scope, nên audit.LogAsync (có SaveChanges) lưu luôn hash mới.
        // Test chạy đúng các service thật của app để xem kết quả trên database.
        SkipIfNoDatabase();
        const string password = "MatKhauCu@123";
        var username = _prefix + "rehash";

        var v2Hasher = new PasswordHasher<AppUserEntity>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2
        }));
        var user = new AppUserEntity
        {
            Username = username,
            FullName = "Rehash",
            Role = UserRole.CU_DAN,
            Status = UserStatus.Approved,
            IsActive = true
        };
        user.PasswordHash = v2Hasher.HashPassword(user, password);
        var oldHash = user.PasswordHash;
        Assert.Equal(0x00, Convert.FromBase64String(oldHash)[0]); // 0x00 = định dạng V2 cũ, cần băm lại

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<IAuthService>();
            var result = await auth.LoginAsync(new LoginRequest(username, password, UserRole.BQT)); // đúng mật khẩu, sai tư cách
            Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUserEntity>>();
            var stored = await db.Users.AsNoTracking().SingleAsync(u => u.Username == username);

            Assert.NotEqual(oldHash, stored.PasswordHash);
            Assert.Equal(0x01, Convert.FromBase64String(stored.PasswordHash)[0]); // đã lên V3
            Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(stored, stored.PasswordHash, password));

            var log = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Username == username);
            Assert.Equal(AuditAction.LoginFailed, log.Action);
            Assert.Contains("Sai tư cách đăng nhập", log.Detail);
        }
    }

    // ===== Q11: API nhật ký không nhận method khác GET =====

    [SkippableTheory]
    [Trait("TestId", "Q11")]
    [InlineData("DELETE", HttpStatusCode.MethodNotAllowed)]
    [InlineData("PUT", HttpStatusCode.MethodNotAllowed)]
    [InlineData("PATCH", HttpStatusCode.MethodNotAllowed)]
    // Ghi nhận hành vi hiện tại (J6): POST cũng bị routing trả 405, nhưng UseStatusCodePagesWithReExecute
    // chạy lại request tới trang /not-found bằng POST, trang Blazor đó trả 400. Request vẫn bị chặn.
    [InlineData("POST", HttpStatusCode.BadRequest)]
    public async Task NonGetMethods_AreRejected(string method, HttpStatusCode expected)
    {
        SkipIfNoDatabase();
        var client = await AdminClientAsync();
        long lastId;
        await using (var scope = factory.Services.CreateAsyncScope())
            lastId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs.MaxAsync(a => (long?)a.Id) ?? 0;

        using var request = new HttpRequestMessage(new HttpMethod(method), Api);
        var response = await client.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);

        // Không có thao tác nào được thực hiện, kể cả ghi log
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs.AnyAsync(a => a.Id > lastId));
    }

    // ===== Q12: Database chặn sửa/xóa nhật ký =====

    [SkippableFact]
    [Trait("TestId", "Q12")]
    public async Task Database_BlocksUpdateAndDeleteOnAuditLogs()
    {
        // Trigger audit_logs_no_update_delete (migration AddAuditLogs): BEFORE UPDATE OR DELETE, FOR EACH ROW
        SkipIfNoDatabase();
        var seeded = (await SeedAsync(Log("u", AuditAction.LoginSuccess, T0, detail: "goc")))[0];

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var update = await Assert.ThrowsAnyAsync<PostgresException>(() =>
            db.AuditLogs.Where(a => a.Id == seeded.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Detail, "da sua")));
        var delete = await Assert.ThrowsAnyAsync<PostgresException>(() =>
            db.AuditLogs.Where(a => a.Id == seeded.Id).ExecuteDeleteAsync());

        // Đường đi thông thường của EF: sửa entity đang track rồi SaveChanges
        var tracked = await db.AuditLogs.SingleAsync(a => a.Id == seeded.Id);
        tracked.Detail = "da sua";
        var save = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        foreach (var error in new[] { update, delete, Assert.IsType<PostgresException>(save.InnerException) })
        {
            Assert.Equal("P0001", error.SqlState); // RAISE EXCEPTION trong trigger
            Assert.Contains("append-only", error.MessageText);
        }

        db.ChangeTracker.Clear();
        var stored = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Id == seeded.Id);
        Assert.Equal("goc", stored.Detail);
    }
}
