# Báo cáo lỗi (Bug report)

> Danh sách mọi lỗi Tester đã phát hiện trong Sprint 1, kèm mức độ, trạng thái, issue và plan sửa.
> Cập nhật: **25/11/2026**, code app `1231392` (`main` sau PR #53). Issue: tab **Issues** của repo trên GitHub.
>
> Cách đếm theo Product Checklist (dòng "Bug report"): số lỗi có issue / số lỗi đã phát hiện.
> Không tính các điểm đang chờ PO quyết định (mục 5).

## 1. Quy ước mức độ

| Mức độ | Khi nào |
|---|---|
| Nghiêm trọng | Chặn luồng chính, mất hoặc sai dữ liệu, lộ dữ liệu cá nhân |
| Trung bình | Không đạt một AC hoặc NFR, hoặc yếu về bảo mật, nhưng vẫn có cách làm khác để dùng được |
| Thấp | Hiển thị, câu thông báo, trường hợp hiếm hoặc chỉ gặp khi gọi thẳng API |

## 2. Tổng hợp

| | Số lỗi |
|---|---|
| Đã phát hiện | 20 |
| Đã sửa | 7 (F1, F3, #31, UI-1, NK-1, G2, J3) |
| Không sửa, đã có test chặn | 1 (F2) |
| Đang mở | 12 |
| Có issue | **19/20**: #25, #26, #27, #31, #33, #35 (NK-1 và J3), #54, #67 – #77 |
| Sửa không qua issue | 1 (UI-1) |

Issue #59 – #66 được tạo trùng với #70 – #77 (H1 – J7), đóng với lý do trùng. Theo dõi lỗi ở #70 – #77.

## 3. Danh sách lỗi

| Mã | Mô tả ngắn | PBI | Phát hiện ở | Mức độ | Trạng thái | Issue | Người sửa đề xuất |
|---|---|---|---|---|---|---|---|
| F1 | Không có trang `/403`, bị từ chối quyền thì hiện "Not Found" | PBI-026 | B4 | Trung bình | Đã sửa (PR #28) | #25 | Ly |
| F2 | Chưa cấu hình `FallbackPolicy`: trang quên `[Authorize]` sẽ mở công khai | PBI-026 | Đọc code | Trung bình | Không sửa (nhóm quyết định), test R18 chặn trên CI | #26 | — |
| F3 | Menu trỏ tới trang chưa xây dựng, bấm vào ra "Not Found" | PBI-026 | B9 | Thấp | Đã sửa (PR #28, #48, #50) | #27 | Ly, Nam |
| #31 | Không lưu được nhân khẩu ở trang Hồ sơ căn hộ | PBI-001 | Chạy tay hồ sơ căn hộ | Nghiêm trọng | Đã sửa (PR #32) | #31 | Ly |
| UI-1 | Lưu căn hộ hoặc hộ xong thì thông báo xanh bị ẩn ngay, nút trở về "Tạo …" | PBI-001 | Chạy tay hồ sơ căn hộ | Thấp | Đã sửa (PR #36) | — | Ly |
| NK-1 | Trang Nhật ký: 4 thao tác hồ sơ căn hộ, hộ khẩu hiện tên tiếng Anh, ô lọc không có lựa chọn cho các thao tác này | PBI-029 | D11 | Thấp | Đã sửa (PR #36), test D11, N7 chặn tái phát | #35 | Ly |
| G2 | Đổi mật khẩu hoặc được cấp lại mật khẩu mà các phiên khác vẫn đăng nhập | PBI-027 | C8, C11 | Trung bình | Đã sửa (PR #34) | #33 | Ly |
| J3 | Mô tả trang Nhật ký chỉ nhắc 4 loại thao tác | PBI-029 | D10 | Thấp | Đã sửa (PR #36) | #35 | Ly |
| #54 | Lỗi kiểm tra dữ liệu ở tầng đầu vào trả tiếng Anh, không theo dạng `{code, message}` (mọi form) | PBI-001 (chung) | Y7, Y8, Y9 bước 5 | Trung bình | Đang mở | #54 | Minh |
| #1 | `ADMIN_INIT_PWD` không bị kiểm tra độ dài tối thiểu 8 ký tự | PBI-030 | Đọc code | Trung bình | Đang mở | #67 | Ly (DEV-3) |
| #3 | Lần sai mật khẩu thứ 5, nhật ký ghi "Sai mật khẩu, lần 0" | PBI-027 | D1 bước 10, A2, A5 | Thấp | Đang mở | #68 | Ly (DEV-3) |
| G3 | Đổi mật khẩu, nhập sai mật khẩu hiện tại bao nhiêu lần cũng không bị khóa | PBI-027 | P5 | Trung bình | Đang mở | #69 | Ly (DEV-3) |
| H1 | Phiên chỉ gia hạn khi thao tác sau phút thứ 5, có thể hết phiên 5 – 10 phút sau thao tác cuối | PBI-028 | T2 (Fail), S5 | Trung bình | Đang mở, chờ PO chốt NFR-008 | #70 | Tiến |
| H2 | Gõ phím, điền form không giữ phiên, không cảnh báo trước khi hết phiên | PBI-028 | T4 | Thấp | Đang mở, cần PO đồng ý cách sửa | #71 | Tiến |
| H3 | Hết phiên khi bấm nút thì về `/login` không nhớ trang đang dở | PBI-028 | T4 bước 5 | Thấp | Đang mở | #72 | Tiến |
| J1 | API nhật ký trả 500 khi `from`/`to` có múi giờ khác UTC | PBI-029 | Q7 (đang Skip) | Thấp | Đang mở | #73 | Minh |
| J2 | Lọc nhật ký theo tài khoản phân biệt hoa thường, API không cắt khoảng trắng | PBI-029 | D3 | Thấp | Đang mở | #74 | Minh |
| J4 | "Từ ngày" sau "Đến ngày" thì danh sách rỗng, không báo lỗi | PBI-029 | D4 bước 5 | Thấp | Đang mở | #75 | Minh |
| J6 | `POST /api/nhat-ky` trả 400 thay vì 405 | PBI-029 | D7 bước 4, Q11 | Thấp | Đang mở | #76 | Minh |
| J7 | Thời gian hiện mili giây sau năm: `14:05:09 25/11/2026.123` | PBI-029 | D9, test JS "J7" | Thấp | Đang mở | #77 | Minh |

Người sửa đề xuất lấy theo người được giao PBI trong `Work Distribution.xlsx`. #1, #3, G3 thuộc PBI của DEV-3 (Ly):
sau khi Ly sửa, **thành viên khác** nghiệm thu lại.

## 4. Plan sửa các lỗi đang mở

| Mã | Plan sửa | Test cần chạy lại hoặc sửa theo |
|---|---|---|
| #54 | Cấu hình `InvalidModelStateResponseFactory` trong `Program.cs` một lần cho mọi controller: trả `ApiError {code: "INVALID", message: "<tên trường tiếng Việt>: <lỗi>"}`; đặt `ErrorMessage` tiếng Việt cho các attribute | Y7, Y8, Y9 bước 5; test của các form khác |
| #1 | Tạo `PasswordRules` (8 – 100 ký tự) dùng chung cho đăng ký, đổi mật khẩu và `ADMIN_INIT_PWD`; `Program.cs` dừng app và báo lỗi cấu hình nếu sai độ dài | Unit test mới cho `PasswordRules`; test đăng ký, đổi mật khẩu |
| #3 | `AuthService.cs` dòng 66 – 71: ghi nhật ký với số lần sai **trước** khi đặt lại về 0 | D1 (đang mong đợi "lần 0"), A2, A5 |
| G3 | Dùng chung quy tắc khóa của đăng nhập (5 lần, 15 phút) cho `ChangeOwnPasswordAsync`; đang bị khóa thì từ chối; ghi nhật ký từng lần sai; đề xuất đổi `SecurityStamp` khi khóa để đăng xuất mọi phiên | Thay test P5 (`ChangePasswordTests.cs:130`) bằng test khóa sau 5 lần |
| H1 | Trong `OnValidatePrincipal` (`Program.cs`), user hợp lệ thì đặt `ctx.ShouldRenew = true` để mọi thao tác đều gia hạn phiên | S5, T2 |
| H2 | Script chung đếm thời gian từ request cuối, phút thứ 9 hiện cảnh báo có nút "Tiếp tục làm việc" (cần sửa H1 trước; cần PO đồng ý, liên quan RSK-011) | Test JS mới cho script này; T4 |
| H3 | Một hàm dùng chung chuyển tới `/login?ReturnUrl=<trang hiện tại>` khi API trả 401, thay ở 13 file trong `wwwroot/js` | 2 test JS đang ghi nhận `/login` (profile, audit log) |
| J1 | `AuditRepository.cs` dòng 27 – 28: đổi `from`/`to` sang UTC trước khi so sánh | Bỏ Skip Q7 |
| J2 | `AuditRepository.cs` dòng 30 – 31: `Trim()` và so sánh không phân biệt hoa thường (`EF.Functions.ILike`, thoát `%` và `_`) | Q2, D3 bước 4 |
| J4 | API trả 400 `{"code":"INVALID_DATE_RANGE", ...}` khi `from` > `to`; trang đã hiện `message` của lỗi | Test xUnit mới, D4 bước 5 |
| J6 | Không áp dụng `UseStatusCodePagesWithReExecute` (`Program.cs` dòng 168) cho đường dẫn `/api` | Q11 (mong đợi 405) |
| J7 | `formatTime` trong `audit-log.js`: tạo riêng chuỗi ngày và chuỗi giờ, ghép thành `25/11/2026 14:05:09.123`; tăng version script | Test JS "J7", "D9" |

## 5. Không tính là lỗi

| Mã | Nội dung | Lý do |
|---|---|---|
| F4 | Cư dân tự đăng ký được duyệt ngay; ai cũng xin được vai trò BQT (phải chờ duyệt) | PO đã chốt PA1: giữ nguyên |
| G4 | Tên đăng nhập phân biệt chữ hoa, chữ thường | Chờ PO |
| H4 | Hết phiên không ghi vào nhật ký | Chờ PO |
| J5 | Trigger chặn sửa, xóa nhật ký không chặn `TRUNCATE`; tài khoản DB của app có quyền tạo bảng | Chờ PO |
| #2 | Hash tạo lại khi đăng nhập sai tư cách không được lưu | Đính chính 09/11/2026: không phải lỗi (test Q10) |
