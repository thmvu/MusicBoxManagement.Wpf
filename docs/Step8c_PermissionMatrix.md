# Bước 8c — Ma trận quyền Staff/Manager

## Nền tảng nghiệp vụ

Đối chiếu mục 5, 32 và 55 của bản tham chiếu cùng plan WPF: Admin bật/tắt quyền của từng vai trò Staff hoặc Manager. Mọi tài khoản thuộc vai trò đó dùng quyền mới ở thao tác tiếp theo vì PermissionService luôn đọc database hiện hành. Manager không tự kế thừa các thay đổi của Staff. Không có quyền riêng từng tài khoản.

Admin luôn toàn quyền cố định; không có đường ghi ma trận Admin. Staff không được quyền báo cáo; User.Manage, Permission.Manage và Audit.View chỉ dành cho Admin. Invoice.Print cần Invoice.View, Report.Export cần Report.View. Cấp quyền danh mục cho Staff được phép, không tự cấp quyền khác.

`PermissionManagementService.Read(actor, role)` kiểm tra Permission.Manage hiện hành và đọc trong một read transaction. DTO `RolePermissionMatrix` chỉ đọc gồm Role, Revision và danh sách PermissionCodes có hiệu lực. Revision là SHA-256 của toàn bộ mã quyền đang lưu, kể cả grant sai bị PermissionService che đi. Nhờ vậy, hai form không thể âm thầm ghi đè khi grant sai thay đổi giữa lần đọc và lưu.

`Update(actor, original, selectedCodes)` lấy writer trước rồi kiểm tra lại tài khoản, stamp và Permission.Manage trong chính transaction. Chỉ Staff/Manager được sửa. Mã lạ, null, quyền cấm hoặc thiếu dependency bị từ chối; mã trùng được gộp, không tự trim/đổi hoa thường. `ValidateSelection` là helper thuần kiểm tra cùng quy tắc cho màn hình xác nhận, không đọc/ghi database.

Lưu thay toàn bộ grant của đúng vai trò và ghi AuditLog `Permission.Update`/`RolePermission` (vai trò, danh sách trước/sau) cùng commit. Lỗi audit rollback cả ma trận. Không thay schema (SQLite v8), không sửa role của user, không làm mất phiên hay đổi SecurityStamp khi sửa ma trận. No-op không ghi audit. Lưu lại DTO đã che grant sai sẽ dọn các grant sai tồn tại trước đó và có ghi audit.

## Kiểm tra nền tảng

`Tests/Verify-PermissionMatrix.ps1` dùng database GUID trong thư mục tạm, không chạm database đang dùng. Bộ kiểm tra bao gồm Admin-only hiện hành, cố định Admin, grant/revoke có hiệu lực với phiên đã đăng nhập, độc lập Staff/Manager, dependency, mã sai/null, no-op/gộp trùng, stale, grant legacy sai/cleanup, rollback khi audit lỗi, hai service writer cùng bản gốc và tài khoản bị khóa khi writer đang chờ.

Build Debug/Release đã đạt ở `bin/VerifyParallelDebug` và `bin/VerifyParallelRelease`. `Verify-PermissionMatrix.ps1` đã chạy đạt cả hai build ngày 18/10/2026, bao gồm rollback, cạnh tranh stale và kiểm tra tài khoản sau khi lấy writer. Đối chiếu lại PermissionService xác nhận cùng giới hạn Staff/Manager/Admin và dependencies; không cần sửa đường đọc quyền hiện hành.

Aiven/MySQL tiếp tục được chuyển riêng theo plan; bước này phục vụ ứng dụng WPF đang dùng SQLite, chưa thay provider của app. Nghiệm thu UI và kết quả hồi quy chung được ghi ở phần tiếp theo.

## Giao diện WPF đã nối

Ngày 18/10/2026. Đăng nhập Admin → **Ma trận quyền** ở sidebar → chọn Staff hoặc Manager → Làm mới → chọn/bỏ quyền → **Kiểm tra / Lưu** → **Xác nhận lưu**. **Quay lại** giữ form để sửa tiếp và chưa ghi dữ liệu. Không có lựa chọn sửa Admin. Những quyền bị cấm hiện giới hạn rõ ràng, không cho bật checkbox.

`StaffReservationService.ForPermissions` kiểm tra Permission.Manage trước khi mở. MainWindow chặn mở trùng và refresh quyền/dashboard sau khi đóng. Menu ẩn với Guest/Staff/Manager. `PermissionsViewModel` tải/ghi qua Task.Run, khóa thao tác khi bận, giữ bản gốc và bản chọn đã xác nhận riêng. Sửa checkbox sẽ bỏ xác nhận cũ; đổi vai trò xóa bảng/xác nhận và yêu cầu Làm mới.

Prepare đọc lại quyền và Revision, dùng cùng ValidateSelection với writer; thiếu dependency báo lỗi để sửa lại form. Stale/mất quyền/lỗi đọc xóa ma trận cũ và xác nhận. Confirm vẫn kiểm tra lại trong service, chặn click lặp/đóng khi bận. Nếu commit thành công nhưng reload lỗi thì vẫn báo **Đã lưu**, không gửi lại thao tác. Không đổi user/role hoặc stamp khi lưu ma trận: nhân viên giữ phiên và được kiểm tra tập quyền mới tại thao tác tiếp theo.

`PermissionsWindow` có bảng cuộn, hai vai trò cố định, nội dung tiếng Việt, màu trung tính và khu xác nhận nằm dưới bảng; nút xác nhận/đóng nhìn thấy ở cửa sổ tối thiểu 960×640. Chưa có màn hình xem nhật ký trong bước này; audit của các thay đổi được ghi vào bảng hiện có.

## Kiểm tra UI và build cuối

- Build cuối Debug/Release đạt tại bin/VerifyParallelFinalDebug và bin/VerifyParallelFinalRelease; thay đổi cuối chỉ làm danh sách Roles chỉ đọc, các service không đổi so với build tích hợp đầu.
- Verify-PermissionMatrixUi đạt cả hai build cuối, **8 ảnh mỗi build**. Có kiểm tra checkbox WPF thật, menu Guest/Staff/Manager ẩn/Admin mở được qua sidebar thật; read/save/confirm/keep/dependency/đổi role/stale/revoked/rollback/lỗi refresh sau commit/busy/duplicate/chặn đóng và cửa sổ tối thiểu.
- Đã xem ảnh full/compact-confirm/menu route: chữ và nút hiển thị rõ, bảng cuộn được. Chỉ dùng database và dữ liệu tạm.
- Verify-PermissionMatrix nền tảng đã đạt Debug/Release trên build tích hợp như phần trên. Hồi quy chung được chốt sau khi hoàn tất.

## Hai nhánh làm tiếp

Theo yêu cầu người dùng ngày 18/10, tiếp tục song song: **WPF: màn hình nhật ký theo Audit.View**, sau đó báo cáo/Excel và lịch sử khách; **Aiven: 3d khách hàng/SĐT**, rồi các service nghiệp vụ. Ma trận quyền mới cũng cần đường MySQL trước khi chuyển provider của App, không coi 2a/2b đã tự bao gồm writer mới này. Giữ kế hoạch sao lưu/nhập/đối chiếu dữ liệu và ảnh, kiểm tra UI riêng trước chuyển nguồn vận hành.

## Kết quả chốt hai nhánh

- **27/27 bộ hồi quy SQLite cũ đạt**, gồm Authentication/Accounts/RoomTypes/Rooms/Services/Customers/Availability/Reservations/GuestBooking/GuestLookup/StaffReservations/StaffBooking/Calendar/GuestCalendar/CheckIn/WalkIn/Extension/Sessions/GuestSessions/Orders/Billing/Checkout/Invoices/InvoicePrinting/Dashboard/Foundation/AuthenticationUi.
- **Hai bộ mới PermissionMatrix và PermissionMatrixUi đều đạt Debug/Release**. AuthenticationUi cũ render 131 màn hình; UI ma trận mới render 8 ảnh mỗi build. Các ảnh full/compact/menu route đã được kiểm tra trực quan.
- Build mặc định **bin/Debug** và **bin/Release** đạt; mở bản build mới hoặc F5 để dùng menu Ma trận quyền. Không mở app thật hoặc chỉnh quyền người dùng thật trong quá trình kiểm tra.
- Nhánh Aiven: MySqlServices đạt online Debug/Release và MySqlFoundation Debug đạt, dùng GUID DB riêng. Không đổi provider/schema/dữ liệu thật; ảnh còn local. Source chuẩn của service không đổi sau build tích hợp; thay đổi cuối UI chỉ làm Roles chỉ đọc.
