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
    private static readonly byte[] ProofBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");
    private const int PeriodId = 1;

    private readonly FakePaymentRepository _repo = new();
    private readonly FakeAuditService _audit = new();
    private readonly FakePaymentProofStore _proofs = new();
    private readonly PaymentService _service;

    public PaymentServiceTests()
    {
        _service = new PaymentService(_repo, _audit, _proofs, new FakeTimeProvider(Now));
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
        }, Proof());

        AssertInvalid(result, message);
        Assert.Empty(_repo.Payments);
    }

    [Fact]
    public async Task MissingPaidAt_OrSmallClockSkew()
    {
        var missing = await _service.RecordAsync(1, "ketoan", new RecordPaymentRequest
        {
            BillingPeriodId = PeriodId, HouseholdId = 1, Amount = 1_000m, Method = PaymentMethod.TienMat
        }, Proof());
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
        var unknownPeriod = await _service.RecordAsync(1, "ketoan", Request(404, 1, 1_000m, PaymentMethod.TienMat), Proof());
        Assert.Equal(FeeItemStatus.NotFound, unknownPeriod.Status);

        AssertInvalid(await _service.RecordAsync(1, "ketoan", Request(PeriodId, 99, 1_000m, PaymentMethod.TienMat), Proof()),
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
        await _service.RecordAsync(1, "ketoan", Request(PeriodId, 2, 914_975m, PaymentMethod.TienMat), Proof());

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

    // ===== Ảnh minh chứng =====

    [Fact]
    public async Task MissingProof_IsRejected_WithMessage()
    {
        var result = await _service.RecordAsync(1, "ketoan",
            Request(PeriodId, 1, 100_000m, PaymentMethod.TienMat), null);

        AssertInvalid(result, "Vui lòng đính kèm ảnh minh chứng khi ghi nhận lần nộp.");
        Assert.Empty(_repo.Payments);
        Assert.Empty(_proofs.Files);
    }

    public static TheoryData<string, string, long, string> InvalidProofs => new()
    {
        { "minh-chung.gif", "image/gif", ProofBytes.Length, "Ảnh minh chứng chỉ chấp nhận định dạng JPG, PNG hoặc WebP." },
        { "minh-chung.png", "image/jpeg", ProofBytes.Length, "Ảnh minh chứng không đúng định dạng JPG, PNG hoặc WebP." },
        { "minh-chung.png", "image/png", PaymentProofRules.MaxBytes + 1, "Ảnh minh chứng không được vượt quá 5 MB." }
    };

    [Theory]
    [MemberData(nameof(InvalidProofs))]
    public async Task InvalidProof_IsRejected_WithMessage(string fileName, string contentType, long length, string message)
    {
        var result = await _service.RecordAsync(1, "ketoan",
            Request(PeriodId, 1, 100_000m, PaymentMethod.TienMat), Proof(fileName, contentType, length));

        AssertInvalid(result, message);
        Assert.Empty(_repo.Payments);
        Assert.Empty(_proofs.Files);
    }

    [Fact]
    public async Task SuccessfulPayment_SavesProof_AndExposesDownloadUrl()
    {
        var result = await RecordAsync(100_000m, PaymentMethod.TienMat);

        Assert.Equal(FeeItemStatus.Success, result.Status);
        var payment = Assert.Single(_repo.Payments);
        Assert.NotNull(payment.ProofImagePath);
        Assert.EndsWith(".png", payment.ProofImagePath);
        Assert.True(_proofs.Files.ContainsKey(payment.ProofImagePath));
        Assert.Equal(PaymentService.ProofUrl(payment.Id), result.Data!.Payment.ProofImageUrl);
    }

    [Fact]
    public async Task GetProofAsync_ReturnsSavedImage_AndNullForPaymentsWithoutProof()
    {
        await RecordAsync(100_000m, PaymentMethod.TienMat);
        var payment = Assert.Single(_repo.Payments);

        var proof = await _service.GetProofAsync(payment.Id);

        Assert.NotNull(proof);
        Assert.Equal("image/png", proof.Value.ContentType);
        using (proof.Value.Stream)
        using (var buffer = new MemoryStream())
        {
            await proof.Value.Stream.CopyToAsync(buffer);
            Assert.Equal(ProofBytes, buffer.ToArray());
        }

        Assert.Null(await _service.GetProofAsync(999)); // không tồn tại

        _repo.Payments.Add(new PaymentEntity { Id = 900, ProofImagePath = null });
        Assert.Null(await _service.GetProofAsync(900)); // lần nộp cũ trước khi có ảnh
    }

    // ===== Hàm hỗ trợ =====

    private Task<FeeItemOperationResult<RecordPaymentResponse>> RecordAsync(
        decimal amount, PaymentMethod method, string? reference = null, string? payer = null, DateTimeOffset? paidAt = null) =>
        _service.RecordAsync(1, "ketoan", Request(PeriodId, 1, amount, method, reference, payer, paidAt), Proof());

    /// <summary>Ảnh PNG 1x1; có thể đổi tên/content-type/length để kiểm tra các trường hợp từ chối.</summary>
    private static PaymentProofUpload Proof(
        string fileName = "minh-chung.png", string contentType = "image/png", long? length = null) =>
        new(fileName, contentType, length ?? ProofBytes.Length, new MemoryStream(ProofBytes, writable: false));

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

    /// <summary>Kho ảnh trong bộ nhớ: chép nội dung, cấp tên ngẫu nhiên, mở stream mới và xóa khi được yêu cầu.</summary>
    private sealed class FakePaymentProofStore : IPaymentProofStore
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

        public Task<string> SaveAsync(PaymentProofUpload upload, CancellationToken ct = default)
        {
            using var buffer = new MemoryStream();
            upload.Content.CopyTo(buffer);
            var storedName = $"{Guid.NewGuid():N}.png";
            Files[storedName] = buffer.ToArray();
            return Task.FromResult(storedName);
        }

        public Task<(Stream Stream, string ContentType)?> OpenAsync(string storedName, CancellationToken ct = default) =>
            Files.TryGetValue(storedName, out var bytes)
                ? Task.FromResult<(Stream, string)?>((new MemoryStream(bytes, writable: false), "image/png"))
                : Task.FromResult<(Stream, string)?>(null);

        public Task DeleteAsync(string storedName, CancellationToken ct = default)
        {
            Files.Remove(storedName);
            return Task.CompletedTask;
        }
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

        public Task<string?> GetProofPathAsync(int paymentId, CancellationToken ct = default) =>
            Task.FromResult(Payments.FirstOrDefault(p => p.Id == paymentId)?.ProofImagePath);

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
