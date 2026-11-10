# Kiểm thử chấp nhận: Tra cứu căn hộ theo mã, loại hình, diện tích

> Tài liệu dành cho người kiểm thử. Chạy từng test case trên app thật, ghi kết quả vào các ô trống rồi ký nghiệm thu ở cuối.
>
> **Tự động hóa:** phần API có test chạy cùng `dotnet test` (cần biến `BLUEMOON_TEST_CONNECTION`), file
> `tests/.../Integration/Acceptance/ApartmentSearchAcceptanceTests.cs` (K2, K5, K7). Chạy riêng: `dotnet test --filter "Acceptance=K5"`.
> Nhãn loại hình phủ đủ mọi giá trị: unit test K6. Lưu và sửa loại hình ở service: unit test trong `ApartmentProfileServiceTests`.
> **Việc lọc danh sách chạy bằng JavaScript trên trình duyệt**, nên K1 – K4 phải **chạy tay**.

## 1. Mục đích và phạm vi

Xác nhận **PBI-002 / US-002**: "When BQT tra cứu theo mã căn, loại hình hoặc diện tích, Then hệ thống hiển thị đúng căn hộ
khớp với tiêu chí". Loại hình theo **LOG-008**: căn hộ ở (căn hộ cơ bản), kiot, penthouse.

**Ngoài phạm vi:** tìm kiếm phía server (trang tải toàn bộ danh sách rồi lọc trên trình duyệt, đủ nhanh với khoảng 200 căn);
cư dân xem loại hình căn hộ của mình (PBI-003); đơn giá theo loại hình (PBI-010).

## 2. Môi trường

| Mục | Giá trị |
|---|---|
| Nhánh / commit được kiểm thử | |
| Trình duyệt | |
| URL app | `https://localhost:7127` |

**Chuẩn bị:** chạy app trên database **mới** (cấu hình theo README), đăng nhập `admin`, Tư cách **Ban quản trị**,
vào **Hồ sơ căn hộ**. Mở bằng cửa sổ ẩn danh để trình duyệt nạp đúng bản script mới (`apartment-profiles.js?v=6`).

## 3. Dữ liệu test (dữ liệu giả)

Tạo 3 căn bằng **+ Thêm căn hộ**:

| Mã căn hộ | Tòa nhà | Tầng | Diện tích | Loại hình |
|---|---|---|---|---|
| `AT-K-01` | Tòa A | 5 | 70 | Căn hộ ở |
| `AT-K-02` | Tòa A | 1 | 30 | Kiot |
| `AT-K-03` | Tòa B | 30 | 250 | Penthouse |

---

## 4. Test case

### K1. Tra theo mã căn

> **Chạy tay.**

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Gõ `K-02` vào ô **Tìm một phần mã căn, tòa nhà...** | Chỉ còn `AT-K-02`. Số đếm hiện "1 / 3 căn hộ" |
| 2 | Xóa ô tìm | Hiện lại đủ 3 căn |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### K2. Tra theo loại hình

> **Tự động** phần lưu, trả về, sửa loại hình và các lựa chọn trên trang (`Acceptance=K2`). **Chạy tay** việc lọc.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Xem danh sách căn hộ | Mỗi căn có dòng mô tả kiểu "Tòa A · Tầng 1 · Kiot" |
| 2 | Ở ô **Loại hình**, chọn **Kiot** | Chỉ còn `AT-K-02`, số đếm "1 / 3 căn hộ" |
| 3 | Chọn **Penthouse**, rồi **Căn hộ ở** | Mỗi lần chỉ còn đúng căn tương ứng |
| 4 | Chọn `AT-K-01`, đổi **Loại hình** trong form thành **Penthouse**, bấm **Lưu thay đổi**. Lọc lại **Penthouse** | Ra 2 căn `AT-K-01` và `AT-K-03` |
| 5 | Chọn **Tất cả loại hình** | Hiện lại đủ 3 căn |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### K3. Tra theo diện tích chính xác

> **Chạy tay.**

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Gõ `250` vào ô **Diện tích chính xác (m²)** | Chỉ còn `AT-K-03` |
| 2 | Gõ `68.5` | "Chưa có căn hộ phù hợp." |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### K4. Kết hợp các tiêu chí

> **Chạy tay.**

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Ô tìm `Tòa A`, Loại hình **Kiot** | Chỉ còn `AT-K-02` |
| 2 | Giữ nguyên, thêm Diện tích `70` | "Chưa có căn hộ phù hợp." (không căn nào thỏa cả ba) |
| 3 | Xóa ô tìm, ô diện tích, chọn **Tất cả loại hình** | Hiện lại đủ 3 căn, số đếm "3 căn hộ" |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### K5. Thiếu hoặc sai loại hình bị từ chối

> **Tự động** (`Acceptance=K5`). Không cần chạy tay.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Bấm **+ Thêm căn hộ**, nhập đủ các ô nhưng để **Loại hình** là "Chọn loại hình", bấm **Tạo căn hộ** | Trình duyệt báo ô bắt buộc, không tạo căn |
| 2 | Gọi thẳng API tạo căn hộ thiếu `type`, hoặc `type` là `"Villa"` hay số `5` | Status **400**, không có căn nào được tạo |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

Căn hộ tạo trước khi có loại hình được gán mặc định **Căn hộ ở** (test K7).

---

## 5. Giới hạn và câu hỏi

| # | Nội dung |
|---|---|
| 1 | Lọc chạy trên trình duyệt sau khi tải toàn bộ danh sách. Nếu sau này cần tìm phía server (vd cho PBI-043), làm thành task riêng |
| 2 | **Câu hỏi cho PO:** đề bài có "4 tầng đế" nhưng không nói công năng. Hiện chỉ có 3 loại hình theo LOG-008; có cần thêm loại cho tầng đế không? |
| 3 | Căn hộ có sẵn trước khi thêm loại hình đều nhận **Căn hộ ở**; BQT cần sửa lại các kiot, penthouse |

## 6. Tổng hợp và nghiệm thu

| Test case | Pass/Fail | Ghi chú |
|---|---|---|
| K1. Tra theo mã | | |
| K2. Tra theo loại hình | | |
| K3. Tra theo diện tích | | |
| K4. Kết hợp tiêu chí | | |
| K5. Thiếu, sai loại hình bị từ chối | | |

| | |
|---|---|
| Người kiểm thử | |
| Ngày kiểm thử | |
| Kết luận | ☐ Đạt  ☐ Không đạt |
| Chữ ký | |
