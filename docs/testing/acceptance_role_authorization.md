# Kiểm thử chấp nhận: Tài khoản riêng và phân quyền theo vai trò

> Tài liệu dành cho **người kiểm thử độc lập** (không phải người viết code/test).
> Người kiểm thử chạy từng test case trên app thật, ghi kết quả vào các ô trống rồi ký nghiệm thu ở cuối.

## 1. Mục đích và phạm vi

Xác nhận:
- Mỗi người dùng có **tài khoản riêng**, tên đăng nhập không trùng, đăng nhập đúng tư cách (vai trò) của mình.
- Mỗi vai trò (**Ban Quản Trị**, **Kế Toán**, **Cư Dân**) chỉ thấy và chỉ truy cập được chức năng của vai trò đó, kể cả khi gõ thẳng URL hoặc gọi API.
- Ban Quản Trị chỉ thao tác được tài khoản **trong phạm vi của mình**: mọi cư dân, và các tài khoản BQT / Kế toán do mình (hoặc cấp dưới của mình) cấp.
- Thu hồi quyền (vô hiệu hóa) có hiệu lực **ngay ở request kế tiếp**.

**Ngoài phạm vi:** các trang Tài chính, Báo cáo, Căn hộ, Nhân khẩu (chưa xây dựng, bấm vào sẽ ra trang Not Found).

Unit test tương ứng (chạy bằng `dotnet test`):
- `tests/BlueMoonApartementManagementConsole.Tests/Services/AuthServiceRoleTests.cs` (R1 đến R6)
- `tests/BlueMoonApartementManagementConsole.Tests/Services/AccountScopeTests.cs` (R7 đến R15)
- `tests/BlueMoonApartementManagementConsole.Tests/Security/AuthorizationMetadataTests.cs` (R16 đến R18)

## 2. Môi trường

| Mục | Giá trị |
|---|---|
| Nhánh / commit được kiểm thử | |
| Hệ điều hành | |
| Trình duyệt 1 / Trình duyệt 2 (hoặc cửa sổ ẩn danh) | |
| URL app | `https://localhost:7127` |

**Chuẩn bị:**
1. Dùng database **mới** để kết quả không lẫn dữ liệu cũ. Trong thư mục `BlueMoonApartementManagementConsole/`, chạy:
   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=BlueMoon_AT_RBAC;Username=<user>;Password=<pass>"
   ```
2. Chạy app: `dotnet run --launch-profile https`. Database và tài khoản `admin` được tạo tự động.
3. Cần **2 phiên đăng nhập cùng lúc** cho B7: dùng 2 trình duyệt khác nhau, hoặc 1 cửa sổ thường cộng 1 cửa sổ ẩn danh.

## 3. Dữ liệu test

### 3.1. Cây tài khoản cần tạo

```
admin (Ban Quản Trị, tạo tự động)
├── bqt_a (Ban Quản Trị)       do admin tạo
│   └── ketoan_a1 (Kế Toán)    do bqt_a tạo
├── bqt_b (Ban Quản Trị)       do admin tạo
└── ketoan_tudk (Kế Toán)      tự đăng ký, admin duyệt ở B2
cudan_01 (Cư Dân)              tự đăng ký ở B1
```

### 3.2. Cách tạo

| # | Bước |
|---|---|
| 1 | Đăng nhập `admin`, Tư cách **Ban quản trị**. Vào `/quan-tri/tai-khoan`, mở **Tạo tài khoản mới**, tạo `bqt_a` (vai trò Ban quản trị) và `bqt_b` (vai trò Ban quản trị). **Ghi lại mật khẩu tạm** của từng tài khoản |
| 2 | Đăng xuất. Đăng nhập `bqt_a` bằng mật khẩu tạm. App sẽ chuyển tới `/ho-so`: đổi mật khẩu thành `TestPass@01` |
| 3 | Vẫn ở `bqt_a`, vào `/quan-tri/tai-khoan`, tạo `ketoan_a1` (vai trò Kế toán). Ghi lại mật khẩu tạm |
| 4 | Lần lượt đăng nhập `bqt_b` và `ketoan_a1` bằng mật khẩu tạm, đổi mật khẩu thành `TestPass@01` |

> **Quan trọng:** tài khoản chưa đổi mật khẩu tạm sẽ bị chặn mọi trang trừ `/ho-so` và mọi API trừ `/api/ho-so`.
> Phải đổi mật khẩu xong cho tất cả tài khoản trước khi chạy B3 đến B7, nếu không kết quả bị chặn sẽ do **chưa đổi mật khẩu** chứ không phải do **phân quyền**.

---

## 4. Test case

### B1. Tài khoản riêng

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Vào `/login`, bấm **Đăng ký**. Tên người dùng `Cu Dan 01`, Tài khoản `cudan_01`, Mật khẩu `TestPass@01`, Vai trò **Cư dân**, bấm **Đăng ký** | "Đăng ký thành công. Bạn có thể đăng nhập ngay." |
| 2 | Đăng ký lại tài khoản `cudan_01` (mật khẩu, vai trò bất kỳ) | "Tài khoản này đã tồn tại." |
| 3 | Đăng ký tài khoản trùng với tài khoản do BQT tạo, ví dụ `bqt_a` | "Tài khoản này đã tồn tại." |
| 4 | Đăng nhập `cudan_01` / `TestPass@01`, Tư cách **Kế toán** | "Tài khoản, mật khẩu hoặc tư cách đăng nhập không đúng." |
| 5 | Đăng nhập `cudan_01` / `TestPass@01`, Tư cách **Cư dân** | Vào trang chủ, thấy "Bạn đang đăng nhập với vai trò **Cư Dân**." |
| 6 | Lặp lại bước 5 với `ketoan_a1` (Tư cách Kế toán) và `bqt_a` (Tư cách Ban quản trị) | Thấy đúng vai trò **Kế Toán** / **Ban Quản Trị**, và tên tài khoản của chính mình |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### B2. Duyệt tài khoản theo vai trò

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | Đăng ký `ketoan_tudk` / `TestPass@01`, Vai trò **Kế toán** | "Đăng ký thành công. Tài khoản cần được Ban quản trị khác xác nhận trước khi đăng nhập." |
| 2 | Đăng nhập `ketoan_tudk`, Tư cách **Kế toán** | "Tài khoản đang chờ Ban quản trị xác nhận." |
| 3 | Đăng nhập `admin`, vào `/quan-tri/duyet-tai-khoan` | Thấy `ketoan_tudk` trong danh sách chờ |
| 4 | Bấm **Xác nhận** ở dòng `ketoan_tudk` | "Đã xác nhận tài khoản." Dòng biến mất khỏi danh sách |
| 5 | Đăng nhập lại `ketoan_tudk` | Vào được, vai trò **Kế Toán** |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### B3. Menu và trang chủ theo vai trò

Đăng nhập lần lượt từng tài khoản, quan sát menu bên trái và mục "Công việc thường dùng" trên trang chủ.

| Tài khoản | Menu mong đợi | Không được thấy |
|---|---|---|
| `bqt_a` | Trang chủ, **Căn hộ & cư dân**, **Tài chính**, **Báo cáo**, **Quản trị** | — |
| `ketoan_a1` | Trang chủ, **Tài chính**, **Báo cáo** | Căn hộ & cư dân, Quản trị |
| `cudan_01` | Chỉ **Trang chủ** | Căn hộ & cư dân, Tài chính, Báo cáo, Quản trị |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### B4. Chặn truy cập trang quản trị bằng URL

Gõ thẳng từng địa chỉ vào thanh địa chỉ: `/quan-tri/tai-khoan`, `/quan-tri/duyet-tai-khoan`, `/quan-tri/nhat-ky`.

| # | Đăng nhập bằng | Kết quả mong đợi |
|---|---|---|
| 1 | `ketoan_a1` | **Không** thấy nội dung trang quản trị. Ghi lại trang thực tế hiện ra (xem lỗi đã biết F1) |
| 2 | `cudan_01` | Như trên |
| 3 | Chưa đăng nhập (cửa sổ ẩn danh mới) | Bị chuyển về `/login` |
| 4 | `bqt_a` (đối chứng) | Xem được cả 3 trang |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### B5. Chặn gọi API trực tiếp

Gõ thẳng từng địa chỉ vào thanh địa chỉ: `/api/tai-khoan`, `/api/tai-khoan/cho-xac-nhan`, `/api/nhat-ky`.
Xem **mã trạng thái** ở DevTools (F12) → tab **Network** → cột **Status**.

| # | Đăng nhập bằng | Kết quả mong đợi |
|---|---|---|
| 1 | `ketoan_a1` | Status **403**, không có dữ liệu tài khoản / nhật ký |
| 2 | `cudan_01` | Status **403** |
| 3 | Chưa đăng nhập (cửa sổ ẩn danh mới) | Status **401** |
| 4 | `bqt_a` (đối chứng) | Status **200**, trả về dữ liệu JSON |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### B6. Phạm vi quản lý theo cây người cấp

Vào `/quan-tri/tai-khoan`, quan sát cột cuối (thao tác) của từng dòng. Có nút (**Vô hiệu hóa**, **Cấp lại mật khẩu**...) nghĩa là được quản lý, `—` nghĩa là không.

| Dòng tài khoản | Đăng nhập `bqt_a` | Đăng nhập `bqt_b` | Đăng nhập `admin` |
|---|---|---|---|
| `admin` | `—` | `—` | `—` (chính mình) |
| `bqt_a` | `—` (chính mình) | `—` | Có nút |
| `bqt_b` | `—` | `—` (chính mình) | Có nút |
| `ketoan_a1` | Có nút | `—` | Có nút |
| `ketoan_tudk` | `—` | `—` | Có nút |
| `cudan_01` | Có nút | Có nút | Có nút |

Thêm: kiểm tra cột **Người cấp** của `ketoan_a1` là `bqt_a`, của `ketoan_tudk` là `admin`.

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### B7. Thu hồi quyền có hiệu lực ngay

| # | Bước | Kết quả mong đợi |
|---|---|---|
| 1 | **Trình duyệt 2:** đăng nhập `ketoan_a1`, đang ở trang chủ | Vào được |
| 2 | **Trình duyệt 1:** đăng nhập `bqt_a`, vào `/quan-tri/tai-khoan`, bấm **Vô hiệu hóa** ở dòng `ketoan_a1`, xác nhận | "Đã thực hiện thao tác." Trạng thái `ketoan_a1` là "Vô hiệu hóa" |
| 3 | **Trình duyệt 2:** tải lại trang (F5) | Bị đăng xuất, về `/login` |
| 4 | **Trình duyệt 2:** đăng nhập lại `ketoan_a1` | "Tài khoản, mật khẩu hoặc tư cách đăng nhập không đúng." |
| 5 | **Trình duyệt 1:** bấm **Kích hoạt** ở dòng `ketoan_a1` | Trạng thái về "Đang hoạt động" |
| 6 | **Trình duyệt 2:** đăng nhập lại `ketoan_a1` | Vào được |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

### B8. Nhật ký ghi đúng người thực hiện

Đăng nhập `admin`, vào `/quan-tri/nhat-ky`.

| # | Sự kiện cần tìm | Người thực hiện mong đợi |
|---|---|---|
| 1 | Tạo tài khoản `bqt_a`, `bqt_b` | `admin` |
| 2 | Tạo tài khoản `ketoan_a1` | `bqt_a` |
| 3 | Xác nhận tài khoản `ketoan_tudk` | `admin` |
| 4 | Vô hiệu hóa và kích hoạt `ketoan_a1` (B7) | `bqt_a` |
| 5 | Đăng nhập sai tư cách của `cudan_01` (B1 bước 4) | `cudan_01`, ghi là đăng nhập thất bại |

| Kết quả thực tế | Pass/Fail | Ghi chú |
|---|---|---|
| | | |

---

## 5. Lỗi đã biết (không cần báo lại)

Các vấn đề dưới đây đã được ghi nhận từ việc đọc code. Gặp đúng các vấn đề này thì ghi chú, **không** đánh Fail test case vì chúng:

| # | Mô tả | Ảnh hưởng tới |
|---|---|---|
| F1 | Không có trang `/403`. Khi bị từ chối quyền, app chuyển tới `/403` và nhiều khả năng hiện trang **"Not Found"** thay vì "Không có quyền". Tiêu chí Pass của B4 là **không xem được nội dung**, ghi lại trang thực tế hiện ra để xác nhận lỗi | B4 |
| F2 | Chưa cấu hình `FallbackPolicy`: trang mới nào quên gắn `[Authorize]` sẽ mở công khai. Hiện đã có unit test R18 chặn lỗi này trên CI | Không thấy được khi test tay |
| F3 | Menu **Căn hộ & cư dân**, **Tài chính**, **Báo cáo** trỏ tới các trang chưa xây dựng, bấm vào sẽ ra Not Found | B3 (chỉ kiểm tra menu hiện hay ẩn, không bấm vào) |
| F4 | Câu hỏi nghiệp vụ đang chờ PO: cư dân tự đăng ký được duyệt ngay không qua xác minh; ai cũng có thể tự đăng ký xin vai trò Ban Quản Trị (phải chờ duyệt) | B1, B2 |

## 6. Tổng hợp và nghiệm thu

| Test case | Pass/Fail | Ghi chú |
|---|---|---|
| B1. Tài khoản riêng | | |
| B2. Duyệt tài khoản theo vai trò | | |
| B3. Menu và trang chủ theo vai trò | | |
| B4. Chặn trang quản trị bằng URL | | |
| B5. Chặn gọi API trực tiếp | | |
| B6. Phạm vi quản lý theo cây | | |
| B7. Thu hồi quyền có hiệu lực ngay | | |
| B8. Nhật ký ghi đúng người thực hiện | | |

| | |
|---|---|
| Người kiểm thử | |
| Ngày kiểm thử | |
| Kết luận | ☐ Đạt  ☐ Không đạt |
| Chữ ký | |
