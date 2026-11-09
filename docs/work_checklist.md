# Checklist công việc

Nguồn: `Work Distribution.xlsx` (bản 08/11/2026), đối chiếu commit trên `main`. Cập nhật lần cuối: **09/11/2026** (`b5b88a4`).

- `[x]` xong, `[ ]` chưa xong. Task con có code đã merge vào `main` thì tick, kể cả khi WBS chưa cập nhật (ghi ở Notes).
- Dòng PBI (in đậm) chỉ tick khi mọi task con của nó đã tick.
- Cột Commit ghi: hash, người commit, nội dung.

## 1. Phân chia vai trò

| Thành viên | Vai trò | Tài khoản GitHub / tên trong commit | Commit trên `main` (09/11) |
|---|---|---|---|
| Hoàng Nhật Minh | PO, DEV-1 | `ZanXialdpgg`, commit ghi `Hoang Nhat Minh` | 11 |
| Nguyễn Bảo Nam | BA, DEV-2 | `NamNguynz` (chủ repo) | 13 |
| Nguyễn Đỗ Hương Ly | Tester, DEV-3 | commit ghi `lyy` / `Nguyen Do Huong Ly` | 19 |
| Đỗ Việt Tiến | DEV-4 | Chưa có commit | 0 |

Ly vừa là Tester vừa là DEV-3, nên test các PBI của DEV-3 cần một thành viên khác nghiệm thu độc lập.

## 2. Bảng theo dõi công việc

### Khởi động (24/09 – 05/10)

| Xong | WBS | Task | Người làm | Thời gian | Commit | Notes |
|---|---|---|---|---|---|---|
| [x] | **1** | **Nhận tài liệu dự án của khách hàng và khởi động** | Minh (PO) | 24/09 – 25/09 | | |
| [x] | 1.1 | Tập hợp đội nhóm | Minh (PO) | 24/09 | | |
| [x] | 1.2 | Gặp mặt khách hàng; đàm phán và ký kết hợp đồng | Minh (PO) | 24/09 | | |
| [x] | 1.3 | Nhận tài liệu chứa yêu cầu ban đầu của dự án | Minh (PO) | 24/09 | | |
| [x] | 1.4 | Khảo sát thêm nhu cầu (Client's Wish List) và thực trạng | Minh (PO) | 24/09 | | |
| [x] | 1.5 | Xây dựng và bàn giao Project Charter | Minh (PO) | 24/09 – 25/09 | | |
| [x] | 1.6 | Tạo Project Repository, phân quyền cho thành viên | Minh (PO) | 25/09 | `f7c1731` Nam: khởi tạo cấu trúc project (03/11) | Repo do `NamNguynz` tạo và là chủ |
| [x] | **2** | **Khảo sát, tổng hợp và phân tích kết quả khảo sát** | Nam (BA) | 27/09 – 01/10 | | |
| [x] | 2.1 | Khảo sát hiện trạng, thăm dò nhu cầu khách hàng | Nam (BA) | 27/09 – 29/09 | | |
| [x] | 2.1.1 | Khảo sát hiện trạng quy trình nghiệp vụ | Nam (BA) | 27/09 | | |
| [x] | 2.1.2 | Gửi form khảo sát cho BQT, kế toán, cư dân | Nam (BA) | 27/09 | | |
| [x] | 2.1.3 | Tổ chức các buổi phỏng vấn với các bên liên quan | Nam (BA) | 28/09 – 29/09 | | |
| [x] | 2.1.4 | Thu thập tài liệu, giấy tờ nghiệp vụ | Nam (BA) | 28/09 – 29/09 | | |
| [x] | 2.1.5 | Tổng hợp thành Raw Collection | Nam (BA) | 29/09 | | |
| [x] | 2.2 | Xây dựng Wish List, As-Is Scenario, Raw Log, Raw NFR | Nam (BA) | 30/09 | | |
| [x] | 2.3 | Đề xuất Epics, Features, Visionary Scenario, Acceptance Criteria | Nam (BA) | 30/09 – 01/10 | | |
| [x] | **3** | **Xây dựng và chốt Product Backlog, DoD** | Minh (PO) | 03/10 – 05/10 | | |

### Sprint 1 (07/10 – 09/11)

| Xong | WBS | Task | Người làm | Thời gian | Commit | Notes |
|---|---|---|---|---|---|---|
| [ ] | **4.01** | **PBI-030: Mã hóa dữ liệu cá nhân và băm mật khẩu** | Ly (DEV-3) | 07/10 – 08/10 | | |
| [ ] | 4.01.01 | BackEnd | Ly (DEV-3) | 07/10 – 08/10 | `61e157a` Nam: băm mật khẩu (trong commit đăng nhập) | WBS ghi xong nhưng mới có băm mật khẩu. **Chưa mã hóa dữ liệu cá nhân**: CCCD trong bảng nhân khẩu (PR #17) đang lưu dạng rõ. Chưa chốt trường cần mã hóa, người làm, nơi lưu khóa |
| [x] | 4.01.02 | Test | Ly (Tester) | 08/10 | `866490b` `36a610e` `f849dba` `26c4edc` Ly: project test, unit test băm mật khẩu, acceptance (PR #11) | Acceptance chưa có người chạy độc lập. Lỗi #1, #3 chưa tạo issue. Lỗi #2 trong tài liệu cần đính chính (test Q10 cho thấy không có thật) |
| [x] | **4.02** | **PBI-026: Tài khoản riêng và phân quyền theo vai trò** | Nam (DEV-2) | 07/10 – 09/10 | | |
| [x] | 4.02.01 | BackEnd | Nam (DEV-2) | 07/10 – 08/10 | `d09ba49` Nam: duyệt tài khoản; `48a5e1d` Nam: quản lý tài khoản | |
| [x] | 4.02.02 | FrontEnd | Nam (DEV-2) | 08/10 – 09/10 | `d09ba49` `48a5e1d` Nam: trang duyệt, trang tài khoản | |
| [x] | 4.02.03 | Test | Ly (Tester) | 09/10 | `9d9435e` `550ce49` `2f8cc37` `ab192e4` Ly: unit test, test phân quyền, acceptance (PR #12) | Acceptance chưa chạy độc lập. F1, F2 chưa tạo issue. F3 cần cập nhật (đã có vài trang mới). F4 chờ PO |
| [x] | **4.03** | **PBI-027: Đăng nhập và đổi mật khẩu** | Ly (DEV-3) | 09/10 – 11/10 | | |
| [x] | 4.03.01 | BackEnd | Ly (DEV-3) | 09/10 – 10/10 | `61e157a` Nam: đăng nhập, đăng ký; `48a5e1d` Nam: đổi mật khẩu | WBS giao DEV-3, code do `NamNguynz` commit |
| [x] | 4.03.02 | FrontEnd | Ly (DEV-3) | 10/10 | `61e157a` `48a5e1d` Nam: trang đăng nhập, hồ sơ | WBS giao DEV-3, code do `NamNguynz` commit |
| [x] | 4.03.03 | Test | Ly (Tester) | 11/10 | `c4e1089` `760fc2b` `8f866a2` Ly: unit test đăng nhập, đổi mật khẩu, acceptance (PR #13) | Acceptance chưa chạy độc lập. G1, G2, G3 chưa tạo issue. G4 chờ PO |
| [x] | **4.04** | **PBI-028: Tự động kết thúc phiên sau 10 phút** | Tiến (DEV-4) | 12/10 | | |
| [x] | 4.04.01 | BackEnd | Tiến (DEV-4) | 12/10 | `61e157a` Nam: cấu hình phiên 10 phút | WBS giao DEV-4, code do `NamNguynz` commit |
| [x] | 4.04.02 | Test | Ly (Tester) | 12/10 | `ac8a6fd` `bf740bf` `b712c7b` `083cb18` Ly: hạ tầng test tích hợp, CI PostgreSQL, test phiên, acceptance (PR #14) | Acceptance chưa chạy độc lập. H2, H3 chưa tạo issue. H1, H4 chờ PO. Kiểm tra CI có thật sự chạy test tích hợp |
| [x] | **4.05** | **PBI-029: Tra cứu nhật ký thao tác quan trọng** | Minh (DEV-1) | 13/10 – 14/10 | | |
| [x] | 4.05.01 | BackEnd | Minh (DEV-1) | 13/10 | `22c821e` Nam: nhật ký | WBS giao DEV-1, code do `NamNguynz` commit |
| [x] | 4.05.02 | FrontEnd | Minh (DEV-1) | 13/10 – 14/10 | `22c821e` Nam: trang nhật ký | WBS giao DEV-1, code do `NamNguynz` commit |
| [x] | 4.05.03 | Test | Ly (Tester) | 14/10 | `9688824` `b5c07be` `80c5f5b` Ly: unit test, test tích hợp, acceptance (PR #15) | Acceptance chưa chạy độc lập. J1–J4, J6 chưa tạo issue. J5 chờ PO |
| [ ] | **4.06** | **PBI-001: Tạo & cập nhật hồ sơ căn hộ, hộ gia đình** | Minh (DEV-1) | 15/10 – 17/10 | | WBS chưa tick |
| [x] | 4.06.01 | BackEnd | Minh (DEV-1) | 15/10 – 16/10 | `e33bfee` `398b2d2` `cdd66f4` `508cceb` Minh: bảng căn hộ/hộ/nhân khẩu, API, nghiệp vụ, 4 unit test (PR #17) | Chủ hộ đang là chuỗi tên (`HeadName`), chưa là một nhân khẩu |
| [x] | 4.06.02 | FrontEnd | Minh (DEV-1) | 16/10 – 17/10 | `9ae4e42` `c995417` Minh: trang hồ sơ căn hộ (PR #17) | Trang Nhật ký chưa có 4 action hồ sơ mới (lọc, nhãn tiếng Việt) |
| [ ] | 4.06.03 | Test | Ly (Tester) | 17/10 | | **Bắt đầu được**: code đã merge |
| [ ] | **4.07** | **PBI-002: Tra cứu căn hộ theo mã, loại hình, diện tích** | Nam (DEV-2) | 18/10 – 19/10 | | WBS không có dòng Test |
| [ ] | 4.07.01 | BackEnd | Nam (DEV-2) | 18/10 | `b3fb3e1` Minh: tìm theo mã và diện tích (PR #18) | **Chưa có tra cứu theo loại hình** (căn hộ chưa có trường này). WBS giao DEV-2, code do DEV-1 commit |
| [ ] | 4.07.02 | FrontEnd | Nam (DEV-2) | 19/10 | `b3fb3e1` Minh (PR #18) | Như trên |
| [ ] | **4.08** | **PBI-004: Quản lý nhân khẩu, chủ hộ và tình trạng cư trú** | Ly (DEV-3) | 20/10 – 23/10 | | Quá hạn |
| [ ] | 4.08.01 | BackEnd | Ly (DEV-3) | 20/10 – 21/10 | | Bảng nhân khẩu đã có trong PR #17. Còn thiếu: chủ hộ là một nhân khẩu, quan hệ với chủ hộ, tình trạng cư trú. Cần chốt ranh giới với DEV-1 |
| [ ] | 4.08.02 | FrontEnd | Ly (DEV-3) | 21/10 – 22/10 | | |
| [ ] | 4.08.03 | Test | Ly (Tester) | 22/10 – 23/10 | | Cần thành viên khác nghiệm thu (Ly là DEV-3) |
| [ ] | **4.09** | **PBI-007: Ghi nhận biến động nhân khẩu** | Tiến (DEV-4) | 24/10 – 27/10 | | Quá hạn. Tiêu chí cần yêu cầu từ PBI-006, nhưng PBI-006 xếp lịch sau (13/11) |
| [ ] | 4.09.01 | BackEnd | Tiến (DEV-4) | 24/10 – 25/10 | | |
| [ ] | 4.09.02 | FrontEnd | Tiến (DEV-4) | 25/10 – 26/10 | | |
| [ ] | 4.09.03 | Test | Ly (Tester) | 26/10 – 27/10 | | |
| [x] | **4.10** | **PBI-009: Định nghĩa và phân loại khoản thu** | Nam (DEV-2) | 20/10 – 22/10 | | WBS chưa tick. WBS không có dòng Test |
| [x] | 4.10.01 | BackEnd | Nam (DEV-2) | 20/10 – 21/10 | `01e541c` Nam: danh mục khoản thu, API | |
| [x] | 4.10.02 | FrontEnd | Nam (DEV-2) | 21/10 – 22/10 | `01e541c` Nam: trang `/tai-chinh/khoan-thu` | |
| [ ] | **4.11** | **PBI-010: Thiết lập đơn giá, cơ sở tính, thời điểm hiệu lực** | Minh (DEV-1) | 23/10 – 25/10 | | |
| [x] | 4.11.01 | BackEnd | Minh (DEV-1) | 23/10 – 24/10 | `01e541c` Nam: đơn giá theo ngày hiệu lực, đổi đơn giá giữ lịch sử | WBS giao DEV-1, code do `NamNguynz` commit. WBS chưa tick |
| [ ] | 4.11.02 | Test | Ly (Tester) | 24/10 – 25/10 | | Bắt đầu được |
| [ ] | **4.12** | **PBI-011: Phân biệt khoản bắt buộc, đóng góp, bảo trì riêng** | Nam (DEV-2) | 26/10 – 27/10 | | |
| [ ] | 4.12.01 | BackEnd | Nam (DEV-2) | 26/10 | `01e541c` Nam: có loại Bắt buộc / Tự nguyện / Bảo trì riêng | Cần đối chiếu tiêu chí chấp nhận |
| [ ] | 4.12.02 | Test | Ly (Tester) | 27/10 | | |
| [ ] | **4.13** | **PBI-013: Tra cứu đơn giá và cơ sở tính lịch sử** | Ly (DEV-3) | 28/10 – 30/10 | | Quá hạn |
| [ ] | 4.13.01 | BackEnd | Ly (DEV-3) | 28/10 | `01e541c` Nam: API tra đơn giá theo ngày | Mới có một phần, chưa có cơ sở tính lịch sử |
| [ ] | 4.13.02 | FrontEnd | Ly (DEV-3) | 29/10 | | |
| [ ] | 4.13.03 | Test | Ly (Tester) | 30/10 | | Cần thành viên khác nghiệm thu (Ly là DEV-3) |
| [ ] | **4.14** | **PBI-014: Tạo kỳ thu và bảng kê phải thu theo hộ** | Tiến (DEV-4) | 28/10 – 30/10 | | Quá hạn |
| [ ] | 4.14.01 | BackEnd | Tiến (DEV-4) | 28/10 | | |
| [ ] | 4.14.02 | FrontEnd | Tiến (DEV-4) | 29/10 | | |
| [ ] | 4.14.03 | Test | Ly (Tester) | 30/10 | | |
| [ ] | **4.15** | **PBI-015: Tính phí dịch vụ theo đơn giá và cơ sở tính** | Minh (DEV-1) | 01/11 – 02/11 | | Quá hạn |
| [ ] | 4.15.01 | BackEnd | Minh (DEV-1) | 01/11 – 02/11 | | |
| [ ] | 4.15.02 | Test | Ly (Tester) | 02/11 | | |
| [ ] | **4.16** | **PBI-016: Ghi nhận thu tiền thực tế, hỗ trợ nộp một phần** | Nam (DEV-2) | 03/11 – 06/11 | | Quá hạn |
| [ ] | 4.16.01 | BackEnd | Nam (DEV-2) | 03/11 – 04/11 | | |
| [ ] | 4.16.02 | FrontEnd | Nam (DEV-2) | 04/11 – 05/11 | | |
| [ ] | 4.16.03 | Test | Ly (Tester) | 05/11 – 06/11 | | |
| [ ] | **4.17** | **PBI-019: Danh sách hộ chưa đóng và nhắc nộp** | Ly (DEV-3) | 07/11 – 09/11 | | WBS không có dòng Test |
| [ ] | 4.17.01 | BackEnd | Ly (DEV-3) | 07/11 – 08/11 | | |
| [ ] | 4.17.02 | FrontEnd | Ly (DEV-3) | 08/11 – 09/11 | | |
| [ ] | **4.18** | **PBI-022: Xem báo cáo tổng hợp thu và công nợ theo kỳ** | Tiến (DEV-4) | 07/11 – 09/11 | | |
| [ ] | 4.18.01 | BackEnd | Tiến (DEV-4) | 07/11 – 08/11 | | |
| [ ] | 4.18.02 | FrontEnd | Tiến (DEV-4) | 08/11 – 09/11 | | |
| [ ] | 4.18.03 | Test | Ly (Tester) | 09/11 | | |
| [ ] | **4.19** | **PBI-023: Tổng hợp thu–chi theo tuần, tháng, quý, năm** | Minh (DEV-1) | 07/11 – 09/11 | | WBS không có dòng Test |
| [ ] | 4.19.01 | BackEnd | Minh (DEV-1) | 07/11 – 08/11 | | |
| [ ] | 4.19.02 | FrontEnd | Minh (DEV-1) | 08/11 – 09/11 | | |

### Sprint 2 (09/11 – 24/11)

Số WBS khôi phục theo thứ tự dòng, vì file Excel tự đổi `5.01` thành ngày.

| Xong | WBS | Task | Người làm | Thời gian | Commit | Notes |
|---|---|---|---|---|---|---|
| [ ] | **5.01** | **PBI-031: Sao lưu dữ liệu định kỳ** | Ly (DEV-3) | 09/11 – 11/11 | | |
| [ ] | 5.01.01 | BackEnd | Ly (DEV-3) | 09/11 – 10/11 | | |
| [ ] | 5.01.02 | Test | Ly (Tester) | 10/11 – 11/11 | | Cần thành viên khác nghiệm thu (Ly là DEV-3) |
| [ ] | **5.02** | **PBI-003: Cư dân xem căn hộ mình đứng tên sở hữu** | Minh (DEV-1) | 09/11 – 12/11 | | |
| [ ] | 5.02.01 | BackEnd | Minh (DEV-1) | 09/11 – 10/11 | | |
| [ ] | 5.02.02 | FrontEnd | Minh (DEV-1) | 10/11 – 11/11 | | |
| [ ] | 5.02.03 | Test | Ly (Tester) | 11/11 – 12/11 | | |
| [ ] | **5.03** | **PBI-005: Cư dân xem thông tin thành viên trong hộ** | Nam (DEV-2) | 13/11 – 16/11 | | Chưa chốt cư dân được xem những trường nào |
| [ ] | 5.03.01 | BackEnd | Nam (DEV-2) | 13/11 – 14/11 | | |
| [ ] | 5.03.02 | FrontEnd | Nam (DEV-2) | 14/11 – 15/11 | | |
| [ ] | 5.03.03 | Test | Ly (Tester) | 15/11 – 16/11 | | |
| [ ] | **5.04** | **PBI-006: Cư dân gửi yêu cầu cập nhật thông tin thành viên** | Ly (DEV-3) | 13/11 – 15/11 | | WBS không có dòng Test |
| [ ] | 5.04.01 | BackEnd | Ly (DEV-3) | 13/11 – 14/11 | | |
| [ ] | 5.04.02 | FrontEnd | Ly (DEV-3) | 14/11 – 15/11 | | |
| [ ] | **5.05** | **PBI-008: Tra cứu và xuất danh sách nhân khẩu** | Tiến (DEV-4) | 13/11 – 16/11 | | |
| [ ] | 5.05.01 | BackEnd | Tiến (DEV-4) | 13/11 – 14/11 | | |
| [ ] | 5.05.02 | FrontEnd | Tiến (DEV-4) | 14/11 – 15/11 | | |
| [ ] | 5.05.03 | Test | Ly (Tester) | 15/11 – 16/11 | | |
| [ ] | **5.06** | **PBI-043: Truy vấn hồ sơ, nhân khẩu, khoản phí ≤ 1,5 giây** | Nam (DEV-2) | 16/11 – 17/11 | | |
| [ ] | 5.06.01 | BackEnd | Nam (DEV-2) | 16/11 – 17/11 | | |
| [ ] | 5.06.02 | Test | Ly (Tester) | 17/11 | | |
| [ ] | **5.07** | **PBI-025: Tra cứu và xuất báo cáo cư trú cho cơ quan chức năng** | Nam (DEV-2) | 18/11 – 19/11 | | WBS không có dòng Test |
| [ ] | 5.07.01 | BackEnd | Nam (DEV-2) | 18/11 – 19/11 | | |
| [ ] | 5.07.02 | FrontEnd | Nam (DEV-2) | 19/11 | | |
| [ ] | **5.08** | **PBI-045: Xuất dữ liệu cho cơ quan chức năng, có kiểm soát quyền** | Tiến (DEV-4) | 18/11 – 21/11 | | |
| [ ] | 5.08.01 | BackEnd | Tiến (DEV-4) | 18/11 – 19/11 | | |
| [ ] | 5.08.02 | FrontEnd | Tiến (DEV-4) | 19/11 – 20/11 | | |
| [ ] | 5.08.03 | Test | Ly (Tester) | 20/11 – 21/11 | | |
| [ ] | **5.09** | **PBI-012: Tạo và quản lý đợt đóng góp tự nguyện** | Minh (DEV-1) | 18/11 – 21/11 | | |
| [ ] | 5.09.01 | BackEnd | Minh (DEV-1) | 18/11 – 19/11 | | |
| [ ] | 5.09.02 | FrontEnd | Minh (DEV-1) | 19/11 – 20/11 | | |
| [ ] | 5.09.03 | Test | Ly (Tester) | 20/11 – 21/11 | | |
| [ ] | **5.10** | **PBI-017: Cư dân xem khoản cần đóng và cơ sở tính** | Nam (DEV-2) | 18/11 – 20/11 | | WBS không có dòng Test |
| [ ] | 5.10.01 | BackEnd | Nam (DEV-2) | 18/11 – 19/11 | | |
| [ ] | 5.10.02 | FrontEnd | Nam (DEV-2) | 19/11 – 20/11 | | |
| [ ] | **5.11** | **PBI-021: Xuất bảng kê thu / hóa đơn cho cư dân** | Tiến (DEV-4) | 18/11 – 20/11 | | WBS không có dòng Test |
| [ ] | 5.11.01 | BackEnd | Tiến (DEV-4) | 18/11 – 19/11 | | |
| [ ] | 5.11.02 | FrontEnd | Tiến (DEV-4) | 19/11 – 20/11 | | |
| [ ] | **5.12** | **PBI-018: Cư dân xem lịch sử thanh toán và xác nhận** | Ly (DEV-3) | 22/11 – 24/11 | | |
| [ ] | 5.12.01 | BackEnd | Ly (DEV-3) | 22/11 – 23/11 | | |
| [ ] | 5.12.02 | FrontEnd | Ly (DEV-3) | 23/11 – 24/11 | | |
| [ ] | 5.12.03 | Test | Ly (Tester) | 24/11 | | Cần thành viên khác nghiệm thu (Ly là DEV-3) |
| [ ] | **5.13** | **PBI-024: Xuất dữ liệu báo cáo tài chính và kê khai thuế** | Minh (DEV-1) | 23/11 – 24/11 | | |
| [ ] | 5.13.01 | BackEnd | Minh (DEV-1) | 23/11 – 24/11 | | |
| [ ] | 5.13.02 | Test | Ly (Tester) | 24/11 | | |
| [ ] | **5.14** | **PBI-032: Hiển thị mã lỗi và hướng dẫn khắc phục** | Tiến (DEV-4) | 23/11 – 24/11 | | |
| [ ] | 5.14.01 | FrontEnd | Tiến (DEV-4) | 23/11 – 24/11 | | |
| [ ] | 5.14.02 | Test | Ly (Tester) | 24/11 | | |
| [ ] | **5.15** | **PBI-033: Đáp ứng tối thiểu 50 phiên đồng thời** | Minh (DEV-1) | 23/11 – 24/11 | | |
| [ ] | 5.15.01 | BackEnd | Minh (DEV-1) | 23/11 – 24/11 | | |
| [ ] | 5.15.02 | Test | Ly (Tester) | 24/11 | | |
| [ ] | **5.16** | **PBI-044: Báo cáo tài chính và danh sách nợ ≤ 3 giây** | Ly (DEV-3) | 23/11 – 24/11 | | |
| [ ] | 5.16.01 | BackEnd | Ly (DEV-3) | 23/11 – 24/11 | | |
| [ ] | 5.16.02 | Test | Ly (Tester) | 24/11 | | Cần thành viên khác nghiệm thu (Ly là DEV-3) |

### Sprint 3 và bàn giao v1.0 (25/11 – 03/12)

| Xong | WBS | Task | Người làm | Thời gian | Commit | Notes |
|---|---|---|---|---|---|---|
| [ ] | **6.01** | **PBI-020: Thanh toán khoản phí bằng QR** | Minh (DEV-1) | 25/11 – 27/11 | | |
| [ ] | 6.01.01 | BackEnd | Minh (DEV-1) | 25/11 – 26/11 | | |
| [ ] | 6.01.02 | FrontEnd | Minh (DEV-1) | 26/11 – 27/11 | | |
| [ ] | 6.01.03 | Test | Ly (Tester) | 27/11 | | |
| [ ] | **7** | **Nghiệm thu Release v1.0** (BQT và kế toán chạy thử các luồng chính) | Minh (PO) | 28/11 – 29/11 | | |
| [ ] | **8** | **Ghi lại Report và viết tài liệu hướng dẫn** | Nam (BA) | 30/11 – 01/12 | | |
| [ ] | **9** | **Bàn giao Release v1.0** | Minh (PO) | 02/12 – 03/12 | | |

### Release v2.0 (05/12 – 06/12) và v3.0 (07/12 – 10/12)

| Xong | WBS | Task | Người làm | Thời gian | Commit | Notes |
|---|---|---|---|---|---|---|
| [ ] | **10.01** | **PBI-039: Quản lý phương tiện và phí gửi xe theo tháng** | Nam (DEV-2) | 05/12 – 06/12 | | |
| [ ] | 10.01.01 | BackEnd | Nam (DEV-2) | 05/12 – 06/12 | | |
| [ ] | 10.01.02 | FrontEnd | Nam (DEV-2) | 05/12 – 06/12 | | |
| [ ] | 10.01.03 | Test | Ly (Tester) | 05/12 – 06/12 | | |
| [ ] | **10.02** | **PBI-040: Ghi nhận thu hộ điện, nước, internet** | Ly (DEV-3) | 05/12 – 06/12 | | |
| [ ] | 10.02.01 | BackEnd | Ly (DEV-3) | 05/12 – 06/12 | | |
| [ ] | 10.02.02 | Test | Ly (Tester) | 05/12 – 06/12 | | Cần thành viên khác nghiệm thu (Ly là DEV-3) |
| [ ] | **11.01** | **PBI-034: Tạo và phát hành thông báo vận hành** | Minh (DEV-1) | 07/12 – 08/12 | | |
| [ ] | 11.01.01 | BackEnd | Minh (DEV-1) | 07/12 – 08/12 | | |
| [ ] | 11.01.02 | FrontEnd | Minh (DEV-1) | 07/12 – 08/12 | | |
| [ ] | 11.01.03 | Test | Ly (Tester) | 07/12 – 08/12 | | |
| [ ] | **11.02** | **PBI-035: Cư dân gửi phản ánh, khiếu nại qua cổng** | Nam (DEV-2) | 07/12 – 08/12 | | |
| [ ] | 11.02.01 | BackEnd | Nam (DEV-2) | 07/12 – 08/12 | | WBS ghi xong nhưng repo chưa có code, có thể ghi nhầm |
| [ ] | 11.02.02 | FrontEnd | Nam (DEV-2) | 07/12 – 08/12 | | |
| [ ] | 11.02.03 | Test | Ly (Tester) | 07/12 – 08/12 | | |
| [ ] | **11.03** | **PBI-036: Tra cứu và cập nhật tình trạng xử lý phản ánh** | Ly (DEV-3) | 07/12 – 08/12 | | WBS không có dòng Test |
| [ ] | 11.03.01 | BackEnd | Ly (DEV-3) | 07/12 – 08/12 | | |
| [ ] | 11.03.02 | FrontEnd | Ly (DEV-3) | 07/12 – 08/12 | | |
| [ ] | **11.04** | **PBI-037: Cư dân xem thông báo, khoản phải đóng, xác nhận thanh toán trên Portal** | Tiến (DEV-4) | 07/12 – 08/12 | | |
| [ ] | 11.04.01 | FrontEnd | Tiến (DEV-4) | 07/12 – 08/12 | | |
| [ ] | 11.04.02 | Test | Ly (Tester) | 07/12 – 08/12 | | |
| [ ] | **11.05** | **PBI-038: Tham gia lấy ý kiến, biểu quyết trực tuyến** | Minh (DEV-1) | 07/12 – 08/12 | | |
| [ ] | 11.05.01 | BackEnd | Minh (DEV-1) | 07/12 – 08/12 | | |
| [ ] | 11.05.02 | FrontEnd | Minh (DEV-1) | 07/12 – 08/12 | | |
| [ ] | 11.05.03 | Test | Ly (Tester) | 07/12 – 08/12 | | |
| [ ] | **11.06** | **PBI-041: Theo dõi tình trạng cơ sở vật chất công cộng** | Tiến (DEV-4) | 07/12 – 08/12 | | WBS không có dòng Test |
| [ ] | 11.06.01 | BackEnd | Tiến (DEV-4) | 07/12 – 08/12 | | |
| [ ] | 11.06.02 | FrontEnd | Tiến (DEV-4) | 07/12 – 08/12 | | |
| [ ] | **11.07** | **PBI-042: Cập nhật tình trạng hạng mục bảo trì** | Minh (DEV-1) | 07/12 – 08/12 | | |
| [ ] | 11.07.01 | BackEnd | Minh (DEV-1) | 07/12 – 08/12 | | |
| [ ] | 11.07.02 | FrontEnd | Minh (DEV-1) | 07/12 – 08/12 | | |
| [ ] | 11.07.03 | Test | Ly (Tester) | 07/12 – 08/12 | | |
| [ ] | **12** | **Nghiệm thu Release v3.0** | Minh (PO) | 09/12 – 10/12 | | |
| [ ] | **13** | **Bàn giao Release v3.0 và kết thúc dự án** | Minh (PO) | 10/12 | | |

## 3. Commit không thuộc task nào trong WBS

| Commit | Người commit | Nội dung |
|---|---|---|
| `7fc5082` `d7eed22` | Nam | Giao diện ban đầu, trang chủ, chuyển file trang chủ |
| `5252c59` `bd807d9` | Nam | Hướng dẫn commit, README |
| `6add2a4` `3e2678b` `740378c` `eabcc85` | Minh | `.gitignore`, README, bỏ theo dõi `appsettings.json`, bỏ mật khẩu admin mặc định |
| `d973cbd` | Nam | Thư mục `tests/` |
| `af75f3b` | Ly | Nội dung quy ước commit (PR #10) |
| `586238e` `9af355c` | Nam | Dọn file đẩy nhầm |
