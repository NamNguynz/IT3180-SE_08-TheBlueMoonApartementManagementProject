using BlueMoonApartementManagementConsole.BackEnd.Models;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.Tests.Services;

/// <summary>
/// K6: nhãn trạng thái căn hộ (ApartmentStatusLabels) phủ đủ mọi giá trị của ApartmentStatus.
/// Thêm trạng thái mới mà quên nhãn thì form và ô lọc ở trang Hồ sơ căn hộ thiếu lựa chọn; test này báo ngay trên CI.
/// </summary>
public class ApartmentStatusLabelsTests
{
    [Fact]
    [Trait("TestId", "K6")]
    public void EveryApartmentStatus_HasExactlyOneLabel()
    {
        var labelled = ApartmentStatusLabels.All.Select(x => x.Status).ToList();

        Assert.Equal(Enum.GetValues<ApartmentStatus>().Order(), labelled.Order());
        Assert.Equal(labelled.Count, labelled.Distinct().Count());
    }

    [Fact]
    [Trait("TestId", "K6")]
    public void Labels_AreNonEmpty_AndUnique()
    {
        var labels = ApartmentStatusLabels.All.Select(x => x.Label).ToList();

        Assert.All(labels, l => Assert.False(string.IsNullOrWhiteSpace(l)));
        Assert.Equal(labels.Count, labels.Distinct().Count());
    }
}
