using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using BlueMoonApartementManagementConsole.Tests.Fakes;

namespace BlueMoonApartementManagementConsole.Tests.Services;

/// <summary>
/// Unit test AuditService: chuẩn hóa dữ liệu khi ghi log, giới hạn tham số phân trang và chuyển sang DTO khi tra cứu.
/// Việc lọc, sắp xếp, phân trang thật trên database được kiểm ở Integration/AuditLogQueryTests.
/// </summary>
public class AuditServiceTests
{
    private readonly FakeAuditRepository _repo = new();
    private readonly AuditService _service;

    public AuditServiceTests()
    {
        _service = new AuditService(_repo);
    }

    private static AuditQuery Query(int page = 1, int pageSize = 20) =>
        new(From: null, To: null, Action: null, Username: null, Page: page, PageSize: pageSize);

    // ===== N1: Ghi log =====

    [Fact]
    [Trait("TestId", "N1")]
    public async Task Log_StoresAllFields_TrimsUsername_SavesOnce()
    {
        var before = DateTimeOffset.UtcNow;

        await _service.LogAsync(AuditAction.ResetPassword, "  admin  ", userId: 1, target: "7", detail: "Cấp lại mật khẩu cho cudan01");

        var after = DateTimeOffset.UtcNow;
        var log = Assert.Single(_repo.Added);
        Assert.Equal(AuditAction.ResetPassword, log.Action);
        Assert.Equal("admin", log.Username);
        Assert.Equal(1, log.UserId);
        Assert.Equal("7", log.Target);
        Assert.Equal("Cấp lại mật khẩu cho cudan01", log.Detail);
        Assert.Equal(1, _repo.SaveChangesCount);

        Assert.Equal(TimeSpan.Zero, log.OccurredAt.Offset); // lưu theo UTC
        Assert.InRange(log.OccurredAt, before, after);
    }

    [Theory]
    [Trait("TestId", "N1")]
    [InlineData(49, 49)]
    [InlineData(50, 50)]
    [InlineData(51, 50)]
    [InlineData(80, 50)]
    public async Task Log_UsernameLongerThan50_IsCutTo50(int length, int expectedLength)
    {
        // Cột Username trong DB tối đa 50 ký tự; người đăng nhập sai có thể gõ tên dài tùy ý
        var name = new string('a', length);

        await _service.LogAsync(AuditAction.LoginFailed, name);

        var log = Assert.Single(_repo.Added);
        Assert.Equal(expectedLength, log.Username.Length);
        Assert.StartsWith(log.Username, name);
    }

    [Fact]
    [Trait("TestId", "N1")]
    public async Task Log_TrimsBeforeCutting()
    {
        var name = "   " + new string('b', 50) + "   ";

        await _service.LogAsync(AuditAction.LoginFailed, name);

        Assert.Equal(new string('b', 50), Assert.Single(_repo.Added).Username);
    }

    // ===== N2: Tên tài khoản null =====

    [Fact]
    [Trait("TestId", "N2")]
    public async Task Log_NullUsername_StoresEmptyString()
    {
        await _service.LogAsync(AuditAction.LoginFailed, null!);

        var log = Assert.Single(_repo.Added);
        Assert.Equal(string.Empty, log.Username);
        Assert.Equal(1, _repo.SaveChangesCount);
    }

    // ===== N3: Giới hạn tham số phân trang =====

    [Theory]
    [Trait("TestId", "N3")]
    [InlineData(1, 20, 1, 20)]       // mặc định của giao diện
    [InlineData(0, 20, 1, 20)]       // trang nhỏ hơn 1
    [InlineData(-3, 20, 1, 20)]
    [InlineData(2, 20, 2, 20)]
    [InlineData(1, 0, 1, 10)]        // số dòng nhỏ hơn 10
    [InlineData(1, 5, 1, 10)]
    [InlineData(1, 10, 1, 10)]       // biên dưới
    [InlineData(1, 100, 1, 100)]     // biên trên
    [InlineData(1, 101, 1, 100)]
    [InlineData(1, 1000, 1, 100)]    // số dòng lớn hơn 100
    public async Task Query_ClampsPageAndPageSize(int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        var result = await _service.QueryAsync(Query(page, pageSize));

        Assert.Equal(expectedPage, _repo.LastQuery!.Page);
        Assert.Equal(expectedPageSize, _repo.LastQuery.PageSize);
        Assert.Equal(expectedPage, result.Page);
        Assert.Equal(expectedPageSize, result.PageSize);
    }

    [Fact]
    [Trait("TestId", "N3")]
    public async Task Query_PassesFiltersToRepositoryUnchanged()
    {
        var from = new DateTimeOffset(2026, 1, 1, 17, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 2, 17, 0, 0, TimeSpan.Zero);

        await _service.QueryAsync(new AuditQuery(from, to, AuditAction.Logout, "adm", Page: 0, PageSize: 1000));

        var sent = _repo.LastQuery!;
        Assert.Equal(from, sent.From);
        Assert.Equal(to, sent.To);
        Assert.Equal(AuditAction.Logout, sent.Action);
        Assert.Equal("adm", sent.Username);
    }

    // ===== N4: Chuyển sang DTO =====

    [Fact]
    [Trait("TestId", "N4")]
    public async Task Query_MapsToDto_KeepsOrder_AndTotal()
    {
        var newer = new AuditLog
        {
            Id = 12, OccurredAt = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero),
            UserId = 1, Username = "admin", Action = AuditAction.UnlockAccount, Target = "7", Detail = "Mở khóa tài khoản cudan01"
        };
        var older = new AuditLog
        {
            Id = 3, OccurredAt = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero),
            UserId = null, Username = "khongton", Action = AuditAction.LoginFailed, Target = null, Detail = null
        };
        _repo.ItemsToReturn = [newer, older];
        _repo.TotalToReturn = 42;

        var result = await _service.QueryAsync(Query(page: 3, pageSize: 20));

        Assert.Equal(42, result.Total);
        Assert.Equal(3, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Collection(result.Items,
            first =>
            {
                Assert.Equal(12, first.Id);
                Assert.Equal(newer.OccurredAt, first.OccurredAt);
                Assert.Equal("admin", first.Username);
                Assert.Equal("UnlockAccount", first.Action); // tên dạng chữ, không phải số
                Assert.Equal("7", first.Target);
                Assert.Equal("Mở khóa tài khoản cudan01", first.Detail);
            },
            second =>
            {
                Assert.Equal(3, second.Id);
                Assert.Equal("LoginFailed", second.Action);
                Assert.Null(second.Target);
                Assert.Null(second.Detail);
            });
    }

    [Fact]
    [Trait("TestId", "N4")]
    public async Task Query_NoResults_ReturnsEmptyItems_WithTotal()
    {
        _repo.TotalToReturn = 5; // trang vượt quá số trang thực tế: không có dòng nhưng vẫn biết tổng

        var result = await _service.QueryAsync(Query(page: 9));

        Assert.Empty(result.Items);
        Assert.Equal(5, result.Total);
    }
}
