using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;

/// <summary>Kỳ con dùng để tổng hợp tiền thực thu theo ngày xác nhận thu.</summary>
public enum ReportPeriodKind
{
    Tuan,
    Thang,
    Quy,
    Nam
}

/// <summary>
/// Bộ lọc báo cáo thu và công nợ. Bắt buộc chọn một kỳ thu vì phải thu được gắn với kỳ.
/// PageSize = 0 được dùng riêng cho xuất Excel: lấy mọi dòng đã GROUP BY, không phân trang.
/// </summary>
public sealed record DebtReportQuery(
    int? KyThu,
    FeeType? LoaiKhoan,
    PaymentStatus? TrangThai,
    string? Ho,
    int Page = 1,
    int PageSize = 10);

public sealed record DebtReportSummary(
    decimal MustPay,
    decimal Paid,
    decimal Remaining,
    int OwedHouseholds);

public sealed record DebtReportRow(
    int HouseholdId,
    string ApartmentCode,
    string BuildingName,
    string HouseholdNumber,
    string HeadName,
    decimal MustPay,
    decimal Paid,
    decimal Remaining,
    PaymentStatus Status);

public sealed record DebtReportResult(
    string PeriodLabel,
    DebtReportSummary Summary,
    PagedResult<DebtReportRow> Rows);

/// <summary>
/// Bộ lọc báo cáo thu chi. Mốc thời gian là một ngày bất kỳ nằm trong kỳ cần xem;
/// có thể thay bằng cặp từ ngày/đến ngày. Khi có cả hai thì cặp ngày được ưu tiên.
/// </summary>
public sealed record IncomeReportQuery(
    ReportPeriodKind Ky,
    DateOnly? Moc,
    DateOnly? From,
    DateOnly? To,
    int Page = 1,
    int PageSize = 10);

public sealed record IncomeReportRow(
    string Label,
    DateOnly From,
    DateOnly To,
    IReadOnlyDictionary<string, decimal> ByFeeType,
    decimal Total);

public sealed record IncomeReportResult(
    string KyLabel,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<string> FeeTypes,
    IReadOnlyDictionary<string, string> FeeTypeLabels,
    PagedResult<IncomeReportRow> Rows,
    decimal Total);
