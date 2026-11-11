namespace BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

/// <summary>
/// Loại biến động nhân khẩu (PBI-007, US-007, FEA-006).
/// ChuyenDen, TamTru tạo nhân khẩu mới; TamVang, TroVe, ChuyenDi áp dụng cho nhân khẩu đã có trong hộ.
/// TroVe (kết thúc tạm vắng) không có trong danh sách của W02, thêm vào để nhân khẩu tạm vắng
/// có đường quay về thường trú (chờ PO xác nhận).
/// Lưu trong DB dạng chuỗi; thêm giá trị mới thì sửa cả check constraint ck_resident_changes_type
/// và ResidentChangeLabels.
/// </summary>
public enum ResidentChangeType
{
    ChuyenDen,
    TamTru,
    TamVang,
    TroVe,
    ChuyenDi
}

/// <summary>
/// Một lần biến động nhân khẩu đã được BQT ghi nhận. Bản ghi chỉ thêm, không sửa, không xóa,
/// để giữ lịch sử cư trú của hộ (BBT Bài 7.4: lịch sử thay đổi nhân khẩu, tạm trú tạm vắng).
/// </summary>
public class ResidentChangeEntity
{
    public int Id { get; set; }

    /// <summary>Hộ tại thời điểm ghi nhận, giữ riêng để lịch sử của hộ không đổi khi nhân khẩu chuyển hộ sau này.</summary>
    public int HouseholdId { get; set; }

    public int ResidentId { get; set; }
    public ResidentEntity Resident { get; set; } = null!;
    public ResidentChangeType Type { get; set; }

    /// <summary>Ngày biến động có hiệu lực (ngày chuyển đến/đi, ngày bắt đầu tạm trú/tạm vắng, ngày trở về).</summary>
    public DateOnly FromDate { get; set; }

    /// <summary>Ngày dự kiến kết thúc, chỉ có với TamTru và TamVang.</summary>
    public DateOnly? ToDate { get; set; }

    /// <summary>
    /// Nơi đi/đến: nơi ở trước (ChuyenDen), nơi thường trú (TamTru), nơi đến khi tạm vắng (TamVang),
    /// nơi chuyển đến (ChuyenDi). Là dữ liệu cá nhân nên được mã hóa như PreviousResidence (PBI-030).
    /// </summary>
    public string? Place { get; set; }

    public string? Reason { get; set; }
    public int RecordedById { get; set; }
    public AppUserEntity RecordedBy { get; set; } = null!;
    public DateTimeOffset RecordedAt { get; set; }
}
