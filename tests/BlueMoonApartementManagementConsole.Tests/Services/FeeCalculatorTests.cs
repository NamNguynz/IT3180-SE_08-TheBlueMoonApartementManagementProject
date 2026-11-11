using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using BlueMoonApartementManagementConsole.BackEnd.Repositories;
using BlueMoonApartementManagementConsole.BackEnd.Services;

namespace BlueMoonApartementManagementConsole.Tests.Services;

/// <summary>
/// Unit test quy tắc tính phí dùng chung (PBI-015): chọn đơn giá theo ngày, cơ sở tính theo loại, làm tròn,
/// và cơ sở tính chưa cấu hình. Test chấp nhận độc lập (WBS 4.15.02) do Tester làm riêng.
/// </summary>
[Trait("PBI", "PBI-015")]
public sealed class FeeCalculatorTests
{
    // Lịch sử đơn giá: 5.000 từ 01/01 đến hết 09/11, 8.000 từ 10/11 trở đi (EffectiveTo là ngày đầu tiên hết hiệu lực)
    private static readonly FeeRateEntity OldRate = new()
    {
        Id = 1, Rate = 5_000m, EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = new DateOnly(2026, 11, 10)
    };
    private static readonly FeeRateEntity NewRate = new()
    {
        Id = 2, Rate = 8_000m, EffectiveFrom = new DateOnly(2026, 11, 10), EffectiveTo = null
    };
    private static readonly FeeRateEntity[] History = [NewRate, OldRate];

    private static HouseholdBillingBasis Household(decimal area = 72.5m, int residents = 3) =>
        new(1, "HK-0501", "A-501", area, residents);

    // ===== Bước 1: đơn giá theo ngày =====

    [Theory]
    [InlineData(2025, 12, 31, null)]  // trước đơn giá đầu tiên: không có
    [InlineData(2026, 1, 1, 5_000)]   // ngày bắt đầu hiệu lực được tính
    [InlineData(2026, 11, 9, 5_000)]  // ngày cuối của đơn giá cũ
    [InlineData(2026, 11, 10, 8_000)] // ngày EffectiveTo của đơn giá cũ không còn thuộc đơn giá cũ
    [InlineData(2030, 6, 1, 8_000)]   // đơn giá chưa có ngày hết hiệu lực
    public void RateOn_UsesFromInclusive_ToExclusive(int year, int month, int day, int? expected)
    {
        var rate = FeeCalculator.RateOn(History, new DateOnly(year, month, day));

        Assert.Equal(expected, rate is null ? null : (int)rate.Rate);
    }

    [Fact]
    public void RateOn_OverlappingLegacyRates_TakesTheLatestStart()
    {
        var overlapping = new FeeRateEntity { Id = 3, Rate = 9_000m, EffectiveFrom = new DateOnly(2026, 6, 1) };

        var rate = FeeCalculator.RateOn([OldRate, overlapping], new DateOnly(2026, 7, 1));

        Assert.Same(overlapping, rate);
    }

    // ===== Bước 2: cơ sở tính =====

    [Theory]
    [InlineData(CalculationBasis.DienTich, 72.5)]
    [InlineData(CalculationBasis.NhanKhau, 3)]
    [InlineData(CalculationBasis.Ho, 1)]
    [InlineData(CalculationBasis.CoDinh, 1)]
    public void QuantityFor_EachBasis(CalculationBasis basis, decimal expected)
    {
        Assert.Equal(expected, FeeCalculator.QuantityFor(basis, Household()));
    }

    // ===== Bước 3: làm tròn =====

    [Theory]
    [InlineData(7_000, 72.5, 507_500)]      // ví dụ trong tài liệu: 72,5 m² × 7.000
    [InlineData(2_505, 72.5, 181_613)]      // 181.612,5 → .5 làm tròn lên
    [InlineData(2_505, 10.05, 25_175)]      // 25.175,25 → làm tròn xuống
    [InlineData(16_500, 33.33, 549_945)]    // mức trần phí dịch vụ trong đề bài
    [InlineData(0, 50, 0)]
    public void AmountFor_RoundsToWholeDong(decimal rate, decimal quantity, decimal expected)
    {
        Assert.Equal(expected, FeeCalculator.AmountFor(rate, quantity));
    }

    // ===== Cơ sở tính chưa cấu hình =====

    [Fact]
    public void BasisError_AreaFeeOnApartmentWithoutArea_IsReported()
    {
        var areaFee = new FeeItemEntity { Code = "PHI_DV", CalculationBasis = CalculationBasis.DienTich };

        Assert.Equal("Căn hộ A-501 chưa có diện tích hợp lệ, chưa tính được khoản thu PHI_DV theo diện tích.",
            FeeCalculator.BasisError(areaFee, Household(area: 0)));
        Assert.Null(FeeCalculator.BasisError(areaFee, Household(area: 72.5m)));
    }

    [Fact]
    public void BasisError_HouseholdWithoutResidents_IsNotAnError()
    {
        var perPersonFee = new FeeItemEntity { Code = "PHI_VS", CalculationBasis = CalculationBasis.NhanKhau };

        Assert.Null(FeeCalculator.BasisError(perPersonFee, Household(residents: 0)));
        Assert.Equal(0m, FeeCalculator.QuantityFor(CalculationBasis.NhanKhau, Household(residents: 0)));
    }
}
