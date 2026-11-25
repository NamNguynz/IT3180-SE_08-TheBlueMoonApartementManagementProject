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

    /// <summary>Các khoản thu bắt buộc đưa vào kỳ, gồm cả khoản toàn chung cư và khoản chỉ định hộ.</summary>
    [Required]
    public IReadOnlyList<int> FeeItemIds { get; init; } = [];

    /// <summary>
    /// Danh sách hộ phải nộp của từng khoản chỉ định hộ có trong FeeItemIds (chọn ở pop-up khi lập kỳ).
    /// Khoản toàn chung cư thu của mọi hộ nên không có ở đây.
    /// </summary>
    public IReadOnlyList<TargetedFeeItemRequest> TargetedFeeItems { get; init; } = [];

    /// <summary>
    /// Chế độ đơn giá chọn ở trang kỳ thu cho từng khoản thu tính theo diện tích.
    /// Thiếu một khoản thì mặc định Chung, để tương thích với client/API cũ.
    /// </summary>
    public IReadOnlyList<FeeRateChoiceRequest> RateChoices { get; init; } = [];

    [StringLength(500)]
    public string? Note { get; init; }
}

/// <summary>Chế độ đơn giá (Chung/TheoLoai) được chọn khi lập kỳ; không cho nhập số tiền ở đây.</summary>
public sealed record FeeRateChoiceRequest
{
    public int FeeItemId { get; init; }

    public RateMode Mode { get; init; } = RateMode.Chung;
}

/// <summary>Một khoản thu chỉ định hộ và các hộ được chọn để thu khoản đó trong kỳ (ít nhất một hộ).</summary>
public sealed record TargetedFeeItemRequest
{
    public int FeeItemId { get; init; }

    [Required]
    public IReadOnlyList<int> HouseholdIds { get; init; } = [];
}

/// <summary>Một hộ trong pop-up chọn hộ cho khoản thu chỉ định.</summary>
public sealed record HouseholdOptionResponse(int HouseholdId, string HouseholdNumber, string ApartmentCode);

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

/// <summary>
/// Khoản thu trong kỳ. Với chế độ Chung, Rate/RateEffectiveFrom giải thích số tiền đã tính;
/// với chế độ TheoLoai, Rate/RateEffectiveFrom là null và TypeRates chứa ba giá đã chụp (AC-007).
/// </summary>
public sealed record BillingPeriodItemResponse(
    int FeeItemId,
    string Code,
    string Name,
    FeeCycle FeeCycle,
    CalculationBasis CalculationBasis,
    string Unit,
    RateMode RateMode,
    decimal? Rate,
    DateOnly? RateEffectiveFrom,
    IReadOnlyList<BillingPeriodItemTypeRateResponse> TypeRates,
    decimal TotalAmount);

/// <summary>Một giá theo loại căn hộ đã chụp lại trong kỳ thu.</summary>
public sealed record BillingPeriodItemTypeRateResponse(
    ApartmentType ApartmentType,
    decimal Rate,
    DateOnly EffectiveFrom);

/// <summary>Bảng kê của một hộ: các khoản bắt buộc gom thành một lần thu, TotalAmount là số phải nộp.</summary>
public sealed record HouseholdBillResponse(
    int HouseholdId,
    string HouseholdNumber,
    string ApartmentCode,
    string Building,
    IReadOnlyList<ReceivableLineResponse> Lines,
    decimal TotalAmount);

/// <summary>Amount = Rate × Quantity, làm tròn đến đồng. Rate là đơn giá thực tế đã áp dụng cho hộ này.</summary>
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
    RateMode RateMode,
    ApartmentType? ApartmentType,
    decimal Rate,
    DateOnly RateEffectiveFrom,
    decimal Amount);
