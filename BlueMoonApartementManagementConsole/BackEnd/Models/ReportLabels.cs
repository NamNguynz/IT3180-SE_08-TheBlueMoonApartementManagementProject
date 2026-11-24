using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.BackEnd.Models;

/// <summary>Nhãn dùng chung cho ô lọc của 2 trang báo cáo và cho file Excel.</summary>
public static class ReportLabels
{
    public static IReadOnlyList<FeeType> FeeTypes { get; } =
        [FeeType.BatBuoc, FeeType.TuNguyen, FeeType.BaoTriRieng];

    public static IReadOnlyDictionary<string, string> FeeTypeLabels { get; } =
        new Dictionary<string, string>
        {
            [FeeType.BatBuoc.ToString()] = "Khoản bắt buộc",
            [FeeType.TuNguyen.ToString()] = "Khoản tự nguyện",
            [FeeType.BaoTriRieng.ToString()] = "Bảo trì riêng"
        };

    public static IReadOnlyList<(ReportPeriodKind Ky, string Label)> PeriodKinds { get; } =
        [
            (ReportPeriodKind.Tuan, "Tuần"),
            (ReportPeriodKind.Thang, "Tháng"),
            (ReportPeriodKind.Quy, "Quý"),
            (ReportPeriodKind.Nam, "Năm")
        ];
}
