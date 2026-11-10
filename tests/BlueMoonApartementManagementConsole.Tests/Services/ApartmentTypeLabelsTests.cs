using BlueMoonApartementManagementConsole.BackEnd.Models;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.Tests.Services;

/// <summary>
/// K6: nhãn loại hình căn hộ (ApartmentTypeLabels) phủ đủ mọi giá trị của ApartmentType.
/// Thêm loại hình mới mà quên nhãn thì form và ô lọc ở trang Hồ sơ căn hộ thiếu lựa chọn; test này báo ngay trên CI.
/// </summary>
public class ApartmentTypeLabelsTests
{
    [Fact]
    [Trait("TestId", "K6")]
    public void EveryApartmentType_HasExactlyOneLabel()
    {
        var labelled = ApartmentTypeLabels.All.Select(x => x.Type).ToList();

        Assert.Equal(Enum.GetValues<ApartmentType>().Order(), labelled.Order());
        Assert.Equal(labelled.Count, labelled.Distinct().Count());
    }

    [Fact]
    [Trait("TestId", "K6")]
    public void Labels_AreNonEmpty_AndUnique()
    {
        var labels = ApartmentTypeLabels.All.Select(x => x.Label).ToList();

        Assert.All(labels, l => Assert.False(string.IsNullOrWhiteSpace(l)));
        Assert.Equal(labels.Count, labels.Distinct().Count());
    }
}
