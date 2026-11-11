using System.Globalization;
using BlueMoonApartementManagementConsole.BackEnd.Models;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;

namespace BlueMoonApartementManagementConsole.BackEnd.Services;

public interface IPaymentService
{
    Task<PeriodCollectionResponse?> GetPeriodAsync(int periodId, CancellationToken ct = default);
    Task<HouseholdPaymentDetailResponse?> GetHouseholdAsync(int periodId, int householdId, CancellationToken ct = default);
    Task<FeeItemOperationResult<RecordPaymentResponse>> RecordAsync(
        int actorId, string actorName, RecordPaymentRequest request, CancellationToken ct = default);
}

/// <summary>
/// Ghi nhận thu tiền thực tế, hỗ trợ nộp một phần (PBI-016, US-016, AC-018 – AC-020, OBS-002, OBS-003).
///
/// Số phải thu của một hộ trong kỳ = tổng các dòng phải thu của bảng kê (PBI-014). Từ đó:
///   đã thu    = tổng các lần nộp đã ghi;
///   còn thiếu = phải thu − đã thu;
///   trạng thái: chưa nộp (đã thu = 0), nộp một phần (0 &lt; đã thu &lt; phải thu), đã nộp đủ (đã thu = phải thu).
/// Ba số này tính lại mỗi lần đọc, không lưu riêng, nên luôn khớp với các lần nộp.
///
/// Cách ghi nhận một lần nộp (RecordAsync):
///   1. Kiểm tra dữ liệu nhập: phương thức, số tiền &gt; 0 và là đồng nguyên, thời điểm nhận không sau hiện tại,
///      chuyển khoản phải có mã giao dịch / nội dung để đối chiếu.
///   2. Kiểm tra khoản phải thu: kỳ thu tồn tại, hộ có khoản phải thu trong kỳ, chưa nộp đủ,
///      số tiền không vượt số còn thiếu (không nhận nộp thừa).
///   3. Lưu trong transaction Serializable (đọc lại tổng đã nộp ngay trước khi thêm), rồi ghi nhật ký.
/// Dữ liệu sai thì từ chối với thông báo chỉ rõ chỗ sai, không lưu gì, số đã thu không đổi (AC-019).
/// </summary>
public sealed class PaymentService(
    IPaymentRepository repo,
    IAuditService audit,
    TimeProvider clock) : IPaymentService
{
    // Cho phép đồng hồ máy Kế toán nhanh hơn máy chủ một chút khi nhập "vừa nhận tiền"
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    public async Task<PeriodCollectionResponse?> GetPeriodAsync(int periodId, CancellationToken ct = default)
    {
        var period = await repo.GetPeriodAsync(periodId, ct);
        if (period is null)
            return null;

        var paid = (await repo.GetPaidAsync(periodId, null, ct)).ToDictionary(p => p.HouseholdId);
        var households = (await repo.GetDuesAsync(periodId, null, ct))
            .Select(due => ToRow(due, paid.GetValueOrDefault(due.HouseholdId)))
            .OrderBy(h => h.Building)
            .ThenBy(h => h.ApartmentCode)
            .ToList();

        return new PeriodCollectionResponse(period.Id, period.Year, period.Month, period.DueDate,
            households.Sum(h => h.AmountDue), households.Sum(h => h.AmountPaid), households.Sum(h => h.Remaining),
            households);
    }

    public async Task<HouseholdPaymentDetailResponse?> GetHouseholdAsync(int periodId, int householdId, CancellationToken ct = default)
    {
        var period = await repo.GetPeriodAsync(periodId, ct);
        if (period is null)
            return null;

        var due = (await repo.GetDuesAsync(periodId, householdId, ct)).SingleOrDefault();
        if (due is null)
            return null;

        var row = ToRow(due, (await repo.GetPaidAsync(periodId, householdId, ct)).SingleOrDefault());
        var lines = (await repo.GetBillLinesAsync(periodId, householdId, ct))
            .Select(l => new BillLineResponse(l.Code, l.Name, l.Amount))
            .ToList();
        var payments = (await repo.GetPaymentsAsync(periodId, householdId, ct)).Select(Map).ToList();

        return new HouseholdPaymentDetailResponse(period.Id, period.Year, period.Month, period.DueDate,
            row.HouseholdId, row.HouseholdNumber, row.ApartmentCode, row.Building, lines,
            row.AmountDue, row.AmountPaid, row.Remaining, row.Status, payments);
    }

    public async Task<FeeItemOperationResult<RecordPaymentResponse>> RecordAsync(
        int actorId, string actorName, RecordPaymentRequest request, CancellationToken ct = default)
    {
        // Bước 1: dữ liệu nhập
        var error = ValidateInput(request);
        if (error is not null)
            return Invalid(error);

        // Bước 2: khoản phải thu của hộ trong kỳ
        var period = await repo.GetPeriodAsync(request.BillingPeriodId, ct);
        if (period is null)
            return new(FeeItemStatus.NotFound, Message: "Không tìm thấy kỳ thu.");

        var due = (await repo.GetDuesAsync(period.Id, request.HouseholdId, ct)).SingleOrDefault();
        if (due is null)
            return Invalid($"Hộ này không có khoản phải thu trong kỳ thu tháng {period.Month:00}/{period.Year}.");

        var paid = (await repo.GetPaidAsync(period.Id, request.HouseholdId, ct)).SingleOrDefault()?.AmountPaid ?? 0;
        var remaining = due.AmountDue - paid;
        if (remaining <= 0)
            return Invalid($"Hộ {due.HouseholdNumber} đã nộp đủ kỳ thu tháng {period.Month:00}/{period.Year}.");
        if (request.Amount > remaining)
            return Invalid($"Số tiền nộp vượt số còn thiếu ({Money(remaining)} đ). Không nhận nộp thừa.");

        // Bước 3: lưu (đọc lại tổng đã nộp trong transaction), rồi ghi nhật ký
        var payment = new PaymentEntity
        {
            BillingPeriodId = period.Id,
            HouseholdId = due.HouseholdId,
            Amount = request.Amount,
            // Cột timestamptz chỉ nhận offset 0 (Npgsql); client có thể gửi giờ +07:00, đổi về UTC, giữ nguyên thời điểm
            PaidAt = request.PaidAt.ToUniversalTime(),
            Method = request.Method!.Value,
            Reference = NormalizeOptional(request.Reference),
            PayerName = NormalizeOptional(request.PayerName),
            Note = NormalizeOptional(request.Note),
            RecordedById = actorId,
            RecordedAt = clock.GetUtcNow()
        };
        var saved = await repo.AddAsync(payment, due.AmountDue, ct);
        if (saved != PaymentSaveResult.Saved)
            return new(FeeItemStatus.Conflict, Message:
                "Vừa có lần nộp khác được ghi cho hộ này nên số còn thiếu đã thay đổi. Hãy tải lại và kiểm tra trước khi ghi.");

        await audit.LogAsync(AuditAction.RecordPayment, actorName, actorId, payment.Id.ToString(),
            $"Biên nhận {ReceiptNumber(payment.Id)}: hộ {due.HouseholdNumber}, kỳ thu tháng {period.Month:00}/{period.Year}, " +
            $"{Money(payment.Amount)} đ ({PaymentLabels.Label(payment.Method)}), còn thiếu {Money(remaining - payment.Amount)} đ",
            ct);

        var bill = (await GetHouseholdAsync(period.Id, due.HouseholdId, ct))!;
        return new(FeeItemStatus.Success, new RecordPaymentResponse(bill.Payments.Single(p => p.Id == payment.Id), bill));
    }

    // ---- Quy tắc ----

    private string? ValidateInput(RecordPaymentRequest request)
    {
        if (request.Method is null)
            return "Hãy chọn phương thức thanh toán.";
        if (request.Amount <= 0)
            return "Số tiền nộp phải lớn hơn 0.";
        if (request.Amount != decimal.Truncate(request.Amount))
            return "Số tiền nộp phải là số đồng nguyên.";
        if (request.PaidAt == default)
            return "Hãy nhập thời điểm nhận tiền.";
        if (request.PaidAt > clock.GetUtcNow() + ClockSkew)
            return "Thời điểm nhận tiền không được sau thời điểm hiện tại.";
        if (request.Method == PaymentMethod.ChuyenKhoan && NormalizeOptional(request.Reference) is null)
            return "Chuyển khoản phải có mã giao dịch hoặc nội dung chuyển khoản để đối chiếu.";
        return null;
    }

    /// <summary>
    /// Kiểm "đã nộp đủ" trước, để hộ có số phải thu 0 đ (không phải nộp gì) là đã đủ, khớp với RecordAsync
    /// (còn thiếu ≤ 0 thì không nhận nộp thêm), không bị tính vào danh sách chưa nộp.
    /// </summary>
    public static PaymentStatus StatusOf(decimal amountDue, decimal amountPaid) =>
        amountPaid >= amountDue ? PaymentStatus.DaNopDu
        : amountPaid <= 0 ? PaymentStatus.ChuaNop
        : PaymentStatus.NopMotPhan;

    public static string ReceiptNumber(int paymentId) => $"BN-{paymentId:D6}";

    /// <summary>Số tiền kiểu Việt Nam: 1.234.567 (không phụ thuộc culture của máy chủ). Dùng chung với DebtService.</summary>
    internal static string Money(decimal value) =>
        value.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '.');

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static FeeItemOperationResult<RecordPaymentResponse> Invalid(string message) =>
        new(FeeItemStatus.Invalid, Message: message);

    // ---- Map ----

    private static HouseholdCollectionResponse ToRow(HouseholdDueRow due, HouseholdPaidRow? paid)
    {
        var amountPaid = paid?.AmountPaid ?? 0;
        return new HouseholdCollectionResponse(due.HouseholdId, due.HouseholdNumber, due.ApartmentCode, due.Building,
            due.AmountDue, amountPaid, due.AmountDue - amountPaid, StatusOf(due.AmountDue, amountPaid),
            paid?.PaymentCount ?? 0);
    }

    private static PaymentResponse Map(PaymentEntity p) =>
        new(p.Id, ReceiptNumber(p.Id), p.Amount, p.PaidAt, p.Method, p.Reference, p.PayerName, p.Note,
            p.RecordedBy.Username, p.RecordedAt);
}
