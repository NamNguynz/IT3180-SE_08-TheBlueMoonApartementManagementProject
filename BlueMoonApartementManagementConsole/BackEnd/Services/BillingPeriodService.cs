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

    /// <summary>
    /// Tính thử số phải thu của một hộ cho một khoản thu vào một ngày (mặc định hôm nay), không lưu gì (PBI-015).
    /// Cùng các bước kiểm, công thức và cơ sở tính với bảng kê: khoản bảng kê từ chối (tự nguyện, chỉ định hộ,
    /// chưa có đơn giá) thì tính thử cũng từ chối với cùng thông báo; còn lại ra đúng số bảng kê sẽ lập.
    /// </summary>
    Task<FeeItemOperationResult<FeeCalculationResponse>> CalculateAsync(
        int feeItemId, int householdId, DateOnly? date, CancellationToken ct = default);
}

/// <summary>
/// Lập kỳ thu và bảng kê số tiền phải thu theo hộ (PBI-014, US-014, LOG-033).
///
/// Cách lập bảng kê (CreateAsync):
///   1. Kiểm tra tháng, ngày tính phí, hạn nộp. Ngày tính phí phải nằm trong tháng của kỳ và không sau hôm nay:
///      chỉ lập được khi "đã đến ngày đã đặt để tính phí", và phí tháng nào lấy đơn giá của tháng đó.
///   2. Kiểm tra từng khoản thu: phải là khoản bắt buộc, áp dụng toàn chung cư, có đơn giá hiệu lực vào ngày tính phí.
///      Đóng góp tự nguyện thu theo đợt riêng (PBI-012) nên không đưa vào kỳ thu.
///   3. Chặn thu trùng theo chu kỳ: khoản hàng tháng chỉ một lần mỗi tháng, hàng năm một lần mỗi năm, một lần thì chỉ một lần.
///   4. Với mỗi khoản thu và mỗi hộ: số phải thu = đơn giá × cơ sở tính, làm tròn đến đồng, theo FeeCalculator (PBI-015).
///      Căn hộ thiếu diện tích cho khoản theo diện tích thì từ chối cả kỳ (cơ sở tính chưa cấu hình);
///      cơ sở tính bằng 0 hợp lệ (ví dụ hộ không còn nhân khẩu) thì không có dòng.
///   5. Lưu kỳ thu cùng đơn giá đã áp dụng và các dòng phải thu trong một lần lưu, rồi ghi nhật ký.
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

        // Bước 3: không thu trùng một khoản trong cùng chu kỳ
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

        // Bước 4: cơ sở tính của mọi hộ phải hợp lệ, rồi mới tính (không lập bảng kê thiếu hộ một cách lặng lẽ)
        foreach (var feeItem in feeItems)
        {
            error = households.Select(h => FeeCalculator.BasisError(feeItem, h)).FirstOrDefault(e => e is not null);
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
            foreach (var household in households)
            {
                var quantity = FeeCalculator.QuantityFor(feeItem.CalculationBasis, household);
                if (quantity <= 0)
                    continue;
                item.Receivables.Add(new ReceivableEntity
                {
                    HouseholdId = household.HouseholdId,
                    Quantity = quantity,
                    Amount = FeeCalculator.AmountFor(rate.Rate, quantity)
                });
            }
            period.Items.Add(item);
        }

        // Bước 5: lưu một lần (kỳ, khoản, các dòng phải thu), rồi ghi nhật ký
        await repo.AddAsync(period, ct);
        await repo.SaveChangesAsync(ct);

        var receivables = period.Items.SelectMany(i => i.Receivables).ToList();
        var householdCount = receivables.Select(r => r.HouseholdId).Distinct().Count();
        await audit.LogAsync(AuditAction.CreateBillingPeriod, actorName, actorId, period.Id.ToString(),
            $"Lập kỳ thu tháng {period.Month:00}/{period.Year}: {period.Items.Count} khoản thu, " +
            $"{householdCount} hộ, tổng phải thu {receivables.Sum(r => r.Amount):N0} đ", ct);

        return new(FeeItemStatus.Success, await GetByIdAsync(period.Id, ct));
    }

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
        var today = Today();
        if (periodStart > today)
            return $"Chưa đến kỳ thu tháng {request.Month:00}/{request.Year}: chỉ lập được từ ngày {periodStart:dd/MM/yyyy}.";
        if (request.ChargeDate == default)
            return "Ngày tính phí là bắt buộc.";
        // Phí của tháng nào tính theo đơn giá hiệu lực trong tháng đó, kể cả khi lập bù kỳ cũ
        if (request.ChargeDate < periodStart || request.ChargeDate > periodEnd)
            return $"Ngày tính phí phải nằm trong tháng của kỳ thu ({periodStart:dd/MM/yyyy} – {periodEnd:dd/MM/yyyy}).";
        if (request.ChargeDate > today)
            return $"Chưa đến ngày tính phí {request.ChargeDate:dd/MM/yyyy}, chưa lập được bảng kê.";
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
        { FeeScope: FeeScope.ChiDinh } =>
            $"Khoản thu {feeItem.Code} áp dụng cho hộ chỉ định, chưa có danh sách hộ nên chưa đưa vào kỳ thu được.",
        _ => null
    };

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
        var receivables = items.SelectMany(i => i.Receivables.Select(r => (Item: i, Receivable: r))).ToList();

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
