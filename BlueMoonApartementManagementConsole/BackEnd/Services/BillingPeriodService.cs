using BlueMoonApartementManagementConsole.BackEnd.Models;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;

namespace BlueMoonApartementManagementConsole.BackEnd.Services;

public interface IBillingPeriodService
{
    Task<IReadOnlyList<BillingPeriodSummaryResponse>> GetAllAsync(CancellationToken ct = default);
    Task<BillingPeriodResponse?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<FeeItemOperationResult<BillingPeriodResponse>> CreateAsync(
        int actorId, string actorName, CreateBillingPeriodRequest request, CancellationToken ct = default);

    /// <summary>Các hộ để chọn trong pop-up của khoản thu chỉ định hộ, xếp theo mã căn.</summary>
    Task<IReadOnlyList<HouseholdOptionResponse>> GetHouseholdOptionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Tính thử số phải thu của một hộ cho một khoản thu vào một ngày (mặc định hôm nay), không lưu gì (PBI-015).
    /// Cùng các bước kiểm, công thức, chế độ đơn giá và cơ sở tính với bảng kê.
    /// </summary>
    Task<FeeItemOperationResult<FeeCalculationResponse>> CalculateAsync(
        int feeItemId, int householdId, DateOnly? date, RateMode mode, CancellationToken ct = default);
}

/// <summary>
/// Lập kỳ thu và bảng kê số tiền phải thu theo hộ (PBI-014, US-014, LOG-033).
///
/// Cách lập bảng kê (CreateAsync):
///   1. Kiểm tra tháng, ngày tính phí, hạn nộp. Lập được kỳ của các tháng đã qua, tháng này và tháng sau
///      (lập trước để kịp báo phí cho cư dân), không lập xa hơn. Ngày tính phí phải nằm trong tháng của kỳ,
///      để phí tháng nào lấy đơn giá của tháng đó.
///   2. Kiểm tra từng khoản thu: phải là khoản bắt buộc, có đơn giá hiệu lực vào ngày tính phí theo chế độ đã chọn.
///      Khoản tính theo diện tích có thể dùng giá chung hoặc ba giá theo loại căn hộ (Chung/TheoLoai);
///      TheoLoai cần đủ ba giá, thiếu giá thì báo lỗi, không lập kỳ. Các khoản khác chỉ dùng giá chung.
///   3. Chặn thu trùng một khoản theo chu kỳ (tháng/năm/một lần), không phân biệt phạm vi áp dụng.
///      Một tháng chỉ có một kỳ thu: nếu tháng đó đã có kỳ thì bổ sung khoản mới vào kỳ đó thay vì lập kỳ trùng tên,
///      để báo cáo thu và công nợ tổng hợp được cả tháng (PBI-022/PBI-044).
///   4. Xác định hộ phải nộp từng khoản: khoản toàn chung cư là mọi hộ, khoản chỉ định hộ là các hộ được chọn
///      (ít nhất một hộ).
///   5. Với mỗi khoản thu và mỗi hộ phải nộp: số phải thu = đơn giá × cơ sở tính, làm tròn đến đồng, theo FeeCalculator
///      (PBI-015). Đơn giá thực tế (giá chung hoặc giá của loại căn) được chụp lại ở Receivable.Rate.
///      Căn hộ thiếu diện tích cho khoản theo diện tích thì từ chối cả kỳ (cơ sở tính chưa cấu hình).
///      Hộ không phải nộp đồng nào (cơ sở tính bằng 0, hoặc số tiền làm tròn về 0) thì không có dòng, nên không hiện
///      ở trang thu tiền. Bảng kê không có dòng nào thì không lập/bổ sung kỳ.
///   6. Lưu kỳ thu (hoặc các khoản bổ sung) cùng đơn giá đã áp dụng và các dòng phải thu trong một lần lưu, rồi ghi nhật ký.
///      Đổi đơn giá về sau không làm thay đổi số đã chụp của kỳ này.
/// Các khoản bắt buộc của một hộ cộng lại thành một số phải nộp cho cả kỳ (gom thành một lần thu).
/// </summary>
public sealed class BillingPeriodService(
    IBillingPeriodRepository repo,
    IAuditService audit,
    TimeProvider clock) : IBillingPeriodService
{
    /// <summary>Đơn giá đã chọn cho một khoản trong kỳ: một giá chung hoặc đủ ba giá theo loại căn.</summary>
    private sealed record ItemRates(
        RateMode Mode,
        EffectiveRate? Common,
        IReadOnlyDictionary<ApartmentType, EffectiveRate> ByType);

    public async Task<IReadOnlyList<BillingPeriodSummaryResponse>> GetAllAsync(CancellationToken ct = default) =>
        (await repo.GetSummariesAsync(ct))
            .Select(r => new BillingPeriodSummaryResponse(r.Id, r.Year, r.Month, r.ChargeDate, r.DueDate,
                r.ItemCount, r.HouseholdCount, r.TotalAmount, r.CreatedBy, r.CreatedAt))
            .ToList();

    public async Task<BillingPeriodResponse?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var period = await repo.GetDetailAsync(id, ct);
        return period is null ? null : Map(period);
    }

    public async Task<FeeItemOperationResult<BillingPeriodResponse>> CreateAsync(
        int actorId, string actorName, CreateBillingPeriodRequest request, CancellationToken ct = default)
    {
        // Bước 1: tháng, ngày tính phí, hạn nộp, danh sách khoản thu
        var error = ValidatePeriod(request);
        if (error is not null)
            return Invalid(error);

        // Bước 2: từng khoản thu phải hợp lệ và có đơn giá vào ngày tính phí theo chế độ đã chọn
        var feeItems = (await repo.GetFeeItemsAsync(request.FeeItemIds, ct)).OrderBy(f => f.Code).ToList();
        var missing = request.FeeItemIds.Where(id => feeItems.All(f => f.Id != id)).ToList();
        if (missing.Count > 0)
            return Invalid($"Không tìm thấy khoản thu #{missing[0]}.");

        var modes = ParseRateChoices(request.RateChoices ?? [], feeItems, out var choiceError);
        if (choiceError is not null)
            return Invalid(choiceError);

        var rates = new Dictionary<int, ItemRates>();
        foreach (var feeItem in feeItems)
        {
            var mode = modes.GetValueOrDefault(feeItem.Id, RateMode.Chung);
            var (selected, rateError) = BillableRates(feeItem, request.ChargeDate, mode);
            if (selected is null)
                return Invalid(rateError!);
            rates[feeItem.Id] = selected;
        }

        // Bước 3: một khoản chỉ được lập một lần trong chu kỳ của nó, dù áp dụng cho cả chung cư hay chỉ định hộ.
        // Khoản chỉ định hộ phát sinh thêm trong cùng tháng thì bổ sung vào kỳ có sẵn (xem bước 6), không lập kỳ mới.
        var billed = await repo.GetBilledItemsAsync(request.FeeItemIds, ct);
        foreach (var feeItem in feeItems)
        {
            var duplicate = billed.FirstOrDefault(b => b.FeeItemId == feeItem.Id && SameCycle(feeItem.FeeCycle, b, request));
            if (duplicate is not null)
                return new(FeeItemStatus.Conflict, Message:
                    $"Khoản thu {feeItem.Code} đã được lập trong kỳ thu tháng {duplicate.Month:00}/{duplicate.Year}.");
        }

        var households = await repo.GetHouseholdBasesAsync(ct);
        if (households.Count == 0)
            return Invalid("Chưa có hộ gia đình nào để lập bảng kê.");

        // Bước 4: hộ phải nộp của từng khoản (khoản chỉ định hộ thì danh sách hộ được chọn phải hợp lệ)
        var targets = request.TargetedFeeItems ?? [];
        error = ValidateTargets(feeItems, targets, households);
        if (error is not null)
            return Invalid(error);
        var payersByFeeItem = feeItems.ToDictionary(f => f.Id, f => PayersOf(f, targets, households));

        // Bước 5: cơ sở tính của mọi hộ phải nộp phải hợp lệ, rồi mới tính (không lập bảng kê thiếu hộ một cách lặng lẽ)
        foreach (var feeItem in feeItems)
        {
            error = payersByFeeItem[feeItem.Id].Select(h => FeeCalculator.BasisError(feeItem, h)).FirstOrDefault(e => e is not null);
            if (error is not null)
                return Invalid(error);
        }
        // Một tháng một kỳ. Kỳ đã có thì giữ nguyên ngày tính phí/hạn nộp đã lập và chỉ bổ sung khoản mới;
        // đơn giá của từng khoản bổ sung vẫn được chụp theo ngày tính phí trong yêu cầu hiện tại.
        var existing = await repo.GetByMonthAsync(request.Year, request.Month, ct);
        var supplement = existing is not null;
        var period = existing ?? new BillingPeriodEntity
        {
            Year = request.Year,
            Month = request.Month,
            ChargeDate = request.ChargeDate,
            DueDate = request.DueDate,
            Note = NormalizeOptional(request.Note),
            CreatedById = actorId,
            CreatedAt = clock.GetUtcNow()
        };

        var newItems = new List<BillingPeriodItemEntity>();
        foreach (var feeItem in feeItems)
        {
            var selected = rates[feeItem.Id];
            var item = new BillingPeriodItemEntity { FeeItemId = feeItem.Id, RateMode = selected.Mode };
            if (selected.Mode == RateMode.Chung && selected.Common is { } common)
            {
                item.FeeRateId = common.SourceId;
                item.Rate = common.Rate;
            }
            else
            {
                // Chụp cả ba giá đã dùng để bảng chi tiết giải thích được kỳ thu ngay cả khi danh mục đổi giá sau này
                item.TypeRates.AddRange(ApartmentTypeLabels.All.Select(t =>
                {
                    var effective = selected.ByType[t.Type];
                    return new BillingPeriodItemTypeRateEntity
                    {
                        ApartmentType = t.Type,
                        Rate = effective.Rate,
                        FeeTypeRateId = effective.SourceId,
                        EffectiveFrom = effective.EffectiveFrom
                    };
                }));
            }

            foreach (var household in payersByFeeItem[feeItem.Id])
            {
                var quantity = FeeCalculator.QuantityFor(feeItem.CalculationBasis, household);
                if (quantity <= 0)
                    continue;

                var effective = RateFor(selected, household.ApartmentType);
                // Không phải nộp đồng nào thì không đưa vào bảng kê, để trang thu tiền không coi hộ đó là "Đã nộp đủ"
                var amount = FeeCalculator.AmountFor(effective.Rate, quantity);
                if (amount <= 0)
                    continue;

                item.Receivables.Add(new ReceivableEntity
                {
                    HouseholdId = household.HouseholdId,
                    Quantity = quantity,
                    Rate = effective.Rate,
                    Amount = amount
                });
            }
            newItems.Add(item);
        }
        if (newItems.All(i => i.Receivables.Count == 0))
            return Invalid("Bảng kê trống: không hộ nào phải nộp số tiền lớn hơn 0 đ, chưa lập kỳ thu.");
        period.Items.AddRange(newItems);

        // Bước 6: lưu một lần (kỳ hoặc khoản bổ sung, các dòng phải thu), rồi ghi nhật ký
        if (!supplement)
            await repo.AddAsync(period, ct);
        await repo.SaveChangesAsync(ct);

        var receivables = newItems.SelectMany(i => i.Receivables).ToList();
        var householdCount = receivables.Select(r => r.HouseholdId).Distinct().Count();
        var typedItemCount = newItems.Count(i => i.RateMode == RateMode.TheoLoai);
        await audit.LogAsync(AuditAction.CreateBillingPeriod, actorName, actorId, period.Id.ToString(),
            $"{(supplement ? "Bổ sung khoản thu vào kỳ thu tháng" : "Lập kỳ thu tháng")} " +
            $"{period.Month:00}/{period.Year}: {newItems.Count} khoản thu, " +
            $"{typedItemCount} khoản giá theo loại, {householdCount} hộ, " +
            $"tổng phải thu {receivables.Sum(r => r.Amount):N0} đ", ct);

        return new(FeeItemStatus.Success, await GetByIdAsync(period.Id, ct));
    }

    public async Task<IReadOnlyList<HouseholdOptionResponse>> GetHouseholdOptionsAsync(CancellationToken ct = default) =>
        (await repo.GetHouseholdBasesAsync(ct))
            .OrderBy(h => h.ApartmentCode, StringComparer.Ordinal)
            .ThenBy(h => h.HouseholdNumber, StringComparer.Ordinal)
            .Select(h => new HouseholdOptionResponse(h.HouseholdId, h.HouseholdNumber, h.ApartmentCode))
            .ToList();

    public async Task<FeeItemOperationResult<FeeCalculationResponse>> CalculateAsync(
        int feeItemId, int householdId, DateOnly? date, RateMode mode, CancellationToken ct = default)
    {
        var feeItem = (await repo.GetFeeItemsAsync([feeItemId], ct)).SingleOrDefault();
        if (feeItem is null)
            return new(FeeItemStatus.NotFound, Message: "Không tìm thấy khoản thu.");

        var household = await repo.GetHouseholdBasisAsync(householdId, ct);
        if (household is null)
            return new(FeeItemStatus.NotFound, Message: "Không tìm thấy hộ gia đình.");

        // Cùng các bước kiểm như khi lập bảng kê: loại khoản thu, đơn giá theo ngày, cơ sở tính của hộ
        var day = date ?? Today();
        var (selected, error) = BillableRates(feeItem, day, mode);
        if (selected is null)
            return new(FeeItemStatus.Invalid, Message: error);

        error = FeeCalculator.BasisError(feeItem, household);
        if (error is not null)
            return new(FeeItemStatus.Invalid, Message: error);

        var quantity = FeeCalculator.QuantityFor(feeItem.CalculationBasis, household);
        var effective = RateFor(selected!, household.ApartmentType);
        return new(FeeItemStatus.Success, new FeeCalculationResponse(
            feeItem.Id, feeItem.Code, feeItem.Name, feeItem.CalculationBasis, feeItem.Unit,
            household.HouseholdId, household.HouseholdNumber, household.ApartmentCode,
            day, quantity, selected!.Mode,
            selected.Mode == RateMode.TheoLoai ? household.ApartmentType : null,
            effective.Rate, effective.EffectiveFrom, FeeCalculator.AmountFor(effective.Rate, quantity)));
    }

    // ---- Quy tắc ----

    private string? ValidatePeriod(CreateBillingPeriodRequest request)
    {
        if (request.Year is < 2000 or > 2100 || request.Month is < 1 or > 12)
            return "Tháng của kỳ thu không hợp lệ.";

        var periodStart = new DateOnly(request.Year, request.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        // Lập trước được kỳ của tháng sau (ví dụ cuối tháng 11 lập kỳ tháng 12), không lập xa hơn
        var today = Today();
        var nextMonthStart = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        if (periodStart > nextMonthStart)
            return $"Chỉ lập trước được kỳ thu của tháng sau ({nextMonthStart.Month:00}/{nextMonthStart.Year}), " +
                   $"chưa lập được kỳ tháng {request.Month:00}/{request.Year}.";
        if (request.ChargeDate == default)
            return "Ngày tính phí là bắt buộc.";
        // Phí của tháng nào tính theo đơn giá hiệu lực trong tháng đó, kể cả khi lập bù kỳ cũ hay lập trước kỳ tháng sau
        if (request.ChargeDate < periodStart || request.ChargeDate > periodEnd)
            return $"Ngày tính phí phải nằm trong tháng của kỳ thu ({periodStart:dd/MM/yyyy} – {periodEnd:dd/MM/yyyy}).";
        if (request.DueDate == default)
            return "Hạn nộp là bắt buộc.";
        if (request.DueDate < request.ChargeDate)
            return "Hạn nộp không được trước ngày tính phí.";

        if (request.FeeItemIds.Count == 0)
            return "Hãy chọn ít nhất một khoản thu.";
        if (request.FeeItemIds.Distinct().Count() != request.FeeItemIds.Count)
            return "Danh sách khoản thu bị trùng.";
        return null;
    }

    /// <summary>
    /// Chế độ đơn giá do người dùng chọn ở kỳ thu. Mặc định Chung nếu client cũ không gửi;
    /// TheoLoai chỉ hợp lệ với khoản tính theo diện tích.
    /// </summary>
    private static Dictionary<int, RateMode> ParseRateChoices(
        IReadOnlyList<FeeRateChoiceRequest> choices, List<FeeItemEntity> feeItems, out string? error)
    {
        var modes = new Dictionary<int, RateMode>();
        foreach (var choice in choices)
        {
            if (choice is null || choice.FeeItemId == 0)
            {
                error = "Lựa chọn đơn giá không hợp lệ.";
                return modes;
            }
            var feeItem = feeItems.FirstOrDefault(f => f.Id == choice.FeeItemId);
            if (feeItem is null)
            {
                error = $"Có lựa chọn đơn giá cho khoản thu #{choice.FeeItemId} nhưng khoản này không được chọn vào kỳ.";
                return modes;
            }
            if (modes.ContainsKey(choice.FeeItemId))
            {
                error = $"Khoản thu {feeItem.Code} có nhiều hơn một lựa chọn đơn giá.";
                return modes;
            }
            if (choice.Mode == RateMode.TheoLoai && feeItem.CalculationBasis != CalculationBasis.DienTich)
            {
                error = $"Khoản thu {feeItem.Code} không tính theo diện tích, không thể dùng đơn giá theo loại căn hộ.";
                return modes;
            }
            modes[choice.FeeItemId] = choice.Mode;
        }
        error = null;
        return modes;
    }

    /// <summary>
    /// Đơn giá dùng để thu khoản này vào ngày đã cho theo chế độ đã chọn, hoặc lý do không thu được
    /// (khoản không đưa vào kỳ thu, chưa có đơn giá, thiếu một trong ba loại căn hộ).
    /// Dùng chung cho lập bảng kê và tính thử, để hai nơi luôn cho cùng kết quả.
    /// </summary>
    private static (ItemRates? Rates, string? Error) BillableRates(
        FeeItemEntity feeItem, DateOnly day, RateMode mode)
    {
        var error = ValidateFeeItem(feeItem);
        if (error is not null)
            return (null, error);

        if (mode == RateMode.TheoLoai)
        {
            if (feeItem.CalculationBasis != CalculationBasis.DienTich)
                return (null, $"Khoản thu {feeItem.Code} không tính theo diện tích, không thể dùng đơn giá theo loại căn hộ.");

            var byType = new Dictionary<ApartmentType, EffectiveRate>();
            foreach (var (type, label) in ApartmentTypeLabels.All)
            {
                var effective = FeeCalculator.RateOn(feeItem.Rates, feeItem.TypeRates, day, type, RateMode.TheoLoai);
                if (effective is null)
                    return (null, $"Khoản thu {feeItem.Code} chưa có đơn giá hiệu lực vào ngày {day:dd/MM/yyyy} " +
                                 $"cho loại căn hộ {label}.");
                byType[type] = effective;
            }
            return (new ItemRates(RateMode.TheoLoai, null, byType), null);
        }

        var common = FeeCalculator.RateOn(feeItem.Rates, feeItem.TypeRates, day, default, RateMode.Chung);
        return common is null
            ? (null, $"Khoản thu {feeItem.Code} chưa có đơn giá hiệu lực vào ngày {day:dd/MM/yyyy}.")
            : (new ItemRates(RateMode.Chung, common, new Dictionary<ApartmentType, EffectiveRate>()), null);
    }

    /// <summary>Giá thực tế áp dụng cho một hộ: giá chung, hoặc giá của đúng loại căn hộ của hộ đó.</summary>
    private static EffectiveRate RateFor(ItemRates selected, ApartmentType apartmentType) =>
        selected.Mode == RateMode.Chung
            ? selected.Common!
            : selected.ByType[apartmentType];

    private static string? ValidateFeeItem(FeeItemEntity feeItem) => feeItem switch
    {
        { FeeType: FeeType.TuNguyen } =>
            $"Khoản thu {feeItem.Code} là đóng góp tự nguyện, thu theo đợt riêng, không đưa vào kỳ thu.",
        { FeeType: FeeType.BaoTriRieng } =>
            $"Khoản thu {feeItem.Code} (bảo trì riêng) chưa khả dụng.",
        _ => null
    };

    /// <summary>
    /// Danh sách hộ của khoản chỉ định hộ: mỗi khoản chỉ định được chọn có đúng một danh sách, ít nhất một hộ,
    /// không trùng hộ, hộ phải tồn tại. Khoản toàn chung cư thu của mọi hộ nên không nhận danh sách hộ.
    /// </summary>
    private static string? ValidateTargets(
        List<FeeItemEntity> feeItems, IReadOnlyList<TargetedFeeItemRequest> targets, List<HouseholdBillingBasis> households)
    {
        // JSON gửi [null] hoặc householdIds chứa null thì không đọc tiếp được, báo lỗi thay vì để lỗi 500
        if (targets.Any(t => t is null || t.HouseholdIds is null))
            return "Danh sách hộ của khoản thu chỉ định không hợp lệ.";

        foreach (var target in targets)
        {
            var feeItem = feeItems.FirstOrDefault(f => f.Id == target.FeeItemId);
            if (feeItem is null)
                return $"Có danh sách hộ cho khoản thu #{target.FeeItemId} nhưng khoản này không được chọn vào kỳ.";
            if (feeItem.FeeScope != FeeScope.ChiDinh)
                return $"Khoản thu {feeItem.Code} áp dụng toàn chung cư, không chọn hộ riêng.";
            if (targets.Count(t => t.FeeItemId == target.FeeItemId) > 1)
                return $"Khoản thu {feeItem.Code} có nhiều hơn một danh sách hộ.";
        }

        var householdIds = households.Select(h => h.HouseholdId).ToHashSet();
        foreach (var feeItem in feeItems.Where(f => f.FeeScope == FeeScope.ChiDinh))
        {
            var selected = targets.FirstOrDefault(t => t.FeeItemId == feeItem.Id)?.HouseholdIds ?? [];
            if (selected.Count == 0)
                return $"Khoản thu {feeItem.Code} áp dụng cho hộ chỉ định: hãy chọn ít nhất một hộ.";
            if (selected.Distinct().Count() != selected.Count)
                return $"Danh sách hộ của khoản thu {feeItem.Code} bị trùng.";
            var unknown = selected.FirstOrDefault(id => !householdIds.Contains(id), -1);
            if (unknown != -1)
                return $"Không tìm thấy hộ gia đình #{unknown} trong danh sách hộ của khoản thu {feeItem.Code}.";
        }
        return null;
    }

    /// <summary>Hộ phải nộp một khoản: khoản toàn chung cư là mọi hộ, khoản chỉ định hộ là các hộ đã chọn.</summary>
    private static List<HouseholdBillingBasis> PayersOf(
        FeeItemEntity feeItem, IReadOnlyList<TargetedFeeItemRequest> targets, List<HouseholdBillingBasis> households)
    {
        if (feeItem.FeeScope == FeeScope.Chung)
            return households;

        var selected = targets.Single(t => t.FeeItemId == feeItem.Id).HouseholdIds.ToHashSet();
        return households.Where(h => selected.Contains(h.HouseholdId)).ToList();
    }

    /// <summary>Cùng chu kỳ với kỳ đang lập: hàng tháng thì cùng tháng, hàng năm thì cùng năm, một lần thì bất kỳ kỳ nào.</summary>
    private static bool SameCycle(FeeCycle cycle, BilledFeeItemRow billed, CreateBillingPeriodRequest request) => cycle switch
    {
        FeeCycle.Thang => billed.Year == request.Year && billed.Month == request.Month,
        FeeCycle.Nam => billed.Year == request.Year,
        _ => true // MotLan
    };

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static FeeItemOperationResult<BillingPeriodResponse> Invalid(string message) =>
        new(FeeItemStatus.Invalid, Message: message);

    // ---- Map ----

    private static BillingPeriodResponse Map(BillingPeriodEntity period)
    {
        var items = period.Items.OrderBy(i => i.FeeItem.Code).ToList();
        // Dòng 0 đ (kỳ lập trước khi bỏ các dòng này) không hiện, để hộ không phải nộp gì không có trong bảng kê
        var receivables = items
            .SelectMany(i => i.Receivables.Where(r => r.Amount > 0).Select(r => (Item: i, Receivable: r)))
            .ToList();

        var households = receivables
            .GroupBy(x => x.Receivable.HouseholdId)
            .Select(g =>
            {
                var household = g.First().Receivable.Household;
                var lines = g.Select(x => new ReceivableLineResponse(
                        x.Item.FeeItemId, x.Receivable.Quantity, x.Receivable.Rate, x.Receivable.Amount))
                    .ToList();
                return new HouseholdBillResponse(household.Id, household.HouseholdNumber,
                    household.Apartment.Code, household.Apartment.Building.Name, lines, lines.Sum(l => l.Amount));
            })
            .OrderBy(h => h.Building)
            .ThenBy(h => h.ApartmentCode)
            .ToList();

        var itemResponses = items
            .Select(i => new BillingPeriodItemResponse(
                i.FeeItemId, i.FeeItem.Code, i.FeeItem.Name, i.FeeItem.FeeCycle, i.FeeItem.CalculationBasis,
                i.FeeItem.Unit, i.RateMode, i.Rate, i.FeeRate?.EffectiveFrom,
                i.TypeRates.OrderBy(r => r.ApartmentType)
                    .Select(r => new BillingPeriodItemTypeRateResponse(
                        r.ApartmentType, r.Rate, r.EffectiveFrom))
                    .ToList(),
                i.Receivables.Sum(r => r.Amount)))
            .ToList();

        return new BillingPeriodResponse(period.Id, period.Year, period.Month, period.ChargeDate, period.DueDate,
            period.Note, period.CreatedBy.Username, period.CreatedAt,
            households.Sum(h => h.TotalAmount), itemResponses, households);
    }
}
