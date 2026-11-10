using System.Security.Cryptography;
using System.Text;

namespace BlueMoonApartementManagementConsole.BackEnd.Infrastructures.Security;

/// <summary>
/// Mã hóa dữ liệu cá nhân trước khi ghi vào CSDL (PBI-030, AC-038, NFR-007).
/// Được gắn vào EF Core bằng ValueConverter trong AppDbContext, nên service và API vẫn làm việc với bản rõ.
/// </summary>
public interface IPersonalDataEncryptor
{
    /// <summary>Mã hóa AES-256-GCM. Kết quả: "v1:" + base64(nonce | bản mã | tag). Mỗi lần gọi cho một kết quả khác.</summary>
    string Protect(string plaintext);

    /// <summary>
    /// Giải mã giá trị do <see cref="Protect"/> tạo ra. Giá trị không có tiền tố "v1:" là bản rõ lưu từ trước khi có mã hóa,
    /// được trả nguyên để PersonalDataBackfill mã hóa lại. Bản mã bị sửa hoặc sai khóa thì ném CryptographicException.
    /// </summary>
    string Unprotect(string stored);

    /// <summary>
    /// HMAC-SHA256 (hex, 64 ký tự) của số giấy tờ đã bỏ khoảng trắng hai đầu và viết hoa.
    /// Dùng để kiểm trùng CCCD, vì bản mã của cùng một số mỗi lần một khác.
    /// </summary>
    string HashIdentity(string identityNumber);
}

public sealed class PersonalDataEncryptor : IPersonalDataEncryptor
{
    /// <summary>Tên cấu hình chứa khóa gốc: base64 của 32 byte ngẫu nhiên (user-secrets khi dev, biến môi trường khi triển khai).</summary>
    public const string ConfigKey = "PII_ENC_KEY";

    /// <summary>Tiền tố phiên bản của bản mã. Đổi thuật toán hoặc khóa sau này thì dùng "v2:".</summary>
    public const string Prefix = "v1:";

    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _encryptionKey;
    private readonly byte[] _hashKey;

    public PersonalDataEncryptor(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
            throw new InvalidOperationException(
                $"Thiếu cấu hình {ConfigKey} (khóa mã hóa dữ liệu cá nhân). Xem mục cài đặt trong README.");

        byte[] masterKey;
        try
        {
            masterKey = Convert.FromBase64String(base64Key.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{ConfigKey} phải là chuỗi base64 của {KeySize} byte.");
        }

        if (masterKey.Length != KeySize)
            throw new InvalidOperationException(
                $"{ConfigKey} phải là chuỗi base64 của {KeySize} byte, hiện là {masterKey.Length} byte.");

        // Tách hai khóa con từ một khóa gốc: chỉ phải giữ một bí mật, mà khóa mã hóa và khóa băm vẫn độc lập
        _encryptionKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, KeySize,
            info: Encoding.UTF8.GetBytes("BlueMoon PII encryption v1"));
        _hashKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, KeySize,
            info: Encoding.UTF8.GetBytes("BlueMoon PII identity hash v1"));
        CryptographicOperations.ZeroMemory(masterKey);
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var plain = Encoding.UTF8.GetBytes(plaintext);
        var payload = new byte[NonceSize + plain.Length + TagSize];
        var nonce = payload.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_encryptionKey, TagSize);
        aes.Encrypt(nonce, plain, payload.AsSpan(NonceSize, plain.Length), payload.AsSpan(NonceSize + plain.Length, TagSize));

        return Prefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
            return stored;

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(stored[Prefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Dữ liệu cá nhân đã mã hóa bị hỏng.", ex);
        }

        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException("Dữ liệu cá nhân đã mã hóa bị hỏng.");

        var cipherLength = payload.Length - NonceSize - TagSize;
        var plain = new byte[cipherLength];

        using var aes = new AesGcm(_encryptionKey, TagSize);
        aes.Decrypt(payload.AsSpan(0, NonceSize), payload.AsSpan(NonceSize, cipherLength),
            payload.AsSpan(NonceSize + cipherLength, TagSize), plain);

        return Encoding.UTF8.GetString(plain);
    }

    public string HashIdentity(string identityNumber)
    {
        ArgumentNullException.ThrowIfNull(identityNumber);

        var normalized = identityNumber.Trim().ToUpperInvariant();
        return Convert.ToHexStringLower(HMACSHA256.HashData(_hashKey, Encoding.UTF8.GetBytes(normalized)));
    }
}
