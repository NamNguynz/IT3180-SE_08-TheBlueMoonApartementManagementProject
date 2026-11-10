# Kiểm thử chấp nhận: Đăng nhập và đổi mật khẩu

> Tài liệu dành cho **người kiểm thử độc lập** (không phải người viết code/test).
> Người kiểm thử chạy từng test case trên app thật, ghi kết quả vào các ô trống rồi ký nghiệm thu ở cuối.
>
> **Tự động hóa:** cả 11 case C1 – C11 đã có test chạy cùng `dotnet test` (cần biến `BLUEMOON_TEST_CONNECTION`),
> chủ yếu trong `tests/.../Integration/Acceptance/LoginChangePasswordAcceptanceTests.cs`. Chạy riêng một case: `dotnet test --filter "Acceptance=C1"`.
> Không còn bước nào phải chạy tay. Lỗi G2 đã sửa (10/11/2026), test C8 chạy lại bình thường.

## 1. Mục đích và phạm vi

Xác nhận luồng **đăng nhập**, **phiên đăng nhập**, **đăng xuất** và **đổi mật khẩu** hoạt động đúng và an toàn.

**Đã kiểm ở tài liệu khác, không lặp lại ở đây:**

| Nội dung | Tài liệu |
|---|---|
| Đăng nhập đúng/sai, khóa sau 5 lần sai (A2) | `acceptance_password_hashing.md` |
| BQT cấp lại mật khẩu tạm, bắt đổi mật khẩu ở trang `/ho-so` (A3) | `acceptance_password_hashing.md` |
| Các trường hợp đổi mật khẩu bị từ chối và thành công (A4) | `acceptance_password_hashing.md` |
| Đăng nhập sai tư cách (B1), vô hiệu hóa thì bị đăng xuất ngay (B7) | `acceptance_role_authorization.md` |

Unit test tương ứng (chạy bằng `dotnet test`):
- `tests/BlueMoonApartementManagementConsole.Tests/Services/AuthServiceLoginTests.cs` (L1 đến L10)
- `tests/BlueMoonApartementManagementConsole.Tests/Services/ChangePasswordTests.cs` (P1 đến P5)

## 2. Môi trường

| Mục | Giá trị |
|---|---|
| Nhánh / commit được kiểm thử | |
| Hệ điều hành | |
| Trình duyệt 1 / Trình duyệt 2 (hoặc cửa sổ ẩn danh) | |
| URL app | `https://localhost:7127` |

**Chuẩn bị:**
1. Dùng database **mới**. Trong thư mục `BlueMoonApartementManagementConsole/`, chạy:
   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=BlueMoon_AT_LOGIN;Username=<user>;Password=<pass>"
   ```
2. Chạy app: `dotnet run --launch-profile https`.
3. C6 cần khoảng **25 phút**, nên làm C6 song song với các test case khác ở một trình duyệt riêng.

## 3. Dữ liệu test

| Tài khoản | Mật khẩu | Vai trò | Cách tạo | Dùng ở |
|---|---|---|---|---|
| `admin` | giá trị `ADMIN_INIT_PWD` | Ban Quản Trị | Tạo tự động | C1, C4, C9 |
| `cudan_lg` | `TestPass@01` | Cư Dân | Tự đăng ký ở `/login?mode=register` | C2 đến C8, C10 |
| `ketoan_lg` | `TestPass@01` | Kế Toán | Tự đăng ký, **không duyệt** | C3 |
| `bqt_c` | mật khẩu tạm | Ban Quản Trị | `admin` tạo ở `/quan-tri/tai-khoan` lúc chạy C9 | C9 |

---

## 4. Test case

### C1. Quay về đúng trang sau khi đăng nhập

> **Tự động** (`Acceptance=C1`). Không cần chạy tay.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Chưa đăng nhập, gõ `https://localhost:7127/quan-tri/nhat-ky` | Bị chuyển tới `/login?ReturnUrl=%2Fquan-tri%2Fnhat-ky` |
| 2 | Đăng nhập `admin`, Tư cách **Ban quản trị** | Vào thẳng trang **Nhật ký** (`/quan-tri/nhat-ky`), không phải trang chủ |
| 3 | Đăng xuất. Vào `/login` (không có `ReturnUrl`) rồi đăng nhập | Vào trang chủ `/` |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### C2. Không bị chuyển hướng ra trang ngoài (kiểm chứng G1)

> **Tự động** (`Acceptance=C2`). Không cần chạy tay. Kết quả: **G1 không xảy ra**, cả 3 địa chỉ đều ở lại site.

Với **từng** địa chỉ dưới đây: mở địa chỉ khi chưa đăng nhập, đăng nhập `cudan_lg` / `TestPass@01` (Tư cách Cư dân), ghi lại địa chỉ trên thanh địa chỉ sau khi đăng nhập, rồi đăng xuất trước khi thử địa chỉ tiếp theo.

| # | Địa chỉ mở | Kết quả mong đợi | Địa chỉ thực tế sau đăng nhập |
|---|---|---|---|
| 1 | `https://localhost:7127/login?ReturnUrl=https://example.com` | Ở lại site, về `https://localhost:7127/` | |
| 2 | `https://localhost:7127/login?ReturnUrl=//example.com` | Ở lại site, về `https://localhost:7127/` | |
| 3 | `https://localhost:7127/login?ReturnUrl=/%5Cexample.com` | Ở lại site: tới `https://localhost:7127//example.com` (trang "Không tìm thấy trang" của chính site) | |

Nếu **bất kỳ** dòng nào bị chuyển sang `example.com` thì đánh **Fail** và ghi "xác nhận G1".

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### C3. Thông báo lỗi không lộ thông tin

> **Tự động** (`Acceptance=C3`). Không cần chạy tay.

| # | Bước (trang `/login`) | Kết quả mong đợi |
|---|---|---|
| 1 | `cudan_lg`, mật khẩu sai `SaiMatKhau@1`, Tư cách Cư dân | "Tài khoản, mật khẩu hoặc tư cách đăng nhập không đúng." |
| 2 | `khongtontai`, `TestPass@01`, Tư cách Cư dân | **Cùng câu** với bước 1 |
| 3 | `ketoan_lg` (đang chờ duyệt), mật khẩu **sai**, Tư cách Kế toán | **Cùng câu** với bước 1 (không được báo "đang chờ xác nhận") |
| 4 | `ketoan_lg`, mật khẩu **đúng**, Tư cách Kế toán | "Tài khoản đang chờ Ban quản trị xác nhận." |
| 5 | Bỏ trống ô Tài khoản, bấm **Đăng nhập** | "Vui lòng nhập tài khoản." |
| 6 | Bỏ trống ô Mật khẩu, bấm **Đăng nhập** | "Vui lòng nhập mật khẩu." |
| 7 | Sau bước 1, nhìn lại ô Mật khẩu | Ô Mật khẩu đã bị xóa trống, ô Tài khoản vẫn giữ nguyên |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### C4. BQT mở khóa tài khoản

> **Tự động** (`Acceptance=C4`). Không cần chạy tay.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Đăng nhập `cudan_lg` với mật khẩu sai 5 lần, rồi đăng nhập đúng | "Tài khoản tạm bị khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau 15 phút." |
| 2 | Trình duyệt khác: đăng nhập `admin`, vào `/quan-tri/tai-khoan` | Dòng `cudan_lg` có trạng thái "Khóa đến ..." và nút **Mở khóa** |
| 3 | Bấm **Mở khóa** | "Đã thực hiện thao tác." Trạng thái về "Đang hoạt động" |
| 4 | Đăng nhập `cudan_lg` / `TestPass@01` ngay | Vào được, không phải chờ 15 phút |
| 5 | Ở `/quan-tri/nhat-ky`, tìm dòng **Mở khóa tài khoản** | Người thực hiện là `admin` |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### C5. Cookie đăng nhập được bảo vệ

> **Tự động** bằng test S7 (`SessionTimeoutTests`): kiểm thuộc tính HttpOnly, Secure, SameSite=Strict, cookie phiên.
> Bước 6 (`document.cookie`) được thay bằng kiểm HttpOnly: cookie có HttpOnly thì JavaScript không đọc được. Không cần chạy tay.

Đăng nhập `cudan_lg`. Mở DevTools (F12) → **Application** → **Cookies** → `https://localhost:7127`.

| # | Kiểm tra | Kết quả mong đợi |
|---|---|---|
| 1 | Có cookie tên `.AspNetCore.Cookies` | Có |
| 2 | Cột **HttpOnly** | ✓ |
| 3 | Cột **Secure** | ✓ |
| 4 | Cột **SameSite** | `Strict` |
| 5 | Cột **Expires / Max-Age** | `Session` (mất khi đóng trình duyệt) |
| 6 | Tab **Console**, gõ `document.cookie` rồi Enter | Kết quả **không** chứa `.AspNetCore.Cookies` |
| 7 | Bấm **Đăng xuất**, xem lại danh sách cookie | `.AspNetCore.Cookies` không còn |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### C6. Hết phiên sau 10 phút không thao tác (NFR-008)

> **Tự động** bằng test S2 – S5 (`SessionTimeoutTests`, dùng đồng hồ giả nên không phải chờ). Không cần chạy tay.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Đăng nhập `cudan_lg`. Ghi lại giờ. **Không thao tác gì** trong 11 phút | — |
| 2 | Sau 11 phút, bấm F5 | Bị chuyển về `/login` |
| 3 | Đăng nhập lại. Ghi lại giờ. Bấm F5 ở các mốc **phút 4, phút 8, phút 12** | Cả 3 lần đều vẫn còn đăng nhập (phiên được gia hạn khi có thao tác) |

| Giờ đăng nhập (bước 1) | Giờ F5 (bước 2) | Kết quả thực tế | Pass/Fail |
|---|---|---|---|
| | | | |

### C7. Đăng xuất

> **Tự động** (`Acceptance=C7`). Không cần chạy tay.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Đăng nhập `cudan_lg`, bấm **Đăng xuất** ở góc trên | Về trang `/login` |
| 2 | Bấm nút **Back** của trình duyệt, rồi F5 | Không vào lại được, bị chuyển về `/login` |
| 3 | Gõ thẳng `/ho-so` | Bị chuyển về `/login` |
| 4 | Đăng nhập `admin`, vào `/quan-tri/nhat-ky`, lọc **Đăng xuất** | Có dòng Đăng xuất của `cudan_lg` |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### C8. Phiên khác sau khi đổi mật khẩu (kiểm chứng G2)

> **Tự động** (`Acceptance=C8`). Không cần chạy tay.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | **Trình duyệt 1** và **trình duyệt 2**: cùng đăng nhập `cudan_lg` / `TestPass@01` | Cả hai vào được |
| 2 | **Trình duyệt 1:** vào `/ho-so`, đổi mật khẩu thành `NewPass@02` | "Đã đổi mật khẩu. Đang chuyển đến trang chủ..." |
| 3 | **Trình duyệt 2:** bấm F5, rồi bấm sang trang **Hồ sơ** | Theo yêu cầu bảo mật: **bị đăng xuất**, về `/login` |
| 4 | **Trình duyệt 1:** tiếp tục dùng | Vẫn dùng bình thường (phiên của người vừa đổi mật khẩu) |
| 5 | Một phiên cũ khác (đăng nhập trước bước 2) gọi thẳng API đổi mật khẩu | Status **401**, mật khẩu không bị đổi |

Nếu ở bước 3, trình duyệt 2 **vẫn dùng được** thì đánh **Fail** (lỗi G2 quay lại).

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### C9. Chặn mọi chức năng khi chưa đổi mật khẩu tạm

> **Tự động** (`Acceptance=C9`). Không cần chạy tay.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Đăng nhập `admin`, vào `/quan-tri/tai-khoan`, tạo `bqt_c` (vai trò Ban quản trị). Ghi lại mật khẩu tạm. Đăng xuất | Có mật khẩu tạm 12 ký tự |
| 2 | Đăng nhập `bqt_c` bằng mật khẩu tạm, Tư cách Ban quản trị | Bị đưa tới `/ho-so`, có cảnh báo "Mật khẩu hiện tại là mật khẩu tạm..." |
| 3 | Gõ thẳng `/quan-tri/tai-khoan`, rồi `/` | Cả hai đều bị đưa về `/ho-so` |
| 4 | Gõ thẳng `/api/tai-khoan`. Xem tab **Network** của DevTools | Status **403**, nội dung có `PASSWORD_CHANGE_REQUIRED` |
| 5 | Bấm **Đăng xuất** | Đăng xuất được, về `/login` |
| 6 | Đăng nhập lại `bqt_c` bằng mật khẩu tạm, đổi mật khẩu thành `TestPass@01` | Đổi thành công, về trang chủ |
| 7 | Vào `/quan-tri/tai-khoan` | Vào được bình thường |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### C10. Tên đăng nhập

> **Tự động** (`Acceptance=C10`). Không cần chạy tay.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Đăng nhập với tài khoản `  cudan_lg  ` (có khoảng trắng hai đầu), mật khẩu đúng | Vào được |
| 2 | Đăng nhập với tài khoản `CUDAN_LG` (chữ hoa), mật khẩu đúng | "Tài khoản, mật khẩu hoặc tư cách đăng nhập không đúng." (G4: đang chờ PO xác nhận đây là hành vi mong muốn) |

Lưu ý: nếu đã chạy C8 thì mật khẩu của `cudan_lg` lúc này là `NewPass@02`.

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### C11. BQT cấp lại mật khẩu thì các phiên đang mở bị đăng xuất

> **Tự động** (`Acceptance=C11`). Không cần chạy tay.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | **Trình duyệt 1:** đăng nhập `cudan_lg`, vào `/ho-so` | Vào được |
| 2 | **Trình duyệt 2:** đăng nhập `admin`, vào `/quan-tri/tai-khoan`, bấm **Cấp lại mật khẩu** ở dòng `cudan_lg`. Ghi lại mật khẩu tạm | Có mật khẩu tạm 12 ký tự |
| 3 | **Trình duyệt 1:** bấm F5 | **Bị đăng xuất**, về `/login` |
| 4 | **Trình duyệt 2:** tải lại trang Tài khoản | `admin` vẫn dùng bình thường |
| 5 | **Trình duyệt 1:** đăng nhập `cudan_lg` bằng mật khẩu tạm | Vào được, bị đưa tới `/ho-so` để đổi mật khẩu (như C9) |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

---

## 5. Lỗi đã biết

### 5.1. Lỗi bảo mật đang kiểm chứng: đánh **Fail** nếu xảy ra

| # | Mô tả | Test case |
|---|---|---|
| G1 | ~~`ReturnUrl` dạng `/\example.com` có thể đưa người dùng ra trang ngoài~~. **Không xảy ra** (test C2, 09/11/2026): server chuyển tới `https://localhost//example.com`, vẫn ở chính site | C2 |
| G2 | ~~Đổi mật khẩu **không** đăng xuất các phiên đăng nhập khác của cùng tài khoản~~. **Đã sửa** (10/11/2026, issue #33): mỗi tài khoản có mã `SecurityStamp` ghi trong cookie, đổi hoặc được cấp lại mật khẩu thì đổi mã, phiên mang mã cũ bị đăng xuất. Sau khi cập nhật bản sửa, mọi người phải đăng nhập lại một lần | C8, C11 |

### 5.2. Đã ghi nhận, không đánh Fail

| # | Mô tả | Test case |
|---|---|---|
| G3 | Ở trang đổi mật khẩu, nhập sai mật khẩu hiện tại bao nhiêu lần cũng không bị khóa | Không test tay (đã có unit test P5) |
| G4 | Tên đăng nhập phân biệt chữ hoa/thường. Đang chờ PO xác nhận | C10 |
| — | Lần sai thứ 5, nhật ký ghi "Sai mật khẩu, lần 0" thay vì "lần 5" | C4 bước 1 |

## 6. Tổng hợp và nghiệm thu

| Test case | Pass/Fail | Ghi chú |
|---|---|---|
| C1. Quay về đúng trang | | |
| C2. Không chuyển hướng ra ngoài | | |
| C3. Thông báo lỗi không lộ thông tin | | |
| C4. BQT mở khóa tài khoản | | |
| C5. Cookie được bảo vệ | | |
| C6. Hết phiên sau 10 phút | | |
| C7. Đăng xuất | | |
| C8. Phiên khác sau khi đổi mật khẩu | | |
| C9. Chặn khi chưa đổi mật khẩu tạm | | |
| C10. Tên đăng nhập | | |
| C11. Cấp lại mật khẩu đăng xuất các phiên | | |

| | |
|---|---|
| Người kiểm thử | |
| Ngày kiểm thử | |
| Kết luận | ☐ Đạt  ☐ Không đạt |
| Chữ ký | |
