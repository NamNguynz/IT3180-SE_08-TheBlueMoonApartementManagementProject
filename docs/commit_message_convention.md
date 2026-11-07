# Quy ước đặt Commit Message

## Cấu trúc

```text
<type>(<scope>): <thông điệp ngắn gọn mô tả thay đổi>
```

- **type**: loại thay đổi, chọn một trong các giá trị ở bảng bên dưới.
- **scope**: phạm vi/module bị ảnh hưởng, viết thường, ngắn gọn (ví dụ: `login`, `acc`, `audit`).
- **thông điệp**: mô tả ngắn gọn thay đổi đã làm, đặt ngay sau dấu `: ` (dấu hai chấm và một khoảng trắng).

## Các loại type

| Type | Tên đầy đủ | Ý nghĩa |
|---|---|---|
| `feat` | Feature | Thêm một tính năng mới (chức năng mới cho UI, API mới,...). |
| `fix` | Bug Fix | Sửa lỗi trong mã nguồn. |
| `refactor` | Refactoring | Sửa đổi code nhưng không làm thay đổi tính năng (tối ưu performance, cấu trúc lại hàm/thư mục,...). |
| `docs` | Documentation | Thay đổi hoặc bổ sung tài liệu (Markdown, README, API Docs). |
| `style` | Code Style | Thay đổi format code (khoảng trắng, dấu chấm phẩy, định dạng CSS/Razor) không làm thay đổi logic. |
| `test` | Testing | Bổ sung hoặc sửa đổi các test case (Unit Test, Integration Test). |
| `chore` | Maintenance | Các công việc linh tinh không đụng đến code logic (cập nhật file .gitignore, cài thêm gói NuGet/npm,...). |

## Scope đã dùng trong repo

| Scope | Phạm vi |
|---|---|
| `init` | Khởi tạo project |
| `login` | Đăng nhập / đăng ký |
| `UI` | Giao diện chung |
| `approval` | Duyệt tài khoản |
| `acc` | Quản lý tài khoản |
| `audit` | Nhật ký hệ thống (Audit Log) |
| `tests` | Thư mục / project test |

Nên dùng lại các scope trên cho thống nhất. Với module mới thì đặt scope mới theo tên module (ví dụ: `profile`).

## Ví dụ

Commit đúng quy ước, lấy từ lịch sử repo:

```text
feat(login): Add login and register screen and feature
feat(approval): Add account approval feature
feat(audit): Add Audit Log feature for Security
refactor(relocation): relocation WelcomePage.razor to Pages folder
docs(readmeinit): add initial description in README.md
chore(init): Initialize Project Structure and Instruction File
```

Commit **chưa** đúng quy ước trong repo và cách viết lại:

| Đã viết | Nên viết |
|---|---|
| `Untracking appsettings.json` | `chore(gitignore): untrack appsettings.json` |
| `Remove default owner password` | `chore(config): remove default owner password` |

## Lưu ý

- Mỗi commit chỉ nên chứa một loại thay đổi. Nếu vừa thêm tính năng vừa sửa lỗi thì tách thành hai commit.
- Không dùng thông điệp chung chung như `update`, `fix bug`, `sửa code`.
- Không commit file chứa thông tin bí mật (`appsettings.json`, mật khẩu, chuỗi kết nối).
