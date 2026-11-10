using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlueMoonApartementManagementConsole.Tests.Integration.Acceptance;

/// <summary>
/// Tự động hóa docs/testing/acceptance_audit_log.md: D1, D2, D4, D10, D11 và phần API của D3, D5, D6.
/// Bộ lọc của trang Nhật ký chỉ là JavaScript ghép tham số rồi gọi /api/nhat-ky, nên test gọi đúng API đó
/// với cùng tham số. D7 đã có ở AuditLogQueryTests (Q11, Q12).
/// Còn chạy tay: D3 bước 5, D5 bước 2, nút Trước/Sau của D6, D8, D9 (phần hiển thị trên trình duyệt).
/// </summary>
[Collection(SequentialDatabaseCollection.Name)]
public class AuditLogAcceptanceTests(BlueMoonAppFactory factory)
    : AcceptanceTestBase(factory), IClassFixture<BlueMoonAppFactory>
{
    private const string WrongPassword = "SaiMatKhau@1";
    private static readonly TimeSpan Vn = TimeSpan.FromHours(7);

    /// <summary>12 loại thao tác có từ đầu (trước khi thêm nhật ký cho hồ sơ căn hộ và khoản thu).</summary>
    private static readonly AuditAction[] TwelveActions =
    [
        AuditAction.LoginSuccess, AuditAction.LoginFailed, AuditAction.LoginLocked, AuditAction.Logout,
        AuditAction.Register, AuditAction.ApproveAccount, AuditAction.CreateAccount, AuditAction.DisableAccount,
        AuditAction.EnableAccount, AuditAction.UnlockAccount, AuditAction.ResetPassword, AuditAction.ChangePassword
    ];

    private sealed record Flow(HttpClient Admin, long Mark, DateTimeOffset StartedAt, int AdminId,
        string Cudan, int CudanId, string Ketoan, int KetoanId, string Bqt, int BqtId, string Ghost);

    /// <summary>D1 bước 1 – 17: làm lần lượt đủ 12 loại thao tác qua giao diện và API thật.</summary>
    private async Task<Flow> RunAllActionsAsync()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var mark = await Accounts.LastLogIdAsync();
        var adminId = await Accounts.AdminIdAsync();
        var admin = await AdminAsync();                                                            // 1
        var guest = NewBrowser();

        var cudan = Accounts.Name("cudan_nk");
        var ketoan = Accounts.Name("ketoan_nk");
        await UiFlow.RegisterAsync(guest, cudan, Pw, "Cu Dan NK", UserRole.CU_DAN);                 // 2
        await UiFlow.RegisterAsync(guest, ketoan, Pw, "Ke Toan NK", UserRole.KE_TOAN);              // 3
        Assert.Equal(UiFlow.PendingMessage, (await UiFlow.LoginAsync(guest, ketoan, Pw, UserRole.KE_TOAN)).Alert); // 4
        var ketoanId = (await Accounts.FindAsync(ketoan))!.Id;
        Assert.Equal(HttpStatusCode.NoContent, (await UiFlow.PostAsync(admin, $"/api/tai-khoan/{ketoanId}/xac-nhan")).StatusCode); // 5

        await UiFlow.LoginAsync(guest, cudan, Pw, UserRole.KE_TOAN);                                // 6
        var ghost = Accounts.Name("khongtontai");
        await UiFlow.LoginAsync(guest, ghost, "abc12345", UserRole.CU_DAN);                         // 7
        await UiFlow.LoginOkAsync(guest, cudan, Pw, UserRole.CU_DAN);                               // 8
        await UiFlow.LogoutAsync(guest);                                                            // 9
        for (var i = 0; i < 5; i++)
            await UiFlow.LoginAsync(guest, cudan, WrongPassword, UserRole.CU_DAN);                  // 10
        Assert.Equal(UiFlow.LockedMessage, (await UiFlow.LoginAsync(guest, cudan, "abc12345", UserRole.CU_DAN)).Alert); // 11

        var cudanId = (await Accounts.FindAsync(cudan))!.Id;
        Assert.Equal(HttpStatusCode.NoContent, (await UiFlow.PostAsync(admin, $"/api/tai-khoan/{cudanId}/mo-khoa")).StatusCode); // 12
        var temp = await UiFlow.ResetPasswordAsync(admin, cudanId);                                 // 13
        await UiFlow.LoginOkAsync(guest, cudan, temp, UserRole.CU_DAN);                             // 14
        Assert.Equal(HttpStatusCode.NoContent, (await UiFlow.ChangePasswordAsync(guest, temp, "NewPass@02")).StatusCode);

        var bqt = Accounts.Name("bqt_nk");
        var created = await UiFlow.CreateAccountAsync(admin, bqt, UserRole.BQT);                    // 15
        Assert.Equal(HttpStatusCode.NoContent, (await UiFlow.PostAsync(admin, $"/api/tai-khoan/{cudanId}/vo-hieu-hoa")).StatusCode); // 16
        Assert.Equal(HttpStatusCode.NoContent, (await UiFlow.PostAsync(admin, $"/api/tai-khoan/{cudanId}/kich-hoat")).StatusCode);   // 17

        return new Flow(admin, mark, startedAt, adminId, cudan, cudanId, ketoan, ketoanId, bqt, created.Id, ghost);
    }

    private static string Iso(DateTimeOffset t) =>
        t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>0 giờ hôm nay theo giờ Việt Nam, đổi sang UTC: giống buildQuery trong audit-log.js.</summary>
    private static DateTimeOffset VnToday() =>
        new DateTimeOffset(DateTimeOffset.UtcNow.ToOffset(Vn).Date, Vn).ToUniversalTime();

    private static Task<PagedResult<AuditLogDto>> QueryAsync(HttpClient admin, string query) =>
        UiFlow.GetJsonAsync<PagedResult<AuditLogDto>>(admin, "/api/nhat-ky?" + query);

    [SkippableFact]
    [Trait("Acceptance", "D1")]
    public async Task D1_AllTwelveActions_AreLoggedWithActorTargetAndDetail()
    {
        SkipIfNoDatabase();
        var f = await RunAllActionsAsync();
        var logs = await Accounts.LogsAfterAsync(f.Mark);

        void Has(AuditAction action, string actor, int? target = null, string? detail = null) =>
            Assert.Contains(logs, l => l.Action == action && l.Username == actor
                && (target is null || l.Target == target.Value.ToString())
                && (detail is null || l.Detail == detail));

        Has(AuditAction.LoginSuccess, "admin", f.AdminId);
        Has(AuditAction.Register, f.Cudan, f.CudanId, "Vai trò CU_DAN, trạng thái Approved");
        Has(AuditAction.Register, f.Ketoan, f.KetoanId, "Vai trò KE_TOAN, trạng thái PendingApproval");
        Has(AuditAction.LoginFailed, f.Ketoan, detail: "Tài khoản đang chờ xác nhận");
        Has(AuditAction.ApproveAccount, "admin", f.KetoanId, $"Xác nhận tài khoản {f.Ketoan}");
        Has(AuditAction.LoginFailed, f.Cudan, detail: "Sai tư cách đăng nhập: chọn KE_TOAN");
        Has(AuditAction.LoginFailed, f.Ghost, detail: "Tài khoản không tồn tại hoặc bị vô hiệu");
        Has(AuditAction.LoginSuccess, f.Cudan, f.CudanId);
        Has(AuditAction.Logout, f.Cudan, f.CudanId);
        for (var i = 1; i <= 4; i++)
            Has(AuditAction.LoginFailed, f.Cudan, f.CudanId, $"Sai mật khẩu, lần {i}");
        Has(AuditAction.LoginFailed, f.Cudan, f.CudanId, "Sai mật khẩu, lần 0"); // lỗi #3 đã biết: lần thứ 5 ghi "lần 0"
        Assert.Contains(logs, l => l.Action == AuditAction.LoginLocked && l.Username == f.Cudan && l.Detail!.StartsWith("Khóa đến "));
        Has(AuditAction.UnlockAccount, "admin", f.CudanId, $"Mở khóa tài khoản {f.Cudan}");
        Has(AuditAction.ResetPassword, "admin", f.CudanId, $"Cấp lại mật khẩu cho {f.Cudan}");
        Has(AuditAction.ChangePassword, f.Cudan, f.CudanId);
        Has(AuditAction.CreateAccount, "admin", f.BqtId, $"Tạo tài khoản {f.Bqt}, vai trò BQT");
        Has(AuditAction.DisableAccount, "admin", f.CudanId, $"Tài khoản {f.Cudan}");
        Has(AuditAction.EnableAccount, "admin", f.CudanId, $"Tài khoản {f.Cudan}");

        // Bước 18: chọn từng giá trị ở ô Hành động, cả 12 loại đều có ít nhất 1 dòng
        foreach (var action in TwelveActions)
            Assert.True((await QueryAsync(f.Admin, $"action={action}&from={Iso(f.StartedAt)}")).Total >= 1, $"Không có dòng {action}");
    }

    [SkippableFact]
    [Trait("Acceptance", "D2")]
    public async Task D2_FilterByAction()
    {
        SkipIfNoDatabase();
        var f = await RunAllActionsAsync();
        var from = Iso(f.StartedAt);

        var failed = await QueryAsync(f.Admin, $"action=LoginFailed&from={from}&pageSize=100");
        Assert.All(failed.Items, i => Assert.Equal("LoginFailed", i.Action));
        Assert.Equal(8, failed.Total); // bước 4, 6, 7 và 5 lần ở bước 10

        Assert.Equal(1, (await QueryAsync(f.Admin, $"action=ResetPassword&from={from}")).Total);

        var all = await QueryAsync(f.Admin, $"from={from}&pageSize=100");
        Assert.Equal(TwelveActions.Select(a => a.ToString()).Order(), all.Items.Select(i => i.Action).Distinct().Order());
    }

    [SkippableFact]
    [Trait("Acceptance", "D3")]
    public async Task D3_FilterByUsername_ActorOnly_PartialMatch_CaseSensitive()
    {
        // Bước 5 (giao diện tự cắt khoảng trắng) là JavaScript: vẫn chạy tay
        SkipIfNoDatabase();
        var f = await RunAllActionsAsync();

        var cudanRows = await QueryAsync(f.Admin, $"username={f.Cudan}&pageSize=100");
        Assert.All(cudanRows.Items, i => Assert.Equal(f.Cudan, i.Username));
        // Ghi nhận J2: thao tác do admin làm trên cudan không hiện khi lọc theo cudan
        Assert.DoesNotContain(cudanRows.Items, i => i.Action is "UnlockAccount" or "ResetPassword" or "DisableAccount" or "EnableAccount");

        var partial = await QueryAsync(f.Admin, $"username={Uri.EscapeDataString(Accounts.Prefix[2..])}&pageSize=100"); // một phần tên
        Assert.Contains(partial.Items, i => i.Username == f.Cudan);
        Assert.Contains(partial.Items, i => i.Username == f.Ketoan);

        // Ghi nhận J2: phân biệt chữ hoa, chữ thường
        Assert.Equal(0, (await QueryAsync(f.Admin, $"username={f.Cudan.ToUpperInvariant()}")).Total);
    }

    [SkippableFact]
    [Trait("Acceptance", "D4")]
    public async Task D4_FilterByVietnamDate_ToDateIsInclusive()
    {
        // Ngày tính theo giờ Việt Nam: test làm lại đúng công thức của audit-log.js (buildQuery)
        SkipIfNoDatabase();
        var f = await RunAllActionsAsync();
        var today = VnToday();
        var tomorrow = today.AddDays(1);
        var mine = "username=" + Uri.EscapeDataString(Accounts.Prefix);

        var todayRows = await QueryAsync(f.Admin, $"{mine}&from={Iso(today)}&to={Iso(tomorrow)}");
        Assert.True(todayRows.Total > 0);

        Assert.Equal(0, (await QueryAsync(f.Admin, $"{mine}&from={Iso(today.AddDays(-1))}&to={Iso(today)}")).Total);  // hôm qua
        Assert.Equal(0, (await QueryAsync(f.Admin, $"{mine}&from={Iso(tomorrow)}")).Total);                           // từ ngày mai
        Assert.Equal(todayRows.Total, (await QueryAsync(f.Admin, $"{mine}&to={Iso(tomorrow)}")).Total);               // chỉ "đến hôm nay"

        // Ghi nhận J4: "Từ ngày" sau "Đến ngày" chỉ ra danh sách rỗng, không báo lỗi
        var reversed = await f.Admin.GetAsync($"/api/nhat-ky?{mine}&from={Iso(tomorrow)}&to={Iso(tomorrow)}");
        Assert.Equal(HttpStatusCode.OK, reversed.StatusCode);
        Assert.Equal(0, (await UiFlow.ReadOkJsonAsync<PagedResult<AuditLogDto>>(reversed)).Total);
    }

    [SkippableFact]
    [Trait("Acceptance", "D5")]
    public async Task D5_CombinedFilters_AllMustMatch()
    {
        // Bước 2 (nút Xóa lọc đưa form về trống) là JavaScript: vẫn chạy tay
        SkipIfNoDatabase();
        var f = await RunAllActionsAsync();
        var today = VnToday();

        var rows = await QueryAsync(f.Admin,
            $"action=LoginFailed&username={f.Cudan}&from={Iso(today)}&to={Iso(today.AddDays(1))}&pageSize=100");

        Assert.Equal(6, rows.Total); // bước 6 và 5 lần ở bước 10
        Assert.All(rows.Items, i =>
        {
            Assert.Equal("LoginFailed", i.Action);
            Assert.Equal(f.Cudan, i.Username);
        });
    }

    [SkippableFact]
    [Trait("Acceptance", "D6")]
    public async Task D6_Paging_20PerPage_NewestFirst()
    {
        // Nút Trước/Sau và dòng "Trang X / Y" là JavaScript: vẫn chạy tay
        SkipIfNoDatabase();
        var f = await RunAllActionsAsync();
        var expectedTotal = (await Accounts.LogsAfterAsync(f.Mark)).Count;
        Assert.True(expectedTotal > 20, $"Luồng D1 chỉ tạo {expectedTotal} dòng");

        var from = Iso(f.StartedAt);
        var page1 = await QueryAsync(f.Admin, $"from={from}&page=1&pageSize=20");
        var page2 = await QueryAsync(f.Admin, $"from={from}&page=2&pageSize=20");

        Assert.Equal(expectedTotal, page1.Total);
        Assert.Equal(20, page1.Items.Count);
        Assert.Equal(expectedTotal - 20, page2.Items.Count);
        Assert.Empty(page1.Items.Select(i => i.Id).Intersect(page2.Items.Select(i => i.Id)));

        var all = page1.Items.Concat(page2.Items).ToList();
        Assert.Equal(all.OrderByDescending(i => i.OccurredAt).ThenByDescending(i => i.Id).Select(i => i.Id), all.Select(i => i.Id));
    }

    [SkippableFact]
    [Trait("Acceptance", "D10")]
    public async Task D10_Page_IsReadOnly_DescriptionRecorded()
    {
        SkipIfNoDatabase();
        var page = await UiFlow.GetAsync(await AdminAsync(), "/quan-tri/nhat-ky");

        Assert.Contains("Nhật ký chỉ đọc, không thể sửa hoặc xóa.", page.Html);
        // Chỉ xét phần nội dung trang (bỏ layout và khung kết nối lại của Blazor)
        var content = Regex.Match(page.Html, "<section class=\"page\">(.*?)</section>", RegexOptions.Singleline).Groups[1].Value;
        var buttons = Regex.Matches(content, "<button[^>]*>([^<]*)</button>").Select(m => m.Groups[1].Value.Trim()).ToArray();
        // Chỉ có nút lọc và phân trang: không có nút sửa hay xóa nhật ký
        Assert.Equal(["Lọc", "Xóa lọc", "← Trước", "Sau →"], buttons);

        // J3 đã sửa: mô tả nhắc đủ các nhóm thao tác được ghi nhật ký
        Assert.Contains("Ghi nhận các thao tác quan trọng: tài khoản, hồ sơ căn hộ và hộ khẩu, khoản thu và đơn giá.", page.Html);
        Assert.DoesNotContain("Ghi nhận đăng nhập, đăng xuất, đăng ký và xác nhận tài khoản.", page.Html);
    }

    [SkippableFact]
    [Trait("Acceptance", "D11")]
    public async Task D11_ProfileActions_AreLogged_Filterable_AndLabelledInVietnamese()
    {
        SkipIfNoDatabase();
        var startedAt = DateTimeOffset.UtcNow;
        var admin = await AdminAsync();
        var code = "AT" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        try
        {
            // Tạo rồi sửa hồ sơ căn hộ và hồ sơ hộ khẩu, như trang Hồ sơ căn hộ gọi API (dữ liệu giả)
            var apartmentRequest = new CreateApartmentRequest
            {
                Code = code, Building = "Tòa AT", Floor = 5, Area = 70m, BedroomCount = 2, Status = "Đang ở"
            };
            var apartment = await ReadCreatedAsync<ApartmentProfileResponse>(await admin.PostAsJsonAsync("/api/can-ho", apartmentRequest, UiFlow.Json));
            await UiFlow.ReadOkJsonAsync<ApartmentProfileResponse>(await admin.PutAsJsonAsync($"/api/can-ho/{apartment.Id}",
                new UpdateApartmentRequest
                {
                    Code = code, Building = "Tòa AT", Floor = 5, Area = 72m, BedroomCount = 2, Status = "Đang ở", Notes = "Sửa ở D11"
                }, UiFlow.Json));

            var household = await ReadCreatedAsync<HouseholdProfileResponse>(await admin.PostAsJsonAsync($"/api/ho-gia-dinh/can-ho/{apartment.Id}",
                new CreateHouseholdRequest
                {
                    HouseholdNumber = code, HeadName = "Nguyễn Văn Thử", HouseNumber = "P501",
                    RegisteredAt = new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero), Residents = []
                }, UiFlow.Json));
            await UiFlow.ReadOkJsonAsync<HouseholdProfileResponse>(await admin.PutAsJsonAsync($"/api/ho-gia-dinh/{household.Id}",
                new UpdateHouseholdRequest
                {
                    HouseholdNumber = code, HeadName = "Nguyễn Văn Thử Sửa", HouseNumber = "P501",
                    RegisteredAt = household.RegisteredAt, Residents = []
                }, UiFlow.Json));

            // Lọc theo từng loại thao tác (như chọn ở ô Hành động) thì thấy đúng đối tượng vừa tạo/sửa
            (AuditAction Action, int Target)[] expected =
            [
                (AuditAction.CreateApartmentProfile, apartment.Id),
                (AuditAction.UpdateApartmentProfile, apartment.Id),
                (AuditAction.CreateHouseholdProfile, household.Id),
                (AuditAction.UpdateHouseholdProfile, household.Id)
            ];
            foreach (var (action, target) in expected)
            {
                var result = await QueryAsync(admin, $"action={action}&username={TestAccounts.AdminUsername}&from={Iso(startedAt)}&pageSize=100");
                Assert.All(result.Items, i => Assert.Equal(action.ToString(), i.Action));
                Assert.Contains(result.Items, i => i.Target == target.ToString(CultureInfo.InvariantCulture));
            }

            // Ô lọc Hành động có đủ mọi loại thao tác với nhãn tiếng Việt (bảng trên trang dùng chính các nhãn này)
            var page = await UiFlow.GetAsync(admin, "/quan-tri/nhat-ky");
            foreach (var (action, label) in AuditActionLabels.All)
                Assert.Contains($"<option value=\"{action}\">{label}</option>", page.Html);
        }
        finally
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Apartments.Where(a => a.Code == code).ExecuteDeleteAsync();
        }
    }

    private static async Task<T> ReadCreatedAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} trả {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, UiFlow.Json)!;
    }
}
