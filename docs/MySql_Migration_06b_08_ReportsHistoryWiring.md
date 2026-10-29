# Báo cáo còn lại, lịch sử khách và nối WPF — 29/10/2026

Task này tiếp tục liên tục đến hết MySQL theo yêu cầu người dùng; các checkpoint không phải lời yêu cầu người dùng nhắn “tiếp tục”. Đọc MySql_Completion_Task và MySql_Migration_Status trước nhận việc.

## 06b–d và 07

- MySqlRoomUsageReportService: lượt Completed theo ActualEndTime; occupancy union actual intervals (Active đến now), giao ca 09–12/13–23, CreatedAt/kỳ/now, giữ lịch sử phòng khóa. Mẫu số lịch tiêu chuẩn, không giả lịch mở bán thực tế. Giữ công thức constant-time của SQLite.
- MySqlBookingStatusReportService: StartTime ngày Việt Nam, đủ 5 trạng thái; Confirmed hết đúng +15 phút chiếu NoShow, không ghi maintenance.
- MySqlServiceSalesReportService: chỉ Order Completed có Invoice, lọc PaidAt, tên/giá snapshot, nhóm ServiceId; Top10 theo quantity/revenue/ID, không gộp tên trùng. Bốn đường Excel phòng/booking/món/top món dùng cùng core query và ReportExcelWriter, View+Export; Staff luôn bị chặn Report.
- MySqlCustomerHistoryService: Customer.View riêng, CustomerId hiện hành/tên/SĐT hiện hành; booking trạng thái đã lưu, phiên actual/expected/snapshot/Completed count/LastUsedAt; invoice summaries chỉ khi có Invoice.View cùng snapshot. Không Guest route hoặc tự maintenance/ghi audit.
- Cả bốn dùng RR read-only/ReadAccess/một clock và DTO hiện có, không đổi luật SQLite. Truy vấn thời gian MySQL dùng UTC O canonical; import chuẩn hóa legacy trước khi nhập.

## 08 — Hợp đồng và wiring

ServiceContracts chứa hợp đồng của các service đang có hai provider và consumer WPF thực tế; không thêm DI framework. SQLite/MySQL implements cùng hợp đồng; VM/Views đổi kiểu nhận dependency, giữ logic/binding/layout/palette. Factory giữ các kiểm tra quyền cũ và dùng explicit interface bridges để vẫn giữ public API cụ thể cho caller/test cũ. IBookingService/IInvoiceService có sẵn tiếp tục dùng.

ApplicationServices tạo MainWindow và worker từ **một provider** trong suốt phiên chạy. MySQL nối đủ danh mục/Guest/Staff/factory/report/history; storageRoot truyền xuyên suốt phòng/catalog/đặt hộ. NoShowWorker có overload MySqlDatabase, cùng timer/duplicate/dispose/retry nhịp sau; không initialize schema mỗi tick MySQL. App startup lỗi nguồn đã chọn báo lỗi, không tự fallback ghi sang SQLite.

Cấu hình App.config DatabaseProvider (SQLite hoặc MySql), MySqlDatabase. Biến process/user kế thừa MUSICBOX_PROVIDER/MUSICBOX_MYSQL_DATABASE/MUSICBOX_SQLITE_PATH/MUSICBOX_STORAGE_ROOT dùng để chọn rõ nguồn khi diễn tập/khôi phục. Không lưu mật khẩu vào config; AivenDatabase vẫn đọc Windows User secret/certificate như trước. Ảnh vẫn local, không suy rằng MySQL cloud tự upload Content. MySQL root mặc định `%LOCALAPPDATA%/MusicBoxManagement.Wpf.MySql`, tách khỏi backup bất biến và SQLite nguồn.

Checkpoint wiring giữ SQLite; sau backup/rehearsal/import/verify, App.config đã chuyển mặc định MySql. Xem [cutover và dữ liệu thực](MySql_Migration_09_Import_Cutover.md), không dùng fixture GUID thay nghiệm thu import.

## Bằng chứng và giới hạn

- Build VerifyWiringMySqlDebug/Release đạt; runner toàn bộ Debug (44 local + 26 MySQL lúc bắt đầu) đã đạt 70/70, được ghi riêng trong `%TEMP%/MusicBoxFullWiringDebug_20261029.log`. Summary cuối: `%TEMP%/MusicBoxRegression_b2fa8ba726ff47279fb493ab97ee939b/results.json`; cả 70 exit 0.
- 44/44 SQLite/WPF Release đạt trên VerifyCompletionFinalRelease, summary `%TEMP%/MusicBoxRegression_52edb3e1ec074c3ea19fa8cecfba55e4/results.json`.
- RemainingReads bản đầu Debug/Release đạt, dùng walk-in/Order/checkout/booking thật, kiểm tra room CreatedAt/ca/history khóa phòng, identity/current phone/Invoice boundary, effective booking status/grace không ghi, món snapshot/Active exclusion và bốn Excel exports. Bản cuối 12 món/Top10/tên trùng/Service filter Debug/Release đạt; log ở cutover report.
- OperationsUi Debug/Release đạt qua ApplicationServices thật: Guest room type/photo, đăng nhập MainWindow bằng cửa sổ thật, Dashboard Invoice MySQL, VM+cửa sổ Revenue/Room/CustomerHistory và revoke xóa PII. Bản cuối có worker NoShow MySQL xử lý booking thật hết grace. 5 ảnh/build trong TEMP; không máy in thật, không dữ liệu thật, không tuyên bố mọi tổ hợp UI đã được thử.
- Import/Content/backup/chuẩn hóa/dữ liệu thực và kết quả runner cuối nằm ở báo cáo riêng; giữ ERD/từ điển/demo/rollback trong scope.
