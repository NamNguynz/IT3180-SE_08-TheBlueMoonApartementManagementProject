using System.Security.Cryptography;
using BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Security;

namespace BlueMoonApartementManagementConsole.Tests.Security;

/// <summary>
/// Unit test bộ mã hóa dữ liệu cá nhân (PBI-030, AC-038, NFR-007), không cần database.
/// Phần gắn vào EF Core (cột trong DB là bản mã, API trả bản rõ) nằm ở Integration/PersonalDataEncryptionTests.
/// Mọi dữ liệu ở đây là dữ liệu giả (RSK-008).
/// </summary>
public class PersonalDataEncryptorTests
{
    private static string Key(byte seed) =>
        Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)(seed + i)).ToArray());

    private static readonly PersonalDataEncryptor Encryptor = new(Key(1));

    [Theory]
    [Trait("TestId", "E1")]
    [InlineData("001203004567")]
    [InlineData("Công giáo")]
    [InlineData("Kinh")]
    [InlineData("Cục Cảnh sát quản lý hành chính về trật tự xã hội")]
    [InlineData("")]
    public void Protect_ThenUnprotect_ReturnsOriginal(string plaintext)
    {
        Assert.Equal(plaintext, Encryptor.Unprotect(Encryptor.Protect(plaintext)));
    }

    [Fact]
    [Trait("TestId", "E2")]
    public void Protect_SameValueTwice_GivesDifferentCiphertexts_WithoutPlaintext()
    {
        const string cccd = "001203004567";

        var first = Encryptor.Protect(cccd);
        var second = Encryptor.Protect(cccd);

        Assert.NotEqual(first, second);
        Assert.All(new[] { first, second }, c =>
        {
            Assert.StartsWith(PersonalDataEncryptor.Prefix, c);
            Assert.DoesNotContain(cccd, c);
        });
    }

    [Theory]
    [Trait("TestId", "E3")]
    [InlineData(0)]   // sửa nonce
    [InlineData(12)]  // sửa byte đầu của bản mã
    [InlineData(-1)]  // sửa byte cuối của tag
    public void Unprotect_TamperedCiphertext_Throws(int byteIndex)
    {
        var payload = Convert.FromBase64String(Encryptor.Protect("Phật giáo")[PersonalDataEncryptor.Prefix.Length..]);
        payload[byteIndex >= 0 ? byteIndex : payload.Length + byteIndex] ^= 0x01;
        var tampered = PersonalDataEncryptor.Prefix + Convert.ToBase64String(payload);

        Assert.ThrowsAny<CryptographicException>(() => Encryptor.Unprotect(tampered));
    }

    [Fact]
    [Trait("TestId", "E3")]
    public void Unprotect_CorruptedFormat_Throws()
    {
        Assert.ThrowsAny<CryptographicException>(() => Encryptor.Unprotect(PersonalDataEncryptor.Prefix + "khong-phai-base64!"));
        Assert.ThrowsAny<CryptographicException>(() => Encryptor.Unprotect(PersonalDataEncryptor.Prefix + "AAAA"));
    }

    [Fact]
    [Trait("TestId", "E4")]
    public void Unprotect_WithAnotherKey_Throws()
    {
        var ciphertext = Encryptor.Protect("001203004567");

        Assert.ThrowsAny<CryptographicException>(() => new PersonalDataEncryptor(Key(100)).Unprotect(ciphertext));
    }

    [Fact]
    [Trait("TestId", "E5")]
    public void HashIdentity_IsStable_IgnoresSurroundingSpacesAndCase_AndDependsOnKey()
    {
        var hash = Encryptor.HashIdentity("001203004567");

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal(hash, Encryptor.HashIdentity("  001203004567 "));
        Assert.Equal(Encryptor.HashIdentity("b1234567"), Encryptor.HashIdentity("B1234567"));
        Assert.NotEqual(hash, Encryptor.HashIdentity("001203004568"));
        Assert.NotEqual(hash, new PersonalDataEncryptor(Key(100)).HashIdentity("001203004567"));
        Assert.DoesNotContain("001203004567", hash);
    }

    [Theory]
    [Trait("TestId", "E6")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingKey_Throws_NamingTheSetting(string? key)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new PersonalDataEncryptor(key));

        Assert.Contains(PersonalDataEncryptor.ConfigKey, ex.Message);
    }

    [Theory]
    [Trait("TestId", "E6")]
    [InlineData("khong-phai-base64!")]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==")] // 16 byte
    public void InvalidKey_Throws(string key)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new PersonalDataEncryptor(key));

        Assert.Contains("32 byte", ex.Message);
    }

    [Theory]
    [Trait("TestId", "E7")]
    [InlineData("001203004567")]
    [InlineData("Phật giáo")]
    public void Unprotect_LegacyPlaintext_ReturnsAsIs(string legacy)
    {
        Assert.Equal(legacy, Encryptor.Unprotect(legacy));
    }
}
