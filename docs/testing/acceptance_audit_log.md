# Kiểm thử chấp nhận: Tra cứu nhật ký thao tác quan trọng

> Tài liệu dành cho **người kiểm thử độc lập** (không phải người viết code/test).
> Người kiểm thử chạy từng test case trên app thật, ghi kết quả vào các ô trống rồi ký nghiệm thu ở cuối.
>
> **Tự động hóa (cập nhật 25/11/2026):** mọi test case D1 – D12 đều có test tự động, **không còn bước chạy tay bắt buộc**.
> - Phần server: chạy cùng `dotnet test` (cần biến `BLUEMOON_TEST_CONNECTION`), file `tests/.../Integration/Acceptance/AuditLogAcceptanceTests.cs`
>   và `tests/.../Integration/AuditLogQueryTests.cs`. Chạy riêng một case: `dotnet test --filter "Acceptance=D1"`.
> - Phần chạy trên trình duyệt (bộ lọc, nút Trước/Sau, cách hiện chữ và thời gian): `node --test tests/js/audit-log.test.mjs`.
>   Test chạy chính file `wwwroot/js/audit-log.js` trên DOM giả, không cần trình duyệt, không cài thêm gói.
> - Không tự động: phần chỉ mắt người thấy (màu nút bị tắt, bố cục bảng). Phần này không ảnh hưởng kết luận, không bắt buộc kiểm.
>
> Các bước bên dưới giữ nguyên để chạy lại trên app thật khi cần.

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
| `tests/.../Integration/Acceptance/AuditLogAcceptanceTests.cs` | D1 – D6, D10 – D12: phần server của từng test case | Có |
| `tests/js/audit-log.test.mjs` | Phần trình duyệt: D3 bước 5, D5, D6, D8, D9, J7, D11 bước 3, hết phiên thì về `/login`. Chạy: `node --test tests/js/*.test.mjs` | Không (cần Node) |

Chạy ở gốc repo:
```bash
BLUEMOON_TEST_CONNECTION="Host=localhost;Port=5432;Database=BlueMoon_IntegrationTest;Username=<user>;Password=<pass>" dotnet test
```
Không đặt biến này thì Q1 đến Q12 bị bỏ qua (Skipped). Riêng Q7 luôn ở trạng thái Skipped cho tới khi sửa J1.

## 2. Môi trường

| Mục | Giá trị |
|---|---|
| Nhánh / commit được kiểm thử | Code app `1231392` (`main` sau PR #53). Test chạy trên nhánh `docs/test-plan-report` |
| Hệ điều hành | macOS (Darwin 27), .NET 10, PostgreSQL trên máy, Node v26 |
| Trình duyệt chính / trình duyệt phụ (hoặc cửa sổ ẩn danh) | Không dùng ở lần nghiệm thu 25/11/2026: phần trình duyệt chạy bằng test JS trên DOM giả |
| URL app | `https://localhost:7127` |

**Chuẩn bị:**
1. Dùng database **mới**. Trong thư mục `BlueMoonApartementManagementConsole/`, chạy:
   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=BlueMoon_AT_AUDIT;Username=<user>;Password=<pass>"
   ```
   Lệnh trên ghi đè chuỗi kết nối đang lưu trong user-secrets (DB làm việc hằng ngày). Nếu không muốn đổi, bỏ bước 1, 2
   và chạy app bằng biến môi trường, chỉ có tác dụng trong lần chạy đó:
   ```bash
   ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=BlueMoon_AT_AUDIT;Username=<user>;Password=<pass>" \
   ADMIN_INIT_PWD="<mật khẩu admin>" \
   PII_ENC_KEY="$(openssl rand -base64 32)" \
   dotnet run --launch-profile https
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
| Tạo hồ sơ căn hộ / Cập nhật hồ sơ căn hộ | BQT tạo hoặc sửa căn hộ ở trang Hồ sơ căn hộ. Cột Đối tượng là Id căn hộ (D11) |
| Tạo hồ sơ hộ khẩu / Cập nhật hồ sơ hộ khẩu | BQT tạo hoặc sửa hồ sơ hộ (kèm nhân khẩu). Cột Đối tượng là Id hộ (D11) |
| Tạo khoản thu / Điều chỉnh giá khoản thu | Ở trang Khoản thu và đơn giá |

Nhãn của ô lọc **Hành động** và của cột Hành động trong bảng lấy chung từ một danh sách (`AuditActionLabels.cs`).

Cột **Đối tượng** là **Id** (số) của tài khoản bị tác động. Tên tài khoản bị tác động nằm ở cột **Chi tiết**.

### 2.1. Dữ liệu test (dữ liệu giả)

Chỉ dùng dữ liệu giả, không dùng tên, số giấy tờ hay số điện thoại thật (RSK-008).

| Dữ liệu | Giá trị | Dùng ở |
|---|---|---|
| Ban quản trị có sẵn | `admin`, mật khẩu là giá trị `ADMIN_INIT_PWD` lúc chạy app | Mọi case |
| Cư dân tự đăng ký | `cudan_nk` / `TestPass@01`, Tên người dùng "Cu Dan NK". Sau D1 bước 14 đổi thành `TestPass@02` | D1, D3, D5, D6 |
| Kế toán tự đăng ký | `ketoan_nk` / `TestPass@01`, Tên người dùng "Ke Toan NK" (chờ BQT duyệt) | D1, D3, D9 |
| BQT do admin tạo | `bqt_nk`, Tên người dùng "BQT NK", mật khẩu tạm do hệ thống cấp | D1 |
| Tài khoản không tồn tại | `khong_ton_tai` / `abc12345` | D1, D6 (tạo thêm bản ghi) |
| Mật khẩu sai | `SaiMatKhau@1` (hoặc bất kỳ) | D1 bước 10 |
| Chuỗi có mã HTML | `<img src=x onerror=alert(1)>` (tên đăng nhập), `<script>alert(1)</script>` (chi tiết, chỉ ở test JS) | D8 |
| Hồ sơ căn hộ | Căn `AT-NK-01`, hộ khẩu cùng mã, chủ hộ "Nguyễn Văn Thử" | D11 |
| Khoản thu | `D12_TEST` "Phí test D12", Bắt buộc, Cố định, đơn giá 10.000 đ rồi đổi thành 12.000 đ | D12 |

**Test tự động:**
- Tên tài khoản có tiền tố riêng cho mỗi lần chạy (`at<8 ký tự>_cudan_nk`, xem `TestAccounts.cs`), mật khẩu `TestPass@01`. Tài khoản, căn hộ, khoản thu được xóa sau khi chạy.
  Dòng nhật ký thì giữ lại, vì bảng `audit_logs` chặn xóa (đúng thiết kế).
- Test JS (`tests/js/audit-log.test.mjs`) dùng dữ liệu giả viết ngay trong file test, không gọi server.

---

## 3. Test case

### D1. Ghi đủ 12 loại thao tác

> **Tự động** (`Acceptance=D1`). Không cần kiểm tra lại. Chỉ cần làm các bước để có dữ liệu cho D3, D5, D6, D8, D9.

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
| Tự động `D1_AllTwelveActions_AreLoggedWithActorTargetAndDetail`, ngày 25/11/2026: đủ 12 loại, đúng người thực hiện, đối tượng, chi tiết | Pass | Lần sai thứ 5 ghi `lần 0` (#3), ghi nhận |

### D2. Lọc theo hành động

> **Tự động** (`Acceptance=D2`). Không cần chạy tay.

**Tiền điều kiện:** đã chạy D1.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Chọn Hành động **Đăng nhập thất bại**, bấm **Lọc** | Chỉ có các dòng "Đăng nhập thất bại". Đếm số dòng: ____ (từ D1 là ít nhất 8: bước 4, 6, 7 và 5 dòng ở bước 10) |
| 2 | Chọn **Reset mật khẩu**, bấm **Lọc** | Đúng 1 dòng (D1 bước 13) |
| 3 | Chọn **Tất cả**, bấm **Lọc** | Hiện lại mọi loại |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| Tự động `D2_FilterByAction`, ngày 25/11/2026 | Pass | |

### D3. Lọc theo tài khoản (J2)

> **Tự động** (`Acceptance=D3`). Bước 5 (JavaScript cắt khoảng trắng trước khi gửi): test JS "D3 bước 5" trong `tests/js/audit-log.test.mjs`.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Ô Tài khoản: `cudan_nk`, bấm **Lọc** | Chỉ có các dòng mà **người thực hiện** là `cudan_nk` |
| 2 | Kiểm tra trong kết quả bước 1 | **Không** có dòng "Mở khóa", "Reset mật khẩu", "Vô hiệu hóa", "Kích hoạt" cho `cudan_nk`, vì người thực hiện các dòng đó là `admin` (ghi nhận J2) |
| 3 | Ô Tài khoản: `_nk` | Có dòng của cả `cudan_nk` và `ketoan_nk` (tìm theo một phần tên) |
| 4 | Ô Tài khoản: `CUDAN_NK` (chữ hoa) | Ghi lại thực tế. Theo code hiện tại: **"Không có nhật ký phù hợp."** (phân biệt chữ hoa, chữ thường; ghi nhận J2) |
| 5 | Ô Tài khoản: `   cudan_nk   ` (có khoảng trắng hai đầu) | Kết quả giống bước 1 (giao diện tự cắt khoảng trắng) |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| Tự động `D3_FilterByUsername_ActorOnly_PartialMatch_CaseSensitive`; bước 5: test JS "D3 bước 5". Ngày 25/11/2026 | Pass | Phân biệt hoa thường, chỉ lọc người thực hiện (J2), ghi nhận |

### D4. Lọc theo ngày (J4)

> **Tự động** (`Acceptance=D4`). Không cần chạy tay. Test làm lại đúng cách trang đổi ngày giờ Việt Nam sang giờ chuẩn trước khi gửi.

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
| Tự động `D4_FilterByVietnamDate_ToDateIsInclusive`; đổi ngày Việt Nam sang UTC ở trang: test JS "D5". Ngày 25/11/2026 | Pass | Từ ngày sau Đến ngày ra danh sách rỗng, không báo lỗi (J4), ghi nhận |

### D5. Kết hợp bộ lọc và nút Xóa lọc

> **Tự động** (`Acceptance=D5`). Bước 2 (nút **Xóa lọc**): test JS "D5 bước 2" trong `tests/js/audit-log.test.mjs`.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Hành động **Đăng nhập thất bại**, Tài khoản `cudan_nk`, Từ ngày = Đến ngày = hôm nay, bấm **Lọc** | Chỉ có các dòng thỏa **cả ba** điều kiện (D1 bước 6 và bước 10), không có dòng của `ketoan_nk` hay `khong_ton_tai` |
| 2 | Bấm **Xóa lọc** | Mọi ô lọc trở về trống / "Tất cả". Danh sách hiện mọi dòng, về **Trang 1** |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| Tự động `D5_CombinedFilters_AllMustMatch`; bước 2: test JS "D5 bước 2". Ngày 25/11/2026 | Pass | |

### D6. Phân trang

> **Tự động** (`Acceptance=D6`) phần dữ liệu: mỗi trang 20 dòng, hai trang không trùng nhau, mới nhất lên trước (bước 2).
> Bước 1, 3, 4, 5, 6 (dòng "Trang X / Y (N bản ghi)", nút bị tắt đúng lúc, lọc xong về Trang 1): 2 test JS "D6" trong `tests/js/audit-log.test.mjs`.
> Không tự động: màu mờ của nút bị tắt (do CSS, chỉ mắt người thấy). Test đã kiểm nút có thuộc tính `disabled` đúng lúc.

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
| Tự động `D6_Paging_20PerPage_NewestFirst`; bước 1, 3 – 6: 2 test JS "D6" (45 bản ghi: 3 trang, trang cuối 5 dòng, nút tắt đúng lúc). Ngày 25/11/2026 | Pass | |

### D7. Chỉ đọc (J6)

> **Tự động** bằng test Q11, Q12 (`AuditLogQueryTests`) và D10. Không cần chạy tay.

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
| Tự động Q11 `NonGetMethods_AreRejected` (DELETE, PATCH, PUT trả 405; POST trả 400), Q12 `Database_BlocksUpdateAndDeleteOnAuditLogs`, D10. Ngày 25/11/2026 | Pass | POST trả 400 thay vì 405 (J6), ghi nhận |

### D8. Hiển thị an toàn

> **Tự động** bằng test JS "D8" trong `tests/js/audit-log.test.mjs`: chữ lạ được ghi vào ô bằng `textContent` nên hiện nguyên văn,
> trang chỉ tạo thẻ `tr`, `td`, `span`, không tạo thẻ `img` hay `script` (DOM giả báo lỗi nếu trang ghi bằng `innerHTML`).
> Phần server (API trả nguyên văn tên đăng nhập): D3.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Trình duyệt phụ: đăng nhập với Tài khoản `<img src=x onerror=alert(1)>`, Mật khẩu `abc12345`, tư cách bất kỳ | Báo sai tài khoản hoặc mật khẩu |
| 2 | Trình duyệt chính: mở Nhật ký, bấm **Lọc** | Có dòng "Đăng nhập thất bại", cột Tài khoản hiện **nguyên văn** `<img src=x onerror=alert(1)>` |
| 3 | Quan sát trang | **Không** có hộp thoại `alert` bật lên, **không** có biểu tượng ảnh vỡ |
| 4 | Ô Tài khoản: `<img`, bấm **Lọc** | Tìm ra dòng ở bước 2, vẫn không có hộp thoại |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| Tự động test JS "D8": `<img src=x onerror=alert(1)>` và `<script>alert(1)</script>` hiện nguyên văn, không tạo thẻ HTML. Ngày 25/11/2026 | Pass | |

### D9. Thời gian hiển thị (J7)

> **Tự động** bằng test JS "D9" và "J7" trong `tests/js/audit-log.test.mjs`: trang đổi giờ UTC sang giờ Việt Nam (`Asia/Ho_Chi_Minh`)
> bằng `toLocaleString('vi-VN')`. Node dùng cùng bộ dữ liệu định dạng ngày giờ (ICU) với Chrome nên kết quả giống trình duyệt.
> Phần server (thời gian lưu đến mili giây): D1, D4.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Trình duyệt phụ: đăng nhập `ketoan_nk` (Tư cách Kế toán). Ghi giờ đồng hồ Việt Nam lúc bấm: ____ | Vào được |
| 2 | Trình duyệt chính: xem dòng "Đăng nhập thành công" của `ketoan_nk` | Giờ hiển thị là **giờ Việt Nam**, lệch không quá 1 phút so với giờ ghi ở bước 1. Có đủ ngày, tháng, năm, giờ, phút, giây và 3 chữ số mili giây |
| 3 | Chép nguyên văn chuỗi thời gian vào ô Ghi chú | Ghi chú cho J7 |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| Tự động test JS "D9", "J7": `2026-11-25T07:05:09.123Z` hiện `14:05:09 25/11/2026.123`; 17:30 UTC hiện 00:30 ngày hôm sau. Ngày 25/11/2026 | Pass | Mili giây dính sau năm (J7), ghi nhận |

### D10. Mô tả trang khớp chức năng (J3 đã sửa)

> **Tự động** (`Acceptance=D10`). Không cần chạy tay.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Đọc dòng mô tả dưới tiêu đề "Nhật ký thao tác" | "Ghi nhận các thao tác quan trọng: tài khoản, hồ sơ căn hộ và hộ khẩu, khoản thu và đơn giá. Nhật ký chỉ đọc, không thể sửa hoặc xóa." |
| 2 | Xem các nút trong trang | Chỉ có **Lọc**, **Xóa lọc**, **← Trước**, **Sau →**. Không có nút sửa hay xóa nhật ký |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| Tự động `D10_Page_IsReadOnly_DescriptionRecorded`, ngày 25/11/2026 | Pass | |

### D11. Nhật ký thao tác hồ sơ căn hộ, hộ khẩu

> **Tự động** phần ghi và lọc nhật ký, phần ô lọc có đủ nhãn (`Acceptance=D11`; nhãn phủ đủ mọi loại: unit test N7).
> Bước 3 (chữ trong cột Hành động do JavaScript vẽ): test JS "D11 bước 3" trong `tests/js/audit-log.test.mjs`. Đã chạy tay đạt ngày 10/11/2026.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Đăng nhập `admin`, vào **Hồ sơ căn hộ**: tạo căn `AT-NK-01`, sửa diện tích. Tạo hồ sơ hộ cho căn đó, rồi sửa tên chủ hộ (dữ liệu giả) | Lưu thành công |
| 2 | Vào **Nhật ký**, mở ô **Hành động** | Có 4 lựa chọn **Tạo hồ sơ căn hộ**, **Cập nhật hồ sơ căn hộ**, **Tạo hồ sơ hộ khẩu**, **Cập nhật hồ sơ hộ khẩu** |
| 3 | Lần lượt chọn từng lựa chọn ở bước 2, bấm **Lọc** | Mỗi lần có ít nhất 1 dòng, cột Hành động hiện **đúng nhãn tiếng Việt** (không hiện tên tiếng Anh như `CreateApartmentProfile`), cột Đối tượng là Id căn hộ hoặc hộ vừa tạo |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| Tự động `D11_ProfileActions_AreLogged_Filterable_AndLabelledInVietnamese`; bước 3: test JS "D11 bước 3" và chạy tay đạt ngày 10/11/2026. Ngày 25/11/2026 | Pass | |

### D12. Nhật ký đổi đơn giá và ghi nhận thu tiền (AC-017)

> AC của PBI-029 yêu cầu ghi nhật ký khi **sửa đơn giá** và khi **ghi nhận hoặc điều chỉnh khoản thu**.
> **Tự động** (`Acceptance=D12`): tạo khoản thu và đổi đơn giá qua API như trang Khoản thu, rồi kiểm 2 dòng nhật ký
> (người thực hiện, đối tượng, nội dung có giá cũ và giá mới) và nhãn tiếng Việt trong ô lọc. Không cần chạy tay.
> Phần ghi nhận thu tiền: `PaymentServiceTests.PartialPayments_AddUp_UpdateRemainingAndStatus_ThenFullyPaid` (unit test)
> và `PaymentApiTests.PartialThenFullPayment_ThroughApi_AddsUp_AndRejectsInvalidPayments` (test tích hợp) kiểm mỗi lần thu tiền
> có đúng một dòng **Ghi nhận thu tiền**.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Trình duyệt chính (`admin`): vào **Khoản thu** (`/tai-chinh/khoan-thu`). Ở form **Thêm khoản thu** nhập: Mã `D12_TEST`, Tên `Phí test D12`, Loại **Bắt buộc**, Phạm vi **Toàn chung cư**, Chu kỳ **Hàng tháng**, Cơ sở tính **Cố định**, Đơn vị tính `hộ`, Cách đặt giá **Giá chung**, Đơn giá `10000`, Hiệu lực từ: hôm nay. Bấm **Tạo khoản thu** | Khoản thu `D12_TEST` có trong danh sách |
| 2 | Ở dòng `D12_TEST`, bấm **Đổi đơn giá**. Cách đặt giá **Giá chung**, Đơn giá mới `12000`, Hiệu lực từ: ngày mai. Bấm **Lưu đơn giá** | Lưu thành công |
| 3 | Vào **Nhật ký**, chọn Hành động **Tạo khoản thu**, bấm **Lọc**. Sau đó chọn **Điều chỉnh giá khoản thu**, bấm **Lọc** | Mỗi lần có 1 dòng của `admin`, cột Đối tượng là Id khoản thu, cột Chi tiết có mã `D12_TEST`. Dòng đổi giá có giá cũ, giá mới và ngày hiệu lực |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| Tự động `D12_CreateFeeItemAndChangeRate_AreLogged_WithActorTargetAndDetail`; thu tiền: `PaymentServiceTests`, `PaymentApiTests` (2 test). Ngày 25/11/2026 | Pass | |

---

## 4. Lỗi đã biết: ghi nhận, không đánh Fail

| # | Mô tả | Test case | Đã xác nhận bằng |
|---|---|---|---|
| J1 (#73) | Gọi thẳng API với thời gian **không theo UTC** (vd `from=2026-01-01T07%3A00%3A00%2B07%3A00`, tức `2026-01-01T07:00:00+07:00`) thì server lỗi 500, vì Npgsql chỉ ghi được thời gian UTC. Giao diện **không** bị ảnh hưởng vì luôn gửi UTC | Không có (chỉ gọi API trực tiếp) | Test tự động Q7 (đang Skip, chờ fix) |
| J2 (#74) | Lọc tài khoản phân biệt chữ hoa, chữ thường. API không cắt khoảng trắng (chỉ giao diện cắt). Chỉ lọc theo **người thực hiện**, không lọc được theo tài khoản bị tác động | D3 | Q2 |
| J3 (#35) | ~~Mô tả trang chỉ nhắc 4 loại thao tác, thực tế có 12 loại~~. **Đã sửa** (10/11/2026): mô tả nhắc đủ tài khoản, hồ sơ căn hộ và hộ khẩu, khoản thu và đơn giá | D10 | D10 |
| J4 (#75) | Chọn "Từ ngày" sau "Đến ngày" thì chỉ ra danh sách rỗng, không báo lỗi | D4 bước 5 | Đọc code |
| J6 (#76) | `POST /api/nhat-ky` trả 400 thay vì 405 như các method khác (request bị chạy lại tới trang `/not-found`). Request vẫn bị chặn, không ghi hay xóa gì | D7 bước 4 | Q11 |
| J7 (#77) | **Đã xác nhận (25/11/2026):** giao diện ghép 3 số mili giây vào **cuối** chuỗi ngày giờ, mà kiểu `vi-VN` hiện giờ trước ngày, nên mili giây dính sau năm: `14:05:09 25/11/2026.123` | D9 | Test JS "J7" (`tests/js/audit-log.test.mjs`) |
| #3 (#68) | Lần sai mật khẩu thứ 5 ghi "lần 0" (đã ghi ở `acceptance_password_hashing.md`) | D1 bước 10 | Đọc code `AuthService.LoginAsync` |

## 5. Câu hỏi cho PO

| # | Câu hỏi |
|---|---|
| J5 | Bảng `audit_logs` có trigger chặn `UPDATE` và `DELETE` từng dòng (test Q12 đã xác nhận). Theo cơ chế của PostgreSQL, trigger dạng này **không** chặn `TRUNCATE`, và tài khoản sở hữu bảng có thể tắt trigger. Hiện app kết nối DB bằng tài khoản có quyền tạo bảng (để chạy migration). Mức bảo vệ này đã đủ chưa, hay cần tách tài khoản DB của app khỏi tài khoản chạy migration? |
| H4 | (Từ `acceptance_session_timeout.md`) Hết phiên có cần ghi vào nhật ký không? |

## 6. Tổng hợp và nghiệm thu

| Test case | Pass/Fail | Ghi chú |
|---|---|---|
| D1. Ghi đủ 12 loại thao tác | Pass | #3 ghi nhận |
| D2. Lọc theo hành động | Pass | |
| D3. Lọc theo tài khoản | Pass | J2 ghi nhận |
| D4. Lọc theo ngày | Pass | J4 ghi nhận |
| D5. Kết hợp bộ lọc, Xóa lọc | Pass | |
| D6. Phân trang | Pass | |
| D7. Chỉ đọc | Pass | J6 ghi nhận |
| D8. Hiển thị an toàn | Pass | |
| D9. Thời gian hiển thị | Pass | J7 ghi nhận |
| D10. Mô tả trang | Pass | |
| D11. Nhật ký hồ sơ căn hộ, hộ khẩu | Pass | |
| D12. Nhật ký đổi đơn giá, thu tiền | Pass | |

Tất cả chạy tự động ngày 25/11/2026: `dotnet test` (có DB) 439 pass, 1 skip (Q7, chờ sửa J1), 0 fail; `node --test tests/js/*.test.mjs` 11 pass, 0 fail.
Lỗi ghi nhận ở mục 4 không ảnh hưởng kết luận (đúng quy tắc của mục 4).

| | |
|---|---|
| Người kiểm thử | Nguyễn Đỗ Hương Ly (Tester) |
| Ngày kiểm thử | 25/11/2026 |
| Kết luận | ☒ Đạt  ☐ Không đạt |
| Chữ ký | Nguyễn Đỗ Hương Ly |
