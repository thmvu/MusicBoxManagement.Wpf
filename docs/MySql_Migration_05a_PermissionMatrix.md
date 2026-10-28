# MySQL 05a — Ma trận quyền Staff/Manager

Ngày 28/10/2026. Đối chiếu reference mục 5/32/55, [Step8c](Step8c_PermissionMatrix.md), plan WPF và [bảng bàn giao](MySql_Migration_Status.md). Port service ma trận quyền; chưa đổi provider/UI hoặc nhập dữ liệu thật. Audit viewer MySQL là bước 05b riêng, không đồng nhất ghi audit ở đây với đã có màn đọc nhật ký MySQL.

## Hợp đồng và giới hạn

Thêm `MySqlPermissionManagementService.Read/Update/ValidateSelection` và `MySqlStaffReservationService.ForPermissions`. Factory kiểm tra Permission.Manage độc lập Reservation.View. Read/Update vẫn kiểm tra quyền hiện hành; Staff/Manager bị chặn kể cả database gán nhầm Permission.Manage. Admin toàn quyền từ PermissionCatalog, không dựa vào grant của Admin trong bảng.

- Chỉ sửa Staff hoặc Manager; không endpoint sửa ma trận Admin hoặc tạo permission code. Hai vai trò lưu riêng, Manager không kế thừa thay đổi của Staff.
- Staff không được Report.View/Export; User.Manage/Permission.Manage/Audit.View chỉ Admin. Invoice.Print cần Invoice.View; Report.Export cần Report.View. Mã null/lạ/sai hoa thường/có khoảng trắng bị từ chối; mã trùng gộp, tập rỗng được phép.
- ValidateSelection gọi lại helper **thuần** của PermissionManagementService hiện có để giữ cùng luật SQLite/MySQL, không mở SQLite hoặc thêm lớp trừu tượng/provider framework. Không thay logic SQLite/helper/DTO/UI.
- DTO RolePermissionMatrix chỉ đọc giữ Role/Revision/PermissionCodes. Revision SHA-256 có length prefix của **toàn bộ raw grants**, gồm mã legacy/quyền cấm/dependency sai bị che khỏi effective DTO. Không thể ghi đè từ form cũ chỉ vì phần thay đổi bị che trên UI.
- Lưu lại tập effective trên raw legacy sẽ dọn grant sai và ghi audit; no-op raw bằng tập đã chọn không ghi, không lấy clock. Không sửa user/role membership/SecurityStamp hoặc buộc đăng nhập lại; phiên đang đăng nhập đọc quyền mới ở thao tác tiếp theo.

## Snapshot và transaction

Read dùng RR read-only, ReadAccess và raw matrix trong cùng snapshot; không Demand writer/clock/audit/maintenance.

Update precheck Permission.Manage và chuẩn hóa tập chọn ngoài transaction, sau đó Serializable:

1. Khóa **AspNetRoles đúng Id/Name Staff hoặc Manager bằng PK FOR UPDATE**, kể cả khi ma trận rỗng; không advisory lock toàn ứng dụng. Điểm này thay writer lock toàn DB của SQLite, tuần tự hóa hai form cùng vai trò.
2. Đọc lại active/stamp/current role/Permission.Manage trong transaction **sau chờ khóa**; precheck không phải quyết định cấp quyền cuối cùng.
3. Đọc raw grants và kiểm tra Revision. So sánh raw với tập chọn cho no-op; không trả no-op trước kiểm tra quyền/stale.
4. DELETE/INSERT chỉ grant của vai trò đó, từng INSERT phải ảnh hưởng đúng một dòng. Truy vấn role theo PK để tránh quét/khóa các vai trò khác không cần thiết.
5. Lấy một UTC IClock sau chờ/ghi, ghi Staff audit `Permission.Update`/`RolePermission` với Role/actor/danh sách raw trước và tập sau; đọc DTO mới và commit.

Lỗi audit/ghi rollback cả ma trận; 1213/1205 trả lỗi yêu cầu tải lại, không tự retry vô hạn. Lỗi khác không bị nuốt. Không tự xử lý lại thao tác khi COMMIT mất kết nối chưa rõ kết quả. Nhóm khóa tài khoản/role/stamp là tài nguyên quản trị hiện có; không thêm khóa Room/Customer/Session vào writer ma trận.

## Kiểm tra và bằng chứng

Build `bin/VerifyMatrixMySqlDebug` và `bin/VerifyMatrixMySqlRelease` đạt. **Verify-MySqlPermissionMatrix online cả hai build đạt exit 0**, log `%TEMP%/MusicBoxMySqlMatrixDebug_20261028.log` và `%TEMP%/MusicBoxMySqlMatrixRelease_20261028.log`; gồm ca writer ma trận/clock/khóa tài khoản thật và dọn database GUID. Production source và bài Matrix không sửa sau hai lượt online; chỉ bổ sung log diagnostic cho hai suite hồi quy cũ khi kết nối lỗi.

**12/12 bộ SQLite/WPF Debug liên quan đạt**: PermissionMatrix, PermissionMatrixUi, Accounts, Authentication, AuditLog, AuditLogUi, StaffReservations, Workflows, AuthenticationUi, Foundation, Dashboard, Invoices. Summary `%TEMP%/MusicBoxMatrixLocalRegression_dcbfe9173b194c108d5f5a9977454be9/results.json`. AuthenticationUi 142 ảnh tại `%TEMP%/MusicBoxUi_a42dea5a55cf4c409b026eaa3b8b8396`; PermissionMatrixUi 8 ảnh `%TEMP%/MusicBoxPermissionsUi_0cf30f2014124599be9fc093670aa82b`; AuditLogUi 6 ảnh `%TEMP%/MusicBoxAuditUi_bb3b60d75aaa428580ec02110e3fe90e`. UI không sửa; đây là hồi quy SQLite, không chứng minh UI đã nối MySQL hoặc Audit viewer MySQL đã port.

Hồi quy 4 MySQL Debug cũ ban đầu: Authentication/Accounts đạt, StaffReservations/Foundation báo Connect Timeout. Summary `%TEMP%/MusicBoxMatrixMySqlRegression_0e20bf3856a34045af3fd21d945fdbe4/results.json` giữ riêng kết quả 2/4. Foundation lỗi ở OpenConnection lúc cleanup nên chưa đủ để kết luận các assertion trước đó; lượt đầu chưa log GUID/lỗi gốc trước finally. Thêm **chỉ log** GUID và lỗi trước cleanup cho hai suite để theo dõi đúng fixture, không đổi assertion/config kết nối/SSL/timeout. Retry tuần tự `%TEMP%/MusicBoxMatrixMySqlRetry_7f93a81fed1444a8b6dd5c5b466a9776/results.json` giữ riêng kết quả 1/2: Foundation đạt, StaffReservations tiếp tục timeout. Probe fresh defaultdb xác nhận GUID Staff retry `musicbox_test_ccbba2b23d0d418e8f6dd89979616fb1` đã vắng, không xóa schema chưa xác định thuộc lượt này. Chưa kết luận nguyên nhân hạ tầng. **Lượt diagnostic Staff sau khi Foundation kết nối lại đạt exit 0**, log riêng `%TEMP%/MusicBoxMatrixStaffDiagnostic_20261028.log`, GUID d4560f949e1f4e9785a8827272ccfa8f được test dọn trước khi kết thúc.

Kết quả mới nhất **17/17 bộ Debug liên quan đạt**: 5 MySQL (Matrix mới + Authentication/Accounts/StaffReservations/Foundation) và 12 SQLite/WPF trên. Không ghi đè summary đầu thành 4/4 hoặc nói mọi suite đều chạy. Runner hiện có 44 SQLite/WPF + 23 MySQL (67 bộ), đây là số có sẵn, không phải phạm vi nghiệm thu của lượt này.

`Verify-MySqlPermissionMatrix` tạo GUID database, log GUID/lỗi trước cleanup, kiểm tra tên trước DROP. Bootstrap/login/tạo Staff/Manager/Admin dự phòng và khóa/mở Admin qua service MySQL thật; SQL fixture chỉ legacy grants/metadata/revoke và trigger lỗi audit/row lock, không dữ liệu quán thật.

Ca kiểm tra: Admin-only/factory/quyền độc lập, hai vai trò không kế thừa, bất biến Admin/Staff, dependencies/mã sai/null/trùng/rỗng; no-op không clock/log; raw Revision/effective/cleanup/stale; quyền đổi ngay với phiên đang đăng nhập; audit actor/entity và rollback; hai writer thật cùng bản gốc chỉ một thắng; clock/ticks sau chờ role row. Writer quản trị tài khoản thật chuẩn bị khóa Admin nhưng chưa commit; matrix precheck RR thấy Admin cũ rồi chờ target role, sau account writer commit và thả role phải bị từ chối, không clock/ma trận/audit. Mở lại tài khoản không phục hồi phiên cũ; role/stamp/active/logout hiện hành được kiểm tra.

```powershell
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlPermissionMatrix.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyMatrixMySqlDebug
```

Đổi BuildDirectory sang VerifyMatrixMySqlRelease để chạy Release. Chỉ GUID/thư mục tạm; không sửa schema/provider/Content hoặc quyền người dùng thật.

## Bàn giao tiếp

Tiếp **05b Audit viewer MySQL** theo Audit.View Admin-only: lọc ngày Việt Nam/actor/action/entity, phân trang keyset, đọc chi tiết/UTC ticks và quyền hiện hành. Sau đó báo cáo/Excel/lịch sử khách và worker/provider/UI toàn app, backup/import/đối chiếu database + Content/cutover/phục hồi, ERD/từ điển/bản xuất/demo/máy in thật.

App/worker vẫn SQLite v8, MySQL v1; chưa wiring PermissionsViewModel/UI hoặc nhập thật. Chỉ Compile entry MySqlPermissionManagementService vào commit; định dạng csproj có sẵn giữ riêng.
