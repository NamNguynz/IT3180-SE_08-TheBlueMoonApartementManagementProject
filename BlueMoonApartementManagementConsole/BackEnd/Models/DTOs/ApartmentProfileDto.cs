using System.ComponentModel.DataAnnotations;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;

/// <summary>Payload for creating an apartment profile.</summary>
public sealed record CreateApartmentRequest
{
    [Required, StringLength(30)]
    public required string Code { get; init; }

    [Required, StringLength(100)]
    public required string Building { get; init; }

    /// <summary>Loại hình (PBI-002): CanHo, Kiot, Penthouse. Thiếu hoặc sai giá trị thì API trả 400.</summary>
    [EnumDataType(typeof(ApartmentType))]
    public required ApartmentType Type { get; init; }

    [Range(0, 200)]
    public int Floor { get; init; }

    [Range(typeof(decimal), "0.01", "100000")]
    public decimal Area { get; init; }

    [Range(0, 50)]
    public int BedroomCount { get; init; }

    [Required, StringLength(30)]
    public required string Status { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }
}

/// <summary>Payload for updating an apartment profile.</summary>
public sealed record UpdateApartmentRequest
{
    [Required, StringLength(30)]
    public required string Code { get; init; }

    [Required, StringLength(100)]
    public required string Building { get; init; }

    /// <summary>Loại hình (PBI-002): CanHo, Kiot, Penthouse. Thiếu hoặc sai giá trị thì API trả 400.</summary>
    [EnumDataType(typeof(ApartmentType))]
    public required ApartmentType Type { get; init; }

    [Range(0, 200)]
    public int Floor { get; init; }

    [Range(typeof(decimal), "0.01", "100000")]
    public decimal Area { get; init; }

    [Range(0, 50)]
    public int BedroomCount { get; init; }

    [Required, StringLength(30)]
    public required string Status { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }
}

/// <summary>Payload for creating a household registration and its initial residents.</summary>
public sealed record CreateHouseholdRequest
{
    [Required, StringLength(30)]
    public required string HouseholdNumber { get; init; }

    [Required, StringLength(100)]
    public required string HeadName { get; init; }

    [Required, StringLength(30)]
    public required string HouseNumber { get; init; }

    public DateTimeOffset RegisteredAt { get; init; }

    [Required]
    public required List<ResidentProfileRequest> Residents { get; init; }
}

/// <summary>
/// Payload for updating a household registration and adding or updating residents.
/// Residents omitted from this request are retained; include an existing resident ID to update that resident.
/// </summary>
public sealed record UpdateHouseholdRequest
{
    [Required, StringLength(30)]
    public required string HouseholdNumber { get; init; }

    [Required, StringLength(100)]
    public required string HeadName { get; init; }

    [Required, StringLength(30)]
    public required string HouseNumber { get; init; }

    public DateTimeOffset RegisteredAt { get; init; }

    [Required]
    public required List<ResidentProfileRequest> Residents { get; init; }
}

/// <summary>Resident data included when creating or updating a household profile.</summary>
public sealed record ResidentProfileRequest
{
    public int? Id { get; init; }

    [Required, StringLength(100)]
    public required string FullName { get; init; }

    [StringLength(100)]
    public string? Alias { get; init; }

    public DateTimeOffset DateOfBirth { get; init; }

    [Required, StringLength(20)]
    public required string Gender { get; init; }

    [StringLength(100)]
    public string? PlaceOfBirth { get; init; }

    [StringLength(100)]
    public string? Hometown { get; init; }

    [StringLength(50)]
    public string? Ethnicity { get; init; }

    [StringLength(50)]
    public string? Religion { get; init; }

    [StringLength(100)]
    public string? Occupation { get; init; }

    [StringLength(150)]
    public string? Workplace { get; init; }

    [StringLength(30)]
    public string? IdentityNumber { get; init; }

    public DateTimeOffset? IdentityIssuedAt { get; init; }

    [StringLength(150)]
    public string? IdentityIssuePlace { get; init; }

    [StringLength(500)]
    public string? PreviousResidence { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }
}

/// <summary>Apartment and optional household information returned by the API.</summary>
public sealed record ApartmentProfileResponse(
    int Id,
    string Code,
    string Building,
    ApartmentType Type,
    int Floor,
    decimal Area,
    int BedroomCount,
    string Status,
    string? Notes,
    HouseholdProfileResponse? Household);

/// <summary>Household registration and its resident list returned by the API.</summary>
public sealed record HouseholdProfileResponse(
    int Id,
    int ApartmentId,
    string HouseholdNumber,
    string HeadName,
    string HouseNumber,
    DateTimeOffset RegisteredAt,
    IReadOnlyList<ResidentProfileResponse> Residents);

/// <summary>Resident information returned by the API.</summary>
public sealed record ResidentProfileResponse(
    int Id,
    string FullName,
    string? Alias,
    DateTimeOffset DateOfBirth,
    string Gender,
    string? PlaceOfBirth,
    string? Hometown,
    string? Ethnicity,
    string? Religion,
    string? Occupation,
    string? Workplace,
    string? IdentityNumber,
    DateTimeOffset? IdentityIssuedAt,
    string? IdentityIssuePlace,
    string? PreviousResidence,
    string? Notes);

public enum ProfileStatus
{
    Success,
    NotFound,
    Conflict,
    Invalid
}

public sealed record ProfileOperationResult<T>(
    ProfileStatus Status,
    T? Data = default,
    string? Message = null);
