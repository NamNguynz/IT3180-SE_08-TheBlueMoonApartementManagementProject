using System.ComponentModel.DataAnnotations;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;

/// <summary>
/// Ghi nhận một lần nộp tiền của hộ cho một kỳ thu (PBI-016). Trang chỉ gửi sau khi Kế toán đã xem lại
/// và bấm xác nhận (AC-018). Quy tắc phụ thuộc dữ liệu (còn thiếu bao nhiêu, đã nộp đủ chưa) kiểm ở PaymentService.
/// </summary>
public sealed record RecordPaymentRequest
{
    [Range(1, int.MaxValue)]
    public int BillingPeriodId { get; init; }

    [Range(1, int.MaxValue)]
    public int HouseholdId { get; init; }

    public decimal Amount { get; init; }

    public DateTimeOffset PaidAt { get; init; }

    // Nullable để thiếu phương thức thì bị từ chối, không lặng lẽ nhận giá trị enum đầu tiên
    public PaymentMethod? Method { get; init; }

    [StringLength(200)]
    public string? Reference { get; init; }

    [StringLength(100)]
    public string? PayerName { get; init; }

    [StringLength(500)]
    public string? Note { get; init; }
}

/// <summary>Ảnh minh chứng do client gửi kèm khi ghi nhận lần nộp (bắt buộc với lần nộp mới).</summary>
public sealed record PaymentProofUpload(string FileName, string ContentType, long Length, Stream Content);

/// <summary>Giới hạn chung cho ảnh minh chứng: JPG/PNG/WebP, tối đa 5 MB.</summary>
public static class PaymentProofRules
{
    public const long MaxBytes = 5L * 1024 * 1024;

    private static readonly Dictionary<string, string> TypesByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp"
    };

    /// <summary>Trả về thông báo lỗi nếu ảnh không hợp lệ, ngược lại trả về null.</summary>
    public static string? Validate(PaymentProofUpload? proof)
    {
        if (proof is null || proof.Length <= 0)
            return "Vui lòng đính kèm ảnh minh chứng khi ghi nhận lần nộp.";
        if (proof.Length > MaxBytes)
            return "Ảnh minh chứng không được vượt quá 5 MB.";

        var extension = Path.GetExtension(proof.FileName);
        if (!TypesByExtension.TryGetValue(extension, out var expectedType))
            return "Ảnh minh chứng chỉ chấp nhận định dạng JPG, PNG hoặc WebP.";
        if (!string.Equals(proof.ContentType, expectedType, StringComparison.OrdinalIgnoreCase))
            return "Ảnh minh chứng không đúng định dạng JPG, PNG hoặc WebP.";

        return null;
    }

    /// <summary>Lấy content-type từ đuôi file đã được kiểm tra; không tin vào tên/content-type do client gửi khi phục vụ lại.</summary>
    public static string? ContentTypeOf(string storedName) =>
        TypesByExtension.TryGetValue(Path.GetExtension(storedName), out var type) ? type : null;
}

/// <summary>Tình hình thu của một kỳ: tổng phải thu, đã thu, còn thiếu và từng hộ.</summary>
public sealed record PeriodCollectionResponse(
    int BillingPeriodId,
    int Year,
    int Month,
    DateOnly DueDate,
    decimal AmountDue,
    decimal AmountPaid,
    decimal Remaining,
    IReadOnlyList<HouseholdCollectionResponse> Households);

public sealed record HouseholdCollectionResponse(
    int HouseholdId,
    string HouseholdNumber,
    string ApartmentCode,
    string Building,
    decimal AmountDue,
    decimal AmountPaid,
    decimal Remaining,
    PaymentStatus Status,
    int PaymentCount);

/// <summary>Khoản phải thu của một hộ trong kỳ: các khoản, số đã thu, còn thiếu và các lần nộp (biên nhận).</summary>
public sealed record HouseholdPaymentDetailResponse(
    int BillingPeriodId,
    int Year,
    int Month,
    DateOnly DueDate,
    int HouseholdId,
    string HouseholdNumber,
    string ApartmentCode,
    string Building,
    IReadOnlyList<BillLineResponse> Lines,
    decimal AmountDue,
    decimal AmountPaid,
    decimal Remaining,
    PaymentStatus Status,
    IReadOnlyList<PaymentResponse> Payments);

public sealed record BillLineResponse(string Code, string Name, decimal Amount);

/// <summary>Một lần nộp, cũng là nội dung biên nhận (OBS-002).</summary>
public sealed record PaymentResponse(
    int Id,
    string ReceiptNumber,
    decimal Amount,
    DateTimeOffset PaidAt,
    PaymentMethod Method,
    string? Reference,
    string? PayerName,
    string? Note,
    string RecordedBy,
    DateTimeOffset RecordedAt)
{
    /// <summary>URL để tải ảnh minh chứng qua API được bảo vệ; null với các lần nộp cũ chưa có ảnh.</summary>
    public string? ProofImageUrl { get; init; }
}

/// <summary>Kết quả ghi nhận: biên nhận của lần nộp vừa ghi và khoản phải thu sau khi cập nhật.</summary>
public sealed record RecordPaymentResponse(PaymentResponse Payment, HouseholdPaymentDetailResponse Bill);
