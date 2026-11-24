using BlueMoonApartementManagementConsole.BackEnd.Models;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;

namespace BlueMoonApartementManagementConsole.BackEnd.Services;

/// <summary>
/// Một service cho mỗi báo cáo. Màn hình và Excel đều gọi cùng phương thức,
/// chỉ khác tham số forExport (không phân trang khi xuất file).
/// </summary>
public interface IDebtReportService
{
    Task<DebtReportResult> GetAsync(DebtReportQuery query, bool forExport, CancellationToken ct = default);
}

public interface IIncomeReportService
{
    Task<IncomeReportResult> GetAsync(IncomeReportQuery query, bool forExport, CancellationToken ct = default);
}

public sealed class DebtReportService(IDebtReportRepository repository) : IDebtReportService
{
    public async Task<DebtReportResult> GetAsync(
        DebtReportQuery query,
        bool forExport,
        CancellationToken ct = default)
    {
        if (query.KyThu is null || query.KyThu <= 0)
            throw new ArgumentException("Vui chọn kỳ thu cần xem báo cáo.");

        var normalized = query with
        {
            Ho = string.IsNullOrWhiteSpace(query.Ho) ? null : query.Ho.Trim(),
            Page = Math.Max(1, query.Page),
            PageSize = forExport ? 0 : ListPageSize.Normalize(query.PageSize)
        };

        var data = await repository.LoadAsync(
            normalized,
            normalized.Page,
            normalized.PageSize,
            ct);

        var label = await repository.GetPeriodLabelAsync(normalized.KyThu!.Value, ct)
                    ?? $"kỳ thu {normalized.KyThu.Value}";

        return new DebtReportResult(
            label,
            data.Summary,
            new PagedResult<DebtReportRow>(data.Rows, data.Total, normalized.Page, normalized.PageSize));
    }
}

public sealed class IncomeReportService : IIncomeReportService
{
    public const int MaxDays = 366;

    public static IReadOnlyList<FeeType> FeeTypes => ReportLabels.FeeTypes;

    public static IReadOnlyDictionary<string, string> FeeTypeLabels => ReportLabels.FeeTypeLabels;

    private static IReadOnlyDictionary<ReportPeriodKind, string> KindLabels { get; } =
        new Dictionary<ReportPeriodKind, string>
        {
            [ReportPeriodKind.Tuan] = "Tuần",
            [ReportPeriodKind.Thang] = "Tháng",
            [ReportPeriodKind.Quy] = "Quý",
            [ReportPeriodKind.Nam] = "Năm"
        };

    private readonly IIncomeReportRepository _repository;

    public IncomeReportService(IIncomeReportRepository repository) => _repository = repository;

    public async Task<IncomeReportResult> GetAsync(
        IncomeReportQuery query,
        bool forExport,
        CancellationToken ct = default)
    {
        if (!Enum.IsDefined(query.Ky))
            throw new ArgumentException("Loại kỳ báo cáo không hợp lệ.");

        var (from, to) = ResolveRange(query);
        var page = Math.Max(1, query.Page);
        var pageSize = forExport ? 0 : ListPageSize.Normalize(query.PageSize);

        var data = await _repository.LoadAsync(from, to, query.Ky, page, pageSize, ct);

        var rows = new List<IncomeReportRow>();
        foreach (var bucket in data.Rows.GroupBy(r => (r.From, r.To)))
        {
            var byFeeType = new Dictionary<string, decimal>();
            foreach (var feeType in FeeTypes)
                byFeeType[feeType.ToString()] = 0m;

            foreach (var item in bucket)
                byFeeType[item.FeeType] = item.Amount;

            rows.Add(new IncomeReportRow(
                Label(bucket.Key.From, bucket.Key.To, query.Ky),
                bucket.Key.From,
                bucket.Key.To,
                byFeeType,
                byFeeType.Values.Sum()));
        }

        return new IncomeReportResult(
            KindLabels[query.Ky],
            from,
            to,
            FeeTypes.Select(f => f.ToString()).ToArray(),
            FeeTypeLabels,
            new PagedResult<IncomeReportRow>(rows, data.Total, page, pageSize),
            data.TotalAmount);
    }

    private static (DateOnly From, DateOnly To) ResolveRange(IncomeReportQuery query)
    {
        if (query.From.HasValue != query.To.HasValue)
            throw new ArgumentException("Vui nhập đầy đủ cả từ ngày và đến ngày.");

        DateOnly from;
        DateOnly to;

        if (query.From.HasValue && query.To.HasValue)
        {
            from = query.From.Value;
            to = query.To.Value;
        }
        else if (query.Moc.HasValue)
        {
            (from, to) = Bounds(query.Moc.Value, query.Ky);
        }
        else
        {
            throw new ArgumentException("Vui chọn mốc thời gian hoặc nhập từ ngày và đến ngày.");
        }

        if (from > to)
            throw new ArgumentException("Từ ngày không được sau đến ngày.");

        if (to.DayNumber - from.DayNumber + 1 > MaxDays)
            throw new ArgumentException($"Khoảng thời gian báo cáo không được vượt quá {MaxDays} ngày.");

        return (from, to);
    }

    private static (DateOnly From, DateOnly To) Bounds(DateOnly moc, ReportPeriodKind kind)
    {
        switch (kind)
        {
            case ReportPeriodKind.Tuan:
            {
                var daysSinceMonday = ((int)moc.DayOfWeek + 6) % 7;
                var start = moc.AddDays(-daysSinceMonday);
                return (start, start.AddDays(6));
            }
            case ReportPeriodKind.Thang:
            {
                var start = new DateOnly(moc.Year, moc.Month, 1);
                return (start, start.AddMonths(1).AddDays(-1));
            }
            case ReportPeriodKind.Quy:
            {
                var firstMonth = ((moc.Month - 1) / 3) * 3 + 1;
                var start = new DateOnly(moc.Year, firstMonth, 1);
                return (start, start.AddMonths(3).AddDays(-1));
            }
            default:
                return (new DateOnly(moc.Year, 1, 1), new DateOnly(moc.Year, 12, 31));
        }
    }

    private static string Label(DateOnly from, DateOnly to, ReportPeriodKind kind) => kind switch
    {
        ReportPeriodKind.Tuan => $"{from:dd/MM/yyyy} – {to:dd/MM/yyyy}",
        ReportPeriodKind.Thang => $"Tháng {from.Month:00}/{from.Year}",
        ReportPeriodKind.Quy => $"Quý {(from.Month - 1) / 3 + 1}/{from.Year}",
        _ => $"Năm {from.Year}"
    };
}
