# MySQL 05b — Tra cứu nhật ký

Ngày 28/10/2026. Đối chiếu reference mục 5/32/33, Step8d_AuditLog và bảng bàn giao. Thêm MySqlAuditLogService.Search/Read và ForAudit theo Audit.View hiện hành, Admin-only kể cả dữ liệu gán nhầm Audit.View cho Staff/Manager. Không cần Reservation.View hoặc các quyền chi tiết nghiệp vụ.

Search kiểm tra quyền → validate bộ lọc → một clock UTC → truy vấn cùng RR read-only snapshot. Lọc ngày Việt Nam [đầu from, đầu ngày sau to), actor Staff/Guest/System, action/entity exact trim, AND, parameter hóa. Keyset AuditLogId giảm dần/beforeId, 1–200 dòng/trang, đọc tối đa pageSize+1 để tính HasMore/NextBeforeId. Bản ghi mới giữa hai lần mở trang không làm lặp các ID cũ. Read chi tiết kiểm tra quyền lại, không tin dữ liệu menu/DTO cũ.

Giữ DTO AuditLogPage/AuditLogEntry, nội dung đầy đủ/entity ID/actor/UserId nullable, nhãn Guest/System và tên nhân viên hiện hành qua LEFT JOIN; DTO cũ tách rời, không bị sửa theo lần đọc sau. Quyền/metadata/dữ liệu cùng snapshot. Không ghi audit khi xem, không maintenance/DDL/đổi trạng thái hoặc cache. Không thêm sửa/xóa nhật ký qua API/UI.

MySQL v1 ràng buộc timestamp UTC dạng O đủ 7 chữ số và +00:00: dùng so sánh chuỗi binary chính xác đến tick, không julianday/round về millisecond. Fixture thời gian được chuẩn hóa sang UTC O trước insert; không tuyên bố đã kiểm tra MySQL chấp nhận chuỗi legacy Z/offset/độ dài biến thiên vốn bị DDL từ chối. Nhánh SQLite vẫn giữ xử lý legacy hiện có; công cụ import sau này phải chuẩn hóa và đối chiếu ticks.

## Kiểm tra

Build VerifyAuditMySqlDebug/Release đạt. Verify-MySqlAuditLog Debug đạt exit 0 (%TEMP%/MusicBoxMySqlAuditDebug_20261028.log); Release đầu lỗi Connect Timeout ở TCP writer phụ trong clock hook (%TEMP%/MusicBoxMySqlAuditRelease_20261028.log), retry cùng assertion đạt exit 0 (%TEMP%/MusicBoxMySqlAuditReleaseRetry_20261028.log). Không đổi SSL/pooling/timeout/quyền để qua test. Giữ hai log riêng, không tính lượt đầu là đạt.

Verify-MySqlAuditLog dùng GUID database có guard cleanup/log lỗi trước cleanup. Có audit Customer.Create qua MySqlCustomerService thật; SQL fixture riêng cho mốc tick/ngày/actor/cursor và trigger từ chối mọi INSERT/UPDATE/DELETE AuditLog khi đọc. Clock hook sau snapshot quyền cho writer cập nhật tên/khóa user commit trước reader đọc bảng; reader giữ tên cũ, lần sau từ chối tài khoản bị khóa. Kiểm tra stamp/current role/logout/null, Staff/Manager gán nhầm Audit.View, trang rỗng/read-only list/filters exact AND/trang mới chèn/ID sai và schema version. Không dữ liệu thật hoặc Content.

Chạy: `powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File Tests/Verify-MySqlAuditLog.ps1 -BuildDirectory MusicBoxManagement.Wpf/bin/VerifyAuditMySqlDebug` (đổi Release để chạy bản Release).

App/worker/UI vẫn SQLite v8, MySQL v1; chưa wiring AuditLogViewModel/UI hoặc import/cutover. Chỉ Compile entry MySqlAuditLogService vào commit, định dạng csproj có sẵn giữ riêng. Tiếp báo cáo doanh thu MySQL trước, sau đó các báo cáo phòng/booking/món/Excel/history; giữ worker/provider/UI/import/Content/ERD/từ điển/demo/máy in thật.

Hồi quy Debug 11/11 bộ liên quan đạt: MySqlAuditLog mới + MySqlAuthentication/MySqlFoundation online và 8 SQLite/WPF (AuditLog/AuditLogUi/Authentication/PermissionMatrix/PermissionMatrixUi/Foundation/StaffReservations/AuthenticationUi). Summary 10 bộ hồi quy: %TEMP%/MusicBoxAuditRegression_9f0648aa3ae945b496809c6bc1e3469c/results.json. AuthenticationUi 142 ảnh tại %TEMP%/MusicBoxUi_dc5bf1035db24575b2ebed8b7a91f713; không sửa UI và không coi ảnh SQLite là UI đã nối MySQL. Runner có 44 SQLite/WPF + 24 MySQL, không tuyên bố đã chạy cả 68 bộ. Test chỉ GUID database, dọn sau khi thành công. Các ca legacy của SQLite được thay bằng fixture UTC O hợp lệ MySQL, không nới ràng buộc production.
