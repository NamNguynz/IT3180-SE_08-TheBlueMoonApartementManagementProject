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
    /// Cùng các bước kiểm, công thức và cơ sở tính với bảng kê: khoản bảng kê từ chối (tự nguyện, bảo trì riêng,
    /// chưa có đơn giá) thì tính thử cũng từ chối với cùng thông báo; còn lại ra đúng số bảng kê sẽ lập.
    /// </summary>
    Task<FeeItemOperationResult<FeeCalculationResponse>> CalculateAsync(
        int feeItemId, int householdId, DateOnly? date, CancellationToken ct = default);
}

/// <summary>
/// Lập kỳ thu và bảng kê số tiền phải thu theo hộ (PBI-014, US-014, LOG-033).
///
/// Cách lập bảng kê (CreateAsync):
///   1. Kiểm tra tháng, ngày tính phí, hạn nộp. Lập được kỳ của các tháng đã qua, tháng này và tháng sau
///      (lập trước để kịp báo phí cho cư dân), không lập xa hơn. Ngày tính phí phải nằm trong tháng của kỳ,
///      để phí tháng nào lấy đơn giá của tháng đó.
///   2. Kiểm tra từng khoản thu: phải là khoản bắt buộc, có đơn giá hiệu lực vào ngày tính phí.
///      Đóng góp tự nguyện thu theo đợt riêng (PBI-012) nên không đưa vào kỳ thu.
///   3. Chặn thu trùng khoản toàn chung cư theo chu kỳ: hàng tháng chỉ một lần mỗi tháng, hàng năm một lần mỗi năm,
///      một lần thì chỉ một lần. Khoản chỉ định hộ thu theo từng lần phát sinh nên lập được nhiều kỳ trong một tháng.
///   4. Xác định hộ phải nộp từng khoản: khoản toàn chung cư là mọi hộ, khoản chỉ định hộ là các hộ được chọn
///      (ít nhất một hộ).
///   5. Với mỗi khoản thu và mỗi hộ phải nộp: số phải thu = đơn giá × cơ sở tính, làm tròn đến đồng, theo FeeCalculator
///      (PBI-015). Căn hộ thiếu diện tích cho khoản theo diện tích thì từ chối cả kỳ (cơ sở tính chưa cấu hình).
///      Hộ không phải nộp đồng nào (cơ sở tính bằng 0, hoặc số tiền làm tròn về 0) thì không có dòng, nên không hiện
///      ở trang thu tiền. Bảng kê không có dòng nào thì không lập kỳ.
///   6. Lưu kỳ thu cùng đơn giá đã áp dụng và các dòng phải thu trong một lần lưu, rồi ghi nhật ký.
/// Các khoản bắt buộc của một hộ cộng lại thành một số phải nộp cho cả kỳ (gom thành một lần thu).
/// </summary>
public sealed class BillingPeriodService(
    IBillingPeriodRepository repo,
    IAuditService audit,
    TimeProvider clock) : IBillingPeriodService
{
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

        // Bước 2: từng khoản thu phải hợp lệ và có đơn giá vào ngày tính phí
        var feeItems = (await repo.GetFeeItemsAsync(request.FeeItemIds, ct)).OrderBy(f => f.Code).ToList();
        var missing = request.FeeItemIds.Where(id => feeItems.All(f => f.Id != id)).ToList();
        if (missing.Count > 0)
            return Invalid($"Không tìm thấy khoản thu #{missing[0]}.");

        var rates = new Dictionary<int, FeeRateEntity>();
        foreach (var feeItem in feeItems)
        {
            var (rate, rateError) = BillableRate(feeItem, request.ChargeDate);
            if (rate is null)
                return Invalid(rateError!);
            rates[feeItem.Id] = rate;
        }

        // Bước 3: không thu trùng một khoản toàn chung cư trong cùng chu kỳ.
        // Khoản chỉ định hộ (ví dụ phí sửa chữa của vài căn) phát sinh nhiều lần nên không chặn theo chu kỳ.
        var billed = await repo.GetBilledItemsAsync(request.FeeItemIds, ct);
        foreach (var feeItem in feeItems.Where(f => f.FeeScope == FeeScope.Chung))
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
        var period = new BillingPeriodEntity
        {
            Year = request.Year,
            Month = request.Month,
            ChargeDate = request.ChargeDate,
            DueDate = request.DueDate,
            Note = NormalizeOptional(request.Note),
            CreatedById = actorId,
            CreatedAt = clock.GetUtcNow()
        };
        foreach (var feeItem in feeItems)
        {
            var rate = rates[feeItem.Id];
            var item = new BillingPeriodItemEntity { FeeItemId = feeItem.Id, FeeRateId = rate.Id, Rate = rate.Rate };
            foreach (var household in payersByFeeItem[feeItem.Id])
            {
                var quantity = FeeCalculator.QuantityFor(feeItem.CalculationBasis, household);
                if (quantity <= 0)
                    continue;

                // Không phải nộp đồng nào thì không đưa vào bảng kê, để trang thu tiền không coi hộ đó là "Đã nộp đủ"
                var amount = FeeCalculator.AmountFor(rate.Rate, quantity);
                if (amount <= 0)
                    continue;

                item.Receivables.Add(new ReceivableEntity
                {
                    HouseholdId = household.HouseholdId,
                    Quantity = quantity,
                    Amount = amount
                });
            }
            period.Items.Add(item);
        }
        if (period.Items.All(i => i.Receivables.Count == 0))
            return Invalid("Bảng kê trống: không hộ nào phải nộp số tiền lớn hơn 0 đ, chưa lập kỳ thu.");

        // Bước 6: lưu một lần (kỳ, khoản, các dòng phải thu), rồi ghi nhật ký
        await repo.AddAsync(period, ct);
        await repo.SaveChangesAsync(ct);

        var receivables = period.Items.SelectMany(i => i.Receivables).ToList();
        var householdCount = receivables.Select(r => r.HouseholdId).Distinct().Count();
        await audit.LogAsync(AuditAction.CreateBillingPeriod, actorName, actorId, period.Id.ToString(),
            $"Lập kỳ thu tháng {period.Month:00}/{period.Year}: {period.Items.Count} khoản thu, " +
            $"{householdCount} hộ, tổng phải thu {receivables.Sum(r => r.Amount):N0} đ", ct);

        return new(FeeItemStatus.Success, await GetByIdAsync(period.Id, ct));
    }

    public async Task<IReadOnlyList<HouseholdOptionResponse>> GetHouseholdOptionsAsync(CancellationToken ct = default) =>
        (await repo.GetHouseholdBasesAsync(ct))
            .OrderBy(h => h.ApartmentCode, StringComparer.Ordinal)
            .ThenBy(h => h.HouseholdNumber, StringComparer.Ordinal)
            .Select(h => new HouseholdOptionResponse(h.HouseholdId, h.HouseholdNumber, h.ApartmentCode))
            .ToList();

    public async Task<FeeItemOperationResult<FeeCalculationResponse>> CalculateAsync(
        int feeItemId, int householdId, DateOnly? date, CancellationToken ct = default)
    {
        var feeItem = (await repo.GetFeeItemsAsync([feeItemId], ct)).SingleOrDefault();
        if (feeItem is null)
            return new(FeeItemStatus.NotFound, Message: "Không tìm thấy khoản thu.");

        var household = await repo.GetHouseholdBasisAsync(householdId, ct);
        if (household is null)
            return new(FeeItemStatus.NotFound, Message: "Không tìm thấy hộ gia đình.");

        // Cùng các bước kiểm như khi lập bảng kê: loại khoản thu, đơn giá theo ngày, cơ sở tính của hộ
        var day = date ?? Today();
        var (rate, error) = BillableRate(feeItem, day);
        if (rate is null)
            return new(FeeItemStatus.Invalid, Message: error);

        error = FeeCalculator.BasisError(feeItem, household);
        if (error is not null)
            return new(FeeItemStatus.Invalid, Message: error);

        var quantity = FeeCalculator.QuantityFor(feeItem.CalculationBasis, household);
        return new(FeeItemStatus.Success, new FeeCalculationResponse(
            feeItem.Id, feeItem.Code, feeItem.Name, feeItem.CalculationBasis, feeItem.Unit,
            household.HouseholdId, household.HouseholdNumber, household.ApartmentCode,
            day, quantity, rate.Rate, rate.EffectiveFrom, FeeCalculator.AmountFor(rate.Rate, quantity)));
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
    /// Đơn giá dùng để thu khoản này vào ngày đã cho, hoặc lý do không thu được (khoản không đưa vào kỳ thu,
    /// chưa có đơn giá). Dùng chung cho lập bảng kê và tính thử, để hai nơi luôn cho cùng kết quả.
    /// </summary>
    private static (FeeRateEntity? Rate, string? Error) BillableRate(FeeItemEntity feeItem, DateOnly day)
    {
        var error = ValidateFeeItem(feeItem);
        if (error is not null)
            return (null, error);

        var rate = FeeCalculator.RateOn(feeItem.Rates, day);
        return rate is null
            ? (null, $"Khoản thu {feeItem.Code} chưa có đơn giá hiệu lực vào ngày {day:dd/MM/yyyy}.")
            : (rate, null);
    }

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
                        x.Item.FeeItemId, x.Receivable.Quantity, x.Item.Rate, x.Receivable.Amount))
                    .ToList();
                return new HouseholdBillResponse(household.Id, household.HouseholdNumber,
                    household.Apartment.Code, household.Apartment.Building, lines, lines.Sum(l => l.Amount));
            })
            .OrderBy(h => h.Building)
            .ThenBy(h => h.ApartmentCode)
            .ToList();

        var itemResponses = items
            .Select(i => new BillingPeriodItemResponse(i.FeeItemId, i.FeeItem.Code, i.FeeItem.Name,
                i.FeeItem.FeeCycle, i.FeeItem.CalculationBasis, i.FeeItem.Unit, i.Rate,
                i.FeeRate.EffectiveFrom, i.Receivables.Sum(r => r.Amount)))
            .ToList();

        return new BillingPeriodResponse(period.Id, period.Year, period.Month, period.ChargeDate, period.DueDate,
            period.Note, period.CreatedBy.Username, period.CreatedAt,
            households.Sum(h => h.TotalAmount), itemResponses, households);
    }
}
