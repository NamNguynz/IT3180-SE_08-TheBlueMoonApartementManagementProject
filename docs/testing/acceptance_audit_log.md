# Kiểm thử chấp nhận: Tra cứu nhật ký thao tác quan trọng

> Tài liệu dành cho **người kiểm thử độc lập** (không phải người viết code/test).
> Người kiểm thử chạy từng test case trên app thật, ghi kết quả vào các ô trống rồi ký nghiệm thu ở cuối.

## 1. Mục đích và phạm vi

Xác nhận:
- Mọi thao tác quan trọng đều được **ghi vào nhật ký** với đúng người thực hiện, đúng đối tượng, đúng thời gian.
- Ban quản trị **tra cứu** được nhật ký theo khoảng ngày, theo loại thao tác, theo tài khoản, có phân trang.
- Nhật ký **chỉ đọc**: không sửa, không xóa được.
- Nội dung do người dùng gõ vào (vd: tên đăng nhập) được hiển thị **an toàn**.

**Đã kiểm ở tài liệu khác, không lặp lại:**

| Nội dung | Tài liệu |
|---|---|
| Chỉ BQT vào được trang `/quan-tri/nhat-ky` và API `/api/nhat-ky` (B4, B5) | `acceptance_role_authorization.md` |
| Nhật ký ghi đúng người thực hiện khi thao tác theo cây tài khoản (B8) | `acceptance_role_authorization.md` |
| Nhật ký không chứa mật khẩu (A5) | `acceptance_password_hashing.md` |
| Hết phiên thì thao tác không được thực hiện và không có dòng nhật ký (T3, T7) | `acceptance_session_timeout.md` |

**Test tự động tương ứng:**

| File | Nội dung | Cần PostgreSQL |
|---|---|---|
| `tests/.../Services/AuditServiceTests.cs` | N1 đến N4: chuẩn hóa tên khi ghi log, giới hạn trang và số dòng, chuyển sang DTO | Không |
| `tests/.../Services/AuditCoverageTests.cs` | N5: nội dung log của đăng ký, tạo tài khoản, mở khóa, cấp lại mật khẩu | Không |
| `tests/.../Security/AuditImmutabilityTests.cs` | N6: API chỉ có GET, không có hàm sửa/xóa | Không |
| `tests/.../Integration/AuditLogQueryTests.cs` | Q1 đến Q12: lọc, ranh giới thời gian, thứ tự, phân trang, đăng xuất, JSON, trigger chặn sửa/xóa | Có |

Chạy ở gốc repo:
```bash
BLUEMOON_TEST_CONNECTION="Host=localhost;Port=5432;Database=BlueMoon_IntegrationTest;Username=<user>;Password=<pass>" dotnet test
```
Không đặt biến này thì Q1 đến Q12 bị bỏ qua (Skipped). Riêng Q7 luôn ở trạng thái Skipped cho tới khi sửa J1.

## 2. Môi trường

| Mục | Giá trị |
|---|---|
| Nhánh / commit được kiểm thử | |
| Hệ điều hành | |
| Trình duyệt chính / trình duyệt phụ (hoặc cửa sổ ẩn danh) | |
| URL app | `https://localhost:7127` |

**Chuẩn bị:**
1. Dùng database **mới**. Trong thư mục `BlueMoonApartementManagementConsole/`, chạy:
   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=BlueMoon_AT_AUDIT;Username=<user>;Password=<pass>"
   ```
2. Chạy app: `dotnet run --launch-profile https`.
3. **Trình duyệt chính:** dùng cho `admin` trong suốt quá trình test.
   **Trình duyệt phụ** (hoặc cửa sổ ẩn danh): dùng cho các tài khoản khác. Hai trình duyệt là hai phiên riêng.
4. Mỗi lần xem nhật ký, bấm **Lọc** (hoặc F5) để tải dữ liệu mới nhất.

**Nhãn hành động trên trang Nhật ký:**

| Nhãn | Ý nghĩa |
|---|---|
| Đăng nhập thành công | |
| Đăng nhập thất bại | Sai mật khẩu, tài khoản không tồn tại, sai tư cách, tài khoản chờ xác nhận |
| Bị khóa tạm thời | Đăng nhập khi tài khoản đang bị khóa |
| Đăng xuất | Bấm nút Đăng xuất |
| Đăng ký | Tự đăng ký ở trang đăng nhập |
| Xác nhận tài khoản | BQT duyệt tài khoản chờ |
| Tạo tài khoản mới | BQT tạo ở trang Tài khoản |
| Vô hiệu hóa tài khoản / Kích hoạt tài khoản | |
| Mở khóa tài khoản | |
| Reset mật khẩu | BQT cấp lại mật khẩu |
| Thay đổi mật khẩu | Người dùng tự đổi |

Cột **Đối tượng** là **Id** (số) của tài khoản bị tác động. Tên tài khoản bị tác động nằm ở cột **Chi tiết**.

---

## 3. Test case

### D1. Ghi đủ 12 loại thao tác

Làm lần lượt từng bước. Sau mỗi bước, ở trình duyệt chính (`admin`), mở `/quan-tri/nhat-ky` và kiểm tra **dòng trên cùng**
(hoặc các dòng trên cùng, nếu bước đó sinh nhiều dòng).

| # | Thao tác | Tài khoản | Hành động | Đối tượng | Chi tiết | Pass/Fail |
|---|---|---|---|---|---|---|
| 1 | Trình duyệt chính: đăng nhập `admin`, Tư cách **Ban quản trị** | `admin` | Đăng nhập thành công | Id của admin | — | |
| 2 | Trình duyệt phụ: vào `/login?mode=register`, đăng ký `cudan_nk` / `TestPass@01`, Vai trò **Cư dân**. **Ghi lại Id** ở cột Đối tượng: Id = ____ | `cudan_nk` | Đăng ký | Id của cudan_nk | `Vai trò CU_DAN, trạng thái Approved` | |
| 3 | Trình duyệt phụ: đăng ký `ketoan_nk` / `TestPass@01`, Vai trò **Kế toán** | `ketoan_nk` | Đăng ký | Id của ketoan_nk | `Vai trò KE_TOAN, trạng thái PendingApproval` | |
| 4 | Trình duyệt phụ: đăng nhập `ketoan_nk`, Tư cách **Kế toán** (chưa được duyệt) | `ketoan_nk` | Đăng nhập thất bại | — | `Tài khoản đang chờ xác nhận` | |
| 5 | Trình duyệt chính: vào **Duyệt tài khoản**, bấm **Xác nhận** ở dòng `ketoan_nk` | `admin` | Xác nhận tài khoản | Id của ketoan_nk | `Xác nhận tài khoản ketoan_nk` | |
| 6 | Trình duyệt phụ: đăng nhập `cudan_nk`, Tư cách **Kế toán** (đúng mật khẩu, sai tư cách) | `cudan_nk` | Đăng nhập thất bại | — | `Sai tư cách đăng nhập: chọn KE_TOAN` | |
| 7 | Trình duyệt phụ: đăng nhập `khong_ton_tai` / `abc12345`, tư cách bất kỳ | `khong_ton_tai` | Đăng nhập thất bại | — | `Tài khoản không tồn tại hoặc bị vô hiệu` | |
| 8 | Trình duyệt phụ: đăng nhập `cudan_nk` đúng mật khẩu, Tư cách **Cư dân** | `cudan_nk` | Đăng nhập thành công | Id của cudan_nk | — | |
| 9 | Trình duyệt phụ: bấm **Đăng xuất** | `cudan_nk` | Đăng xuất | Id của cudan_nk | — | |
| 10 | Trình duyệt phụ: đăng nhập `cudan_nk` **sai mật khẩu 5 lần** liên tiếp, Tư cách Cư dân | `cudan_nk` | 5 dòng Đăng nhập thất bại | Id của cudan_nk | `Sai mật khẩu, lần 1` đến `lần 4`, lần 5 xem ghi chú | |
| 11 | Trình duyệt phụ: đăng nhập `cudan_nk` thêm 1 lần (mật khẩu bất kỳ) | `cudan_nk` | Bị khóa tạm thời | — | `Khóa đến <giờ> <ngày>` | |
| 12 | Trình duyệt chính: vào **Tài khoản & phân quyền**, bấm **Mở khóa** ở dòng `cudan_nk` | `admin` | Mở khóa tài khoản | Id của cudan_nk | `Mở khóa tài khoản cudan_nk` | |
| 13 | Trình duyệt chính: bấm **Cấp lại mật khẩu** ở dòng `cudan_nk`, bấm OK. **Ghi lại mật khẩu tạm** | `admin` | Reset mật khẩu | Id của cudan_nk | `Cấp lại mật khẩu cho cudan_nk` (**không** có mật khẩu tạm) | |
| 14 | Trình duyệt phụ: đăng nhập `cudan_nk` bằng mật khẩu tạm, ở trang Hồ sơ đổi mật khẩu thành `TestPass@02` | `cudan_nk` | Đăng nhập thành công, rồi Thay đổi mật khẩu | Id của cudan_nk | — | |
| 15 | Trình duyệt chính: tạo tài khoản `bqt_nk`, Tên người dùng `BQT NK`, Vai trò **Ban quản trị** | `admin` | Tạo tài khoản mới | Id của bqt_nk | `Tạo tài khoản bqt_nk, vai trò BQT` | |
| 16 | Trình duyệt chính: bấm **Vô hiệu hóa** ở dòng `cudan_nk`, bấm OK | `admin` | Vô hiệu hóa tài khoản | Id của cudan_nk | `Tài khoản cudan_nk` | |
| 17 | Trình duyệt chính: bấm **Kích hoạt** ở dòng `cudan_nk` | `admin` | Kích hoạt tài khoản | Id của cudan_nk | `Tài khoản cudan_nk` | |
| 18 | Ở trang Nhật ký, lần lượt chọn từng giá trị ở ô **Hành động** rồi bấm **Lọc** | — | Cả 12 loại đều có ít nhất 1 dòng | — | — | |

**Ghi chú bước 10:** lần sai thứ 5 hiện ghi `Sai mật khẩu, lần 0` (lỗi đã biết #3 trong `acceptance_password_hashing.md`). Gặp đúng lỗi này thì ghi chú, không đánh Fail.

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### D2. Lọc theo hành động

**Tiền điều kiện:** đã chạy D1.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Chọn Hành động **Đăng nhập thất bại**, bấm **Lọc** | Chỉ có các dòng "Đăng nhập thất bại". Đếm số dòng: ____ (từ D1 là ít nhất 8: bước 4, 6, 7 và 5 dòng ở bước 10) |
| 2 | Chọn **Reset mật khẩu**, bấm **Lọc** | Đúng 1 dòng (D1 bước 13) |
| 3 | Chọn **Tất cả**, bấm **Lọc** | Hiện lại mọi loại |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### D3. Lọc theo tài khoản (J2)

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Ô Tài khoản: `cudan_nk`, bấm **Lọc** | Chỉ có các dòng mà **người thực hiện** là `cudan_nk` |
| 2 | Kiểm tra trong kết quả bước 1 | **Không** có dòng "Mở khóa", "Reset mật khẩu", "Vô hiệu hóa", "Kích hoạt" cho `cudan_nk`, vì người thực hiện các dòng đó là `admin` (ghi nhận J2) |
| 3 | Ô Tài khoản: `_nk` | Có dòng của cả `cudan_nk` và `ketoan_nk` (tìm theo một phần tên) |
| 4 | Ô Tài khoản: `CUDAN_NK` (chữ hoa) | Ghi lại thực tế. Theo code hiện tại: **"Không có nhật ký phù hợp."** (phân biệt chữ hoa, chữ thường; ghi nhận J2) |
| 5 | Ô Tài khoản: `   cudan_nk   ` (có khoảng trắng hai đầu) | Kết quả giống bước 1 (giao diện tự cắt khoảng trắng) |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### D4. Lọc theo ngày (J4)

Ngày tính theo **giờ Việt Nam**. "Đến ngày" được tính **trọn ngày đó**.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Từ ngày = **hôm nay**, Đến ngày = **hôm nay**, bấm **Lọc** | Có đủ các dòng của D1 |
| 2 | Từ ngày = Đến ngày = **hôm qua** | "Không có nhật ký phù hợp." |
| 3 | Chỉ điền Từ ngày = **ngày mai** (Đến ngày để trống) | "Không có nhật ký phù hợp." |
| 4 | Chỉ điền Đến ngày = **hôm nay** (Từ ngày để trống) | Có đủ các dòng của D1 |
| 5 | Từ ngày = **ngày mai**, Đến ngày = **hôm nay** (ngược thứ tự) | Ghi lại thực tế. Theo code hiện tại: chỉ hiện "Không có nhật ký phù hợp.", **không** báo khoảng ngày sai (ghi nhận J4) |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### D5. Kết hợp bộ lọc và nút Xóa lọc

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Hành động **Đăng nhập thất bại**, Tài khoản `cudan_nk`, Từ ngày = Đến ngày = hôm nay, bấm **Lọc** | Chỉ có các dòng thỏa **cả ba** điều kiện (D1 bước 6 và bước 10), không có dòng của `ketoan_nk` hay `khong_ton_tai` |
| 2 | Bấm **Xóa lọc** | Mọi ô lọc trở về trống / "Tất cả". Danh sách hiện mọi dòng, về **Trang 1** |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### D6. Phân trang

**Tiền điều kiện:** tổng số bản ghi lớn hơn 20 (xem dòng "Trang 1 / X (N bản ghi)" dưới bảng).
Nếu chưa đủ, ở trình duyệt phụ đăng nhập `khong_ton_tai` / `abc12345` nhiều lần cho đủ.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Bấm **Xóa lọc**. Ghi lại N = ____ | Hiện `Trang 1 / X (N bản ghi)` với X = N chia 20, làm tròn lên. Có đúng 20 dòng. Nút **← Trước** bị mờ |
| 2 | Kiểm tra thứ tự | Dòng trên cùng là thao tác **mới nhất**, thời gian giảm dần từ trên xuống |
| 3 | Bấm **Sau →** | `Trang 2 / X`. Không có dòng nào trùng với trang 1 |
| 4 | Bấm **Sau →** tới trang cuối | Nút **Sau →** bị mờ. Số dòng ở trang cuối = N − 20 × (X − 1) |
| 5 | Bấm **← Trước** | Quay lại trang trước, dữ liệu đúng như lúc trước |
| 6 | Đang ở trang 2, chọn Hành động **Đăng nhập thất bại**, bấm **Lọc** | Về **Trang 1** của kết quả lọc |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### D7. Chỉ đọc (J6)

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Xem trang Nhật ký | Không có nút sửa hay xóa ở bất kỳ dòng nào. Mô tả trang có câu "Nhật ký chỉ đọc, không thể sửa hoặc xóa." |
| 2 | Ghi lại N (số bản ghi). Mở DevTools (F12) → tab **Console**, dán rồi Enter: `fetch('/api/nhat-ky', { method: 'DELETE' }).then(r => r.status)` | Kết quả `405` |
| 3 | Làm lại bước 2 với `'PUT'` | `405` |
| 4 | Làm lại bước 2 với `'POST'` | Ghi lại thực tế. Theo code hiện tại: `400` (ghi nhận J6). Miễn **không phải** `200` hay `204` |
| 5 | Bấm **Xóa lọc** | Số bản ghi vẫn là N |
| 6 | *(Tùy chọn, cần quyền vào database)* Chạy lần lượt hai lệnh ở khối bên dưới bảng này | Cả hai báo `ERROR:  audit_logs là append-only`. Bấm **Xóa lọc**, số bản ghi vẫn là N |

Lệnh cho bước 6:
```bash
psql -U <user> -d BlueMoon_AT_AUDIT -c "DELETE FROM audit_logs WHERE \"Id\" = 1;"
psql -U <user> -d BlueMoon_AT_AUDIT -c "UPDATE audit_logs SET \"Detail\" = 'x' WHERE \"Id\" = 1;"
```

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### D8. Hiển thị an toàn

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Trình duyệt phụ: đăng nhập với Tài khoản `<img src=x onerror=alert(1)>`, Mật khẩu `abc12345`, tư cách bất kỳ | Báo sai tài khoản hoặc mật khẩu |
| 2 | Trình duyệt chính: mở Nhật ký, bấm **Lọc** | Có dòng "Đăng nhập thất bại", cột Tài khoản hiện **nguyên văn** `<img src=x onerror=alert(1)>` |
| 3 | Quan sát trang | **Không** có hộp thoại `alert` bật lên, **không** có biểu tượng ảnh vỡ |
| 4 | Ô Tài khoản: `<img`, bấm **Lọc** | Tìm ra dòng ở bước 2, vẫn không có hộp thoại |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### D9. Thời gian hiển thị (J7)

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Trình duyệt phụ: đăng nhập `ketoan_nk` (Tư cách Kế toán). Ghi giờ đồng hồ Việt Nam lúc bấm: ____ | Vào được |
| 2 | Trình duyệt chính: xem dòng "Đăng nhập thành công" của `ketoan_nk` | Giờ hiển thị là **giờ Việt Nam**, lệch không quá 1 phút so với giờ ghi ở bước 1. Có đủ ngày, tháng, năm, giờ, phút, giây và 3 chữ số mili giây |
| 3 | Chép nguyên văn chuỗi thời gian vào ô Ghi chú | Ghi chú cho J7 |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### D10. Mô tả trang khớp chức năng (J3)

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Đọc dòng mô tả dưới tiêu đề "Nhật ký thao tác" | Ghi lại thực tế. Theo code hiện tại: chỉ nhắc "đăng nhập, đăng xuất, đăng ký và xác nhận tài khoản", trong khi D1 cho thấy có 12 loại (ghi nhận J3) |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | Không đánh giá, chỉ ghi nhận (J3) | |

---

## 4. Lỗi đã biết: ghi nhận, không đánh Fail

| # | Mô tả | Test case | Đã xác nhận bằng |
|---|---|---|---|
| J1 | Gọi thẳng API với thời gian **không theo UTC** (vd `from=2026-01-01T07%3A00%3A00%2B07%3A00`, tức `2026-01-01T07:00:00+07:00`) thì server lỗi 500, vì Npgsql chỉ ghi được thời gian UTC. Giao diện **không** bị ảnh hưởng vì luôn gửi UTC | Không có (chỉ gọi API trực tiếp) | Test tự động Q7 (đang Skip, chờ fix) |
| J2 | Lọc tài khoản phân biệt chữ hoa, chữ thường. API không cắt khoảng trắng (chỉ giao diện cắt). Chỉ lọc theo **người thực hiện**, không lọc được theo tài khoản bị tác động | D3 | Q2 |
| J3 | Mô tả trang chỉ nhắc 4 loại thao tác, thực tế có 12 loại | D10 | Đọc code |
| J4 | Chọn "Từ ngày" sau "Đến ngày" thì chỉ ra danh sách rỗng, không báo lỗi | D4 bước 5 | Đọc code |
| J6 | `POST /api/nhat-ky` trả 400 thay vì 405 như các method khác (request bị chạy lại tới trang `/not-found`). Request vẫn bị chặn, không ghi hay xóa gì | D7 bước 4 | Q11 |
| J7 | *Nghi ngờ, chưa xác nhận:* giao diện ghép 3 số mili giây vào **cuối** chuỗi ngày giờ; nếu trình duyệt hiển thị giờ trước ngày (kiểu `14:05:09 08/11/2026`) thì mili giây sẽ dính sau năm (`...08/11/2026.123`) | D9 | Chờ D9 |
| #3 | Lần sai mật khẩu thứ 5 ghi "lần 0" (đã ghi ở `acceptance_password_hashing.md`) | D1 bước 10 | Đọc code `AuthService.LoginAsync` |

## 5. Câu hỏi cho PO

| # | Câu hỏi |
|---|---|
| J5 | Bảng `audit_logs` có trigger chặn `UPDATE` và `DELETE` từng dòng (test Q12 đã xác nhận). Theo cơ chế của PostgreSQL, trigger dạng này **không** chặn `TRUNCATE`, và tài khoản sở hữu bảng có thể tắt trigger. Hiện app kết nối DB bằng tài khoản có quyền tạo bảng (để chạy migration). Mức bảo vệ này đã đủ chưa, hay cần tách tài khoản DB của app khỏi tài khoản chạy migration? |
| H4 | (Từ `acceptance_session_timeout.md`) Hết phiên có cần ghi vào nhật ký không? |

## 6. Tổng hợp và nghiệm thu

| Test case | Pass/Fail | Ghi chú |
|---|---|---|
| D1. Ghi đủ 12 loại thao tác | | |
| D2. Lọc theo hành động | | |
| D3. Lọc theo tài khoản | | |
| D4. Lọc theo ngày | | |
| D5. Kết hợp bộ lọc, Xóa lọc | | |
| D6. Phân trang | | |
| D7. Chỉ đọc | | |
| D8. Hiển thị an toàn | | |
| D9. Thời gian hiển thị | | |
| D10. Mô tả trang | Không đánh giá | |

| | |
|---|---|
| Người kiểm thử | |
| Ngày kiểm thử | |
| Kết luận | ☐ Đạt  ☐ Không đạt |
| Chữ ký | |
