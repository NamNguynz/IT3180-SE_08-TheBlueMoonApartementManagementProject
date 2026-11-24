using System.Data;
using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Persistences.DBContexts;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlueMoonApartementManagementConsole.BackEnd.Repositories;

/// <summary>
/// Truy vấn báo cáo thu và công nợ. Mọi tổng hợp (phải thu, thực thu, còn nợ, số hộ) đều do
/// PostgreSQL GROUP BY; ứng dụng chỉ đọc kết quả đã gom nhóm và phân trang bằng LIMIT/OFFSET.
/// </summary>
public interface IDebtReportRepository
{
    Task<DebtReportRepository.Data> LoadAsync(
        DebtReportQuery query,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<string?> GetPeriodLabelAsync(int periodId, CancellationToken ct = default);
}

public sealed class DebtReportRepository(AppDbContext db) : IDebtReportRepository
{
    public sealed record Data(DebtReportSummary Summary, IReadOnlyList<DebtReportRow> Rows, int Total);

    // Dùng chung cho truy vấn tổng và truy vấn dòng: cùng một nguồn dữ liệu đã lọc nên không thể lệch số.
    private const string Cte = """
        WITH dues AS (
            SELECT r."HouseholdId" AS hid, SUM(r."Amount") AS due
            FROM "receivables" r
            INNER JOIN "billing_period_items" bi ON bi."Id" = r."BillingPeriodItemId"
            INNER JOIN "fee_items" fi ON fi."Id" = bi."FeeItemId"
            WHERE bi."BillingPeriodId" = @period
              AND (@feeType::text IS NULL OR fi."FeeType" = @feeType)
            GROUP BY r."HouseholdId"
        ),
        all_dues AS (
            SELECT r."HouseholdId" AS hid, SUM(r."Amount") AS due
            FROM "receivables" r
            INNER JOIN "billing_period_items" bi ON bi."Id" = r."BillingPeriodItemId"
            WHERE bi."BillingPeriodId" = @period
            GROUP BY r."HouseholdId"
        ),
        paid AS (
            SELECT p."HouseholdId" AS hid, SUM(p."Amount") AS amount
            FROM "payments" p
            WHERE p."BillingPeriodId" = @period
            GROUP BY p."HouseholdId"
        ),
        household_rows AS (
            SELECT h."Id" AS hid,
                   a."Code" AS apartment,
                   COALESCE(b."Name", '') AS building,
                   h."HouseholdNumber" AS householdNumber,
                   h."HeadName" AS headName,
                   ROUND(COALESCE(d.due, 0), 2) AS mustPay,
                   ROUND(
                       CASE WHEN @feeType::text IS NULL THEN COALESCE(p.amount, 0)
                            WHEN COALESCE(ad.due, 0) = 0 THEN 0
                            ELSE COALESCE(p.amount, 0) * COALESCE(d.due, 0) / ad.due
                       END, 2) AS paid
            FROM dues d
            INNER JOIN "households" h ON h."Id" = d.hid
            INNER JOIN "apartments" a ON a."Id" = h."ApartmentId"
            LEFT JOIN "buildings" b ON b."Id" = a."BuildingId"
            LEFT JOIN all_dues ad ON ad.hid = d.hid
            LEFT JOIN paid p ON p.hid = d.hid
        ),
        classified AS (
            SELECT hid, apartment, building, householdNumber, headName, mustPay, paid,
                   GREATEST(mustPay - paid, 0) AS remaining,
                   CASE WHEN paid >= mustPay THEN 'DaNopDu'
                        WHEN paid <= 0 THEN 'ChuaNop'
                        ELSE 'NopMotPhan' END AS status
            FROM household_rows
        ),
        filtered AS (
            SELECT * FROM classified
            WHERE (@status::text IS NULL OR status = @status)
              AND (@search::text IS NULL OR apartment ILIKE '%' || @search || '%'
                   OR building ILIKE '%' || @search || '%'
                   OR householdNumber ILIKE '%' || @search || '%'
                   OR headName ILIKE '%' || @search || '%')
        )
        """;

    private const string SummarySql = Cte + """
        SELECT COALESCE(SUM(mustPay), 0), COALESCE(SUM(paid), 0),
               COALESCE(SUM(remaining), 0), COUNT(*) FILTER (WHERE remaining > 0)
        FROM filtered
        """;

    private const string RowsSql = Cte + """
        SELECT hid, apartment, building, householdNumber, headName, mustPay, paid, remaining, status
        FROM filtered
        ORDER BY remaining DESC, apartment ASC, hid ASC
        LIMIT NULLIF(@pageSize, 0) OFFSET @offset
        """;

    public async Task<Data> LoadAsync(
        DebtReportQuery query,
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
            AddParameters(command, query, page, pageSize);

            var summary = new DebtReportSummary(0, 0, 0, 0);
            command.CommandText = SummarySql;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                if (await reader.ReadAsync(ct))
                {
                    summary = new DebtReportSummary(
                        reader.GetDecimal(0),
                        reader.GetDecimal(1),
                        reader.GetDecimal(2),
                        (int)reader.GetInt64(3));
                }
            }

            var rows = new List<DebtReportRow>();
            command.CommandText = RowsSql;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    rows.Add(new DebtReportRow(
                        reader.GetInt32(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetDecimal(5),
                        reader.GetDecimal(6),
                        reader.GetDecimal(7),
                        Enum.Parse<PaymentStatus>(reader.GetString(8))));
                }
            }

            var total = pageSize == 0
                ? rows.Count
                : await CountAsync(connection, query, ct);

            return new Data(summary, rows, total);
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    private static async Task<int> CountAsync(
        System.Data.Common.DbConnection connection,
        DebtReportQuery query,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        AddParameters(command, query, 1, 1);
        command.CommandText = Cte + "SELECT COUNT(*) FROM filtered";
        var value = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(value);
    }

    private static void AddParameters(
        System.Data.Common.DbCommand command,
        DebtReportQuery query,
        int page,
        int pageSize)
    {
        static void Add(System.Data.Common.DbCommand cmd, string name, object? value) =>
            cmd.Parameters.Add(new NpgsqlParameter(name, value ?? DBNull.Value));

        Add(command, "period", query.KyThu!.Value);
        Add(command, "feeType", query.LoaiKhoan?.ToString());
        Add(command, "status", query.TrangThai?.ToString());
        Add(command, "search", string.IsNullOrWhiteSpace(query.Ho) ? null : query.Ho.Trim());
        Add(command, "pageSize", pageSize);
        Add(command, "offset", (page - 1) * pageSize);
    }

    public async Task<string?> GetPeriodLabelAsync(int periodId, CancellationToken ct = default)
    {
        var label = await db.BillingPeriods
            .AsNoTracking()
            .Where(p => p.Id == periodId)
            .Select(p => $"tháng {p.Month:00}/{p.Year}")
            .FirstOrDefaultAsync(ct);
        return label;
    }
}
