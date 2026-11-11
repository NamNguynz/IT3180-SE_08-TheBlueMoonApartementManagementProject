namespace BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

/// <summary>
/// Phương thức thanh toán (OBS-002): tiền mặt tại văn phòng BQT/Kế toán, hoặc chuyển khoản.
/// Lưu trong DB dạng chuỗi; thêm giá trị mới thì sửa cả check constraint ck_payments_method và PaymentLabels.
/// </summary>
public enum PaymentMethod
{
    TienMat,
    ChuyenKhoan
}

/// <summary>
/// Trạng thái thanh toán của một hộ trong một kỳ thu. Không lưu trong DB mà tính lại mỗi lần đọc
/// từ số phải thu và tổng các lần nộp, nên không thể lệch với các lần nộp thực tế (AC-018, AC-020).
/// </summary>
public enum PaymentStatus
{
    ChuaNop,
    NopMotPhan,
    DaNopDu
}

/// <summary>
/// Một lần nộp tiền đã được Kế toán xác nhận (PBI-016, US-016). Các khoản bắt buộc của hộ trong kỳ đã gom
/// thành một lần thu (PBI-014), nên lần nộp gắn với (kỳ thu, hộ). Nộp một phần thì có nhiều lần nộp,
/// cộng dồn lại (AC-020). Đã ghi thì không sửa, không xóa, để đối chiếu được về sau.
/// Số biên nhận là "BN-" + Id (6 chữ số), không lưu riêng.
/// </summary>
public class PaymentEntity
{
    public int Id { get; set; }
    public int BillingPeriodId { get; set; }
    public BillingPeriodEntity BillingPeriod { get; set; } = null!;
    public int HouseholdId { get; set; }
    public HouseholdEntity Household { get; set; } = null!;

    /// <summary>Số tiền nhận, đồng nguyên, lớn hơn 0 và không vượt số còn thiếu lúc ghi.</summary>
    public decimal Amount { get; set; }

    /// <summary>Thời điểm nhận tiền (cư dân nộp tiền mặt hoặc tiền về tài khoản).</summary>
    public DateTimeOffset PaidAt { get; set; }

    public PaymentMethod Method { get; set; }

    /// <summary>Mã giao dịch hoặc nội dung chuyển khoản cư dân gửi lại; bắt buộc khi chuyển khoản, làm căn cứ đối chiếu.</summary>
    public string? Reference { get; set; }

    public string? PayerName { get; set; }
    public string? Note { get; set; }
    public int RecordedById { get; set; }
    public AppUserEntity RecordedBy { get; set; } = null!;

    /// <summary>Thời điểm Kế toán xác nhận và lưu.</summary>
    public DateTimeOffset RecordedAt { get; set; }
}
