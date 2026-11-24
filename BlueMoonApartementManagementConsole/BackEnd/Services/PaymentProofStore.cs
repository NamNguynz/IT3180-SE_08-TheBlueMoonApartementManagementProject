using BlueMoonApartementManagementConsole.BackEnd.Models.DTOs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace BlueMoonApartementManagementConsole.BackEnd.Services;

/// <summary>
/// Lưu ảnh minh chứng của lần nộp vào thư mục ngoài wwwroot. File không được phục vụ tĩnh mà chỉ trả
/// qua endpoint có policy TaiChinh, nên không lộ URL công khai cho người ngoài tổ chức.
/// </summary>
public interface IPaymentProofStore
{
    /// <summary>Lưu nội dung ảnh với tên ngẫu nhiên; trả về tên file tương đối cần lưu vào DB.</summary>
    Task<string> SaveAsync(PaymentProofUpload upload, CancellationToken ct = default);

    /// <summary>Mở ảnh đã lưu; trả về null nếu không tìm thấy hoặc tên file không hợp lệ.</summary>
    Task<(Stream Stream, string ContentType)?> OpenAsync(string storedName, CancellationToken ct = default);

    /// <summary>Xóa file khi lần nộp không được lưu (dọn dẹp sau lỗi DB).</summary>
    Task DeleteAsync(string storedName, CancellationToken ct = default);
}

public sealed class PaymentProofStore(IWebHostEnvironment env, IConfiguration config) : IPaymentProofStore
{
    private const string DefaultRelativeDirectory = "App_Data/payment-proofs";
    private readonly string _directory = ResolveDirectory(env, config);

    private static string ResolveDirectory(IWebHostEnvironment env, IConfiguration config)
    {
        var configured = config["PaymentProof:Directory"];
        if (string.IsNullOrWhiteSpace(configured)) configured = DefaultRelativeDirectory;
        return Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured);
    }

    public async Task<string> SaveAsync(PaymentProofUpload upload, CancellationToken ct = default)
    {
        // Kiểm tra lại ở tầng lưu file để không phụ thuộc vào việc service đã gọi validate hay chưa.
        var error = PaymentProofRules.Validate(upload);
        if (error is not null) throw new InvalidOperationException(error);

        var extension = Path.GetExtension(upload.FileName).ToLowerInvariant();
        var storedName = $"{Guid.NewGuid():N}{extension}";
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, storedName);

        await using (var output = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
        {
            await upload.Content.CopyToAsync(output, ct);
        }
        return storedName;
    }

    public Task<(Stream Stream, string ContentType)?> OpenAsync(string storedName, CancellationToken ct = default)
    {
        var path = ResolvePath(storedName);
        var contentType = PaymentProofRules.ContentTypeOf(storedName);
        if (path is null || contentType is null || !File.Exists(path))
            return Task.FromResult<(Stream Stream, string ContentType)?>(null);

        Stream stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        return Task.FromResult<(Stream Stream, string ContentType)?>((stream, contentType));
    }

    public Task DeleteAsync(string storedName, CancellationToken ct = default)
    {
        var path = ResolvePath(storedName);
        if (path is not null && File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    /// <summary>Chỉ chấp nhận tên file đơn giản do chính mình tạo, chặn mọi cố gắng đi ra ngoài thư mục.</summary>
    private string? ResolvePath(string storedName)
    {
        if (string.IsNullOrWhiteSpace(storedName)) return null;
        if (storedName.Contains('/') || storedName.Contains('\\') || storedName.Contains("..")) return null;
        if (!string.Equals(Path.GetFileName(storedName), storedName, StringComparison.Ordinal)) return null;
        if (PaymentProofRules.ContentTypeOf(storedName) is null) return null;
        return Path.Combine(_directory, storedName);
    }
}
