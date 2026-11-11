using System.Text.RegularExpressions;
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
                    ?? ValidateRate(feeType, request.Rate, request.EffectiveFrom, request.EffectiveTo);
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
            CreatedById = actorId,
            CreatedAt = clock.GetUtcNow(),
            Rates =
            [
                new FeeRateEntity
                {
                    Rate = request.Rate,
                    EffectiveFrom = request.EffectiveFrom,
                    EffectiveTo = request.EffectiveTo
                }
            ]
        };

        await repo.AddAsync(entity, ct);
        if (!await SaveAsync(ct))
            return Conflict<FeeItemResponse>("Mã khoản thu đã tồn tại.");

        await audit.LogAsync(AuditAction.CreateFeeItem, actorName, actorId,
            entity.Id.ToString(), $"Tạo khoản thu {entity.Code} - {entity.Name}", ct);

        return new(FeeItemStatus.Success, Map(entity));
    }

    public async Task<FeeItemOperationResult<FeeItemResponse>> ChangeRateAsync(
        int actorId, string actorName, int id, ChangeRateRequest request, CancellationToken ct = default)
    {
        var entity = await repo.GetByIdAsync(id, tracking: true, ct);
        if (entity is null)
            return new(FeeItemStatus.NotFound, Message: "Không tìm thấy khoản thu.");

        var error = ValidateRate(entity.FeeType, request.Rate, request.EffectiveFrom, null);
        if (error is not null)
            return Invalid<FeeItemResponse>(error);

        var current = entity.Rates.OrderByDescending(r => r.EffectiveFrom).FirstOrDefault();
        if (current is not null && request.EffectiveFrom <= current.EffectiveFrom)
            return Conflict<FeeItemResponse>("Thời điểm hiệu lực phải sau thời điểm bắt đầu của đơn giá hiện tại.");

        var oldText = current?.Rate.ToString("N0") ?? "chưa có";

        // Close the current rate at the new date. Do not delete it, so history is kept (AC-007).
        if (current is not null && (current.EffectiveTo is null || current.EffectiveTo > request.EffectiveFrom))
            current.EffectiveTo = request.EffectiveFrom;

        entity.Rates.Add(new FeeRateEntity
        {
            Rate = request.Rate,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = null
        });

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
            f.Unit, f.Note,
            f.Rates
                .OrderByDescending(r => r.EffectiveFrom)
                .Select(r => new FeeRateResponse(r.Id, r.Rate, r.EffectiveFrom, r.EffectiveTo))
                .ToList());
}
