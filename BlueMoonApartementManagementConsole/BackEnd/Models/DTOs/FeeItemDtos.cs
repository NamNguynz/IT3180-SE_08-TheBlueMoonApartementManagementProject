using System.ComponentModel.DataAnnotations;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;

public sealed record FeeTypeRateRequest
{
    [Required]
    public ApartmentType ApartmentType { get; init; }

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal Rate { get; init; }
}

public sealed record CreateFeeItemRequest
{
    [Required, StringLength(30)]
    public required string Code { get; init; }

    [Required, StringLength(100)]
    public required string Name { get; init; }

    // Nullable so a missing field is rejected instead of silently taking the enum's first value.
    [Required]
    public FeeType? FeeType { get; init; }

    [Required]
    public FeeScope? FeeScope { get; init; }

    [Required]
    public FeeCycle? FeeCycle { get; init; }

    [Required]
    public CalculationBasis? CalculationBasis { get; init; }

    [Required, StringLength(20)]
    public required string Unit { get; init; }

    [StringLength(500)]
    public string? Note { get; init; }

    /// <summary>Mặc định Chung để tương thích với client cũ; TheoLoai chỉ hợp lệ với cơ sở DienTich.</summary>
    public RateMode RateMode { get; init; } = RateMode.Chung;

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal Rate { get; init; }

    public IReadOnlyList<FeeTypeRateRequest> TypeRates { get; init; } = [];

    public DateOnly EffectiveFrom { get; init; }

    public DateOnly? EffectiveTo { get; init; }
}

public sealed record ChangeRateRequest
{
    public RateMode RateMode { get; init; } = RateMode.Chung;

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal Rate { get; init; }

    public IReadOnlyList<FeeTypeRateRequest> TypeRates { get; init; } = [];

    public DateOnly EffectiveFrom { get; init; }
}

public sealed record FeeItemResponse(
    int Id,
    string Code,
    string Name,
    FeeType FeeType,
    FeeScope FeeScope,
    FeeCycle FeeCycle,
    CalculationBasis CalculationBasis,
    RateMode RateMode,
    string Unit,
    string? Note,
    IReadOnlyList<FeeRateResponse> Rates,
    IReadOnlyList<FeeTypeRateResponse> TypeRates);

public sealed record FeeRateResponse(
    int Id,
    decimal Rate,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

public sealed record FeeTypeRateResponse(
    int Id,
    ApartmentType ApartmentType,
    decimal Rate,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

public enum FeeItemStatus
{
    Success,
    NotFound,
    Conflict,
    Invalid,
    FeatureUnavailable
}

public sealed record FeeRateLookupResponse(
    int FeeItemId,
    string Code,
    string Name,
    CalculationBasis CalculationBasis,
    string Unit,
    DateOnly Date,
    decimal Rate,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo);

/// <summary>Bản xem trước ba giá theo loại căn hộ tại một ngày, dùng chung cho danh mục và trang kỳ thu.</summary>
public sealed record FeeTypeRateLookupResponse(
    int FeeItemId,
    string Code,
    string Name,
    CalculationBasis CalculationBasis,
    string Unit,
    RateMode RateMode,
    DateOnly Date,
    IReadOnlyList<FeeTypeRateResponse> Rates);

public sealed record FeeItemOperationResult<T>(
    FeeItemStatus Status,
    T? Data = default,
    string? Message = null);
