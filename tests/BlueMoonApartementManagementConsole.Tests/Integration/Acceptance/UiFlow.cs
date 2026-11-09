using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BlueMoonApartementManagementConsole.BackEnd.Models.Enities;

namespace BlueMoonApartementManagementConsole.Tests.Integration.Acceptance;

/// <summary>Kết quả sau khi gửi một form hoặc mở một trang: status, chỗ redirect tới, và HTML đã giải mã.</summary>
public sealed record PageResult(HttpStatusCode Status, Uri? Location, string Html)
{
    /// <summary>Đường dẫn redirect (bỏ phần https://localhost), null nếu không redirect.</summary>
    public string? RedirectPath => Location is null
        ? null
        : (Location.IsAbsoluteUri ? Location : new Uri(new Uri("https://localhost"), Location)).PathAndQuery;

    public bool IsRedirect => Status is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.SeeOther;

    /// <summary>Nội dung khung thông báo (class "alert"), đã bỏ thẻ HTML. Null nếu không có.</summary>
    public string? Alert
    {
        get
        {
            var m = Regex.Match(Html, "<div class=\"alert[^\"]*\" role=\"(?:alert|status)\"[^>]*>(.*?)</div>", RegexOptions.Singleline);
            return m.Success ? Regex.Replace(m.Groups[1].Value, "<[^>]+>", string.Empty).Trim() : null;
        }
    }

    /// <summary>Giá trị thuộc tính value của ô input có id cho trước.</summary>
    public string? InputValue(string id)
    {
        var tag = Regex.Match(Html, $"<input[^>]*\\bid=\"{Regex.Escape(id)}\"[^>]*>");
        if (!tag.Success) return null;
        var value = Regex.Match(tag.Value, "\\bvalue=\"([^\"]*)\"");
        return value.Success ? value.Groups[1].Value : string.Empty;
    }

    public static async Task<PageResult> FromAsync(HttpResponseMessage response) =>
        new(response.StatusCode, response.Headers.Location,
            WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
}

/// <summary>
/// Thao tác như người dùng thật: gửi đúng form Đăng nhập / Đăng ký của trang /login (Blazor SSR),
/// kèm token antiforgery và trường _handler lấy từ chính trang đó; gọi API như JavaScript của các trang.
/// Client phải tạo bằng BlueMoonAppFactory.CreateHttpsClient() (giữ cookie, không tự đi theo redirect).
/// </summary>
public static class UiFlow
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public const string WrongCredentials = "Tài khoản, mật khẩu hoặc tư cách đăng nhập không đúng.";
    public const string LockedMessage = "Tài khoản tạm bị khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau 15 phút.";
    public const string PendingMessage = "Tài khoản đang chờ Ban quản trị xác nhận.";

    public static Task<PageResult> GetAsync(HttpClient client, string url) => SendAsync(client.GetAsync(url));

    public static Task<PageResult> LoginAsync(HttpClient client, string username, string password, UserRole role, string? returnUrl = null)
    {
        var url = returnUrl is null ? "/login" : "/login?ReturnUrl=" + Uri.EscapeDataString(returnUrl);
        return SubmitFormAsync(client, url, new Dictionary<string, string>
        {
            ["Input.Username"] = username,
            ["Input.Password"] = password,
            ["Input.Role"] = role.ToString()
        });
    }

    public static Task<PageResult> RegisterAsync(HttpClient client, string username, string password, string fullName, UserRole role) =>
        SubmitFormAsync(client, "/login?mode=register", new Dictionary<string, string>
        {
            ["RegInput.FullName"] = fullName,
            ["RegInput.Username"] = username,
            ["RegInput.Password"] = password,
            ["RegInput.Role"] = role.ToString()
        });

    /// <summary>Đăng nhập và yêu cầu thành công (redirect, không có thông báo lỗi).</summary>
    public static async Task LoginOkAsync(HttpClient client, string username, string password, UserRole role)
    {
        var result = await LoginAsync(client, username, password, role);
        Assert.True(result.IsRedirect, $"Đăng nhập {username} không thành công: {result.Alert}");
    }

    public static Task<PageResult> LogoutAsync(HttpClient client) => SendAsync(client.PostAsync("/logout", null));

    /// <summary>Đổi mật khẩu như trang Hồ sơ (profile.js gọi API này).</summary>
    public static Task<HttpResponseMessage> ChangePasswordAsync(HttpClient client, string current, string next) =>
        client.PostAsJsonAsync("/api/ho-so/doi-mat-khau", new { currentPassword = current, newPassword = next }, Json);

    public sealed record CreatedAccount(int Id, string TempPassword);

    /// <summary>BQT tạo tài khoản như form "Tạo tài khoản mới" (accounts.js gọi API này).</summary>
    public static async Task<CreatedAccount> CreateAccountAsync(HttpClient client, string username, UserRole role)
    {
        var response = await client.PostAsJsonAsync("/api/tai-khoan",
            new { username, fullName = "AT " + username, role = role.ToString() }, Json);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Tạo {username} trả {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<CreatedAccount>(body, Json)!;
    }

    /// <summary>Các nút thao tác trên trang (Vô hiệu hóa, Mở khóa, Xác nhận, ...) gọi POST không có body.</summary>
    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string url) => client.PostAsync(url, null);

    private sealed record TempPasswordResponse(string TempPassword);

    /// <summary>BQT cấp lại mật khẩu, trả về mật khẩu tạm (API trả "id": null kèm "tempPassword").</summary>
    public static async Task<string> ResetPasswordAsync(HttpClient client, int userId)
    {
        var response = await ReadOkJsonAsync<TempPasswordResponse>(await PostAsync(client, $"/api/tai-khoan/{userId}/cap-lai-mat-khau"));
        return response.TempPassword;
    }

    public static async Task<T> ReadOkJsonAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} trả {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    public static async Task<T> GetJsonAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {url} trả {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    /// <summary>Lấy thông báo lỗi trong body JSON dạng ApiError {code, message}.</summary>
    public static async Task<(string? Code, string? Message)> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            return (root.TryGetProperty("code", out var c) ? c.GetString() : null,
                    root.TryGetProperty("message", out var m) ? m.GetString() : null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static async Task<PageResult> SendAsync(Task<HttpResponseMessage> request) =>
        await PageResult.FromAsync(await request);

    private static async Task<PageResult> SubmitFormAsync(HttpClient client, string url, Dictionary<string, string> fields)
    {
        // Mở trang trước để lấy token antiforgery (kèm cookie) và _handler của form, giống trình duyệt
        var page = await client.GetAsync(url);
        var html = await page.Content.ReadAsStringAsync();
        Assert.True(page.StatusCode == HttpStatusCode.OK, $"GET {url} trả {(int)page.StatusCode}");

        var form = new Dictionary<string, string>();
        foreach (Match hidden in Regex.Matches(html, "<input[^>]*type=\"hidden\"[^>]*>"))
        {
            var name = Regex.Match(hidden.Value, "\\bname=\"([^\"]+)\"");
            var value = Regex.Match(hidden.Value, "\\bvalue=\"([^\"]*)\"");
            if (name.Success)
                form[name.Groups[1].Value] = WebUtility.HtmlDecode(value.Success ? value.Groups[1].Value : string.Empty);
        }
        foreach (var (key, value) in fields)
            form[key] = value;

        return await SendAsync(client.PostAsync(url, new FormUrlEncodedContent(form)));
    }
}
