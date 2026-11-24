using System.Data;
using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlueMoonApartementManagementConsole.BackEnd.Repositories;

/// <summary>
/// Truy vấn báo cáo thu theo kỳ con. Mốc thời gian là ngày xác nhận thu (payments."RecordedAt"),
/// được đổi sang giờ Việt Nam trước khi gom nhóm. Tiền của một lần thu được phân bổ theo tỷ trọng
/// phải thu của từng loại khoản trong cùng kỳ thu, vì payment không lưu FeeItemId.
/// </summary>
public interface IIncomeReportRepository
{
    Task<IncomeReportRepository.Data> LoadAsync(
        DateOnly from,
        DateOnly to,
        ReportPeriodKind kind,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<Dictionary<int, (int Year, int Month)>> GetPeriodsAsync(CancellationToken ct = default);
}

public sealed class IncomeReportRepository(AppDbContext db) : IIncomeReportRepository
{
    public sealed record Aggregate(DateOnly From, DateOnly To, string FeeType, decimal Amount);
    public sealed record Data(IReadOnlyList<Aggregate> Rows, int Total, decimal TotalAmount);

    private const string Cte = """
        WITH dated AS (
            SELECT (p."RecordedAt" AT TIME ZONE 'Asia/Ho_Chi_Minh')::date AS day,
                   p."Amount" AS amount,
                   p."BillingPeriodId" AS pid
            FROM "payments" p
            WHERE (p."RecordedAt" AT TIME ZONE 'Asia/Ho_Chi_Minh')::date >= @from
              AND (p."RecordedAt" AT TIME ZONE 'Asia/Ho_Chi_Minh')::date <= @to
        ),
        bucketed AS (
            SELECT CASE @kind::text
                       WHEN 'Tuan' THEN date_trunc('week', day)::date
                       WHEN 'Thang' THEN date_trunc('month', day)::date
                       WHEN 'Quy' THEN date_trunc('quarter', day)::date
                       ELSE date_trunc('year', day)::date
                   END AS bucket_from,
                   CASE @kind::text
                       WHEN 'Tuan' THEN (date_trunc('week', day) + interval '6 days')::date
                       WHEN 'Thang' THEN (date_trunc('month', day) + interval '1 month - 1 day')::date
                       WHEN 'Quy' THEN (date_trunc('quarter', day) + interval '3 months - 1 day')::date
                       ELSE (date_trunc('year', day) + interval '1 year - 1 day')::date
                   END AS bucket_to,
                   amount, pid
            FROM dated
        ),
        shares AS (
            SELECT bi."BillingPeriodId" AS pid, fi."FeeType"::text AS ftype, SUM(r."Amount") AS due
            FROM "receivables" r
            INNER JOIN "billing_period_items" bi ON bi."Id" = r."BillingPeriodItemId"
            INNER JOIN "fee_items" fi ON fi."Id" = bi."FeeItemId"
            GROUP BY bi."BillingPeriodId", fi."FeeType"
        ),
        totals AS (
            SELECT pid, SUM(due) AS total FROM shares GROUP BY pid
        ),
        allocated AS (
            SELECT b.bucket_from, b.bucket_to, COALESCE(s.ftype, 'BatBuoc') AS ftype,
                   SUM(b.amount * CASE WHEN COALESCE(t.total, 0) = 0
                       THEN CASE WHEN COALESCE(s.ftype, 'BatBuoc') = 'BatBuoc' THEN 1 ELSE 0 END
                       ELSE s.due / t.total END) AS amount
            FROM bucketed b
            LEFT JOIN shares s ON s.pid = b.pid
            LEFT JOIN totals t ON t.pid = b.pid
            GROUP BY b.bucket_from, b.bucket_to, COALESCE(s.ftype, 'BatBuoc')
        ),
        grouped AS (
            SELECT bucket_from, bucket_to, ftype, ROUND(SUM(amount), 2) AS amount
            FROM allocated
            GROUP BY bucket_from, bucket_to, ftype
        ),
        bucket_page AS (
            SELECT DISTINCT bucket_from, bucket_to
            FROM grouped
            ORDER BY bucket_from
            LIMIT NULLIF(@pageSize, 0) OFFSET @offset
        )
        """;

    private const string SummarySql = Cte + """
        SELECT COUNT(DISTINCT bucket_from), COALESCE(SUM(amount), 0) FROM grouped
        """;

    private const string RowsSql = Cte + """
        SELECT g.bucket_from, g.bucket_to, g.ftype, g.amount
        FROM grouped g
        INNER JOIN bucket_page p
            ON p.bucket_from = g.bucket_from AND p.bucket_to = g.bucket_to
        ORDER BY g.bucket_from, g.ftype
        """;

    public async Task<Data> LoadAsync(
        DateOnly from,
        DateOnly to,
        ReportPeriodKind kind,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(ct);

        try
        {
            await using var command = connection.CreateCommand();
            AddParameters(command, from, to, kind, page, pageSize);

            var total = 0;
            var totalAmount = 0m;
            command.CommandText = SummarySql;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                if (await reader.ReadAsync(ct))
                {
                    total = (int)reader.GetInt64(0);
                    totalAmount = reader.GetDecimal(1);
                }
            }

            var rows = new List<Aggregate>();
            command.CommandText = RowsSql;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    rows.Add(new Aggregate(
                        reader.GetFieldValue<DateOnly>(0),
                        reader.GetFieldValue<DateOnly>(1),
                        reader.GetString(2),
                        reader.GetDecimal(3)));
                }
            }

            return new Data(rows, total, totalAmount);
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    private static void AddParameters(
        System.Data.Common.DbCommand command,
        DateOnly from,
        DateOnly to,
        ReportPeriodKind kind,
        int page,
        int pageSize)
    {
        static void Add(System.Data.Common.DbCommand cmd, string name, object value) =>
            cmd.Parameters.Add(new NpgsqlParameter(name, value));

        Add(command, "from", from);
        Add(command, "to", to);
        Add(command, "kind", kind.ToString());
        Add(command, "pageSize", pageSize);
        Add(command, "offset", (page - 1) * pageSize);
    }

    public async Task<Dictionary<int, (int Year, int Month)>> GetPeriodsAsync(CancellationToken ct = default)
    {
        return await db.BillingPeriods
            .AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => (p.Year, p.Month), ct);
    }
}
