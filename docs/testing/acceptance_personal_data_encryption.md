# Kiểm thử chấp nhận: Mã hóa dữ liệu cá nhân

> Tài liệu dành cho người kiểm thử. Chạy từng test case trên app thật, ghi kết quả vào các ô trống rồi ký nghiệm thu ở cuối.
>
> **Tự động hóa:** phần lớn các bước đã có test chạy cùng `dotnet test`:
> - Unit test, không cần database: `tests/.../Security/PersonalDataEncryptorTests.cs` (E1 – E7).
> - Test tích hợp, cần biến `BLUEMOON_TEST_CONNECTION`: `tests/.../Integration/PersonalDataEncryptionTests.cs` (E8 – E12).
>
> Chạy riêng một nhóm: `dotnet test --filter "TestId=E8|TestId=E9"`.
> Người kiểm thử **chỉ cần chạy tay các bước ghi "Chạy tay"**, rồi ký nghiệm thu ở cuối.

## 1. Mục đích và phạm vi

Xác nhận yêu cầu **PBI-030 / AC-038** ("When hệ thống ghi dữ liệu cá nhân, Then dữ liệu được mã hóa"), **NFR-007** và
DoD Release v1.0 ("dữ liệu cá nhân được mã hóa"):
- 5 trường của nhân khẩu được **lưu dạng mã hóa** trong CSDL: số CMND/CCCD, nơi cấp, dân tộc, tôn giáo, nơi cư trú trước khi chuyển đến.
- Người dùng có quyền vẫn **xem được bình thường** trên giao diện.
- Kiểm tra trùng số CCCD (AC-002) vẫn hoạt động.
- Nhật ký không chứa dữ liệu cá nhân.

Cách mã hóa (chọn ở giai đoạn thiết kế theo LOG-042): AES-256-GCM trong ứng dụng, khóa lấy từ cấu hình `PII_ENC_KEY`.
Giá trị lưu trong CSDL có dạng `v1:<base64>`. Số CCCD có thêm cột `IdentityNumberHash` (HMAC-SHA256) để kiểm trùng.

**Không mã hóa:** họ tên, ngày sinh, giới tính, vì cần tìm kiếm, sắp xếp và thống kê theo giới tính, độ tuổi.

**Ngoài phạm vi:** băm mật khẩu (xem `acceptance_password_hashing.md`).

## 2. Môi trường

| Mục | Giá trị |
|---|---|
| Nhánh / commit được kiểm thử | |
| Hệ điều hành | |
| PostgreSQL (`psql --version`) | |
| URL app | `https://localhost:7127` |

**Chuẩn bị:**
1. Cấu hình user-secrets theo README, **có cả `PII_ENC_KEY`**.
2. Dùng database **mới**, ví dụ `BlueMoon_AT_PII`, để kết quả không lẫn dữ liệu cũ.
3. Chạy app: `dotnet run --launch-profile https` trong thư mục `BlueMoonApartementManagementConsole/`.

Các lệnh `psql` bên dưới dùng `-d BlueMoon_AT_PII`. Nếu dùng tên database khác thì thay vào cho đúng.

## 3. Dữ liệu test

Chỉ dùng **dữ liệu giả** (RSK-008).

| Trường | Giá trị |
|---|---|
| Mã căn hộ | `AT-PII-01` (và `AT-PII-02` cho M2) |
| Họ tên | `Nguyễn Văn Thử` |
| Số CMND/CCCD | `001099000001` |
| Nơi cấp | `Cục Cảnh sát QLHC về TTXH` |
| Dân tộc / Tôn giáo | `Kinh` / `Không` |
| Nơi cư trú trước khi chuyển đến | `Số 1 phố Giả Định, Hà Nội` |

---

## 4. Test case

### M1. Dữ liệu cá nhân lưu dạng mã hóa, giao diện vẫn hiện bình thường

> **Tự động** (E1, E2, E8). **Chạy tay bước 3 và 4**: xem thẳng trong CSDL, và xem trên giao diện.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Đăng nhập `admin`, vào **Hồ sơ căn hộ**. Bấm **+ Thêm căn hộ**, nhập mã `AT-PII-01` và các ô bắt buộc, bấm **Tạo căn hộ** | Căn hộ được tạo |
| 2 | Ở **Hồ sơ hộ gia đình** của căn đó: nhập số hộ khẩu, chủ hộ, số nhà, ngày lập. Bấm **+ Thêm nhân khẩu**, nhập dữ liệu ở mục 3, bấm **Lưu nhân khẩu**, rồi **Tạo hồ sơ hộ khẩu** | Lưu thành công |
| 3 | **Chạy tay.** `psql -d BlueMoon_AT_PII -c 'select "FullName", "IdentityNumber", "Religion", "IdentityNumberHash" from residents;'` | `IdentityNumber`, `Religion` bắt đầu bằng `v1:` và **không** chứa `001099000001`, `Không`. `FullName` là chữ rõ. `IdentityNumberHash` là chuỗi 64 ký tự 0-9, a-f |
| 4 | **Chạy tay.** Tải lại trang, chọn căn `AT-PII-01`, mở nhân khẩu vừa tạo | Số CCCD, nơi cấp, dân tộc, tôn giáo, nơi cư trú trước hiện **đúng chữ đã nhập** |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### M2. Trùng số CCCD vẫn bị từ chối

> **Tự động** (E5, E9, E10). Không cần chạy tay.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Tạo căn `AT-PII-02`, tạo hồ sơ hộ có 1 nhân khẩu dùng lại CCCD `001099000001` (có thể thêm khoảng trắng ở hai đầu) | Bị từ chối, báo "Số CMND/CCCD đã được sử dụng." |
| 2 | Sửa nhân khẩu ở M1, giữ nguyên CCCD, đổi tôn giáo thành `Phật giáo` | Lưu được. Giao diện hiện `Phật giáo` |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### M3. Dữ liệu tạo trước khi có mã hóa được mã hóa khi khởi động

> **Tự động** (E7, E11). Không cần chạy tay.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Có một nhân khẩu lưu bản rõ (dữ liệu cũ). Khởi động app | Dòng đó được mã hóa (bắt đầu bằng `v1:`) và có `IdentityNumberHash`. Giao diện vẫn đọc đúng |
| 2 | Tạo hộ mới dùng lại số CCCD của dòng cũ | Bị từ chối, báo "Số CMND/CCCD đã được sử dụng." |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### M4. Nhật ký không chứa dữ liệu cá nhân

> **Tự động** (E12). Không cần chạy tay.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Sau M1, M2, vào **Nhật ký**, xem các dòng tạo và cập nhật hồ sơ hộ khẩu (hiện đang hiển thị tên tiếng Anh `CreateHouseholdProfile`, `UpdateHouseholdProfile`, xem Notes của PBI-001) | Nội dung **không** chứa số CCCD hay tôn giáo |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### M5. Thiếu khóa thì app không chạy

> **Tự động** phần thông báo lỗi (E6). **Chạy tay**: thử khởi động app khi thiếu khóa.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Trong `BlueMoonApartementManagementConsole/`, chạy `dotnet user-secrets list` và **chép lại giá trị `PII_ENC_KEY`** | Có giá trị |
| 2 | `dotnet user-secrets remove "PII_ENC_KEY"`, rồi chạy app | App dừng khi khởi động, báo "Thiếu cấu hình PII_ENC_KEY" |
| 3 | `dotnet user-secrets set "PII_ENC_KEY" "<giá trị đã chép ở bước 1>"`, chạy lại app, mở nhân khẩu ở M1 | App chạy. Dữ liệu đọc đúng như trước |

Bước 3 phải đặt lại **đúng khóa cũ**. Đặt khóa khác thì dữ liệu đã mã hóa không đọc lại được.

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

---

## 5. Giới hạn đã biết

| # | Mô tả |
|---|---|
| 1 | Tra cứu theo số CCCD chỉ khớp khi nhập **đủ số**, không tìm được một phần số (ảnh hưởng PBI-008) |
| 2 | **Mất khóa `PII_ENC_KEY` là mất dữ liệu đã mã hóa.** Bản sao lưu database (PBI-031) phải đi kèm việc giữ khóa ở chỗ khác |
| 3 | Migration `EncryptResidentPersonalData` khi chạy ngược (Down) không giải mã lại dữ liệu |

## 6. Tổng hợp và nghiệm thu

| Test case | Pass/Fail | Ghi chú |
|---|---|---|
| M1. Lưu mã hóa, giao diện hiện bình thường | | |
| M2. Trùng CCCD bị từ chối | | |
| M3. Mã hóa dữ liệu cũ | | |
| M4. Nhật ký không chứa dữ liệu cá nhân | | |
| M5. Thiếu khóa thì app không chạy | | |

| | |
|---|---|
| Người kiểm thử | |
| Ngày kiểm thử | |
| Kết luận | ☐ Đạt  ☐ Không đạt |
| Chữ ký | |
