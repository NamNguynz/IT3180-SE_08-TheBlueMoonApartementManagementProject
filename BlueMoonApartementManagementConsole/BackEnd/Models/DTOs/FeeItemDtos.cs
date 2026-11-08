using System.ComponentModel.DataAnnotations;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;

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

    [Range(typeof(decimal), "0", "1000000000")]
    public decimal Rate { get; init; }

    public DateOnly EffectiveFrom { get; init; }

    public DateOnly? EffectiveTo { get; init; }
}

public sealed record ChangeRateRequest
{
    [Range(typeof(decimal), "0", "1000000000")]
    public decimal Rate { get; init; }

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
    string Unit,
    string? Note,
    IReadOnlyList<FeeRateResponse> Rates);

public sealed record FeeRateResponse(
    int Id,
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

public sealed record FeeItemOperationResult<T>(
    FeeItemStatus Status,
    T? Data = default,
    string? Message = null);
