# Bằng chứng kiểm thử (Evidence)

> Lưu kết quả các lần chạy test và ảnh chạy tay, làm bằng chứng cho tài liệu acceptance trong `docs/testing/`
> và cho Báo cáo kiểm thử. Mục "Evidence" của Product Checklist trỏ tới thư mục này.

## 1. Quy ước

- Kết quả test tự động: `YYYY-MM-DD_ket_qua_test.md`, tạo từ file `.trx` của `dotnet test` và output của `node --test`.
- Ảnh chạy tay: `img/YYYY-MM-DD_<test case>_buoc<số>.png`, ví dụ `img/2026-11-25_Y9_buoc4.png`. Mỗi ảnh dưới 500 KB.
- **Chỉ dữ liệu giả** (RSK-008). Trước khi đưa ảnh vào repo, cắt bỏ phần lộ tên máy, tên tài khoản máy tính, dữ liệu thật.
- **Không commit file `.trx` gốc**: trong đó có tên máy và đường dẫn thư mục cá nhân.

Cách chạy lại (ở gốc repo):
```bash
BLUEMOON_TEST_CONNECTION="Host=localhost;Port=5432;Database=BlueMoon_IntegrationTest;Username=<user>;Password=<pass>" \
dotnet test tests/BlueMoonApartementManagementConsole.Tests --logger "trx;LogFileName=ket_qua.trx" --results-directory <thư mục tạm>
node --test --test-reporter=spec tests/js/*.test.mjs
```

## 2. Danh sách bằng chứng

| Ngày | Nội dung | Kết quả | File |
|---|---|---|---|
| 25/11/2026 | Toàn bộ test tự động: .NET (xUnit) và JavaScript (Node). Dùng cho 5 tài liệu ký ngày 25/11/2026 | .NET 441 pass, 1 skip (Q7), 0 fail; JS 18 pass, 0 fail | [2026-11-25_ket_qua_test.md](2026-11-25_ket_qua_test.md) |
| 25/11/2026 | Chạy tay Y9 (`acceptance_apartment_profile.md`), K8 bước 1 – 2, K9 phần giao diện (`acceptance_apartment_search.md`) | Đạt, ghi trong ô kết quả của từng tài liệu | Chưa có ảnh |
| 10/11/2026 | Chạy tay K1 – K4, B4, B9, A4 bước 4, M1 bước 3 – 4, M5, D11 bước 3, kiểm lại lỗi #31 | Đạt, ghi trong `docs/work_checklist.md` | Chưa có ảnh |

Ảnh chạy tay còn thiếu: theo dõi ở mục "Làm sau" của phần việc Tester trong `docs/work_checklist.md`.

## 3. Tài liệu acceptance và test tương ứng

| Tài liệu | Kết luận (25/11/2026) | Test tự động |
|---|---|---|
| `acceptance_apartment_profile.md` (PBI-001) | Không đạt (AC-001: thiếu quan hệ chủ hộ – thành viên, chờ PO) | `ApartmentProfileAcceptanceTests`, E9 |
| `acceptance_apartment_search.md` (PBI-002) | Đạt | `ApartmentSearchAcceptanceTests`, K9 trong `ApartmentProfileServiceTests`, `tests/js/apartment-search.test.mjs` |
| `acceptance_role_authorization.md` (PBI-026) | Đạt | `RoleAuthorizationAcceptanceTests` |
| `acceptance_audit_log.md` (PBI-029) | Đạt | `AuditLogAcceptanceTests`, `AuditLogQueryTests`, 2 test thu tiền, `tests/js/audit-log.test.mjs` |
| `acceptance_session_timeout.md` (PBI-028) | Không đạt (T2, lỗi H1) | `SessionTimeoutAcceptanceTests`, `SessionTimeoutTests`, `tests/js/profile.test.mjs` |
| Trang `/Error` (UI test case) | — | `ErrorPageTests` (R20) |
