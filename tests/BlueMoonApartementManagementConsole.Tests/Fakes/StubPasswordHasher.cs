using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;
using Microsoft.AspNetCore.Identity;

namespace BlueMoonApartementManagementConsole.Tests.Fakes;

/// <summary>
/// Hasher giả trả kết quả cố định. Chỉ dùng cho trường hợp không tạo được bằng hasher thật,
/// vd: VerifyHashedPassword trả SuccessRehashNeeded (thuật toán băm được nâng cấp).
/// </summary>
public class StubPasswordHasher(PasswordVerificationResult verifyResult, string hashToReturn) : IPasswordHasher<AppUserEntity>
{
    public int HashCalls { get; private set; }

    public string HashPassword(AppUserEntity user, string password)
    {
        HashCalls++;
        return hashToReturn;
    }

    public PasswordVerificationResult VerifyHashedPassword(AppUserEntity user, string hashedPassword, string providedPassword) =>
        verifyResult;
}
