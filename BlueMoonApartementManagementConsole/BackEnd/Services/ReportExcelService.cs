using ClosedXML.Excel;
using BlueMoonApartementManagementConsole.BackEnd.Models;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.BackEnd.Services;

/// <summary>
/// Sinh file Excel ở server bằng ClosedXML (MIT). Dùng chung dữ liệu với màn hình nhờ nhận vào
/// kết quả của ReportService; không truy vấn DB riêng, không đưa số CCCD vào file.
/// </summary>
public interface IReportExcelService
{
    byte[] BuildDebt(DebtReportResult report);
    byte[] BuildIncome(IncomeReportResult report);
}

public static class ReportFileNames
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    public static string Debt(DebtReportResult report, DateTimeOffset exportedAt) =>
        Build("bao-cao-thu-cong-no", Slug(report.PeriodLabel), exportedAt);

    public static string Income(IncomeReportResult report, DateTimeOffset exportedAt) =>
        Build("bao-cao-thu-chi", $"{Slug(report.KyLabel)}-{report.From:yyyyMMdd}-{report.To:yyyyMMdd}", exportedAt);

    private static string Build(string prefix, string period, DateTimeOffset exportedAt)
    {
        var local = exportedAt.ToOffset(VietnamOffset);
        return $"{prefix}_{period}_{local:yyyyMMdd-HHmmss}.xlsx";
    }

    private static string Slug(string value)
    {
        var chars = value.ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')
            .ToArray();
        var text = new string(chars);
        while (text.Contains("--")) text = text.Replace("--", "-");
        return text.Trim('-');
    }
}

public sealed class ReportExcelService : IReportExcelService
{
    private const string MoneyFormat = "#,##0\" đ\"";
    private const string DateFormat = "dd/mm/yyyy";
    private const string NoExpense = "Chưa có dữ liệu chi phí";

    public byte[] BuildDebt(DebtReportResult report)
    {
        using var workbook = new XLWorkbook();

        var summary = workbook.AddWorksheet("Tong hop");
        Title(summary, 1, 6, "BÁO CÁO THU VÀ CÔNG NỢ - TỔNG HỢP");
        summary.Cell(2, 1).Value = "Kỳ báo cáo";
        summary.Cell(2, 2).Value = report.PeriodLabel;

        var row = 4;
        summary.Cell(row, 1).Value = "Chỉ tiêu";
        summary.Cell(row, 2).Value = "Số tiền / Số lượng";
        summary.Range(row, 1, row, 2).Style.Font.Bold = true;

        AddMetric(summary, ++row, "Tổng phải thu", report.Summary.MustPay, MoneyFormat);
        AddMetric(summary, ++row, "Thực thu", report.Summary.Paid, MoneyFormat);
        AddMetric(summary, ++row, "Công nợ", report.Summary.Remaining, MoneyFormat);
        AddMetric(summary, ++row, "Số hộ còn nợ", report.Summary.OwedHouseholds, "0");

        summary.Columns().AdjustToContents();

        var detail = workbook.AddWorksheet("Chi tiet ho");
        Title(detail, 1, 8, "BÁO CÁO THU VÀ CÔNG NỢ - CHI TIẾT THEO HỘ");
        var headers = new[]
        {
            "Căn hộ", "Tòa nhà", "Số hộ khẩu", "Chủ hộ",
            "Phải thu (đ)", "Đã thu (đ)", "Còn nợ (đ)", "Trạng thái"
        };
        WriteHeader(detail, 3, headers);

        var line = 4;
        foreach (var item in report.Rows.Items)
        {
            detail.Cell(line, 1).Value = item.ApartmentCode;
            detail.Cell(line, 2).Value = item.BuildingName;
            detail.Cell(line, 3).Value = item.HouseholdNumber;
            detail.Cell(line, 4).Value = item.HeadName;
            detail.Cell(line, 5).Value = item.MustPay;
            detail.Cell(line, 6).Value = item.Paid;
            detail.Cell(line, 7).Value = item.Remaining;
            detail.Cell(line, 8).Value = StatusLabel(item.Status);
            line++;
        }

        for (var moneyColumn = 5; moneyColumn <= 7; moneyColumn++)
            detail.Column(moneyColumn).Style.NumberFormat.Format = MoneyFormat;
        detail.Columns().AdjustToContents();
        return Save(workbook);
    }

    public byte[] BuildIncome(IncomeReportResult report)
    {
        using var workbook = new XLWorkbook();

        var periodSheet = workbook.AddWorksheet("Ky thu");
        var lastColumn = 4 + report.FeeTypes.Count;
        Title(periodSheet, 1, lastColumn, "BÁO CÁO THU VÀ CÔNG NỢ - THU THEO KỲ");
        periodSheet.Cell(2, 1).Value = $"Loại kỳ: {report.KyLabel}";
        periodSheet.Cell(2, 3).Value = "Từ ngày";
        periodSheet.Cell(2, 4).Value = "Đến ngày";

        var headers = new List<string> { "Kỳ", "Từ ngày", "Đến ngày" };
        headers.AddRange(report.FeeTypes.Select(f => report.FeeTypeLabels[f]));
        headers.Add("Tổng thu (đ)");
        WriteHeader(periodSheet, 4, headers.ToArray());

        var line = 5;
        foreach (var item in report.Rows.Items)
        {
            periodSheet.Cell(line, 1).Value = item.Label;
            periodSheet.Cell(line, 2).Value = item.From.ToDateTime(TimeOnly.MinValue);
            periodSheet.Cell(line, 3).Value = item.To.ToDateTime(TimeOnly.MinValue);
            periodSheet.Cell(line, 2).Style.NumberFormat.Format = DateFormat;
            periodSheet.Cell(line, 3).Style.NumberFormat.Format = DateFormat;

            var column = 4;
            foreach (var feeType in report.FeeTypes)
            {
                periodSheet.Cell(line, column).Value = item.ByFeeType[feeType];
                column++;
            }
            periodSheet.Cell(line, column).Value = item.Total;
            line++;
        }

        periodSheet.Cell(line + 1, lastColumn - 1).Value = "Tổng cộng";
        periodSheet.Cell(line + 1, lastColumn - 1).Style.Font.Bold = true;
        periodSheet.Cell(line + 1, lastColumn).Value = report.Total;
        periodSheet.Cell(line + 1, lastColumn).Style.Font.Bold = true;
        for (var column = 4; column <= lastColumn; column++)
            periodSheet.Column(column).Style.NumberFormat.Format = MoneyFormat;
        periodSheet.Columns().AdjustToContents();

        var expense = workbook.AddWorksheet("Chi phi");
        Title(expense, 1, 4, "BÁO CÁO THU VÀ CÔNG NỢ - CHI PHÍ");
        expense.Cell(3, 1).Value = NoExpense;
        expense.Cell(3, 1).Style.Font.Italic = true;
        expense.Columns().AdjustToContents();
        return Save(workbook);
    }

    private static void Title(IXLWorksheet sheet, int row, int lastColumn, string text)
    {
        sheet.Range(row, 1, row, lastColumn).Merge();
        var cell = sheet.Cell(row, 1);
        cell.Value = text;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 14;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
    }

    private static void WriteHeader(IXLWorksheet sheet, int row, IReadOnlyList<string> headers)
    {
        for (var column = 0; column < headers.Count; column++)
        {
            var cell = sheet.Cell(row, column + 1);
            cell.Value = headers[column];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F0FB");
        }
    }

    private static void AddMetric(IXLWorksheet sheet, int row, string label, decimal value, string format)
    {
        sheet.Cell(row, 1).Value = label;
        sheet.Cell(row, 2).Value = value;
        sheet.Cell(row, 2).Style.NumberFormat.Format = format;
    }

    private static void AddMetric(IXLWorksheet sheet, int row, string label, int value, string format)
    {
        sheet.Cell(row, 1).Value = label;
        sheet.Cell(row, 2).Value = value;
        sheet.Cell(row, 2).Style.NumberFormat.Format = format;
    }

    private static string StatusLabel(PaymentStatus status) =>
        PaymentLabels.Statuses.First(s => s.Status == status).Label;

    private static byte[] Save(XLWorkbook workbook)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
