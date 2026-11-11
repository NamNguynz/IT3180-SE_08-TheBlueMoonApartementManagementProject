using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;

namespace BlueMoonApartementManagementConsole.BackEnd.Services;

/// <summary>
/// Quy tắc tính phí duy nhất của hệ thống (PBI-015, US-015, LOG-027, OBS-001). Mọi nơi cần số tiền phải thu
/// (bảng kê của kỳ thu, API tính thử, tra đơn giá theo ngày) đều gọi các hàm ở đây, nên cùng đầu vào thì
/// luôn ra cùng một số ("tính nhất quán theo đơn giá và cơ sở tính").
///
/// Cách tính số phải thu của một hộ cho một khoản thu vào một ngày:
///   1. Chọn đơn giá có hiệu lực vào ngày đó: EffectiveFrom ≤ ngày &lt; EffectiveTo (EffectiveTo trống là chưa hết hiệu lực).
///      Không có đơn giá nào thì không tính được.
///   2. Lấy cơ sở tính của hộ theo loại cơ sở tính của khoản thu: diện tích căn hộ (m²), số nhân khẩu chưa chuyển đi,
///      hoặc 1 với khoản tính theo hộ / cố định. Khoản theo diện tích cần căn hộ có diện tích &gt; 0 (cơ sở tính đã cấu hình).
///   3. Số phải thu = đơn giá × cơ sở tính, làm tròn đến đồng (.5 làm tròn lên).
/// Ví dụ: phí dịch vụ 7.000 đ/m², căn 72,5 m² → 72,5 × 7.000 = 507.500 đ;
///        phí quản lý 2.505 đ/m², căn 72,5 m² → 181.612,5 → 181.613 đ.
/// Cơ sở tính điện, nước, internet thuộc v2.0 (FEA-033), chưa có trong v1.0.
/// </summary>
public static class FeeCalculator
{
    /// <summary>Bước 1: đơn giá có hiệu lực vào ngày <paramref name="day"/>, hoặc null nếu không có.</summary>
    public static FeeRateEntity? RateOn(IEnumerable<FeeRateEntity> rates, DateOnly day) =>
        rates
            .Where(r => r.EffectiveFrom <= day && (r.EffectiveTo is null || r.EffectiveTo > day))
            .OrderByDescending(r => r.EffectiveFrom) // phòng dữ liệu cũ có hai khoảng chồng nhau: lấy đơn giá mới nhất
            .FirstOrDefault();

    /// <summary>Bước 2: cơ sở tính của hộ cho loại cơ sở tính của khoản thu.</summary>
    public static decimal QuantityFor(CalculationBasis basis, HouseholdBillingBasis household) => basis switch
    {
        CalculationBasis.DienTich => household.Area,
        CalculationBasis.NhanKhau => household.ResidentCount,
        CalculationBasis.Ho or CalculationBasis.CoDinh => 1, // mỗi hộ một suất
        _ => throw new ArgumentOutOfRangeException(nameof(basis), basis, "Cơ sở tính chưa được hỗ trợ.")
    };

    /// <summary>Bước 3: tiền Việt không có đơn vị lẻ, làm tròn đến đồng, .5 làm tròn lên.</summary>
    public static decimal AmountFor(decimal rate, decimal quantity) =>
        Math.Round(rate * quantity, 0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Cơ sở tính của hộ chưa được cấu hình hợp lệ cho khoản thu: khoản theo diện tích mà căn hộ không có diện tích.
    /// Hộ không còn nhân khẩu thì không phải lỗi, chỉ là không phát sinh phí theo đầu người.
    /// </summary>
    public static string? BasisError(FeeItemEntity feeItem, HouseholdBillingBasis household) =>
        feeItem.CalculationBasis == CalculationBasis.DienTich && household.Area <= 0
            ? $"Căn hộ {household.ApartmentCode} chưa có diện tích hợp lệ, chưa tính được khoản thu {feeItem.Code} theo diện tích."
            : null;
}
