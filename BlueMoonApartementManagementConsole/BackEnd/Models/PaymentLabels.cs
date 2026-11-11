using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.BackEnd.Models;

/// <summary>
/// Nhãn tiếng Việt của phương thức và trạng thái thanh toán (PBI-016). Trang Ghi nhận thu tiền sinh các option
/// từ danh sách này, payments.js đọc nhãn từ đó. Thêm giá trị enum mới thì thêm nhãn ở đây;
/// test trong PaymentServiceTests báo lỗi nếu quên.
/// </summary>
public static class PaymentLabels
{
    public static IReadOnlyList<(PaymentMethod Method, string Label)> Methods { get; } =
    [
        (PaymentMethod.TienMat, "Tiền mặt"),
        (PaymentMethod.ChuyenKhoan, "Chuyển khoản"),
    ];

    public static IReadOnlyList<(PaymentStatus Status, string Label)> Statuses { get; } =
    [
        (PaymentStatus.ChuaNop, "Chưa nộp"),
        (PaymentStatus.NopMotPhan, "Nộp một phần"),
        (PaymentStatus.DaNopDu, "Đã nộp đủ"),
    ];

    public static string Label(PaymentMethod method) => Methods.First(x => x.Method == method).Label;
}
