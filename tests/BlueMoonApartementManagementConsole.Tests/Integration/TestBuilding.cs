using System.Net.Http.Json;
using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;

namespace BlueMoonApartementManagementConsole.Tests.Integration;

/// <summary>
/// Tạo tòa nhà cần thiết cho căn hộ test. Nếu tòa đã tồn tại (chạy lại test / chạy song song)
/// thì lấy Id có sẵn thay vì để API trả 409.
/// </summary>
internal static class TestBuilding
{
    public static async Task<int> IdAsync(HttpClient admin, string name)
    {
        var created = await admin.PostAsJsonAsync("/api/toa-nha", new CreateBuildingRequest { Name = name });
        if (created.IsSuccessStatusCode)
            return (await created.Content.ReadFromJsonAsync<BuildingResponse>())!.Id;

        var buildings = await admin.GetFromJsonAsync<List<BuildingResponse>>("/api/toa-nha")
            ?? throw new InvalidOperationException("Không đọc được danh sách tòa nhà.");
        return buildings.Single(b => b.Name == name).Id;
    }
}
