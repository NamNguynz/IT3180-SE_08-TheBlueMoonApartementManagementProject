# Kiểm thử chấp nhận: Tạo và cập nhật hồ sơ căn hộ, hộ gia đình

> Tài liệu dành cho người kiểm thử. Chạy từng test case trên app thật, ghi kết quả vào các ô trống rồi ký nghiệm thu ở cuối.
>
> **Tự động hóa:** phần API có test chạy cùng `dotnet test` (cần biến `BLUEMOON_TEST_CONNECTION`), file
> `tests/.../Integration/Acceptance/ApartmentProfileAcceptanceTests.cs` (Y1 – Y5, Y7, Y8, Y10). Chạy riêng: `dotnet test --filter "Acceptance=Y7"`.
> Trùng số CCCD (Y6) đã có test E9 trong `PersonalDataEncryptionTests`. Luật ở service: unit test trong `ApartmentProfileServiceTests`.
> **Phần hiển thị trên giao diện (Y9) phải chạy tay.** Đổi trạng thái căn hộ (K8, K9) nằm ở `docs/testing/acceptance_apartment_search.md`.

## 1. Mục đích và phạm vi

Xác nhận **PBI-001 / US-001**:

- **AC-001:** BQT lưu hồ sơ căn hộ hoặc hộ gia đình hợp lệ thì hệ thống lưu, gắn đúng quan hệ căn hộ – hộ – chủ hộ – thành viên,
  và tra cứu lại được.
- **AC-002:** dữ liệu thiếu trường bắt buộc, sai định dạng hoặc trùng thì hệ thống không lưu, báo lỗi và chỉ rõ trường cần sửa
  hoặc thông tin bị trùng.

**Ngoài phạm vi:** quan hệ của từng thành viên với chủ hộ và chủ hộ là một nhân khẩu (PBI-004); tình trạng cư trú, chuyển đi
(PBI-007); mã hóa dữ liệu cá nhân (PBI-030); tra cứu theo loại hình, diện tích (PBI-002).

## 2. Môi trường

| Mục | Giá trị |
|---|---|
| Nhánh / commit được kiểm thử | |
| Trình duyệt | |
| URL app | `https://localhost:7127` |

**Chuẩn bị:** chạy app trên database **mới** (cấu hình theo README), đăng nhập `admin`, Tư cách **Ban quản trị**,
vào **Hồ sơ căn hộ**. Mở bằng cửa sổ ẩn danh để trình duyệt nạp đúng bản script mới (`apartment-profiles.js?v=12`).
Để kiểm Y10 cần thêm một tài khoản **Kế toán** và một tài khoản **Cư dân** đã được duyệt.

## 3. Dữ liệu test (dữ liệu giả)

| Đối tượng | Giá trị |
|---|---|
| Tòa nhà | `Tòa Y` (tạo bằng **Thêm tòa nhà**) |
| Căn hộ 1 | Mã `AT-Y-01`, Tòa Y, tầng 12, diện tích 72,5, 2 phòng ngủ, trạng thái Đang cho thuê, loại hình Căn hộ ở |
| Căn hộ 2 | Mã `AT-Y-02`, các ô khác như căn 1 |
| Hộ của căn 1 | Số hộ khẩu `HK-Y-01`, chủ hộ `Trần Văn Chủ Hộ`, số nhà `P1201`, ngày lập hộ khẩu 02/01/2024 |
| Nhân khẩu | `Trần Văn Chủ Hộ` (Nam, sinh 14/03/1985), `Trần Thị Thành Viên` (Nữ, sinh 20/07/1988). Không cần nhập CCCD |

---

## 4. Test case

### Y1. Lưu căn hộ hợp lệ và tra cứu lại (AC-001)

> **Tự động** (`Acceptance=Y1`). Không cần chạy tay.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Tạo căn hộ với mã ` at-y-01 ` (chữ thường, có khoảng trắng ở hai đầu), các ô khác theo mục 3 | Lưu thành công, mã được lưu là `AT-Y-01` |
| 2 | Mở lại căn hộ vừa tạo | Đúng tòa, tầng, diện tích, số phòng ngủ, loại hình, trạng thái, ghi chú; chưa có hồ sơ hộ |
| 3 | Xem danh sách căn hộ | Có `AT-Y-01` |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### Y2. Lưu hồ sơ hộ, gắn đúng căn hộ, chủ hộ và thành viên (AC-001)

> **Tự động** (`Acceptance=Y2`). Không cần chạy tay.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Tạo hồ sơ hộ cho `AT-Y-01` theo mục 3, kèm 2 nhân khẩu | Lưu thành công |
| 2 | Mở lại hồ sơ hộ | Hộ thuộc căn `AT-Y-01`; đúng số hộ khẩu, chủ hộ, số nhà, ngày lập; có đủ 2 nhân khẩu |
| 3 | Mở lại căn `AT-Y-01` | Căn hộ hiện đúng hồ sơ hộ vừa tạo với 2 nhân khẩu |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### Y3. Cập nhật căn hộ và hồ sơ hộ (AC-001)

> **Tự động** (`Acceptance=Y3`). Không cần chạy tay.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Sửa căn hộ: tầng 15, diện tích 80,25, 3 phòng ngủ, ghi chú "Đã sửa" | Lưu thành công, mở lại thấy giá trị mới |
| 2 | Sửa hộ: đổi tên chủ hộ, sửa nghề nghiệp của một nhân khẩu, thêm một nhân khẩu mới | Lưu thành công, mở lại thấy tên chủ hộ mới, nghề nghiệp mới, nhân khẩu mới |
| 3 | Kiểm tra nhân khẩu không có trong lần sửa | Vẫn còn trong hộ: muốn chuyển đi phải dùng **Biến động nhân khẩu** (PBI-007) |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### Y4. Trùng mã căn hộ bị từ chối (AC-002)

> **Tự động** (`Acceptance=Y4`). Không cần chạy tay.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Tạo căn hộ mới với mã ` at-y-01 ` (trùng mã căn 1, khác chữ hoa và có khoảng trắng) | Bị từ chối (409): **"Mã căn hộ đã tồn tại."**, không có căn mới |
| 2 | Sửa căn `AT-Y-02`, đổi mã thành `AT-Y-01` | Bị từ chối với cùng thông báo, căn 2 vẫn giữ mã `AT-Y-02` |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### Y5. Trùng số hộ khẩu, hoặc căn hộ đã có hộ, bị từ chối (AC-002)

> **Tự động** (`Acceptance=Y5`). Không cần chạy tay.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Tạo hồ sơ hộ cho `AT-Y-02` với số hộ khẩu `HK-Y-01` (đã dùng ở căn 1) | Bị từ chối (409): **"Số hộ khẩu đã tồn tại."**, căn 2 vẫn chưa có hộ |
| 2 | Tạo thêm một hồ sơ hộ thứ hai cho `AT-Y-01` (gọi thẳng API, giao diện không cho tạo) | Bị từ chối (409): **"Căn hộ đã có hồ sơ hộ gia đình."** |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### Y6. Trùng số CMND/CCCD bị từ chối (AC-002)

> **Tự động** bằng test E9 trong `PersonalDataEncryptionTests` (`TestId=E9`). Không cần chạy tay.
> Hệ thống từ chối với **"Số CMND/CCCD đã được sử dụng."** khi số đã có ở hộ khác, và **"Số CMND/CCCD bị trùng trong danh sách."**
> khi hai nhân khẩu trong cùng lần lưu có cùng số.

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### Y7. Thiếu trường bắt buộc bị từ chối, chỉ rõ trường (AC-002)

> **Tự động** (`Acceptance=Y7`): gửi thẳng API với từng trường bỏ trống. Phần giao diện kiểm ở Y9.

| # | Trường bỏ trống | Kết quả mong đợi (API) |
|---|---|---|
| 1 | Mã căn hộ | 400, lỗi nêu trường `Code`; không lưu căn hộ |
| 2 | Số hộ khẩu | 400, lỗi nêu trường `HouseholdNumber`; không lưu hộ |
| 3 | Họ tên chủ hộ | 400, lỗi nêu trường `HeadName`; không lưu hộ |
| 4 | Họ tên nhân khẩu | 400, lỗi nêu trường `FullName`; không lưu hộ |
| 5 | Ngày lập hộ khẩu | 400, **"Ngày lập hộ khẩu là bắt buộc."**; không lưu hộ |
| 6 | Ngày sinh nhân khẩu | 400, **"Ngày sinh nhân khẩu là bắt buộc."**; không lưu hộ |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | Dòng 1 – 4 trả lỗi tiếng Anh, xem mục 5 |

### Y8. Giá trị sai bị từ chối, chỉ rõ trường (AC-002)

> **Tự động** (`Acceptance=Y8`): gửi thẳng API. Trên giao diện, ô số đã có giới hạn nên trình duyệt chặn trước.

| # | Giá trị sai | Kết quả mong đợi (API) |
|---|---|---|
| 1 | Diện tích 0 | 400, lỗi nêu trường `Area`; không lưu |
| 2 | Diện tích âm (-5) | 400, lỗi nêu trường `Area`; không lưu |
| 3 | Tầng 201 (giới hạn 0 – 200) | 400, lỗi nêu trường `Floor`; không lưu |
| 4 | Số phòng ngủ 51 (giới hạn 0 – 50) | 400, lỗi nêu trường `BedroomCount`; không lưu |
| 5 | Tòa nhà không tồn tại | 400, **"Tòa nhà không tồn tại."**; không lưu |
| 6 | Ngày cấp giấy tờ là ngày nhỏ nhất (0001-01-01) | 400, **"Ngày cấp giấy tờ không hợp lệ."**; không lưu hộ |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | Dòng 1 – 4 trả lỗi tiếng Anh, xem mục 5 |

### Y9. Báo lỗi trên giao diện (AC-001, AC-002)

> **Chạy tay.** Dùng dữ liệu ở mục 3.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Bấm **+ Thêm căn hộ**, nhập căn `AT-Y-01` theo mục 3, bấm **Tạo căn hộ** | Hiện thông báo xanh; căn mới có trong danh sách bên trái |
| 2 | Ở mục **Hồ sơ hộ gia đình**, nhập hộ `HK-Y-01`, bấm **+ Thêm nhân khẩu** và **Lưu nhân khẩu** cho 2 người, rồi bấm **Tạo hồ sơ hộ khẩu** | Hiện thông báo xanh; tải lại trang (F5) và chọn lại `AT-Y-01` vẫn thấy hộ và 2 nhân khẩu |
| 3 | **+ Thêm căn hộ**, để trống **Mã căn hộ**, bấm **Tạo căn hộ** | Trình duyệt chặn, báo ô **Mã căn hộ** cần nhập; không gửi đi |
| 4 | **+ Thêm căn hộ**, nhập mã `AT-Y-01` (trùng), bấm **Tạo căn hộ** | Hiện thông báo đỏ **"Mã căn hộ đã tồn tại."**; danh sách không thêm căn |
| 5 | **+ Thêm căn hộ**, ô **Mã căn hộ** chỉ gõ dấu cách, bấm **Tạo căn hộ** | Hiện thông báo đỏ chỉ rõ ô Mã căn hộ. **Dự đoán:** thông báo bằng tiếng Anh `Code: The Code field is required.` (mục 5) |
| 6 | Ở `AT-Y-02`, tạo hộ với số hộ khẩu `HK-Y-01` | Hiện thông báo đỏ **"Số hộ khẩu đã tồn tại."**; căn 2 vẫn chưa có hộ |
| 7 | Mở **Nhật ký** | Có dòng **Tạo hồ sơ căn hộ**, **Tạo hồ sơ hộ khẩu** cho các thao tác thành công; không có dòng cho thao tác bị từ chối |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| Chạy tay ngày 25/11/2026. Bước 1, 2: thông báo xanh, F5 vẫn thấy hộ và 2 nhân khẩu. Bước 3: trình duyệt chặn. Bước 4, 6: thông báo đỏ "Mã căn hộ đã tồn tại.", "Số hộ khẩu đã tồn tại."; F5 không có căn mới, AT-Y-02 vẫn chưa có hộ. Bước 5: thông báo đỏ `Code: The Code field is required.` Bước 7: Nhật ký không có dòng cho bước 4, 5, 6 | Pass | Bước 5 báo tiếng Anh nhưng có nêu tên trường, xem mục 5 dòng 1 (issue #54) |

### Y10. Kế toán và Cư dân không được tạo, sửa hồ sơ (DoD, AC-016)

> **Tự động** (`Acceptance=Y10`). Không cần chạy tay.

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Đăng nhập Kế toán, gọi API tạo căn hộ, sửa căn hộ, sửa hồ sơ hộ | Cả 3 bị từ chối 403 |
| 2 | Lặp lại với tài khoản Cư dân | Cả 3 bị từ chối 403 |
| 3 | Đăng nhập lại `admin`, mở căn hộ và hộ | Không có gì thay đổi |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

---

## 5. Giới hạn và câu hỏi

| # | Nội dung |
|---|---|
| 1 | **Lỗi trả về bằng tiếng Anh (đã xác nhận khi chạy Y7, Y8):** thiếu Mã căn hộ, Số hộ khẩu, Họ tên chủ hộ, Họ tên nhân khẩu, hoặc Diện tích, Tầng, Số phòng ngủ ngoài giới hạn bị ASP.NET chặn trước khi vào code của nhóm. API trả lỗi mặc định, ví dụ `"errors": {"Area": ["The field Area must be between 0.01 and 100000."]}`. Lỗi có nêu tên trường nên vẫn đạt phần "chỉ rõ trường" của AC-002, nhưng bằng tiếng Anh và không theo dạng `{code, message}` như các lỗi khác (NFR-011, NFR-013). Trang Hồ sơ hiện nguyên câu này, ví dụ `Area: The field Area must be ...`. Trên giao diện, các ô đã có `required`, `min`, `max` nên trình duyệt chặn phần lớn trường hợp; chỉ gặp khi gõ toàn dấu cách (Y9 bước 5) hoặc gọi thẳng API |
| 2 | **AC-001 chưa đạt phần "quan hệ chủ hộ – thành viên":** chủ hộ hiện chỉ là tên (`HeadName`), nhân khẩu chưa có trường quan hệ với chủ hộ. Phần này thuộc PBI-004 (4.08), sẽ kiểm khi PBI-004 xong |
| 3 | **Câu hỏi cho PO (đọc code, chưa chạy):** ngày sinh trong tương lai, hoặc ngày cấp giấy tờ trước ngày sinh, hiện vẫn được lưu; hệ thống chỉ chặn ngày bỏ trống. Có cần thêm quy tắc kiểm tra không? |
| 4 | Sửa hồ sơ hộ mà không gửi một nhân khẩu cũ thì nhân khẩu đó vẫn giữ nguyên (Y3 bước 3). Đây là thiết kế hiện tại: chuyển đi phải qua Biến động nhân khẩu (PBI-007) |
| 5 | PBI-001 do DEV-1 và DEV-2 làm, nên Tester (Ly) được nghiệm thu độc lập |

## 6. Tổng hợp và nghiệm thu

| Test case | Pass/Fail | Ghi chú |
|---|---|---|
| Y1. Lưu căn hộ hợp lệ, tra cứu lại | | |
| Y2. Lưu hộ, gắn đúng quan hệ | | |
| Y3. Cập nhật căn hộ và hộ | | |
| Y4. Trùng mã căn hộ | | |
| Y5. Trùng số hộ khẩu, căn đã có hộ | | |
| Y6. Trùng CCCD (E9) | | |
| Y7. Thiếu trường bắt buộc | | |
| Y8. Giá trị sai | | |
| Y9. Báo lỗi trên giao diện | Pass | Chạy tay 25/11/2026. Bước 5 báo tiếng Anh (issue #54) |
| Y10. Ngoài quyền bị từ chối | | |

| | |
|---|---|
| Người kiểm thử | |
| Ngày kiểm thử | |
| Kết luận | ☐ Đạt  ☐ Không đạt |
| Chữ ký | |
