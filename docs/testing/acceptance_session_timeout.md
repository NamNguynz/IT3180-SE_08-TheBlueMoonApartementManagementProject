# Kiểm thử chấp nhận: Tự động kết thúc phiên sau 10 phút không tương tác (NFR-008)

> Tài liệu dành cho **người kiểm thử độc lập** (không phải người viết code/test).
> Người kiểm thử chạy từng test case trên app thật, ghi kết quả vào các ô trống rồi ký nghiệm thu ở cuối.
>
> **Tự động hóa (09/11/2026):** case ghi "Tự động" đã có test chạy cùng `dotnet test` (cần biến `BLUEMOON_TEST_CONNECTION`),
> dùng đồng hồ giả nên không phải chờ 10 phút. File `tests/.../Integration/Acceptance/SessionTimeoutAcceptanceTests.cs`
> và `tests/.../Integration/SessionTimeoutTests.cs`. Chạy riêng một case: `dotnet test --filter "Acceptance=T1"`.
> Người kiểm thử **chỉ cần chạy tay T4 bước 5** (mất khoảng 12 phút), rồi ký nghiệm thu ở cuối. Lịch chạy song song ở mục 3 không còn cần.

## 1. Mục đích và phạm vi

Xác nhận người dùng **không thao tác trong 10 phút** thì tự động bị đăng xuất, có thao tác thì phiên được kéo dài,
và sau khi hết phiên thì **không thực hiện được** thao tác nào nữa.

**"Thao tác" trong tài liệu này** là hành động gửi yêu cầu tới server: mở trang, bấm F5, bấm nút gửi/lưu.
Gõ phím, cuộn trang, di chuột **không** được tính (xem H2).

**Đã kiểm ở tài liệu khác, không lặp lại:**

| Nội dung | Tài liệu |
|---|---|
| Thuộc tính cookie đăng nhập: HttpOnly, Secure, SameSite, hết hạn kiểu Session (C5) | `acceptance_login_change_password.md` |
| Để yên 11 phút thì về `/login`; F5 ở phút 4, 8, 12 thì vẫn còn đăng nhập (C6) | `acceptance_login_change_password.md` |

**Test tự động tương ứng** (S1 đến S7, dùng đồng hồ giả nên chạy trong vài giây):
`tests/BlueMoonApartementManagementConsole.Tests/Integration/SessionTimeoutTests.cs`.
Cần PostgreSQL, chạy ở gốc repo:
```bash
BLUEMOON_TEST_CONNECTION="Host=localhost;Port=5432;Database=BlueMoon_IntegrationTest;Username=<user>;Password=<pass>" dotnet test
```
Không đặt biến này thì các test S1 đến S7 bị bỏ qua (Skipped).

## 2. Môi trường

| Mục | Giá trị |
|---|---|
| Nhánh / commit được kiểm thử | |
| Hệ điều hành | |
| Trình duyệt 1 / 2 / 3 (có thể là cửa sổ ẩn danh) | |
| URL app | `https://localhost:7127` |
| Đồng hồ dùng để bấm giờ | |

**Chuẩn bị:**
1. Dùng database **mới**. Trong thư mục `BlueMoonApartementManagementConsole/`, chạy:
   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=BlueMoon_AT_SESSION;Username=<user>;Password=<pass>"
   ```
2. Chạy app: `dotnet run --launch-profile https`.
3. Đăng ký tài khoản `cudan_tg` / `TestPass@01`, vai trò **Cư dân**.
4. **Không** để máy tính ngủ hoặc khóa màn hình trong lúc chờ.
5. Ghi giờ **chính xác tới giây** ở mỗi bước có bấm giờ.

## 3. Lịch chạy song song (khoảng 35 phút)

Mỗi trình duyệt là một phiên riêng nên chạy song song được. **Không** dùng hai tab của **cùng một** trình duyệt cho hai test case khác nhau (trừ T5), vì các tab dùng chung phiên.

| Trình duyệt | Thứ tự | Thời gian |
|---|---|---|
| Trình duyệt 1 | T1, sau đó T7 | ~22 phút |
| Trình duyệt 2 | T2, sau đó T4 | ~24 phút |
| Trình duyệt 3 | T3, sau đó T5 | ~31 phút |
| Bất kỳ, sau cùng | T6 | ~2 phút |

---

## 4. Test case

### T1. Ranh giới 10 phút và quay về đúng trang

> **Tự động** (`Acceptance=T1`). Không cần chạy tay.

| # | Bước | Kết quả mong đợi | Giờ thực tế |
|---|---|---|---|
| 1 | Đăng nhập `admin`, vào `/quan-tri/nhat-ky`. Ghi giờ (gọi là **t0**) | Vào được | |
| 2 | Không thao tác. Ở **t0 + 9:30**, bấm F5 | **Vẫn còn đăng nhập**, trang Nhật ký hiện bình thường | |
| 3 | Không thao tác tiếp. Ở **t0 + 20:00** (10:30 sau lần F5 ở bước 2), bấm F5 | Bị chuyển tới `/login?ReturnUrl=%2Fquan-tri%2Fnhat-ky` | |
| 4 | Đăng nhập lại `admin` | Quay về đúng `/quan-tri/nhat-ky` | |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### T2. Thao tác sớm rồi để yên (kiểm chứng H1)

> **Tự động** bằng test S5 (`SessionTimeoutTests`). Không cần chạy tay. Kết quả: **H1 xảy ra** (thao tác ở phút 4 không gia hạn phiên).

| # | Bước | Kết quả mong đợi | Giờ thực tế |
|---|---|---|---|
| 1 | Đăng nhập `cudan_tg`. Ghi giờ **t0** | Vào được | |
| 2 | Ở **t0 + 4:00**, bấm vào **Hồ sơ** (thao tác cuối cùng) | Mở trang Hồ sơ | |
| 3 | Không thao tác. Ở **t0 + 10:30** (mới **6:30** sau thao tác cuối), bấm F5 | Theo NFR-008 (10 phút kể từ thao tác cuối): **vẫn còn đăng nhập** | |

Nếu ở bước 3 bị chuyển về `/login` thì đánh **Fail** và ghi "xác nhận H1".

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### T3. Hết phiên rồi không thực hiện được thao tác

> **Tự động** (`Acceptance=T3`). Không cần chạy tay.

| # | Bước | Kết quả mong đợi | Giờ thực tế |
|---|---|---|---|
| 1 | Đăng nhập `admin`, vào `/quan-tri/tai-khoan`. Ghi giờ **t0** | Thấy dòng `cudan_tg` trạng thái "Đang hoạt động" | |
| 2 | Không thao tác. Ở **t0 + 11:00**, bấm **Vô hiệu hóa** ở dòng `cudan_tg`, bấm OK ở hộp xác nhận | Bị chuyển về `/login` | |
| 3 | Đăng nhập lại `admin`, vào `/quan-tri/tai-khoan` | `cudan_tg` **vẫn** "Đang hoạt động" (thao tác ở bước 2 không được thực hiện) | |
| 4 | Vào `/quan-tri/nhat-ky` | **Không** có dòng "Vô hiệu hóa" cho `cudan_tg` | |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### T4. Điền form lâu hơn 10 phút (H2, H3)

> **Tự động** (`Acceptance=T4`) phần server: hết phiên thì đổi mật khẩu bị từ chối và mật khẩu cũ vẫn dùng được.
> **Chạy tay bước 5:** làm bước 1 – 4 trên trình duyệt thật, rồi ghi lại sau khi đăng nhập lại app đưa về trang nào.
> Việc chuyển trang này do JavaScript của trang Hồ sơ quyết định, nên test tự động không kiểm được (lỗi H3).

| # | Bước | Kết quả mong đợi | Giờ thực tế |
|---|---|---|---|
| 1 | Đăng nhập `cudan_tg`, vào `/ho-so`. Ghi giờ **t0** | Thấy form Đổi mật khẩu | |
| 2 | Gõ Mật khẩu hiện tại `TestPass@01`. Trong 11 phút tiếp theo, thỉnh thoảng gõ/xóa vài ký tự ở ô Mật khẩu mới, **không bấm nút nào** | — | |
| 3 | Ở **t0 + 11:00**, điền Mật khẩu mới và Nhập lại là `NewPass@02`, bấm **Đổi mật khẩu** | Bị chuyển về `/login` (gõ phím không giữ phiên, xem H2) | |
| 4 | Đăng nhập `cudan_tg` bằng `TestPass@01` | Vào được, tức là mật khẩu **chưa** bị đổi | |
| 5 | Ghi lại: sau khi đăng nhập ở bước 4, app đưa về trang nào | Ghi chú cho H3 (hiện dự đoán là trang chủ, không phải `/ho-so`) | |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### T5. Nhiều tab trong cùng một trình duyệt

> **Tự động** bằng test S4 (`SessionTimeoutTests`): các tab dùng chung một cookie, nên thao tác ở tab nào cũng gia hạn cùng một phiên. Không cần chạy tay.

| # | Bước | Kết quả mong đợi | Giờ thực tế |
|---|---|---|---|
| 1 | Đăng nhập `cudan_tg` ở **tab A**. Mở thêm **tab B** cùng trình duyệt, vào `/ho-so`. Ghi giờ **t0** | Cả hai tab đều đã đăng nhập | |
| 2 | Chỉ thao tác ở tab A: bấm F5 ở **t0 + 6:00**, **t0 + 12:00**, **t0 + 18:00**. Không đụng vào tab B | Tab A luôn còn đăng nhập | |
| 3 | Ở **t0 + 18:30**, bấm F5 ở **tab B** | **Vẫn còn đăng nhập** (các tab dùng chung một phiên) | |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### T6. Đóng trình duyệt thì mất phiên

> **Tự động** bằng test S7 (`SessionTimeoutTests`): cookie không có `Expires`/`Max-Age`, tức là cookie phiên, mất khi thoát trình duyệt. Không cần chạy tay.

**Trước khi test:** tắt tùy chọn khôi phục phiên trước của trình duyệt (Chrome: *Settings → On startup → Open the New Tab page*; Safari: *Settings → General → Safari opens with: A new window*), vì tùy chọn khôi phục có thể giữ lại cookie phiên.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Đăng nhập `cudan_tg` | Vào được |
| 2 | **Thoát hẳn** trình duyệt (macOS: Cmd + Q; Windows: đóng mọi cửa sổ) | — |
| 3 | Mở lại trình duyệt, gõ `https://localhost:7127/ho-so` | Bị chuyển tới `/login` |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### T7. Nhật ký khi hết phiên (H4)

> **Tự động** (`Acceptance=T7`). Không cần chạy tay. Test ghi nhận hành vi hiện tại: hết phiên không có dòng nhật ký nào.

**Tiền điều kiện:** đã chạy T1 (phiên `admin` đã hết hạn ở bước 3).

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Đăng nhập `admin`, vào `/quan-tri/nhat-ky` | — |
| 2 | Tìm dòng nào ghi lại việc phiên của `admin` hết hạn ở T1 bước 3 | Ghi lại thực tế. Theo code hiện tại: **không có** dòng nào (chỉ có "Đăng xuất" khi bấm nút) |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | Không đánh giá, chờ PO (H4) | |

---

## 5. Lỗi đã biết

### 5.1. Đang kiểm chứng: đánh **Fail** nếu xảy ra

| # | Mô tả | Test case |
|---|---|---|
| H1 | Phiên chỉ được gia hạn khi thao tác **sau phút thứ 5** của phiên hiện tại. Thao tác sớm hơn thì không được gia hạn, nên có thể bị đăng xuất chỉ **5 đến 10 phút** sau thao tác cuối, thay vì đúng 10 phút. Test tự động S5 đã xác nhận hành vi này trong code. Đang chờ PO chốt NFR-008 nghĩa là "tối đa 10 phút" hay "đúng 10 phút kể từ thao tác cuối" | T2 |

### 5.2. Đã ghi nhận, không đánh Fail

| # | Mô tả | Test case |
|---|---|---|
| H2 | Gõ phím, cuộn trang, điền form **không** giữ phiên. App không cảnh báo trước khi hết phiên, nên điền form lâu có thể mất dữ liệu | T4 |
| H3 | Khi hết phiên trong lúc bấm nút (API trả 401), app chuyển về `/login` mà không nhớ trang đang dở. Đăng nhập lại sẽ về trang chủ | T4 bước 5 |
| H4 | Hết phiên không được ghi vào nhật ký. Đang chờ PO quyết định có cần không | T7 |

## 6. Tổng hợp và nghiệm thu

| Test case | Pass/Fail | Ghi chú |
|---|---|---|
| T1. Ranh giới 10 phút, quay về đúng trang | | |
| T2. Thao tác sớm rồi để yên (H1) | | |
| T3. Hết phiên rồi không thao tác được | | |
| T4. Điền form lâu hơn 10 phút | | |
| T5. Nhiều tab | | |
| T6. Đóng trình duyệt | | |
| T7. Nhật ký khi hết phiên | Không đánh giá | |

| | |
|---|---|
| Người kiểm thử | |
| Ngày kiểm thử | |
| Kết luận | ☐ Đạt  ☐ Không đạt |
| Chữ ký | |
