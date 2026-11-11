using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.BackEnd.Models;

/// <summary>
/// Nhãn tiếng Việt của kênh nhắc nộp (PBI-019). Trang Công nợ sinh các option từ danh sách này, debts.js đọc
/// nhãn từ đó. Thêm giá trị enum mới thì thêm nhãn ở đây; test trong DebtServiceTests báo lỗi nếu quên.
/// </summary>
public static class DebtLabels
{
    public static IReadOnlyList<(ReminderChannel Channel, string Label)> Channels { get; } =
    [
        (ReminderChannel.GiayBao, "Giấy báo"),
        (ReminderChannel.DienThoai, "Điện thoại"),
        (ReminderChannel.TrucTiep, "Gặp trực tiếp"),
        (ReminderChannel.TinNhan, "Tin nhắn"),
    ];

    public static string Label(ReminderChannel channel) => Channels.First(x => x.Channel == channel).Label;
}
