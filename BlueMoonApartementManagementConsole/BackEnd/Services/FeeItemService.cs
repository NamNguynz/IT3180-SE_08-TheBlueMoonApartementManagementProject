using System.Text.RegularExpressions;
using BlueMoonApartementManagementConsole.BackEnd.Models;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlueMoonApartementManagementConsole.BackEnd.Services;

public interface IFeeItemService
{
    Task<IReadOnlyList<FeeItemResponse>> GetAllAsync(CancellationToken ct = default);
    Task<FeeItemResponse?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<FeeItemOperationResult<FeeItemResponse>> CreateAsync(
        int actorId, string actorName, CreateFeeItemRequest request, CancellationToken ct = default);
    Task<FeeItemOperationResult<FeeItemResponse>> ChangeRateAsync(
        int actorId, string actorName, int id, ChangeRateRequest request, CancellationToken ct = default);
    Task<FeeRateLookupResponse?> GetRateOnDateAsync(int id, DateOnly? date, CancellationToken ct = default);
    Task<FeeTypeRateLookupResponse?> GetTypeRatesOnDateAsync(int id, DateOnly? date, CancellationToken ct = default);
}

public sealed class FeeItemService(
    IFeeItemRepository repo,
    IAuditService audit,
    TimeProvider clock) : IFeeItemService
{
    // FEA-012: maintenance fee is only built once the business confirms it. Set to true after confirmation.
    private const bool MaintenanceFeeEnabled = false;

    private static readonly Regex CodeRegex = new("^[A-Z0-9_-]+$", RegexOptions.Compiled);

    public async Task<IReadOnlyList<FeeItemResponse>> GetAllAsync(CancellationToken ct = default) =>
        (await repo.GetAllAsync(ct)).Select(Map).ToList();

    public async Task<FeeItemResponse?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var entity = await repo.GetByIdAsync(id, tracking: false, ct);
        return entity is null ? null : Map(entity);
    }

    public async Task<FeeItemOperationResult<FeeItemResponse>> CreateAsync(
        int actorId, string actorName, CreateFeeItemRequest request, CancellationToken ct = default)
    {
        if (request.FeeType is not FeeType feeType
            || request.FeeScope is not FeeScope feeScope
            || request.FeeCycle is not FeeCycle feeCycle
            || request.CalculationBasis is not CalculationBasis basis)
            return Invalid<FeeItemResponse>("Thiếu loại khoản thu, phạm vi, chu kỳ hoặc cơ sở tính.");

        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();
        var unit = request.Unit.Trim();

        var error = ValidateInfo(code, name, unit)
                    ?? ValidateMode(basis, request.RateMode, request.TypeRates);
        if (error is not null)
            return Invalid<FeeItemResponse>(error);

        // Chế độ Chung giữ nguyên luồng cũ; chế độ TheoLoai chỉ có ba giá riêng, không tạo giá chung.
        error = request.RateMode == RateMode.TheoLoai
            ? ValidateTypeRates(feeType, request.TypeRates, request.EffectiveFrom, request.EffectiveTo)
            : ValidateRate(feeType, request.Rate, request.EffectiveFrom, request.EffectiveTo);
        if (error is not null)
            return Invalid<FeeItemResponse>(error);

        if (feeType == FeeType.BaoTriRieng && !MaintenanceFeeEnabled)
            return new(FeeItemStatus.FeatureUnavailable, Message: "Tính năng không khả dụng.");

        if (await repo.CodeExistsAsync(code, null, ct))
            return Conflict<FeeItemResponse>("Mã khoản thu đã tồn tại.");

        var entity = new FeeItemEntity
        {
            Code = code,
            Name = name,
            FeeType = feeType,
            FeeScope = feeScope,
            FeeCycle = feeCycle,
            CalculationBasis = basis,
            Unit = unit,
            Note = NormalizeOptional(request.Note),
            RateMode = request.RateMode,
            CreatedById = actorId,
            CreatedAt = clock.GetUtcNow(),
            Rates = request.RateMode == RateMode.Chung
                ? [new FeeRateEntity
                    {
                        Rate = request.Rate,
                        EffectiveFrom = request.EffectiveFrom,
                        EffectiveTo = request.EffectiveTo
                    }]
                : [],
            TypeRates = request.RateMode == RateMode.TheoLoai
                ? request.TypeRates.Select(r => ToTypeRate(r, request.EffectiveFrom)).ToList()
                : []
        };

        await repo.AddAsync(entity, ct);
        if (!await SaveAsync(ct))
            return Conflict<FeeItemResponse>("Mã khoản thu đã tồn tại hoặc đã có đơn giá với thời điểm hiệu lực này.");

        await audit.LogAsync(AuditAction.CreateFeeItem, actorName, actorId,
            entity.Id.ToString(),
            $"Tạo khoản thu {entity.Code} - {entity.Name}" +
            (entity.RateMode == RateMode.TheoLoai ? " (đơn giá theo loại căn hộ)" : ""), ct);

        return new(FeeItemStatus.Success, Map(entity));
    }

    public async Task<FeeItemOperationResult<FeeItemResponse>> ChangeRateAsync(
        int actorId, string actorName, int id, ChangeRateRequest request, CancellationToken ct = default)
    {
        var entity = await repo.GetByIdAsync(id, tracking: true, ct);
        if (entity is null)
            return new(FeeItemStatus.NotFound, Message: "Không tìm thấy khoản thu.");

        var error = ValidateMode(entity.CalculationBasis, request.RateMode, request.TypeRates);
        if (error is not null)
            return Invalid<FeeItemResponse>(error);

        if (request.RateMode == RateMode.TheoLoai)
        {
            error = ValidateTypeRates(entity.FeeType, request.TypeRates, request.EffectiveFrom, null);
            if (error is not null)
                return Invalid<FeeItemResponse>(error);

            var currentTypes = entity.TypeRates.OrderByDescending(r => r.EffectiveFrom).FirstOrDefault();
            if (currentTypes is not null && request.EffectiveFrom <= currentTypes.EffectiveFrom)
                return Conflict<FeeItemResponse>("Thời điểm hiệu lực phải sau thời điểm bắt đầu của đơn giá hiện tại.");

            var changes = new List<string>();
            foreach (var input in request.TypeRates)
            {
                var label = ApartmentTypeLabels.All.First(t => t.Type == input.ApartmentType).Label;
                var current = entity.TypeRates
                    .Where(r => r.ApartmentType == input.ApartmentType)
                    .OrderByDescending(r => r.EffectiveFrom)
                    .FirstOrDefault();
                changes.Add($"{label}: {current?.Rate.ToString("N0") ?? "chưa có"} → {input.Rate:N0}");
                // Không xóa dòng cũ, chỉ đóng hiệu lực để giữ lịch sử (AC-007).
                if (current is not null && (current.EffectiveTo is null || current.EffectiveTo > request.EffectiveFrom))
                    current.EffectiveTo = request.EffectiveFrom;
            }
            entity.TypeRates.AddRange(request.TypeRates.Select(r => new FeeTypeRateEntity
            {
                ApartmentType = r.ApartmentType,
                Rate = r.Rate,
                EffectiveFrom = request.EffectiveFrom,
                EffectiveTo = null
            }));
            entity.RateMode = RateMode.TheoLoai;

            if (!await SaveAsync(ct))
                return Conflict<FeeItemResponse>("Đã có đơn giá với thời điểm hiệu lực này.");

            await audit.LogAsync(AuditAction.ChangeRate, actorName, actorId, entity.Id.ToString(),
                $"Đổi đơn giá {entity.Code} theo loại căn hộ ({string.Join(", ", changes)}) " +
                $"từ {request.EffectiveFrom:dd/MM/yyyy}", ct);
            return new(FeeItemStatus.Success, Map(entity));
        }

        error = ValidateRate(entity.FeeType, request.Rate, request.EffectiveFrom, null);
        if (error is not null)
            return Invalid<FeeItemResponse>(error);

        var currentCommon = entity.Rates.OrderByDescending(r => r.EffectiveFrom).FirstOrDefault();
        if (currentCommon is not null && request.EffectiveFrom <= currentCommon.EffectiveFrom)
            return Conflict<FeeItemResponse>("Thời điểm hiệu lực phải sau thời điểm bắt đầu của đơn giá hiện tại.");

        var oldText = currentCommon?.Rate.ToString("N0") ?? "chưa có";
        if (currentCommon is not null && (currentCommon.EffectiveTo is null || currentCommon.EffectiveTo > request.EffectiveFrom))
            currentCommon.EffectiveTo = request.EffectiveFrom;

        entity.Rates.Add(new FeeRateEntity
        {
            Rate = request.Rate,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = null
        });
        entity.RateMode = RateMode.Chung;

        if (!await SaveAsync(ct))
            return Conflict<FeeItemResponse>("Đã có đơn giá với thời điểm hiệu lực này.");

        await audit.LogAsync(AuditAction.ChangeRate, actorName, actorId, entity.Id.ToString(),
            $"Đổi đơn giá {entity.Code}: {oldText} → {request.Rate:N0} từ {request.EffectiveFrom:dd/MM/yyyy}", ct);

        return new(FeeItemStatus.Success, Map(entity));
    }

    public async Task<FeeRateLookupResponse?> GetRateOnDateAsync(int id, DateOnly? date, CancellationToken ct = default)
    {
        var entity = await repo.GetByIdAsync(id, tracking: false, ct);
        if (entity is null)
            return null;

        var day = date ?? Today();
        // Cùng quy tắc chọn đơn giá với bảng kê (PBI-015)
        var rate = FeeCalculator.RateOn(entity.Rates, day);
        if (rate is null)
            return null;

        return new FeeRateLookupResponse(entity.Id, entity.Code, entity.Name,
            entity.CalculationBasis, entity.Unit, day, rate.Rate, rate.EffectiveFrom, rate.EffectiveTo);
    }

    /// <summary>
    /// Ba giá theo loại căn hộ có hiệu lực vào ngày đã cho. Thiếu loại thì danh sách ngắn hơn ba phần tử,
    /// để giao diện báo rõ chưa có giá (không fallback về giá chung).
    /// </summary>
    public async Task<FeeTypeRateLookupResponse?> GetTypeRatesOnDateAsync(
        int id, DateOnly? date, CancellationToken ct = default)
    {
        var entity = await repo.GetByIdAsync(id, tracking: false, ct);
        if (entity is null)
            return null;

        var day = date ?? Today();
        var rates = ApartmentTypeLabels.All
            .Select(t => FeeCalculator.RateOn(entity.Rates, entity.TypeRates, day, t.Type, RateMode.TheoLoai))
            .Where(r => r is not null)
            .Select(r => new FeeTypeRateResponse(
                r!.SourceId, r.ApartmentType!.Value, r.Rate, r.EffectiveFrom,
                entity.TypeRates.First(x => x.Id == r.SourceId).EffectiveTo))
            .ToList();

        return new FeeTypeRateLookupResponse(entity.Id, entity.Code, entity.Name,
            entity.CalculationBasis, entity.Unit, entity.RateMode, day, rates);
    }

    // ---- Validation rules (AC-006, AC-009) ----

    private static string? ValidateInfo(string code, string name, string unit)
    {
        if (code.Length == 0 || code.Length > 30 || !CodeRegex.IsMatch(code))
            return "Mã khoản thu chỉ gồm chữ in hoa, số, gạch dưới hoặc gạch ngang, tối đa 30 ký tự.";
        if (name.Length == 0 || name.Length > 100)
            return "Tên khoản thu là bắt buộc, tối đa 100 ký tự.";
        if (unit.Length == 0 || unit.Length > 20)
            return "Đơn vị tính là bắt buộc, tối đa 20 ký tự.";
        return null;
    }

    /// <summary>Chế độ TheoLoai chỉ áp dụng cho khoản tính theo diện tích và cần đủ ba loại căn hộ.</summary>
    private static string? ValidateMode(
        CalculationBasis basis, RateMode mode, IReadOnlyList<FeeTypeRateRequest> typeRates)
    {
        if (mode == RateMode.Chung)
            return null;
        if (basis != CalculationBasis.DienTich)
            return "Đơn giá theo loại căn hộ chỉ áp dụng cho khoản thu tính theo diện tích.";
        if (typeRates.Count != ApartmentTypeLabels.All.Count
            || typeRates.Select(r => r.ApartmentType).Distinct().Count() != ApartmentTypeLabels.All.Count
            || ApartmentTypeLabels.All.Any(t => typeRates.All(r => r.ApartmentType != t.Type)))
            return "Chế độ theo loại căn hộ cần khai báo đủ ba giá: Căn hộ ở, Kiot và Penthouse.";
        return null;
    }

    private string? ValidateTypeRates(
        FeeType feeType, IReadOnlyList<FeeTypeRateRequest> rates, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        foreach (var rate in rates)
        {
            var label = ApartmentTypeLabels.All.FirstOrDefault(t => t.Type == rate.ApartmentType).Label
                        ?? rate.ApartmentType.ToString();
            var error = ValidateRate(feeType, rate.Rate, effectiveFrom, effectiveTo);
            if (error is not null)
                return $"{label}: {error}";
        }
        return null;
    }

    private string? ValidateRate(FeeType feeType, decimal rate, DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        if (rate < 0)
            return "Đơn giá không được âm.";
        if (feeType == FeeType.BatBuoc && rate <= 0)
            return "Khoản thu bắt buộc phải có đơn giá lớn hơn 0.";
        if (effectiveFrom < Today())
            return "Ngày hiệu lực không được trước ngày hiện tại.";
        if (effectiveTo is { } to && to <= effectiveFrom)
            return "Ngày hết hiệu lực phải sau ngày hiệu lực.";
        return null;
    }

    private static FeeTypeRateEntity ToTypeRate(FeeTypeRateRequest r, DateOnly effectiveFrom) => new()
    {
        ApartmentType = r.ApartmentType,
        Rate = r.Rate,
        EffectiveFrom = effectiveFrom,
        EffectiveTo = null
    };

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    // ---- Helpers ----

    private async Task<bool> SaveAsync(CancellationToken ct)
    {
        try
        {
            await repo.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
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

    private static FeeItemOperationResult<T> Invalid<T>(string message) =>
        new(FeeItemStatus.Invalid, Message: message);

    private static FeeItemOperationResult<T> Conflict<T>(string message) =>
        new(FeeItemStatus.Conflict, Message: message);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static FeeItemResponse Map(FeeItemEntity f) =>
        new(f.Id, f.Code, f.Name, f.FeeType, f.FeeScope, f.FeeCycle, f.CalculationBasis,
            f.RateMode, f.Unit, f.Note,
            f.Rates
                .OrderByDescending(r => r.EffectiveFrom)
                .Select(r => new FeeRateResponse(r.Id, r.Rate, r.EffectiveFrom, r.EffectiveTo))
                .ToList(),
            f.TypeRates
                .OrderBy(r => r.ApartmentType)
                .ThenByDescending(r => r.EffectiveFrom)
                .Select(r => new FeeTypeRateResponse(r.Id, r.ApartmentType, r.Rate, r.EffectiveFrom, r.EffectiveTo))
                .ToList());
}
