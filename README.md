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
  "DefaultConnection": "Host=localhost;Port=____;Database=BlueMoon;Username=____;Password=______;;"
}
```
3. Cập nhật Cơ sở dữ liệu (Database Migration):
- Cách 1 : Mở Package Manager Console trong Visual Studio
- Cách 2 : Chạy terminal. cd đến thư mục BlueMoonApartementManagementConsole
```
dotnet ef database update
```
4. Cài đặt mật khẩu tài khoản admin
```
dotnet user-secrets init
dotnet user-secrets set "ADMIN_INIT_PWD" "<mật-khẩu-vừa-sinh>"
```
5. Chạy ứng dụng
- Cách 1 : Chạy nút tam giác màu xanh lá (Start Without Debugging) hoặc dùng tổ hợp phím Ctrl + F5
- Cách 2 : dotnet run
- Sử dụng http://localhost:5105/
