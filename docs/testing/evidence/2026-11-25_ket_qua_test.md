# Kết quả chạy test ngày 25/11/2026

> Bằng chứng cho các tài liệu acceptance đã ký ngày 25/11/2026. Tạo từ file `.trx` của `dotnet test` và output của `node --test`.
> Đã bỏ tên máy và đường dẫn thư mục cá nhân; file `.trx` gốc không đưa vào repo.

## 1. Môi trường

| Mục | Giá trị |
|---|---|
| Code app | `1231392` (`main` sau PR #53) |
| Nhánh test | `test/tester-deliverables` |
| Hệ điều hành | macOS (Darwin 27), .NET 10, PostgreSQL trên máy (database `BlueMoon_IntegrationTest`), Node v26 |

## 2. Tổng hợp

| Bộ test | Lệnh | Pass | Skip | Fail | Tổng |
|---|---|---|---|---|---|
| .NET (xUnit), có DB | `BLUEMOON_TEST_CONNECTION=... dotnet test` | 441 | 1 | 0 | 442 |
| JavaScript (Node, DOM giả) | `node --test tests/js/*.test.mjs` | 18 | 0 | 0 | 18 |

Test bị Skip: Q7 (`AuditLogQueryTests`), chờ sửa lỗi J1. Khi chạy không có DB, test tích hợp bị Skip (325 pass, 117 skip, 0 fail).

## 3. Test theo tài liệu acceptance

### 3.1. Hồ sơ căn hộ, hộ khẩu (`acceptance_apartment_profile.md`, PBI-001)

| Lớp test | Test | Kết quả | Thời gian (s) |
|---|---|---|---|
| `ApartmentProfileAcceptanceTests` | `Y10_AccountantAndResident_CannotCreateOrUpdateProfiles` | Pass | 0.20 |
| `ApartmentProfileAcceptanceTests` | `Y1_ValidApartment_IsSaved_AndCanBeLookedUp` | Pass | 0.04 |
| `ApartmentProfileAcceptanceTests` | `Y2_ValidHousehold_IsLinkedToApartment_HeadAndResidents_AndCanBeLookedUp` | Pass | 0.05 |
| `ApartmentProfileAcceptanceTests` | `Y3_UpdatedApartmentAndHousehold_AreSaved_AndUnsubmittedResidentsAreKept` | Pass | 0.05 |
| `ApartmentProfileAcceptanceTests` | `Y4_DuplicateApartmentCode_IsRejected_WhenCreatingOrUpdating` | Pass | 0.05 |
| `ApartmentProfileAcceptanceTests` | `Y5_DuplicateHouseholdNumber_OrSecondHouseholdForApartment_IsRejected` | Pass | 0.06 |
| `ApartmentProfileAcceptanceTests` | `Y7_MissingRequiredField_IsRejected_NamesTheField_AndNothingIsSaved(kind: "ho-ten-nhan-khau", field: "FullName")` | Pass | 0.04 |
| `ApartmentProfileAcceptanceTests` | `Y7_MissingRequiredField_IsRejected_NamesTheField_AndNothingIsSaved(kind: "ma-can-ho", field: "Code")` | Pass | 0.04 |
| `ApartmentProfileAcceptanceTests` | `Y7_MissingRequiredField_IsRejected_NamesTheField_AndNothingIsSaved(kind: "ngay-lap-ho-khau", field: "Ngày lập hộ khẩu")` | Pass | 0.04 |
| `ApartmentProfileAcceptanceTests` | `Y7_MissingRequiredField_IsRejected_NamesTheField_AndNothingIsSaved(kind: "ngay-sinh", field: "Ngày sinh")` | Pass | 0.05 |
| `ApartmentProfileAcceptanceTests` | `Y7_MissingRequiredField_IsRejected_NamesTheField_AndNothingIsSaved(kind: "so-ho-khau", field: "HouseholdNumber")` | Pass | 0.04 |
| `ApartmentProfileAcceptanceTests` | `Y7_MissingRequiredField_IsRejected_NamesTheField_AndNothingIsSaved(kind: "ten-chu-ho", field: "HeadName")` | Pass | 0.04 |
| `ApartmentProfileAcceptanceTests` | `Y8_InvalidValue_IsRejected_NamesTheField_AndNothingIsSaved(kind: "dien-tich-am", field: "Area")` | Pass | 0.24 |
| `ApartmentProfileAcceptanceTests` | `Y8_InvalidValue_IsRejected_NamesTheField_AndNothingIsSaved(kind: "dien-tich-bang-0", field: "Area")` | Pass | 0.04 |
| `ApartmentProfileAcceptanceTests` | `Y8_InvalidValue_IsRejected_NamesTheField_AndNothingIsSaved(kind: "ngay-cap-giay-to", field: "Ngày cấp")` | Pass | 0.05 |
| `ApartmentProfileAcceptanceTests` | `Y8_InvalidValue_IsRejected_NamesTheField_AndNothingIsSaved(kind: "phong-ngu-qua-nhieu", field: "BedroomCount")` | Pass | 0.04 |
| `ApartmentProfileAcceptanceTests` | `Y8_InvalidValue_IsRejected_NamesTheField_AndNothingIsSaved(kind: "tang-qua-lon", field: "Floor")` | Pass | 0.04 |
| `ApartmentProfileAcceptanceTests` | `Y8_InvalidValue_IsRejected_NamesTheField_AndNothingIsSaved(kind: "toa-nha-khong-ton-tai", field: "Tòa nhà")` | Pass | 0.04 |
| `PersonalDataEncryptionTests` | `SameCccdInAnotherHousehold_IsRejected` | Pass | 0.02 |

### 3.2. Tra cứu căn hộ (`acceptance_apartment_search.md`, PBI-002)

| Lớp test | Test | Kết quả | Thời gian (s) |
|---|---|---|---|
| `ApartmentProfileServiceTests` | `UpdateApartmentStatus_AllowsForbiddenStatus_WhenAllResidentsMovedOut` | Pass | 0.00 |
| `ApartmentProfileServiceTests` | `UpdateApartmentStatus_ChangesStatus_AndWritesAudit` | Pass | 0.00 |
| `ApartmentProfileServiceTests` | `UpdateApartmentStatus_RejectsForbiddenStatus_WhenActiveResidentsExist` | Pass | 0.00 |
| `ApartmentProfileServiceTests` | `UpdateApartment_Put_RejectsForbiddenStatus_WhenActiveResidentsExist` | Pass | 0.00 |
| `ApartmentSearchAcceptanceTests` | `K2_ApartmentType_IsSaved_Returned_Updated_AndOfferedOnPage` | Pass | 0.06 |
| `ApartmentSearchAcceptanceTests` | `K5_MissingOrInvalidType_IsRejected_AndNothingIsSaved(kind: "Villa")` | Pass | 0.04 |
| `ApartmentSearchAcceptanceTests` | `K5_MissingOrInvalidType_IsRejected_AndNothingIsSaved(kind: "missing")` | Pass | 0.04 |
| `ApartmentSearchAcceptanceTests` | `K5_MissingOrInvalidType_IsRejected_AndNothingIsSaved(kind: "number")` | Pass | 0.24 |
| `ApartmentSearchAcceptanceTests` | `K7_ApartmentWithoutType_DefaultsToCanHo` | Pass | 0.04 |
| `ApartmentSearchAcceptanceTests` | `K8_ApartmentStatus_IsChangedViaPatch_AndInvalidStatusIsRejected` | Pass | 0.05 |
| `ApartmentStatusLabelsTests` | `EveryApartmentStatus_HasExactlyOneLabel` | Pass | 0.01 |
| `ApartmentStatusLabelsTests` | `Labels_AreNonEmpty_AndUnique` | Pass | 0.00 |
| `ApartmentTypeLabelsTests` | `EveryApartmentType_HasExactlyOneLabel` | Pass | 0.01 |
| `ApartmentTypeLabelsTests` | `Labels_AreNonEmpty_AndUnique` | Pass | 0.00 |

### 3.3. Phân quyền (`acceptance_role_authorization.md`, PBI-026)

| Lớp test | Test | Kết quả | Thời gian (s) |
|---|---|---|---|
| `RoleAuthorizationAcceptanceTests` | `B1_OwnAccounts_DuplicateRejected_RoleShownAfterLogin` | Pass | 0.25 |
| `RoleAuthorizationAcceptanceTests` | `B2_SelfRegisteredAccountant_WaitsForApproval_ThenCanLogin` | Pass | 0.34 |
| `RoleAuthorizationAcceptanceTests` | `B3_MenuGroups_DependOnRole` | Pass | 0.20 |
| `RoleAuthorizationAcceptanceTests` | `B4_AdminPages_BlockedByUrl_ForNonAdmin_AndAnonymous` | Pass | 0.21 |
| `RoleAuthorizationAcceptanceTests` | `B5_AdminApis_Return403ForNonAdmin_401ForAnonymous_200ForAdmin` | Pass | 0.22 |
| `RoleAuthorizationAcceptanceTests` | `B6_ManageScope_FollowsGrantTree` | Pass | 0.27 |
| `RoleAuthorizationAcceptanceTests` | `B7_DisableTakesEffectImmediately_EnableRestoresAccess` | Pass | 0.18 |
| `RoleAuthorizationAcceptanceTests` | `B8_AuditLog_RecordsTheRealActor` | Pass | 0.36 |
| `RoleAuthorizationAcceptanceTests` | `B9_EveryLinkOnHome_Opens_AndInDevelopmentPages_KeepRoleRules` | Pass | 0.30 |

### 3.4. Nhật ký (`acceptance_audit_log.md`, PBI-029)

| Lớp test | Test | Kết quả | Thời gian (s) |
|---|---|---|---|
| `AuditLogAcceptanceTests` | `D10_Page_IsReadOnly_DescriptionRecorded` | Pass | 0.04 |
| `AuditLogAcceptanceTests` | `D11_ProfileActions_AreLogged_Filterable_AndLabelledInVietnamese` | Pass | 0.15 |
| `AuditLogAcceptanceTests` | `D12_CreateFeeItemAndChangeRate_AreLogged_WithActorTargetAndDetail` | Pass | 0.16 |
| `AuditLogAcceptanceTests` | `D1_AllTwelveActions_AreLoggedWithActorTargetAndDetail` | Pass | 0.56 |
| `AuditLogAcceptanceTests` | `D2_FilterByAction` | Pass | 0.55 |
| `AuditLogAcceptanceTests` | `D3_FilterByUsername_ActorOnly_PartialMatch_CaseSensitive` | Pass | 0.56 |
| `AuditLogAcceptanceTests` | `D4_FilterByVietnamDate_ToDateIsInclusive` | Pass | 0.57 |
| `AuditLogAcceptanceTests` | `D5_CombinedFilters_AllMustMatch` | Pass | 0.76 |
| `AuditLogAcceptanceTests` | `D6_Paging_20PerPage_NewestFirst` | Pass | 0.56 |
| `AuditLogQueryTests` | `CombinedFilters_AllMustMatch` | Pass | 0.00 |
| `AuditLogQueryTests` | `Database_BlocksUpdateAndDeleteOnAuditLogs` | Pass | 0.05 |
| `AuditLogQueryTests` | `FilterByAction_ReturnsOnlyThatAction` | Pass | 0.01 |
| `AuditLogQueryTests` | `FilterByUsername_CurrentLimitations_J2` | Pass | 0.01 |
| `AuditLogQueryTests` | `FilterByUsername_MatchesPartOfName` | Pass | 0.01 |
| `AuditLogQueryTests` | `FilterByVietnamDay_FromInclusive_ToExclusive` | Pass | 0.01 |
| `AuditLogQueryTests` | `FilterWithNonUtcOffset_GivesSameResultAsUtc` | Skip | 0.00 |
| `AuditLogQueryTests` | `Json_HasFieldsUsedByAuditLogJs` | Pass | 0.01 |
| `AuditLogQueryTests` | `Logout_WritesOneLogoutEntryForAdmin` | Pass | 0.01 |
| `AuditLogQueryTests` | `NonGetMethods_AreRejected(method: "DELETE", expected: MethodNotAllowed)` | Pass | 0.04 |
| `AuditLogQueryTests` | `NonGetMethods_AreRejected(method: "PATCH", expected: MethodNotAllowed)` | Pass | 0.00 |
| `AuditLogQueryTests` | `NonGetMethods_AreRejected(method: "POST", expected: BadRequest)` | Pass | 0.00 |
| `AuditLogQueryTests` | `NonGetMethods_AreRejected(method: "PUT", expected: MethodNotAllowed)` | Pass | 0.00 |
| `AuditLogQueryTests` | `Paging_OutOfRangeParameters_AreClamped` | Pass | 0.01 |
| `AuditLogQueryTests` | `Paging_ReturnsCorrectSlices_AndTotal` | Pass | 0.20 |
| `AuditLogQueryTests` | `Results_NewestFirst_TieBrokenByIdDescending` | Pass | 0.01 |
| `AuditLogQueryTests` | `WrongRoleLogin_RehashedPasswordIsStillSaved` | Pass | 0.07 |
| `PaymentApiTests` | `PartialThenFullPayment_ThroughApi_AddsUp_AndRejectsInvalidPayments` | Pass | 0.29 |
| `PaymentServiceTests` | `PartialPayments_AddUp_UpdateRemainingAndStatus_ThenFullyPaid` | Pass | 0.01 |

### 3.5. Hết phiên (`acceptance_session_timeout.md`, PBI-028)

| Lớp test | Test | Kết quả | Thời gian (s) |
|---|---|---|---|
| `SessionTimeoutAcceptanceTests` | `T1_Idle10Minutes_SessionEnds_AndLoginReturnsToSamePage` | Pass | 0.07 |
| `SessionTimeoutAcceptanceTests` | `T3_ActionAfterSessionExpired_IsNotPerformed_NorLogged` | Pass | 0.09 |
| `SessionTimeoutAcceptanceTests` | `T4_ChangePasswordAfterSessionExpired_IsRejected_PasswordUnchanged` | Pass | 0.38 |
| `SessionTimeoutAcceptanceTests` | `T7_SessionExpiry_IsNotWrittenToAuditLog` | Pass | 0.07 |
| `SessionTimeoutTests` | `ActivityAfterHalfway_RenewsCookie_SessionExtendsBeyondOriginal10m` | Pass | 0.00 |
| `SessionTimeoutTests` | `ActivityAfterHalfway_SessionEnds10mAfterThatActivity` | Pass | 0.01 |
| `SessionTimeoutTests` | `ActivityBeforeHalfway_DoesNotRenew_SessionEndsOnly6mAfterThatActivity` | Pass | 0.01 |
| `SessionTimeoutTests` | `AuthCookie_IsSessionCookie_WithSecureAttributes` | Pass | 1.01 |
| `SessionTimeoutTests` | `CookieOptions_MatchNfr008` | Pass | 0.00 |
| `SessionTimeoutTests` | `ExpiredCookie_ReplayedManually_IsRejectedByServer` | Pass | 0.02 |
| `SessionTimeoutTests` | `Idle_10m01s_ApiReturns401` | Pass | 0.00 |
| `SessionTimeoutTests` | `Idle_10m01s_PageRedirectsToLogin` | Pass | 0.00 |
| `SessionTimeoutTests` | `Idle_9m59s_StillSignedIn` | Pass | 0.12 |

### 3.6. Trang lỗi `/Error` (R20)

| Lớp test | Test | Kết quả | Thời gian (s) |
|---|---|---|---|
| `ErrorPageTests` | `ErrorPage_OpensWithoutLogin_ShowsVietnameseMessageAndRequestId` | Pass | 0.21 |
| `ErrorPageTests` | `UnhandledException_ShowsErrorPage_With500_AndNoInternalDetails` | Pass | 0.24 |

### 3.7. Test JavaScript (`tests/js`)

| Trang | Test | Kết quả | Thời gian (s) |
|---|---|---|---|
| Trang Nhật ký (`audit-log.js`) | D3 bước 5: tên tài khoản có khoảng trắng hai đầu được cắt trước khi gửi, kết quả như khi gõ không có khoảng trắng | Pass | 0.014 |
| Trang Nhật ký (`audit-log.js`) | D5: lọc kết hợp gửi đủ 4 điều kiện; ngày Việt Nam đổi sang giờ UTC, "Đến ngày" tính trọn ngày | Pass | 0.004 |
| Trang Nhật ký (`audit-log.js`) | D5 bước 2: Xóa lọc đưa mọi ô lọc về trống và tải lại Trang 1 không còn điều kiện lọc | Pass | 0.005 |
| Trang Nhật ký (`audit-log.js`) | D6: phân trang 45 bản ghi: dòng "Trang X / Y", nút Trước/Sau bị tắt đúng lúc, lọc thì về Trang 1 | Pass | 0.009 |
| Trang Nhật ký (`audit-log.js`) | D6: không có bản ghi nào thì ẩn bảng và thanh phân trang, hiện dòng "không có nhật ký" | Pass | 0.002 |
| Trang Nhật ký (`audit-log.js`) | D8: tên đăng nhập và chi tiết có mã HTML được hiện nguyên văn, không tạo thẻ HTML nào | Pass | 0.002 |
| Trang Nhật ký (`audit-log.js`) | D9: thời gian hiện theo giờ Việt Nam, đủ ngày tháng năm giờ phút giây và 3 số mili giây | Pass | 0.002 |
| Trang Nhật ký (`audit-log.js`) | J7 (ghi nhận, không đánh Fail): kiểu vi-VN đặt giờ trước ngày nên mili giây dính sau năm | Pass | 0.002 |
| Trang Nhật ký (`audit-log.js`) | D11 bước 3: cột Hành động hiện nhãn tiếng Việt lấy từ ô lọc; loại chưa có nhãn thì hiện tên gốc | Pass | 0.002 |
| Trang Nhật ký (`audit-log.js`) | Hết phiên khi đang xem Nhật ký (API trả 401) thì chuyển về trang đăng nhập, không hiện lỗi | Pass | 0.002 |
| Trang Hồ sơ (`profile.js`) | T4 bước 5 (H3, ghi nhận, không đánh Fail): hết phiên khi bấm Đổi mật khẩu thì về /login, không nhớ trang Hồ sơ | Pass | 0.003 |
| Lọc căn hộ (`apartment-profiles.js`) | Mở trang: hiện đủ 3 căn, dòng đếm "3 căn hộ", mỗi căn có dòng mô tả tòa · tầng · loại hình (K2 bước 1) | Pass | 0.004 |
| Lọc căn hộ (`apartment-profiles.js`) | K1: tra theo một phần mã căn (không phân biệt hoa thường), xóa ô tìm thì hiện lại đủ | Pass | 0.006 |
| Lọc căn hộ (`apartment-profiles.js`) | K2: lọc theo loại hình Kiot, Penthouse, Căn hộ ở; "Tất cả loại hình" thì hiện lại đủ | Pass | 0.007 |
| Lọc căn hộ (`apartment-profiles.js`) | K2 bước 4: sau khi AT-K-01 đổi sang Penthouse, lọc Penthouse ra 2 căn | Pass | 0.002 |
| Lọc căn hộ (`apartment-profiles.js`) | K3: tra theo diện tích chính xác; không có căn khớp thì hiện "Chưa có căn hộ phù hợp" | Pass | 0.004 |
| Lọc căn hộ (`apartment-profiles.js`) | K4: kết hợp ô tìm theo tên tòa, loại hình và diện tích; xóa hết thì hiện lại đủ | Pass | 0.009 |
| Lọc căn hộ (`apartment-profiles.js`) | K9 bước 4: hơn 5 căn khớp thì danh sách chỉ hiện 5, có dòng nhắc; bảng trạng thái vẫn hiện đủ | Pass | 0.003 |

## 4. Các test khác (theo lớp)

| Lớp test | Pass | Skip | Fail |
|---|---|---|---|
| `AccountManagementPasswordTests` | 13 | 0 | 0 |
| `AccountScopeTests` | 28 | 0 | 0 |
| `ApartmentProfileServiceTests` | 6 | 0 | 0 |
| `AuditActionLabelsTests` | 2 | 0 | 0 |
| `AuditCoverageTests` | 7 | 0 | 0 |
| `AuditImmutabilityTests` | 7 | 0 | 0 |
| `AuditServiceTests` | 20 | 0 | 0 |
| `AuthServiceLoginTests` | 10 | 0 | 0 |
| `AuthServicePasswordTests` | 17 | 0 | 0 |
| `AuthServiceRoleTests` | 18 | 0 | 0 |
| `AuthorizationMetadataTests` | 42 | 0 | 0 |
| `BillingPeriodApiTests` | 6 | 0 | 0 |
| `BillingPeriodServiceTests` | 37 | 0 | 0 |
| `ChangePasswordTests` | 13 | 0 | 0 |
| `DebtApiTests` | 2 | 0 | 0 |
| `DebtServiceTests` | 14 | 0 | 0 |
| `FeeCalculatorTests` | 17 | 0 | 0 |
| `ListPagesApiTests` | 2 | 0 | 0 |
| `LoginChangePasswordAcceptanceTests` | 11 | 0 | 0 |
| `PasswordHashingAcceptanceTests` | 5 | 0 | 0 |
| `PaymentApiTests` | 4 | 0 | 0 |
| `PaymentServiceTests` | 25 | 0 | 0 |
| `PersonalDataEncryptionTests` | 4 | 0 | 0 |
| `PersonalDataEncryptorTests` | 19 | 0 | 0 |
| `ReportApiTests` | 3 | 0 | 0 |
| `ResidentChangeApiTests` | 4 | 0 | 0 |
| `ResidentChangeServiceTests` | 21 | 0 | 0 |
