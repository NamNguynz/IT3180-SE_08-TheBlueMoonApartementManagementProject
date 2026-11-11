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
    DateTimeOffset RecordedAt);

/// <summary>Kết quả ghi nhận: biên nhận của lần nộp vừa ghi và khoản phải thu sau khi cập nhật.</summary>
public sealed record RecordPaymentResponse(PaymentResponse Payment, HouseholdPaymentDetailResponse Bill);
