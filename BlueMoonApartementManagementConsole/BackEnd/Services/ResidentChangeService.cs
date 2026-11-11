using BlueMoonApartementManagementConsole.BackEnd.Models;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlueMoonApartementManagementConsole.BackEnd.Services;

public interface IResidentChangeService
{
    Task<HouseholdResidencyResponse?> GetHouseholdResidencyAsync(int householdId, CancellationToken ct = default);
    Task<ProfileOperationResult<HouseholdResidencyResponse>> RecordAsync(
        int actorId, string actorName, int householdId, RecordResidentChangeRequest request, CancellationToken ct = default);
}

/// <summary>
/// Ghi nhận biến động nhân khẩu (PBI-007, US-007, FEA-006).
/// Sprint 1 chỉ BQT ghi nhận và biến động có hiệu lực ngay (ghi chú WBS 4.09); phần cư dân tự khai báo,
/// chờ BQT duyệt (AC-004) làm cùng PBI-006.
///
/// Tình trạng cư trú đổi theo bảng sau, loại biến động không có trong bảng thì bị từ chối:
///   ChuyenDen: (nhân khẩu mới) → ThuongTru     TamTru: (nhân khẩu mới) → TamTru
///   TamVang:   ThuongTru → TamVang             TroVe:  TamVang → ThuongTru
///   ChuyenDi:  ThuongTru / TamTru / TamVang → DaChuyenDi (giữ hồ sơ, không xóa)
/// </summary>
public sealed class ResidentChangeService(
    IApartmentProfileRepository profiles,
    IResidentChangeRepository changes,
    IAuditService audit,
    TimeProvider clock) : IResidentChangeService
{
    public async Task<HouseholdResidencyResponse?> GetHouseholdResidencyAsync(int householdId, CancellationToken ct = default)
    {
        var household = await profiles.GetHouseholdByIdAsync(householdId, ct);
        if (household is null)
            return null;

        return Map(household, await changes.GetByHouseholdAsync(householdId, ct));
    }

    public async Task<ProfileOperationResult<HouseholdResidencyResponse>> RecordAsync(
        int actorId, string actorName, int householdId, RecordResidentChangeRequest request, CancellationToken ct = default)
    {
        var household = await profiles.GetHouseholdByIdAsync(householdId, ct);
        if (household is null)
            return new(ProfileStatus.NotFound, Message: "Không tìm thấy hộ gia đình.");

        var place = ApartmentProfileService.NormalizeOptional(request.Place);
        var error = ValidateDates(request) ?? ValidatePlace(request.Type, place);
        if (error is not null)
            return Invalid(error);

        ResidentEntity resident;
        if (CreatesResident(request.Type))
        {
            error = await ValidateNewResidentAsync(request, ct);
            if (error is not null)
                return Invalid(error);

            resident = ApartmentProfileService.CreateResident(request.NewResident!);
            resident.ResidenceStatus = StatusAfter(request.Type);
            // Nơi ở trước (chuyển đến) / nơi thường trú (tạm trú) chỉ nhập một lần, ở ô nơi đi/đến của biến động
            resident.PreviousResidence ??= place;
            household.Residents.Add(resident);
        }
        else
        {
            if (request.NewResident is not null)
                return Invalid("Chỉ chuyển đến và đăng ký tạm trú mới khai báo nhân khẩu mới.");
            if (request.ResidentId is not int residentId)
                return Invalid("Hãy chọn nhân khẩu cần ghi nhận biến động.");

            var existing = household.Residents.SingleOrDefault(r => r.Id == residentId);
            if (existing is null)
                return Invalid("Nhân khẩu không thuộc hộ gia đình này.");

            error = ValidateTransition(request.Type, existing.ResidenceStatus);
            if (error is not null)
                return Invalid(error);

            // Biến động mới không được ghi lùi về trước lần gần nhất, để lịch sử luôn theo đúng thứ tự thời gian
            var history = await changes.GetByHouseholdAsync(householdId, ct);
            var lastDate = history.Where(c => c.ResidentId == existing.Id)
                .Select(c => (DateOnly?)c.FromDate)
                .Max();
            if (lastDate is { } last && request.FromDate < last)
                return Invalid($"Ngày biến động không được trước lần biến động gần nhất ({last:dd/MM/yyyy}).");

            existing.ResidenceStatus = StatusAfter(request.Type);
            resident = existing;
        }

        await changes.AddAsync(new ResidentChangeEntity
        {
            HouseholdId = household.Id,
            Resident = resident, // nhân khẩu mới chưa có Id, EF tự gán ResidentId khi lưu
            Type = request.Type,
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            Place = place,
            Reason = ApartmentProfileService.NormalizeOptional(request.Reason),
            RecordedById = actorId,
            RecordedAt = clock.GetUtcNow()
        }, ct);

        if (!await SaveAsync(ct))
            return new(ProfileStatus.Conflict, Message: "Số CMND/CCCD đã được sử dụng.");

        // Không ghi họ tên vào nhật ký (dữ liệu cá nhân), chỉ ghi mã nhân khẩu và số hộ như các thao tác hồ sơ khác
        var period = request.ToDate is { } to ? $" đến {to:dd/MM/yyyy}" : string.Empty;
        await audit.LogAsync(AuditAction.RecordResidentChange, actorName, actorId, resident.Id.ToString(),
            $"{Label(request.Type)}: nhân khẩu #{resident.Id}, hộ {household.HouseholdNumber}, từ {request.FromDate:dd/MM/yyyy}{period}",
            ct);

        return new(ProfileStatus.Success, await GetHouseholdResidencyAsync(householdId, ct));
    }

    // ---- Quy tắc (AC-002: dữ liệu sai thì từ chối và chỉ rõ trường cần sửa) ----

    private string? ValidateDates(RecordResidentChangeRequest request)
    {
        if (request.FromDate == default)
            return "Ngày biến động là bắt buộc.";
        if (request.FromDate > Today())
            return "Ngày biến động không được sau ngày hiện tại.";

        var hasEndDate = request.Type is ResidentChangeType.TamTru or ResidentChangeType.TamVang;
        if (hasEndDate && request.ToDate is null)
            return "Tạm trú, tạm vắng phải có ngày kết thúc dự kiến.";
        if (!hasEndDate && request.ToDate is not null)
            return "Chỉ tạm trú, tạm vắng mới có ngày kết thúc.";
        if (request.ToDate is { } to && to <= request.FromDate)
            return "Ngày kết thúc phải sau ngày bắt đầu.";
        return null;
    }

    private static string? ValidatePlace(ResidentChangeType type, string? place) => (type, place) switch
    {
        (ResidentChangeType.TamVang, null) => "Hãy nhập nơi đến trong thời gian tạm vắng.",
        (ResidentChangeType.ChuyenDi, null) => "Hãy nhập nơi chuyển đến.",
        _ => null
    };

    private async Task<string?> ValidateNewResidentAsync(RecordResidentChangeRequest request, CancellationToken ct)
    {
        if (request.ResidentId is not null)
            return "Chuyển đến, tạm trú là khai báo nhân khẩu mới, không chọn nhân khẩu có sẵn.";
        if (request.NewResident is not { } resident)
            return "Hãy nhập thông tin nhân khẩu mới.";
        if (resident.Id is not null)
            return "Nhân khẩu mới không được có mã nhân khẩu.";

        // Hai kiểm tra dưới giống ApartmentProfileService.ValidateResidentsAsync
        if (resident.DateOfBirth == default)
            return "Ngày sinh nhân khẩu là bắt buộc.";
        if (resident.IdentityIssuedAt == DateTimeOffset.MinValue)
            return "Ngày cấp giấy tờ không hợp lệ.";

        var identityNumber = ApartmentProfileService.NormalizeOptional(resident.IdentityNumber);
        if (identityNumber is not null && await profiles.ResidentIdentityExistsAsync(identityNumber, null, ct))
            return "Số CMND/CCCD đã được sử dụng.";
        return null;
    }

    private static string? ValidateTransition(ResidentChangeType type, ResidenceStatus status) => (type, status) switch
    {
        (_, ResidenceStatus.DaChuyenDi) => "Nhân khẩu đã chuyển đi, không ghi nhận thêm biến động.",
        (ResidentChangeType.TamVang, ResidenceStatus.TamVang) => "Nhân khẩu đang tạm vắng.",
        (ResidentChangeType.TamVang, not ResidenceStatus.ThuongTru) => "Chỉ nhân khẩu thường trú mới khai báo tạm vắng.",
        (ResidentChangeType.TroVe, not ResidenceStatus.TamVang) => "Chỉ nhân khẩu đang tạm vắng mới ghi nhận trở về.",
        _ => null
    };

    private static bool CreatesResident(ResidentChangeType type) =>
        type is ResidentChangeType.ChuyenDen or ResidentChangeType.TamTru;

    private static ResidenceStatus StatusAfter(ResidentChangeType type) => type switch
    {
        ResidentChangeType.TamTru => ResidenceStatus.TamTru,
        ResidentChangeType.TamVang => ResidenceStatus.TamVang,
        ResidentChangeType.ChuyenDi => ResidenceStatus.DaChuyenDi,
        _ => ResidenceStatus.ThuongTru // ChuyenDen, TroVe
    };

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private static string Label(ResidentChangeType type) =>
        ResidentChangeLabels.Types.First(x => x.Type == type).Label;

    // ---- Helpers ----

    private async Task<bool> SaveAsync(CancellationToken ct)
    {
        try
        {
            await changes.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Hai người cùng thêm một số CCCD: unique index trên IdentityNumberHash chặn người lưu sau
            return false;
        }
    }

    private static bool IsUniqueViolation(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                return true;
        }
        return false;
    }

    private static ProfileOperationResult<HouseholdResidencyResponse> Invalid(string message) =>
        new(ProfileStatus.Invalid, Message: message);

    private static HouseholdResidencyResponse Map(HouseholdEntity household, IReadOnlyList<ResidentChangeEntity> history)
    {
        var ordered = history.OrderByDescending(c => c.FromDate).ThenByDescending(c => c.Id).ToList();
        var names = household.Residents.ToDictionary(r => r.Id, r => r.FullName);

        var residents = household.Residents
            .OrderBy(r => r.ResidenceStatus == ResidenceStatus.DaChuyenDi) // người đã chuyển đi xếp cuối
            .ThenBy(r => r.Id)
            .Select(r =>
            {
                var latest = ordered.FirstOrDefault(c => c.ResidentId == r.Id);
                // Theo bảng chuyển trạng thái, đang tạm trú/tạm vắng thì lần gần nhất chính là lần khai báo đó
                var until = r.ResidenceStatus is ResidenceStatus.TamTru or ResidenceStatus.TamVang ? latest?.ToDate : null;
                return new ResidentResidencyResponse(r.Id, r.FullName, r.DateOfBirth, r.Gender,
                    r.ResidenceStatus, until, latest?.FromDate);
            })
            .ToList();

        var rows = ordered
            .Select(c => new ResidentChangeResponse(c.Id, c.ResidentId,
                names.GetValueOrDefault(c.ResidentId, $"#{c.ResidentId}"),
                c.Type, c.FromDate, c.ToDate, c.Place, c.Reason,
                c.RecordedBy?.Username ?? string.Empty, c.RecordedAt))
            .ToList();

        return new(household.Id, household.ApartmentId, household.HouseholdNumber, residents, rows);
    }
}
