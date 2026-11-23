using BlueMoonApartementManagementConsole.BackEnd.Models;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;
using Npgsql;
using Microsoft.EntityFrameworkCore;

namespace BlueMoonApartementManagementConsole.BackEnd.Services;

public interface IApartmentProfileService
{
    Task<IReadOnlyList<ApartmentProfileResponse>> GetApartmentsAsync(CancellationToken ct = default);
    Task<PagedResult<ApartmentListRow>> QueryApartmentsAsync(ApartmentListQuery query, CancellationToken ct = default);
    Task<ApartmentProfileResponse?> GetApartmentByIdAsync(int id, CancellationToken ct = default);
    Task<ProfileOperationResult<ApartmentProfileResponse>> CreateApartmentAsync(
        int actorId, string actorName, CreateApartmentRequest request, CancellationToken ct = default);
    Task<ProfileOperationResult<ApartmentProfileResponse>> UpdateApartmentAsync(
        int actorId, string actorName, int id, UpdateApartmentRequest request, CancellationToken ct = default);
    Task<ProfileOperationResult<ApartmentProfileResponse>> UpdateApartmentStatusAsync(
        int actorId, string actorName, int id, UpdateApartmentStatusRequest request, CancellationToken ct = default);
    Task<HouseholdProfileResponse?> GetHouseholdByIdAsync(int id, CancellationToken ct = default);
    Task<ProfileOperationResult<HouseholdProfileResponse>> CreateHouseholdAsync(
        int actorId, string actorName, int apartmentId, CreateHouseholdRequest request, CancellationToken ct = default);
    Task<ProfileOperationResult<HouseholdProfileResponse>> UpdateHouseholdAsync(
        int actorId, string actorName, int id, UpdateHouseholdRequest request, CancellationToken ct = default);
}

public sealed class ApartmentProfileService(
    IApartmentProfileRepository profiles,
    IAuditService audit) : IApartmentProfileService
{
    private const string BuildingNotFoundMessage = "Tòa nhà không tồn tại.";
    public async Task<IReadOnlyList<ApartmentProfileResponse>> GetApartmentsAsync(CancellationToken ct = default) =>
        (await profiles.GetApartmentsAsync(ct)).Select(Map).ToList();

    /// <summary>Trang Danh sách căn hộ: chuẩn hóa trang/kích thước trang rồi ủy quyền cho repository.</summary>
    public Task<PagedResult<ApartmentListRow>> QueryApartmentsAsync(
        ApartmentListQuery query, CancellationToken ct = default) =>
        profiles.QueryApartmentsAsync(query with
        {
            Page = Math.Max(1, query.Page),
            PageSize = ListPageSize.Normalize(query.PageSize)
        }, ct);

    public async Task<ApartmentProfileResponse?> GetApartmentByIdAsync(int id, CancellationToken ct = default)
    {
        var apartment = await profiles.GetApartmentByIdAsync(id, tracking: false, ct);
        return apartment is null ? null : Map(apartment);
    }

    public async Task<ProfileOperationResult<ApartmentProfileResponse>> CreateApartmentAsync(
    int actorId, string actorName, CreateApartmentRequest request, CancellationToken ct = default)
    {
        var code = NormalizeCode(request.Code);
        if (await profiles.ApartmentCodeExistsAsync(code, ct: ct))
            return Conflict<ApartmentProfileResponse>("Mã căn hộ đã tồn tại.");

        var building = await profiles.GetBuildingAsync(request.BuildingId, ct);
        if (building is null)
            return new(ProfileStatus.Invalid, Message: BuildingNotFoundMessage);

        var apartment = new ApartmentEntity
        {
            Code = code,
            Building = building,
            Type = request.Type,
            Floor = request.Floor,
            Area = request.Area,
            BedroomCount = request.BedroomCount,
            Status = request.Status,
            Notes = NormalizeOptional(request.Notes)
        };

        await profiles.AddApartmentAsync(apartment, ct);
        if (!await SaveWithUniqueConflictHandlingAsync(ct))
            return Conflict<ApartmentProfileResponse>("Mã căn hộ đã tồn tại.");

        await audit.LogAsync(AuditAction.CreateApartmentProfile, actorName, actorId,
            apartment.Id.ToString(), $"Tạo hồ sơ căn hộ {apartment.Code} tại {building.Name}", ct);

        return new(ProfileStatus.Success, Map(apartment));
    }

    public async Task<ProfileOperationResult<ApartmentProfileResponse>> UpdateApartmentAsync(
    int actorId, string actorName, int id, UpdateApartmentRequest request, CancellationToken ct = default)
    {
        var apartment = await profiles.GetApartmentByIdAsync(id, tracking: true, ct);
        if (apartment is null)
            return new(ProfileStatus.NotFound);

        var code = NormalizeCode(request.Code);
        if (await profiles.ApartmentCodeExistsAsync(code, id, ct))
            return Conflict<ApartmentProfileResponse>("Mã căn hộ đã tồn tại.");

        var building = await profiles.GetBuildingAsync(request.BuildingId, ct);
        if (building is null)
            return new(ProfileStatus.Invalid, Message: BuildingNotFoundMessage);

        // Ô select trong form cũng đổi được trạng thái: chặn ở đây để không vòng qua luật của PATCH.
        var statusError = await ValidateStatusChangeAsync(id, request.Status, apartment.Status, ct);
        if (statusError is not null)
            return new(ProfileStatus.Conflict, Message: statusError);

        apartment.Code = code;
        apartment.Building = building;
        apartment.Type = request.Type;
        apartment.Floor = request.Floor;
        apartment.Area = request.Area;
        apartment.BedroomCount = request.BedroomCount;
        apartment.Status = request.Status;
        apartment.Notes = NormalizeOptional(request.Notes);

        if (!await SaveWithUniqueConflictHandlingAsync(ct))
            return Conflict<ApartmentProfileResponse>("Mã căn hộ đã tồn tại.");

        await audit.LogAsync(AuditAction.UpdateApartmentProfile, actorName, actorId,
            apartment.Id.ToString(), $"Cập nhật hồ sơ căn hộ {apartment.Code}", ct);

        return new(ProfileStatus.Success, Map(apartment));
    }

    /// <summary>Đổi riêng trạng thái căn hộ, dùng cho thao tác kéo thả trên trang Hồ sơ căn hộ.</summary>
    public async Task<ProfileOperationResult<ApartmentProfileResponse>> UpdateApartmentStatusAsync(
        int actorId, string actorName, int id, UpdateApartmentStatusRequest request, CancellationToken ct = default)
    {
        var apartment = await profiles.GetApartmentByIdAsync(id, tracking: true, ct);
        if (apartment is null)
            return new(ProfileStatus.NotFound);

        var previous = apartment.Status;
        if (previous == request.Status)
            return new(ProfileStatus.Success, Map(apartment));

        var statusError = await ValidateStatusChangeAsync(id, request.Status, previous, ct);
        if (statusError is not null)
            return new(ProfileStatus.Conflict, Message: statusError);

        apartment.Status = request.Status;
        await profiles.SaveChangesAsync(ct);

        await audit.LogAsync(AuditAction.UpdateApartmentStatus, actorName, actorId,
            apartment.Id.ToString(),
            $"Đổi trạng thái căn hộ {apartment.Code}: {ApartmentStatusLabels.LabelOf(previous)} → {ApartmentStatusLabels.LabelOf(request.Status)}",
            ct);

        return new(ProfileStatus.Success, Map(apartment));
    }

    public async Task<HouseholdProfileResponse?> GetHouseholdByIdAsync(int id, CancellationToken ct = default)
    {
        var household = await profiles.GetHouseholdByIdAsync(id, ct);
        return household is null ? null : Map(household);
    }

    public async Task<ProfileOperationResult<HouseholdProfileResponse>> CreateHouseholdAsync(
        int actorId, string actorName, int apartmentId, CreateHouseholdRequest request, CancellationToken ct = default)
    {
        if (await profiles.GetApartmentByIdAsync(apartmentId, tracking: false, ct) is null)
            return new(ProfileStatus.NotFound);

        if (request.RegisteredAt == default)
            return new(ProfileStatus.Invalid, Message: "Ngày lập hộ khẩu là bắt buộc.");

        if (await profiles.GetHouseholdByApartmentIdAsync(apartmentId, ct) is not null)
            return Conflict<HouseholdProfileResponse>("Căn hộ đã có hồ sơ hộ gia đình.");

        var number = NormalizeRequired(request.HouseholdNumber);
        if (await profiles.HouseholdNumberExistsAsync(number, ct: ct))
            return Conflict<HouseholdProfileResponse>("Số hộ khẩu đã tồn tại.");

        var validation = await ValidateResidentsAsync(request.Residents, null, ct);
        if (validation is not null)
            return new(ProfileStatus.Invalid, Message: validation);

        var household = new HouseholdEntity
        {
            ApartmentId = apartmentId,
            HouseholdNumber = number,
            HeadName = NormalizeRequired(request.HeadName),
            HouseNumber = NormalizeRequired(request.HouseNumber),
            RegisteredAt = NormalizeDate(request.RegisteredAt),
            Residents = request.Residents.Select(CreateResident).ToList()
        };

        await profiles.AddHouseholdAsync(household, ct);
        if (!await SaveWithUniqueConflictHandlingAsync(ct))
            return Conflict<HouseholdProfileResponse>("Số hộ khẩu hoặc số giấy tờ đã tồn tại.");

        await audit.LogAsync(AuditAction.CreateHouseholdProfile, actorName, actorId,
            household.Id.ToString(), $"Tạo hộ khẩu {household.HouseholdNumber} cho căn hộ {apartmentId}", ct);

        return new(ProfileStatus.Success, Map(household));
    }

    public async Task<ProfileOperationResult<HouseholdProfileResponse>> UpdateHouseholdAsync(
        int actorId, string actorName, int id, UpdateHouseholdRequest request, CancellationToken ct = default)
    {
        var household = await profiles.GetHouseholdByIdAsync(id, ct);
        if (household is null)
            return new(ProfileStatus.NotFound);

        if (request.RegisteredAt == default)
            return new(ProfileStatus.Invalid, Message: "Ngày lập hộ khẩu là bắt buộc.");

        var number = NormalizeRequired(request.HouseholdNumber);
        if (await profiles.HouseholdNumberExistsAsync(number, id, ct))
            return Conflict<HouseholdProfileResponse>("Số hộ khẩu đã tồn tại.");

        var validation = await ValidateResidentsAsync(request.Residents, household, ct);
        if (validation is not null)
            return new(ProfileStatus.Invalid, Message: validation);

        household.HouseholdNumber = number;
        household.HeadName = NormalizeRequired(request.HeadName);
        household.HouseNumber = NormalizeRequired(request.HouseNumber);
        household.RegisteredAt = NormalizeDate(request.RegisteredAt);

        foreach (var requestResident in request.Residents)
        {
            if (requestResident.Id is int residentId)
            {
                var existing = household.Residents.Single(r => r.Id == residentId);
                UpdateResident(existing, requestResident);
            }
            else
            {
                household.Residents.Add(CreateResident(requestResident));
            }
        }

        if (!await SaveWithUniqueConflictHandlingAsync(ct))
            return Conflict<HouseholdProfileResponse>("Số hộ khẩu hoặc số giấy tờ đã tồn tại.");

        await audit.LogAsync(AuditAction.UpdateHouseholdProfile, actorName, actorId,
            household.Id.ToString(), $"Cập nhật hộ khẩu {household.HouseholdNumber}", ct);

        return new(ProfileStatus.Success, Map(household));
    }

    private async Task<string?> ValidateResidentsAsync(
        IReadOnlyCollection<ResidentProfileRequest> residents, HouseholdEntity? household, CancellationToken ct)
    {
        var ids = residents.Where(r => r.Id.HasValue).Select(r => r.Id!.Value).ToList();
        if (ids.Distinct().Count() != ids.Count)
            return "Không được gửi trùng mã nhân khẩu.";

        if (household is null && ids.Count != 0)
            return "Không thể gắn mã nhân khẩu có sẵn khi tạo hộ khẩu.";

        if (household is not null && ids.Any(id => household.Residents.All(r => r.Id != id)))
            return "Nhân khẩu không thuộc hộ gia đình này.";

        var identityNumbers = residents
            .Select(r => NormalizeOptional(r.IdentityNumber))
            .Where(n => n is not null)
            .Cast<string>()
            .ToList();
        if (identityNumbers.Distinct(StringComparer.Ordinal).Count() != identityNumbers.Count)
            return "Số CMND/CCCD bị trùng trong danh sách.";

        foreach (var resident in residents)
        {
            if (resident.DateOfBirth == default)
                return "Ngày sinh nhân khẩu là bắt buộc.";

            if (resident.IdentityIssuedAt == DateTimeOffset.MinValue)
                return "Ngày cấp giấy tờ không hợp lệ.";

            var identityNumber = NormalizeOptional(resident.IdentityNumber);
            if (identityNumber is null)
                continue;

            if (await profiles.ResidentIdentityExistsAsync(identityNumber, resident.Id, ct))
                return "Số CMND/CCCD đã được sử dụng.";
        }

        return null;
    }

    private async Task<bool> SaveWithUniqueConflictHandlingAsync(CancellationToken ct)
    {
        try
        {
            await profiles.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException exception) when (FindPostgresException(exception) is
               { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is PostgresException postgresException)
                return postgresException;
            if (current.InnerException is null)
                break;
        }

        return null;
    }

    /// <summary>
    /// Căn hộ đang có nhân khẩu chưa chuyển đi thì không được chuyển sang Đang cho thuê / Đang sửa chữa.
    /// Dùng chung cho PATCH trạng thái và PUT hồ sơ để hai đường vào cùng một luật.
    /// </summary>
    private async Task<string?> ValidateStatusChangeAsync(
        int apartmentId, ApartmentStatus target, ApartmentStatus current, CancellationToken ct)
    {
        if (target == current || target is not (ApartmentStatus.DangChoThue or ApartmentStatus.DangSuaChua))
            return null;

        return await profiles.HasActiveResidentsAsync(apartmentId, ct)
            ? "Căn hộ đang có nhân khẩu chưa chuyển đi; không thể chuyển sang Đang cho thuê hoặc Đang sửa chữa. " +
              "Vui lòng ghi nhận biến động chuyển đi cho nhân khẩu trước."
            : null;
    }

    private static ProfileOperationResult<T> Conflict<T>(string message) =>
        new(ProfileStatus.Conflict, Message: message);

    private static ApartmentProfileResponse Map(ApartmentEntity apartment) =>
    new(apartment.Id, apartment.Code, apartment.BuildingId, apartment.Building.Name,
        apartment.Type, apartment.Floor, apartment.Area, apartment.BedroomCount,
        apartment.Status, apartment.Notes,
        apartment.Household is null ? null : Map(apartment.Household));

    private static HouseholdProfileResponse Map(HouseholdEntity household) =>
        new(household.Id, household.ApartmentId, household.HouseholdNumber, household.HeadName,
            household.HouseNumber, household.RegisteredAt, household.Residents.OrderBy(r => r.Id).Select(Map).ToList());

    private static ResidentProfileResponse Map(ResidentEntity resident) =>
        new(resident.Id, resident.FullName, resident.Alias, resident.DateOfBirth, resident.Gender,
            resident.PlaceOfBirth, resident.Hometown, resident.Ethnicity, resident.Religion,
            resident.Occupation, resident.Workplace, resident.IdentityNumber, resident.IdentityIssuedAt,
            resident.IdentityIssuePlace, resident.PreviousResidence, resident.Notes, resident.ResidenceStatus);

    /// <summary>Dùng chung với ResidentChangeService (chuyển đến, tạm trú) để hai nơi chuẩn hóa nhân khẩu giống nhau.</summary>
    internal static ResidentEntity CreateResident(ResidentProfileRequest request)
    {
        var resident = new ResidentEntity();
        UpdateResident(resident, request);
        return resident;
    }

    private static void UpdateResident(ResidentEntity resident, ResidentProfileRequest request)
    {
        resident.FullName = NormalizeRequired(request.FullName);
        resident.Alias = NormalizeOptional(request.Alias);
        resident.DateOfBirth = NormalizeDate(request.DateOfBirth);
        resident.Gender = NormalizeRequired(request.Gender);
        resident.PlaceOfBirth = NormalizeOptional(request.PlaceOfBirth);
        resident.Hometown = NormalizeOptional(request.Hometown);
        resident.Ethnicity = NormalizeOptional(request.Ethnicity);
        resident.Religion = NormalizeOptional(request.Religion);
        resident.Occupation = NormalizeOptional(request.Occupation);
        resident.Workplace = NormalizeOptional(request.Workplace);
        resident.IdentityNumber = NormalizeOptional(request.IdentityNumber);
        resident.IdentityIssuedAt = request.IdentityIssuedAt is { } issuedAt
            ? NormalizeDate(issuedAt)
            : null;
        resident.IdentityIssuePlace = NormalizeOptional(request.IdentityIssuePlace);
        resident.PreviousResidence = NormalizeOptional(request.PreviousResidence);
        resident.Notes = NormalizeOptional(request.Notes);
    }

    private static string NormalizeRequired(string value) => value.Trim();
    private static string NormalizeCode(string value) => value.Trim().ToUpperInvariant();
    internal static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTimeOffset NormalizeDate(DateTimeOffset value) =>
        // These are calendar dates, so preserve the submitted date instead of shifting it by its UTC offset.
        new(value.Year, value.Month, value.Day, 0, 0, 0, TimeSpan.Zero);
}
