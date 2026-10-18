# Bước 8e.3 — Booking theo trạng thái

Ngày 18/10/2026. Đối chiếu mục 31.1–31.2 của bản nghiệp vụ tham chiếu: đếm booking theo `Reservation.StartTime` trong kỳ, bao gồm hủy và không đến; Confirmed hết hạn được trình bày NoShow ngay khi worker chưa cập nhật.

## Nền tảng đọc báo cáo

`BookingStatusReportService.Read(actor, fromDate, toDate)` đọc trong một transaction cùng quyền hiện hành và một mốc `IClock`. Chỉ Admin hoặc Manager có `Report.View`; Staff bị chặn kể cả dữ liệu quyền gán nhầm. Không cần `Reservation.View`, Dashboard, quyền xem hóa đơn hay quản lý danh mục.

Hai ngày được hiểu là ngày Việt Nam UTC+7, từ đầu FromDate đến trước đầu ngày sau ToDate. Bỏ thành phần giờ của input. Không dùng CreatedAt, ngày thanh toán, ngày checkout hoặc giờ hiện tại để cắt kỳ. Khoảng đảo chiều và ngày cực biên không thể đổi sang UTC/đầu ngày sau bị từ chối; không áp giới hạn số ngày tùy ý.

SQL chỉ thu hẹp khoảng bằng `julianday` có biên an toàn một giây; quyết định thuộc kỳ dùng ticks chính xác của `DateTimeOffset` trong .NET. Chấp nhận timestamp có offset rõ ràng hoặc Z và số chữ số phần giây khác nhau, không suy đoán timestamp thiếu múi giờ. Timestamp không đọc được làm báo lỗi dữ liệu thay vì âm thầm bỏ qua. Nguồn không chuẩn nằm ngoài biên lọc SQL có thể không được kiểm tra; đây không phải công cụ rà soát chất lượng toàn database.

Kết quả luôn có đúng năm dòng theo thứ tự Confirmed, CheckedIn, Completed, Cancelled, NoShow, kể cả dòng có số lượng bằng 0. Nhãn tiếng Việt tương ứng Đã xác nhận, Đã nhận phòng, Đã hoàn tất, Đã hủy, Không đến. `TotalCount` bằng tổng năm dòng. DTO chỉ có tổng, trạng thái, nhãn, số lượng và metadata kỳ/thời điểm đọc; không có tên/SĐT/ID khách, booking hoặc phiên. Collection chỉ đọc và setter không public.

Confirmed có `now − StartTime >= 15 phút` được tính vào NoShow; trước đó một tick vẫn Confirmed. Bốn trạng thái khác giữ nguyên. Quy tắc báo cáo áp dụng ngay cả anomaly dữ liệu cũ Confirmed đã có phiên nguồn; không tự đổi phiên hoặc cập nhật booking để sửa anomaly. Không gọi worker, không ghi audit và không thay schema. Các giao dịch nghiệp vụ nguồn vẫn chịu quy tắc riêng về phiên nguồn/NoShow.

## Kiểm tra core

`Verify-BookingStatusReport.ps1` đạt trên cả `bin/VerifyBookingReportsReadsDebug` và `bin/VerifyBookingReportsReadsRelease` bằng database tạm riêng. Kiểm tra gồm:

- Nguồn thật qua CreateGuest/CreateStaff, CancelStaff, CheckIn, Checkout và NoShowService; cả năm trạng thái có số đếm đúng.
- Kỳ StartTime khác CreatedAt, ngày Việt Nam, biên đầu/ngày sau ±1 tick, legacy offset/Z, một clock, empty/zero rows và khoảng nhiều năm.
- Worker chậm, grace chính xác +15 phút, trạng thái kết thúc giữ nguyên và anomaly phiên nguồn; không ghi thay đổi/audit khi đọc.
- Manager chỉ có Report.View vẫn đọc được; Staff gán nhầm bị chặn; quyền thu hồi/stamp/khóa/đổi role/logout có hiệu lực. Snapshot hiện hành giữ nhất quán khi thu hồi quyền đồng thời, lần đọc kế tiếp bị chặn.
- DTO không PII/ID nguồn/setter public, collection bất biến; trigger ngăn ghi chứng minh báo cáo chỉ đọc; dữ liệu thời gian lỗi bị từ chối; schema giữ nguyên.

Phần giao diện và hồi quy tích hợp được bổ sung sau khi ghép nhánh. Báo cáo món/top món, Excel đúng bộ lọc, lịch sử khách và chuyển provider Aiven vẫn thuộc các bước tiếp theo; chưa coi toàn bộ mục 31 hoàn tất.

## Giao diện và cách dùng

Admin hoặc Manager có Report.View → **Báo cáo đặt phòng**. Chọn Từ/Đến ngày Việt Nam rồi bấm **Xem báo cáo**. Mặc định đầu tháng đến hôm nay; bảng luôn đủ năm trạng thái và tổng lượt, kể cả trạng thái không có dữ liệu. Bộ lọc dựa trên giờ bắt đầu đặt phòng, không phải ngày tạo hoặc PaidAt; nhãn ngay dưới bảng giải thích mốc này và NoShow hiệu lực khi worker chậm.

MainWindow chỉ hiện menu theo quyền và kiểm tra lại qua ForBookingReports trước mở. Service vẫn kiểm tra độc lập trong mỗi snapshot, không cần Reservation.View. Đổi ngày xóa bảng/tổng cũ; lỗi xóa kết quả, mất quyền/role/stamp xóa cả filter. Busy khóa bộ lọc/gửi lặp/đóng, không giữ transaction khi người dùng đọc màn hình. Window tối thiểu 960×640 và DatePicker dùng vi-VN; layout chờ WPF hoàn tất trước chụp kiểm tra.

Verify-BookingStatusReportUi đạt Debug và Release, 6 ảnh/build. Kiểm tra menu thật Guest/Staff/Manager/Admin, Manager chỉ Report.View không cần Reservation.View, Staff gán nhầm bị chặn; binding ngày, hiệu lực grace không ghi NoShow, đủ năm trạng thái và các dòng 0; empty/invalid/stale, quyền/role/stamp và busy/gửi lặp/đóng. Đã xem ảnh full/compact cuối ở 960×640: cả hai cột, năm dòng, filter/tổng/nút và giải thích đều hiện đủ. Helper ảnh chờ WPF render sau resize, lưu bản sao pixel cố định và đọc lại PNG từ đĩa để kiểm tra ảnh không trống.

## Chốt kiểm tra WPF ngày 18/10/2026

Build Debug/Release tích hợp và mặc định đều đạt. 35/35 bộ hồi quy SQLite trước bước này đạt trên VerifyBookingReportsReadsDebug; thêm BookingStatusReport và BookingStatusReportUi đạt Debug/Release, tổng 37 bộ SQLite/WPF. AuthenticationUi render 131 ảnh; UI mới 6 ảnh/build đã xem full/compact cuối. Các báo cáo doanh thu/phòng, ma trận quyền, nhật ký và các luồng vận hành vẫn đạt. InvoicePrinting chỉ xem trước, không gửi máy in. Không thay schema hoặc dữ liệu/ảnh thật.

Nhánh MySQL 4c.1: MySqlBookingReads bản kiểm tra cuối đạt online Debug/Release; MySqlReservations/MySqlAvailability/MySqlFoundation Debug đạt. Kiểm tra trên database GUID riêng và thư mục ảnh tạm; App vẫn SQLite, chưa port Guest lookup/hủy hoặc danh sách Staff booking. Không coi báo cáo này hay IBookingService MySQL là đã hoàn tất toàn bộ báo cáo/Excel/migration.
