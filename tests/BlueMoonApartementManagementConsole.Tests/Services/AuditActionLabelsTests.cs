using BlueMoonApartementManagementConsole.BackEnd.Models;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.Tests.Services;

/// <summary>
/// N7: nhãn tiếng Việt của trang Nhật ký (AuditActionLabels) phủ đủ mọi loại thao tác.
/// Thêm giá trị mới vào AuditAction mà quên nhãn thì trang sẽ hiện tên tiếng Anh và ô lọc thiếu lựa chọn (như lỗi ở PBI-001);
/// test này báo ngay trên CI.
/// </summary>
public class AuditActionLabelsTests
{
    [Fact]
    [Trait("TestId", "N7")]
    public void EveryAuditAction_HasExactlyOneLabel()
    {
        var labelled = AuditActionLabels.All.Select(x => x.Action).ToList();

        Assert.Equal(Enum.GetValues<AuditAction>().Order(), labelled.Order());
        Assert.Equal(labelled.Count, labelled.Distinct().Count());
    }

    [Fact]
    [Trait("TestId", "N7")]
    public void Labels_AreVietnamese_NonEmpty_AndUnique()
    {
        var labels = AuditActionLabels.All.Select(x => x.Label).ToList();

        Assert.All(labels, l => Assert.False(string.IsNullOrWhiteSpace(l)));
        Assert.Equal(labels.Count, labels.Distinct().Count());
        // Không để lọt tên tiếng Anh của enum làm nhãn
        Assert.All(AuditActionLabels.All, x => Assert.NotEqual(x.Action.ToString(), x.Label));
    }
}
