using System.ComponentModel.DataAnnotations;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;

/// <summary>
/// Lập kỳ thu (PBI-014). Các quy tắc phụ thuộc dữ liệu (ngày tính phí đã đến chưa, khoản thu có hợp lệ,
/// đã lập trong kỳ khác chưa) được kiểm ở BillingPeriodService, thông báo lỗi chỉ rõ chỗ cần sửa.
/// </summary>
public sealed record CreateBillingPeriodRequest
{
    [Range(2000, 2100)]
    public int Year { get; init; }

    [Range(1, 12)]
    public int Month { get; init; }

    public DateOnly ChargeDate { get; init; }

    public DateOnly DueDate { get; init; }

    /// <summary>Các khoản thu bắt buộc, áp dụng toàn chung cư, đưa vào kỳ.</summary>
    [Required]
    public IReadOnlyList<int> FeeItemIds { get; init; } = [];

    [StringLength(500)]
    public string? Note { get; init; }
}

/// <summary>Một dòng trong danh sách kỳ thu.</summary>
public sealed record BillingPeriodSummaryResponse(
    int Id,
    int Year,
    int Month,
    DateOnly ChargeDate,
    DateOnly DueDate,
    int ItemCount,
    int HouseholdCount,
    decimal TotalAmount,
    string CreatedBy,
    DateTimeOffset CreatedAt);

/// <summary>Kỳ thu kèm bảng kê: các khoản thu với đơn giá đã áp dụng, và số phải thu của từng hộ.</summary>
public sealed record BillingPeriodResponse(
    int Id,
    int Year,
    int Month,
    DateOnly ChargeDate,
    DateOnly DueDate,
    string? Note,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    decimal TotalAmount,
    IReadOnlyList<BillingPeriodItemResponse> Items,
    IReadOnlyList<HouseholdBillResponse> Households);

/// <summary>Khoản thu trong kỳ. Rate, RateEffectiveFrom giải thích số tiền đã tính (AC-007).</summary>
public sealed record BillingPeriodItemResponse(
    int FeeItemId,
    string Code,
    string Name,
    FeeCycle FeeCycle,
    CalculationBasis CalculationBasis,
    string Unit,
    decimal Rate,
    DateOnly RateEffectiveFrom,
    decimal TotalAmount);

/// <summary>Bảng kê của một hộ: các khoản bắt buộc gom thành một lần thu, TotalAmount là số phải nộp.</summary>
public sealed record HouseholdBillResponse(
    int HouseholdId,
    string HouseholdNumber,
    string ApartmentCode,
    string Building,
    IReadOnlyList<ReceivableLineResponse> Lines,
    decimal TotalAmount);

/// <summary>Amount = Rate × Quantity, làm tròn đến đồng.</summary>
public sealed record ReceivableLineResponse(
    int FeeItemId,
    decimal Quantity,
    decimal Rate,
    decimal Amount);

/// <summary>
/// Kết quả tính thử phí của một hộ cho một khoản thu vào một ngày (PBI-015): đủ dữ liệu để giải thích số tiền,
/// Amount = Rate × Quantity làm tròn đến đồng, Rate là đơn giá có hiệu lực từ RateEffectiveFrom.
/// </summary>
public sealed record FeeCalculationResponse(
    int FeeItemId,
    string Code,
    string Name,
    CalculationBasis CalculationBasis,
    string Unit,
    int HouseholdId,
    string HouseholdNumber,
    string ApartmentCode,
    DateOnly Date,
    decimal Quantity,
    decimal Rate,
    DateOnly RateEffectiveFrom,
    decimal Amount);
