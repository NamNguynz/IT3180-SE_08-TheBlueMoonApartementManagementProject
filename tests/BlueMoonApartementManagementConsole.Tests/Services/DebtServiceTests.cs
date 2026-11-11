using BlueMoonApartementManagementConsole.BackEnd.Models;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using BlueMoonApartementManagementConsole.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace BlueMoonApartementManagementConsole.Tests.Services;

/// <summary>
/// Unit test danh sách hộ chưa đóng và nhắc nộp (PBI-019): chỉ hộ còn thiếu &gt; 0, trạng thái, quá hạn,
/// lưu vết lần nhắc kèm số còn thiếu lúc nhắc. Test chấp nhận độc lập do Tester làm riêng.
/// </summary>
[Trait("PBI", "PBI-019")]
public sealed class DebtServiceTests
{
    private const int October = 1, November = 2;

    private readonly FakeDebtRepository _repo = new();
    private readonly FakeAuditService _audit = new();
    private readonly DebtService _service;

    public DebtServiceTests()
    {
        // FakeTimeProvider mặc định múi giờ UTC, nên "hôm nay" của service là 12/11/2026
        _service = new DebtService(_repo, _audit, new FakeTimeProvider(new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero)));

        // Kỳ tháng 10 hạn 28/10 (đã quá hạn 15 ngày), kỳ tháng 11 hạn 30/11 (chưa đến hạn)
        _repo.AddDue(October, 2026, 10, new DateOnly(2026, 10, 28), 1, "HK-A", "A-501", "Tòa A", 500_000m);
        _repo.AddDue(October, 2026, 10, new DateOnly(2026, 10, 28), 2, "HK-B", "B-1203", "Tòa B", 600_000m);
        _repo.AddDue(November, 2026, 11, new DateOnly(2026, 11, 30), 1, "HK-A", "A-501", "Tòa A", 500_000m);
        _repo.AddDue(November, 2026, 11, new DateOnly(2026, 11, 30), 2, "HK-B", "B-1203", "Tòa B", 600_000m);
        _repo.Paid.Add(new DebtPaidRow(October, 2, 600_000m));   // HK-B đã nộp đủ tháng 10
        _repo.Paid.Add(new DebtPaidRow(November, 1, 200_000m));  // HK-A nộp một phần tháng 11
    }

    // ===== Danh sách công nợ =====

    [Fact]
    public async Task List_HasOnlyHouseholdsStillOwing_WithStatusOverdueAndTotals()
    {
        var list = (await _service.GetDebtsAsync(null))!;

        // Nợ lâu nhất lên trước; HK-B tháng 10 đã nộp đủ nên không có trong danh sách
        Assert.Equal(new[] { "10:HK-A", "11:HK-A", "11:HK-B" }, list.Rows.Select(r => $"{r.Month}:{r.HouseholdNumber}"));

        var octoberA = list.Rows[0];
        Assert.Equal((500_000m, PaymentStatus.ChuaNop, true, 15), (octoberA.Remaining, octoberA.Status, octoberA.IsOverdue, octoberA.DaysOverdue));
        var novemberA = list.Rows[1];
        Assert.Equal((300_000m, PaymentStatus.NopMotPhan, false, 0), (novemberA.Remaining, novemberA.Status, novemberA.IsOverdue, novemberA.DaysOverdue));

        Assert.Equal((2, 1_400_000m, 1, 500_000m),
            (list.HouseholdCount, list.TotalRemaining, list.OverdueHouseholdCount, list.OverdueRemaining));
    }

    [Fact]
    public async Task List_FiltersByPeriod_AndUnknownPeriodIsNull()
    {
        var november = (await _service.GetDebtsAsync(November))!;

        Assert.All(november.Rows, r => Assert.Equal(11, r.Month));
        Assert.Equal(2, november.Rows.Count);
        Assert.Null(await _service.GetDebtsAsync(404));
    }

    [Theory]
    [InlineData(2026, 11, 12, false, 0)] // hạn nộp là hôm nay: chưa quá hạn
    [InlineData(2026, 11, 11, true, 1)]
    [InlineData(2026, 11, 13, false, 0)]
    public async Task Overdue_StartsTheDayAfterTheDueDate(int year, int month, int day, bool overdue, int days)
    {
        _repo.AddDue(3, 2026, 12, new DateOnly(year, month, day), 9, "HK-Z", "Z-1", "Tòa Z", 100_000m);

        var row = Assert.Single((await _service.GetDebtsAsync(3))!.Rows);

        Assert.Equal((overdue, days), (row.IsOverdue, row.DaysOverdue));
    }

    // ===== Nhắc nộp =====

    [Fact]
    public async Task RecordReminders_KeepsRemainingAtThatTime_AuditsEachHousehold_AndShowsInList()
    {
        var result = await _service.RecordRemindersAsync(1, "ketoan", new RecordRemindersRequest
        {
            Items = [new(October, 1), new(November, 1)],
            Channel = ReminderChannel.GiayBao,
            Note = "  Đã gửi giấy báo vào hộp thư  "
        });

        Assert.Equal(FeeItemStatus.Success, result.Status);
        Assert.Equal(new[] { 500_000m, 300_000m }, result.Data!.Select(r => r.Remaining));
        Assert.All(_repo.Reminders, r => Assert.Equal("Đã gửi giấy báo vào hộp thư", r.Note));

        Assert.Equal(2, _audit.Entries.Count);
        Assert.All(_audit.Entries, e => Assert.Equal(AuditAction.RecordDebtReminder, e.Action));
        Assert.Equal("Nhắc nộp kỳ thu tháng 10/2026: hộ HK-A, còn thiếu 500.000 đ (Giấy báo)", _audit.Entries[0].Detail);

        var row = (await _service.GetDebtsAsync(October))!.Rows.Single();
        Assert.Equal(1, row.ReminderCount);
        Assert.NotNull(row.LastRemindedAt);

        var history = Assert.Single(await _service.GetRemindersAsync(October, 1));
        Assert.Equal(("HK-A", ReminderChannel.GiayBao, "ketoan"), (history.HouseholdNumber, history.Channel, history.RemindedBy));
    }

    [Fact]
    public async Task RemindingAFullyPaidHousehold_RejectsTheWholeRequest()
    {
        var result = await _service.RecordRemindersAsync(1, "ketoan", new RecordRemindersRequest
        {
            Items = [new(October, 1), new(October, 2)],
            Channel = ReminderChannel.DienThoai
        });

        AssertInvalid(result, "Hộ HK-B đã nộp đủ kỳ thu tháng 10/2026, không cần nhắc.");
    }

    public static TheoryData<RecordRemindersRequest, string> InvalidRequests => new()
    {
        { new() { Items = [new(October, 1)] }, "Hãy chọn cách nhắc nộp." },
        { new() { Items = [], Channel = ReminderChannel.GiayBao }, "Hãy chọn ít nhất một hộ cần nhắc." },
        { new() { Items = [new(October, 1), new(October, 1)], Channel = ReminderChannel.GiayBao }, "Danh sách hộ cần nhắc bị trùng." },
        { new() { Items = Enumerable.Range(1, 501).Select(i => new DebtReminderTarget(October, i)).ToList(), Channel = ReminderChannel.GiayBao },
            "Mỗi lần ghi nhận tối đa 500 hộ." },
        { new() { Items = [new(October, 99)], Channel = ReminderChannel.GiayBao }, "Hộ #99 không có khoản phải thu trong kỳ thu tháng 10/2026." },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task InvalidReminderRequest_IsRejected_WithMessage(RecordRemindersRequest request, string message)
    {
        AssertInvalid(await _service.RecordRemindersAsync(1, "ketoan", request), message);
    }

    [Fact]
    public async Task UnknownPeriod_IsNotFound()
    {
        var result = await _service.RecordRemindersAsync(1, "ketoan", new RecordRemindersRequest
        {
            Items = [new(404, 1)],
            Channel = ReminderChannel.GiayBao
        });

        Assert.Equal(FeeItemStatus.NotFound, result.Status);
        Assert.Empty(_repo.Reminders);
    }

    [Fact]
    public void Labels_CoverEveryChannel_Once()
    {
        Assert.Equal(Enum.GetValues<ReminderChannel>().Order(), DebtLabels.Channels.Select(x => x.Channel).Order());
    }

    // ===== Hàm hỗ trợ =====

    private void AssertInvalid(FeeItemOperationResult<IReadOnlyList<ReminderResponse>> result, string message)
    {
        Assert.Equal(FeeItemStatus.Invalid, result.Status);
        Assert.Equal(message, result.Message);
        Assert.Empty(_repo.Reminders);
        Assert.Empty(_audit.Entries);
    }

    /// <summary>Kho trong bộ nhớ; SaveChangesAsync cấp Id và gắn navigation như EF khi đọc lại.</summary>
    private sealed class FakeDebtRepository : IDebtRepository
    {
        private readonly List<DebtReminderEntity> _pending = [];

        public List<DebtDueRow> Dues { get; } = [];
        public List<DebtPaidRow> Paid { get; } = [];
        public List<DebtReminderEntity> Reminders { get; } = [];

        public void AddDue(int periodId, int year, int month, DateOnly dueDate,
            int householdId, string number, string apartment, string building, decimal amount) =>
            Dues.Add(new DebtDueRow(periodId, year, month, dueDate, householdId, number, apartment, building, amount));

        public Task<bool> PeriodExistsAsync(int periodId, CancellationToken ct = default) =>
            Task.FromResult(Dues.Any(d => d.BillingPeriodId == periodId));

        public Task<List<DebtDueRow>> GetDuesAsync(int? periodId, CancellationToken ct = default) =>
            Task.FromResult(Dues.Where(d => periodId is null || d.BillingPeriodId == periodId).ToList());

        public Task<List<DebtPaidRow>> GetPaidAsync(int? periodId, CancellationToken ct = default) =>
            Task.FromResult(Paid.Where(p => periodId is null || p.BillingPeriodId == periodId).ToList());

        public Task<List<ReminderSummaryRow>> GetReminderSummariesAsync(int? periodId, CancellationToken ct = default) =>
            Task.FromResult(Reminders
                .Where(r => periodId is null || r.BillingPeriodId == periodId)
                .GroupBy(r => (r.BillingPeriodId, r.HouseholdId))
                .Select(g => new ReminderSummaryRow(g.Key.BillingPeriodId, g.Key.HouseholdId, g.Max(r => r.RemindedAt), g.Count()))
                .ToList());

        public Task<List<DebtReminderEntity>> GetRemindersAsync(int periodId, int householdId, CancellationToken ct = default) =>
            Task.FromResult(Reminders.Where(r => r.BillingPeriodId == periodId && r.HouseholdId == householdId)
                .OrderByDescending(r => r.RemindedAt).ThenByDescending(r => r.Id).ToList());

        public Task AddRemindersAsync(IEnumerable<DebtReminderEntity> reminders, CancellationToken ct = default)
        {
            _pending.AddRange(reminders);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            foreach (var reminder in _pending)
            {
                var due = Dues.First(d => d.BillingPeriodId == reminder.BillingPeriodId && d.HouseholdId == reminder.HouseholdId);
                reminder.Id = Reminders.Count + 1;
                reminder.BillingPeriod = new BillingPeriodEntity { Id = due.BillingPeriodId, Year = due.Year, Month = due.Month };
                reminder.Household = new HouseholdEntity { Id = due.HouseholdId, HouseholdNumber = due.HouseholdNumber };
                reminder.RemindedBy = new AppUserEntity { Id = reminder.RemindedById, Username = "ketoan" };
                Reminders.Add(reminder);
            }
            _pending.Clear();
            return Task.CompletedTask;
        }
    }
}
