namespace BlueMoonApartementManagementConsole.Tests.Integration;

/// <summary>
/// Nhóm test tích hợp chạy tuần tự, sau khi các test khác chạy xong.
/// Mỗi lớp test tích hợp có một BlueMoonAppFactory riêng, và lúc khởi động app đều chạy migration
/// rồi tạo tài khoản admin. Nếu hai app khởi động cùng lúc trên cùng một database trống (như trên CI),
/// chúng có thể giẫm lên nhau. Lớp test tích hợp mới nên gắn [Collection(SequentialDatabaseCollection.Name)].
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SequentialDatabaseCollection
{
    public const string Name = "Database (tuần tự)";
}
