# BlueMoon Apartment Management System
> Hệ thống quản lý chung cư BlueMoon được xây dựng trên nền tảng .NET 10 / Blazor Web App với kiến trúc N-Layer.

# Công nghệ sử dụng
* **Frontend & Backend Framework:** .NET 10 (Blazor Web App - Interactive Server / SSR)
* **ORM:** Entity Framework Core 10.0.14
* **Database:** PostgreSQL
* **UI Components:** Bootstrap 5 / Custom CSS
* **Version Control:** Git & GitHub

# Cấu trúc dự án

```text
BlueMoonApartementManagementConsole/
├── docs/                   # Tài liệu thiết kế hệ thống & Sơ đồ CSDL
├── tests/                  # Các project Unit Test & Integration Test
└── src/
    └── BlueMoonApartementManagementConsole/
        ├── Components/     # Frontend (Layout & các trang Razor Pages)
        │   ├── Layout/
        │   └── Pages/
        ├── Models/         # Data Layer (Entities, DTOs, Interfaces)
        │   ├── Entities/
        │   ├── DTOs/
        │   └── Interfaces/
        ├── Services/               # Business Logic Layer
        ├── Repositories/           # Data Access Layer
        ├── Infrastructure/         # EF Core DbContext 
        │   └── DBContext
        ├── Controllers/            # Web API Endpoints (nếu có)
        └── wwwroot/                # Tài nguyên tĩnh (CSS, JS, Images)
```

# Hướng dẫn khởi chạy (Getting Started)
## Yêu cầu hệ thống (Prerequisites)
- .NET 10 SDK trở lên
- Visual Studio 2026 (với Workload ASP.NET and web development)
- PostgreSQL

## Các bước cài đặt
1. Clone Repo về máy:
- git clone https://github.com/NamNguynz/IT3180-SE_08-TheBlueMoonApartementManagementProject.git
- cd BlueMoonApartementManagementConsole
2. Cấu hình chuỗi kết nối Database:
- Mở file appsettings.json và cập nhật ConnectionString phù hợp với PostgreSQL của bạn:
```
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=____;Database=BlueMoon;Username=____;Password=______;"
}
```
3. Cài đặt mật khẩu tài khoản admin và khóa mã hóa dữ liệu cá nhân (làm trước bước 4, vì migration cũng cần khóa)
```
dotnet user-secrets init
dotnet user-secrets set "ADMIN_INIT_PWD" "<mật-khẩu-vừa-sinh>"
```
- Khóa mã hóa dữ liệu cá nhân `PII_ENC_KEY` (PBI-030): chuỗi base64 của 32 byte ngẫu nhiên. Tạo và lưu bằng một trong hai cách:
```
# macOS / Linux
dotnet user-secrets set "PII_ENC_KEY" "$(openssl rand -base64 32)"

# Windows (PowerShell 7)
dotnet user-secrets set "PII_ENC_KEY" ([Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)))
```
- Thiếu khóa thì app dừng ngay khi khởi động và báo "Thiếu cấu hình PII_ENC_KEY".
- **Mất khóa là mất dữ liệu đã mã hóa** (CCCD, nơi cấp, dân tộc, tôn giáo, nơi ở trước): không có cách giải mã lại.
  Sao lưu khóa ở chỗ khác với bản sao lưu database (một bản sao lưu bị lộ không được chứa cả khóa).
- Mỗi máy có khóa riêng, nên không dùng chung bản dump database giữa các máy.
  Database tạo trước khi có mã hóa thì không phải xóa: app tự mã hóa dữ liệu cũ khi khởi động.
4. Cập nhật Cơ sở dữ liệu (Database Migration):
- Cách 1 : Mở Package Manager Console trong Visual Studio
- Cách 2 : Chạy terminal. cd đến thư mục BlueMoonApartementManagementConsole
```
dotnet ef database update
```
- Bỏ qua cũng được: app tự chạy migration khi khởi động.
5. Chạy ứng dụng
- Cách 1 : Chạy nút tam giác màu xanh lá (Start Without Debugging) hoặc dùng tổ hợp phím Ctrl + F5
- Cách 2 : dotnet run
- Sử dụng http://localhost:5105/
