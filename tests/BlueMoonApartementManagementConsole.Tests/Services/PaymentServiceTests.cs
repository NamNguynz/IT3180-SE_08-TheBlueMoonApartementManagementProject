using BlueMoonApartementManagementConsole.BackEnd.Models;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;
using BlueMoonApartementManagementConsole.BackEnd.Services;
using BlueMoonApartementManagementConsole.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;

namespace BlueMoonApartementManagementConsole.Tests.Services;

/// <summary>
/// Unit test ghi nhận thu tiền, nộp một phần (PBI-016): cộng dồn, số còn thiếu, trạng thái, từ chối dữ liệu sai
/// mà không thay đổi số đã thu (AC-018 – AC-020). Test chấp nhận độc lập (WBS 4.16.03) do Tester làm riêng.
/// </summary>
[Trait("PBI", "PBI-016")]
public sealed class PaymentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
    private const int PeriodId = 1;

    private readonly FakePaymentRepository _repo = new();
    private readonly FakeAuditService _audit = new();
    private readonly PaymentService _service;

    public PaymentServiceTests()
    {
        _service = new PaymentService(_repo, _audit, new FakeTimeProvider(Now));
        _repo.Periods[PeriodId] = new BillingPeriodEntity
        {
            Id = PeriodId, Year = 2026, Month = 11, ChargeDate = new DateOnly(2026, 11, 12), DueDate = new DateOnly(2026, 11, 30)
        };
        _repo.Dues.Add(new HouseholdDueRow(1, "HK-0501", "A-501", "Tòa A", 701_113m));
        _repo.Dues.Add(new HouseholdDueRow(2, "HK-1203", "B-1203", "Tòa B", 914_975m));
    }

    // ===== Nộp một phần, cộng dồn, nộp đủ =====

    [Fact]
    public async Task PartialPayments_AddUp_UpdateRemainingAndStatus_ThenFullyPaid()
    {
        var first = await RecordAsync(500_000m, PaymentMethod.TienMat, payer: "Nguyễn Văn An");

        Assert.Equal(FeeItemStatus.Success, first.Status);
        Assert.Equal("BN-000001", first.Data!.Payment.ReceiptNumber);
        Assert.Equal((500_000m, 201_113m, PaymentStatus.NopMotPhan),
            (first.Data.Bill.AmountPaid, first.Data.Bill.Remaining, first.Data.Bill.Status));

        var log = Assert.Single(_audit.Entries);
        Assert.Equal(AuditAction.RecordPayment, log.Action);
        Assert.Equal("1", log.Target);
        Assert.Equal("Biên nhận BN-000001: hộ HK-0501, kỳ thu tháng 11/2026, 500.000 đ (Tiền mặt), còn thiếu 201.113 đ", log.Detail);

        var second = await RecordAsync(201_113m, PaymentMethod.ChuyenKhoan, reference: "FT26316123456");

        Assert.Equal(FeeItemStatus.Success, second.Status);
        Assert.Equal((701_113m, 0m, PaymentStatus.DaNopDu),
            (second.Data!.Bill.AmountPaid, second.Data.Bill.Remaining, second.Data.Bill.Status));
        Assert.Equal(new[] { "BN-000001", "BN-000002" }, second.Data.Bill.Payments.Select(p => p.ReceiptNumber));

        AssertInvalid(await RecordAsync(1m, PaymentMethod.TienMat), "Hộ HK-0501 đã nộp đủ kỳ thu tháng 11/2026.");
        Assert.Equal(2, _repo.Payments.Count);
    }

    [Fact]
    public async Task Overpayment_IsRejected_AndNothingChanges()
    {
        await RecordAsync(500_000m, PaymentMethod.TienMat);

        AssertInvalid(await RecordAsync(201_114m, PaymentMethod.TienMat),
            "Số tiền nộp vượt số còn thiếu (201.113 đ). Không nhận nộp thừa.");
        Assert.Single(_repo.Payments);
        Assert.Single(_audit.Entries);
    }

    // ===== Từ chối dữ liệu không hợp lệ, không đổi số đã thu (AC-019) =====

    public static TheoryData<decimal, PaymentMethod?, string?, int, string> InvalidInputs => new()
    {
        { 100_000m, null, null, 0, "Hãy chọn phương thức thanh toán." },
        { 0m, PaymentMethod.TienMat, null, 0, "Số tiền nộp phải lớn hơn 0." },
        { -5_000m, PaymentMethod.TienMat, null, 0, "Số tiền nộp phải lớn hơn 0." },
        { 1_000.5m, PaymentMethod.TienMat, null, 0, "Số tiền nộp phải là số đồng nguyên." },
        { 100_000m, PaymentMethod.TienMat, null, 60, "Thời điểm nhận tiền không được sau thời điểm hiện tại." },
        { 100_000m, PaymentMethod.ChuyenKhoan, "   ", 0, "Chuyển khoản phải có mã giao dịch hoặc nội dung chuyển khoản để đối chiếu." },
    };

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task InvalidInput_IsRejected_WithMessage(
        decimal amount, PaymentMethod? method, string? reference, int minutesInFuture, string message)
    {
        var result = await _service.RecordAsync(1, "ketoan", new RecordPaymentRequest
        {
            BillingPeriodId = PeriodId,
            HouseholdId = 1,
            Amount = amount,
            PaidAt = Now.AddMinutes(minutesInFuture),
            Method = method,
            Reference = reference
        });

        AssertInvalid(result, message);
        Assert.Empty(_repo.Payments);
    }

    [Fact]
    public async Task MissingPaidAt_OrSmallClockSkew()
    {
        var missing = await _service.RecordAsync(1, "ketoan", new RecordPaymentRequest
        {
            BillingPeriodId = PeriodId, HouseholdId = 1, Amount = 1_000m, Method = PaymentMethod.TienMat
        });
        AssertInvalid(missing, "Hãy nhập thời điểm nhận tiền.");

        // Máy Kế toán nhanh hơn máy chủ vài phút vẫn được nhận
        var skewed = await RecordAsync(1_000m, PaymentMethod.TienMat, paidAt: Now.AddMinutes(3));
        Assert.Equal(FeeItemStatus.Success, skewed.Status);
    }

    [Fact]
    public async Task PaidAtInVietnamTime_IsStoredAsUtc_SameInstant()
    {
        // Cột timestamptz của PostgreSQL (Npgsql) chỉ nhận offset 0; 15:30 giờ Việt Nam = 08:30 UTC
        var vietnamTime = new DateTimeOffset(2026, 11, 12, 15, 30, 0, TimeSpan.FromHours(7));

        var result = await RecordAsync(1_000m, PaymentMethod.TienMat, paidAt: vietnamTime);

        Assert.Equal(FeeItemStatus.Success, result.Status);
        var saved = Assert.Single(_repo.Payments).PaidAt;
        Assert.Equal(TimeSpan.Zero, saved.Offset);
        Assert.Equal(vietnamTime, saved); // cùng một thời điểm
    }

    [Fact]
    public async Task UnknownPeriod_OrHouseholdWithoutReceivable_IsRejected()
    {
        var unknownPeriod = await _service.RecordAsync(1, "ketoan", Request(404, 1, 1_000m, PaymentMethod.TienMat));
        Assert.Equal(FeeItemStatus.NotFound, unknownPeriod.Status);

        AssertInvalid(await _service.RecordAsync(1, "ketoan", Request(PeriodId, 99, 1_000m, PaymentMethod.TienMat)),
            "Hộ này không có khoản phải thu trong kỳ thu tháng 11/2026.");
        Assert.Empty(_repo.Payments);
    }

    [Theory]
    [InlineData(PaymentSaveResult.ExceedsDue)]
    [InlineData(PaymentSaveResult.Conflict)]
    public async Task ConcurrentPaymentDetectedWhenSaving_IsAConflict_AndNotAudited(PaymentSaveResult saveResult)
    {
        _repo.NextSaveResult = saveResult;

        var result = await RecordAsync(100_000m, PaymentMethod.TienMat);

        Assert.Equal(FeeItemStatus.Conflict, result.Status);
        Assert.StartsWith("Vừa có lần nộp khác được ghi cho hộ này", result.Message);
        Assert.Empty(_repo.Payments);
        Assert.Empty(_audit.Entries);
    }

    // ===== Tình hình thu của kỳ =====

    [Fact]
    public async Task Period_ShowsEachHouseholdStatus_AndTotals()
    {
        await RecordAsync(500_000m, PaymentMethod.TienMat);
        await _service.RecordAsync(1, "ketoan", Request(PeriodId, 2, 914_975m, PaymentMethod.TienMat));

        var period = (await _service.GetPeriodAsync(PeriodId))!;

        Assert.Equal(new[] { PaymentStatus.NopMotPhan, PaymentStatus.DaNopDu }, period.Households.Select(h => h.Status));
        Assert.Equal((1_616_088m, 1_414_975m, 201_113m), (period.AmountDue, period.AmountPaid, period.Remaining));
        Assert.Null(await _service.GetPeriodAsync(404));
        Assert.Null(await _service.GetHouseholdAsync(PeriodId, 99));
    }

    [Theory]
    [InlineData(100, 0, PaymentStatus.ChuaNop)]
    [InlineData(100, 1, PaymentStatus.NopMotPhan)]
    [InlineData(100, 99, PaymentStatus.NopMotPhan)]
    [InlineData(100, 100, PaymentStatus.DaNopDu)]
    [InlineData(0, 0, PaymentStatus.DaNopDu)] // không phải nộp gì thì là đã đủ, khớp với RecordAsync
    public void StatusOf_DueAndPaid(decimal due, decimal paid, PaymentStatus expected)
    {
        Assert.Equal(expected, PaymentService.StatusOf(due, paid));
    }

    [Fact]
    public void Labels_CoverEveryMethodAndStatus_Once()
    {
        Assert.Equal(Enum.GetValues<PaymentMethod>().Order(), PaymentLabels.Methods.Select(x => x.Method).Order());
        Assert.Equal(Enum.GetValues<PaymentStatus>().Order(), PaymentLabels.Statuses.Select(x => x.Status).Order());
    }

    // ===== Hàm hỗ trợ =====

    private Task<FeeItemOperationResult<RecordPaymentResponse>> RecordAsync(
        decimal amount, PaymentMethod method, string? reference = null, string? payer = null, DateTimeOffset? paidAt = null) =>
        _service.RecordAsync(1, "ketoan", Request(PeriodId, 1, amount, method, reference, payer, paidAt));

    private static RecordPaymentRequest Request(int periodId, int householdId, decimal amount, PaymentMethod method,
        string? reference = null, string? payer = null, DateTimeOffset? paidAt = null) => new()
    {
        BillingPeriodId = periodId,
        HouseholdId = householdId,
        Amount = amount,
        PaidAt = paidAt ?? Now.AddMinutes(-30),
        Method = method,
        Reference = reference,
        PayerName = payer
    };

    private void AssertInvalid(FeeItemOperationResult<RecordPaymentResponse> result, string message)
    {
        Assert.Equal(FeeItemStatus.Invalid, result.Status);
        Assert.Equal(message, result.Message);
    }

    /// <summary>Kho trong bộ nhớ; AddAsync kiểm tổng đã nộp như repository thật và cấp Id, người ghi nhận.</summary>
    private sealed class FakePaymentRepository : IPaymentRepository
    {
        public Dictionary<int, BillingPeriodEntity> Periods { get; } = [];
        public List<HouseholdDueRow> Dues { get; } = [];
        public List<PaymentEntity> Payments { get; } = [];

        /// <summary>Giả lập người khác vừa ghi trong lúc lưu (transaction bị từ chối).</summary>
        public PaymentSaveResult? NextSaveResult { get; set; }

        public Task<BillingPeriodEntity?> GetPeriodAsync(int periodId, CancellationToken ct = default) =>
            Task.FromResult(Periods.GetValueOrDefault(periodId));

        public Task<List<HouseholdDueRow>> GetDuesAsync(int periodId, int? householdId, CancellationToken ct = default) =>
            Task.FromResult(Periods.ContainsKey(periodId)
                ? Dues.Where(d => householdId is null || d.HouseholdId == householdId).ToList()
                : []);

        public Task<List<HouseholdPaidRow>> GetPaidAsync(int periodId, int? householdId, CancellationToken ct = default) =>
            Task.FromResult(Payments
                .Where(p => p.BillingPeriodId == periodId && (householdId is null || p.HouseholdId == householdId))
                .GroupBy(p => p.HouseholdId)
                .Select(g => new HouseholdPaidRow(g.Key, g.Sum(p => p.Amount), g.Count()))
                .ToList());

        public Task<List<BillLineRow>> GetBillLinesAsync(int periodId, int householdId, CancellationToken ct = default) =>
            Task.FromResult(new List<BillLineRow> { new("PHI_DV", "Phí dịch vụ", 507_500m) });

        public Task<List<PaymentEntity>> GetPaymentsAsync(int periodId, int householdId, CancellationToken ct = default) =>
            Task.FromResult(Payments.Where(p => p.BillingPeriodId == periodId && p.HouseholdId == householdId)
                .OrderBy(p => p.PaidAt).ThenBy(p => p.Id).ToList());

        public Task<PaymentSaveResult> AddAsync(PaymentEntity payment, decimal amountDue, CancellationToken ct = default)
        {
            if (NextSaveResult is { } forced)
                return Task.FromResult(forced);

            var paid = Payments.Where(p => p.BillingPeriodId == payment.BillingPeriodId && p.HouseholdId == payment.HouseholdId)
                .Sum(p => p.Amount);
            if (paid + payment.Amount > amountDue)
                return Task.FromResult(PaymentSaveResult.ExceedsDue);

            payment.Id = Payments.Count + 1;
            payment.RecordedBy = new AppUserEntity { Id = payment.RecordedById, Username = "ketoan" };
            Payments.Add(payment);
            return Task.FromResult(PaymentSaveResult.Saved);
        }
    }
}
