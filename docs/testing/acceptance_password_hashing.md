# Kiểm thử chấp nhận: Băm mật khẩu

> Tài liệu dành cho **người kiểm thử độc lập** (không phải người viết code/test).
> Người kiểm thử chạy từng test case trên app thật, ghi kết quả vào các ô trống rồi ký nghiệm thu ở cuối.

## 1. Mục đích và phạm vi

Xác nhận hệ thống **không lưu mật khẩu dạng chữ rõ** ở bất kỳ đâu, và các luồng liên quan đến mật khẩu hoạt động đúng:
đăng ký, đăng nhập, khóa tài khoản, BQT cấp lại mật khẩu, tự đổi mật khẩu, nhật ký hệ thống.

**Ngoài phạm vi:** mã hóa dữ liệu cá nhân (chưa có tính năng, kiểm thử ở giai đoạn sau).

Unit test tương ứng: `tests/BlueMoonApartementManagementConsole.Tests/Services/` (chạy bằng `dotnet test`).

## 2. Môi trường

| Mục | Giá trị |
|---|---|
| Nhánh / commit được kiểm thử | |
| Hệ điều hành | |
| .NET SDK (`dotnet --list-sdks`) | |
| PostgreSQL (`psql --version`) | |
| URL app | `https://localhost:7127` |

**Chuẩn bị:**
1. Cấu hình user-secrets (`ConnectionStrings:DefaultConnection`, `ADMIN_INIT_PWD`) theo README.
2. Dùng database **mới**, ví dụ đặt tên `BlueMoon_AT`, để kết quả không lẫn dữ liệu cũ.
3. Chạy app: `dotnet run --launch-profile https` trong thư mục `BlueMoonApartementManagementConsole/`.

Các lệnh `psql` bên dưới dùng `-d BlueMoon_AT`. Nếu dùng tên database khác thì thay vào cho đúng.

## 3. Dữ liệu test

| Tài khoản | Mật khẩu | Vai trò | Dùng ở |
|---|---|---|---|
| `admin` | giá trị `ADMIN_INIT_PWD` | Ban quản trị | A3 |
| `cudan_at01` | `TestPass@01` | Cư dân | A1, A2, A3 |
| `cudan_at02` | `TestPass@01` (cố ý trùng) | Cư dân | A1, A4 |

---

## 4. Test case

### A1. Mật khẩu được lưu dạng hash, có salt

**Tiền điều kiện:** app đang chạy, chưa có tài khoản `cudan_at01`, `cudan_at02`.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Vào `/login`, bấm **Đăng ký**. Nhập Tên người dùng `Cu dan AT 01`, Tài khoản `cudan_at01`, Mật khẩu `TestPass@01`, Vai trò **Cư dân**, bấm **Đăng ký** | Hiện "Đăng ký thành công. Bạn có thể đăng nhập ngay." |
| 2 | Lặp lại bước 1 với `cudan_at02`, cùng mật khẩu `TestPass@01` | Đăng ký thành công |
| 3 | Đăng ký `cudan_at03` với mật khẩu 7 ký tự `Abc@123` | Bị từ chối, báo mật khẩu phải từ 8 đến 100 ký tự |
| 4 | Chạy `psql -d BlueMoon_AT -c 'select "Username", "PasswordHash" from users;'` | Cột `PasswordHash` **không** chứa `TestPass@01`. Là chuỗi base64 dài khoảng 84 ký tự |
| 5 | So sánh `PasswordHash` của `cudan_at01` và `cudan_at02` | Hai chuỗi **khác nhau** dù cùng mật khẩu |
| 6 | Kiểm tra `cudan_at03` không có trong bảng `users` | Không có |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### A2. Đăng nhập và khóa tài khoản khi sai nhiều lần

**Tiền điều kiện:** đã chạy A1.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Đăng nhập `cudan_at01` / `TestPass@01`, Tư cách **Cư dân** | Vào được hệ thống |
| 2 | Đăng xuất. Đăng nhập `cudan_at01` / `TestPass@01` nhưng Tư cách **Kế toán** | Báo "Tài khoản, mật khẩu hoặc tư cách đăng nhập không đúng." |
| 3 | Đăng nhập `cudan_at01` với mật khẩu sai `SaiMatKhau@1`, Tư cách **Cư dân**, **5 lần liên tiếp** | Cả 5 lần đều báo "Tài khoản, mật khẩu hoặc tư cách đăng nhập không đúng." (không tiết lộ là sai mật khẩu hay sai tài khoản) |
| 4 | Lần thứ 6, nhập **đúng** mật khẩu `TestPass@01` | Báo "Tài khoản tạm bị khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau 15 phút." |
| 5 | Chạy `psql -d BlueMoon_AT -c "select \"Username\", \"LockedUntil\" from users where \"Username\" = 'cudan_at01';"` | `LockedUntil` khoảng 15 phút sau thời điểm lần sai thứ 5 |
| 6 | Đăng nhập tài khoản không tồn tại `khongtontai` / `TestPass@01` | Cùng thông báo như bước 3 |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### A3. BQT cấp lại mật khẩu

**Tiền điều kiện:** đã chạy A2. Có thể chờ hết 15 phút, hoặc để BQT **Mở khóa** `cudan_at01` trước.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Đăng nhập `admin`, Tư cách **Ban quản trị**. Vào `/quan-tri/tai-khoan` | Thấy trang "Tài khoản & phân quyền" |
| 2 | Ở dòng `cudan_at01`, bấm **Cấp lại mật khẩu**, xác nhận | Hiện khung "Mật khẩu tạm cho cudan_at01" kèm mật khẩu 12 ký tự. **Ghi lại** mật khẩu này |
| 3 | Bấm **Đã ghi lại, đóng**, rồi tải lại trang | Mật khẩu tạm **không** hiện lại |
| 4 | Đăng xuất. Đăng nhập `cudan_at01` bằng mật khẩu cũ `TestPass@01` | Bị từ chối |
| 5 | Đăng nhập `cudan_at01` bằng mật khẩu tạm | Vào được, nhưng bị chuyển tới `/ho-so` và có thông báo phải đổi mật khẩu tạm |
| 6 | Gõ thẳng địa chỉ `/` hoặc trang khác vào thanh địa chỉ | Vẫn bị đưa về `/ho-so` |
| 7 | Chạy `psql` xem `PasswordHash` của `cudan_at01` | Không chứa mật khẩu tạm. Khác giá trị hash ở A1 |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### A4. Tự đổi mật khẩu

**Tiền điều kiện:** đăng nhập `cudan_at02` / `TestPass@01`. Vào `/ho-so`, mục **Đổi mật khẩu**.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Nhập mật khẩu hiện tại **sai**, mật khẩu mới `NewPass@02` hai lần, bấm **Đổi mật khẩu** | Bị từ chối, báo mật khẩu hiện tại không đúng |
| 2 | Nhập đúng mật khẩu hiện tại, mật khẩu mới `Abc@123` (7 ký tự) | Bị từ chối, báo mật khẩu mới tối thiểu 8 ký tự |
| 3 | Nhập đúng mật khẩu hiện tại, mật khẩu mới bằng chính `TestPass@01` | Bị từ chối, báo mật khẩu mới phải khác mật khẩu hiện tại |
| 4 | Nhập "Nhập lại mật khẩu mới" không khớp | Báo "Mật khẩu nhập lại không khớp." |
| 5 | Nhập đúng: hiện tại `TestPass@01`, mới `NewPass@02` hai lần | Báo "Đã đổi mật khẩu. Đang chuyển đến trang chủ..." |
| 6 | Đăng xuất. Đăng nhập bằng `TestPass@01` | Bị từ chối |
| 7 | Đăng nhập bằng `NewPass@02` | Vào được |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### A5. Nhật ký hệ thống không chứa mật khẩu

**Tiền điều kiện:** đã chạy A1 đến A4.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Đăng nhập `admin`, vào `/quan-tri/nhat-ky` | Thấy các sự kiện đăng ký, đăng nhập sai, khóa, cấp lại mật khẩu, đổi mật khẩu |
| 2 | Kiểm tra cột chi tiết của từng dòng | Không dòng nào chứa mật khẩu (`TestPass@01`, `SaiMatKhau@1`, `NewPass@02`, mật khẩu tạm ở A3) |
| 3 | Chạy với từng mật khẩu đã dùng: `psql -d BlueMoon_AT -Atc 'copy (select * from audit_logs) to stdout' \| grep -c 'TestPass@01'` | Kết quả `0` cho **mọi** mật khẩu |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

---

## 5. Lỗi đã biết (không cần báo lại)

Các lỗi dưới đây đã được ghi nhận và sẽ sửa ở task riêng. Gặp đúng các lỗi này thì ghi chú, **không** đánh Fail test case vì chúng:

| # | Mô tả | Ảnh hưởng tới |
|---|---|---|
| 1 | `ADMIN_INIT_PWD` không bị kiểm tra độ dài tối thiểu 8 ký tự | Tài khoản `admin` có thể có mật khẩu ngắn |
| 2 | Hash được tạo lại khi đăng nhập không được lưu nếu người dùng chọn sai tư cách | Không thấy được khi test tay |
| 3 | Lần sai mật khẩu thứ 5, nhật ký ghi "Sai mật khẩu, lần 0" thay vì "lần 5" | A2, A5 |

## 6. Tổng hợp và nghiệm thu

| Test case | Pass/Fail | Ghi chú |
|---|---|---|
| A1. Lưu hash, có salt | | |
| A2. Đăng nhập, khóa tài khoản | | |
| A3. BQT cấp lại mật khẩu | | |
| A4. Tự đổi mật khẩu | | |
| A5. Nhật ký không chứa mật khẩu | | |

| | |
|---|---|
| Người kiểm thử | |
| Ngày kiểm thử | |
| Kết luận | ☐ Đạt  ☐ Không đạt |
| Chữ ký | |
