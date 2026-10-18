# Bước 8e.1 — Báo cáo doanh thu theo ngày, tháng và loại phòng

Ngày thực hiện: 18/10/2026. Đây là phần đầu của báo cáo tại mục 31 tài liệu nghiệp vụ; chưa hoàn tất toàn bộ báo cáo/Excel.

## Đã làm ở nền tảng

`ReportService.ReadRevenue(actor, fromDate, toDate, roomTypeCode)` trả cùng một bộ kết quả gồm tổng số hóa đơn, tiền phòng, tiền dịch vụ, tổng thanh toán và ba cách nhóm: ngày, tháng, loại phòng. `RevenueReport`/`RevenueReportRow` không có thông tin khách hàng, tài khoản thanh toán hoặc danh sách hóa đơn; danh sách chỉ đọc và setter không public. Nhãn tiền dùng `decimal`, định dạng nguyên đồng `N0` theo `vi-VN`.

Ngày bắt đầu/kết thúc là ngày Việt Nam UTC+7, bỏ thành phần giờ của bộ lọc. Khoảng thực sự là `[00:00 ngày bắt đầu, 00:00 ngày sau ngày kết thúc)`. Ngày bắt đầu sau ngày kết thúc hoặc đầu/cuối miền thời gian không thể quy đổi an toàn bị từ chối; không tự giới hạn số ngày hoặc cắt khoảng người dùng chọn. Loại phòng cho phép tất cả, Standard hoặc VIP; mã được trim và chuẩn hóa hoa. Không cần lọc đúng giờ thanh toán để dùng màn hình.

Chỉ lấy các `Invoices` đã chốt, phân kỳ theo **PaidAt**, tiền từ `RoomCharge`, `ServiceCharge`, `TotalAmount`. Không cộng tiền tạm tính của phiên Active, không tính lại từ bảng giá hoặc món hiện hành. Nhóm/lọc loại phòng dựa trên `RoomSessions.RoomTypeCodeSnapshot`; đổi loại phòng, giá, tên hoặc khóa phòng sau thanh toán không phân loại lại lịch sử. Nhãn Standard/VIP là hai mã loại cố định; không lấy tên danh mục hiện tại để thay snapshot lịch sử.

Mỗi hóa đơn được đọc một lần rồi cộng bằng `decimal` trong .NET cho tổng và ba nhóm. SQLite lưu tiền dạng TEXT; không dùng `SUM`/ép sang REAL hoặc Int64. Tổng vượt miền `decimal` báo lỗi rõ ràng, không trả tổng thiếu hoặc làm tròn âm thầm. Các nhóm sắp theo khóa ngày/tháng tăng dần; loại Standard trước VIP. Chỉ trả nhóm có hóa đơn, vì vậy khoảng không có thanh toán trả tổng bằng 0 và ba danh sách rỗng.

## Quyền và tính nhất quán

Cần quyền `Report.View` hiện hành của Admin hoặc Manager, độc lập với `Invoice.View`, `Dashboard.View` và các quyền nghiệp vụ khác. Staff luôn bị chặn dù dữ liệu RolePermission bị gán nhầm Report.View/Export. Kiểm tra user active, SecurityStamp, role và permission qua `PermissionService` trong chính read transaction lấy doanh thu. Không tin role hoặc quyền cache từ phiên/UI. Thu hồi Report.View áp dụng ở lần đọc kế tiếp.

Một read transaction ghim quyền và toàn bộ nguồn hóa đơn/phiên, một mốc `IClock` trả `CheckedAt` UTC. Bộ lọc ngày tính bằng UTC+7 cố định, không phụ thuộc timezone máy hoặc offset của clock. SQL dùng `julianday` để khoanh vùng rộng hơn một giây ở hai đầu, sau đó .NET parse timestamp và kiểm tra ticks chính xác. Cách này tránh mất tick sát nửa đêm do độ chính xác của SQLite, đồng thời đọc được cách viết UTC/offset hoặc số chữ số phần giây cũ nếu có trong dữ liệu nhập; dữ liệu mới vẫn tuân schema UTC bảy chữ số.

API chỉ đọc, không tự NoShow/checkout, không ghi log cho thao tác xem báo cáo và không đổi schema. SQLite vẫn schema v8. Không thêm bảng Report, không tính chi điện/thuê mặt bằng và không thay App sang provider Aiven trong bước này.

## Kiểm tra nền tảng

Build Debug/Release tại `bin/VerifyRevenueAvailabilityDebug` và `bin/VerifyRevenueAvailabilityRelease` đã có source báo cáo. `Tests/Verify-RevenueReport.ps1` chạy đạt trên **cả hai build** bằng database tạm riêng, gồm:

- Tạo walk-in, Completed Order và checkout bằng service thật: doanh thu vào đúng ngày PaidAt; tiền phòng/dịch vụ/tổng giữ nguyên `decimal` khi vượt Int64; phiên Active có món chưa thanh toán không bị cộng.
- Gom ngày, tháng, Standard/VIP; nhiều tháng đúng thứ tự; RoomType filter theo snapshot khi phòng hiện tại đã đổi loại/khóa, bảng giá và món đã sửa/ngừng bán.
- Đúng đầu ngày, đúng tick cuối kỳ và loại thời điểm đầu ngày kế tiếp; kiểm tra dữ liệu fixture kiểu timestamp offset cũ bằng cách chỉ nới CHECK trong connection tạo fixture tạm, không sửa DB thật hoặc schema của app.
- Manager chỉ có Report.View vẫn dùng được; Guest/null, Staff gán nhầm, Manager mất quyền, đổi role/stamp/khóa hoặc logout bị chặn.
- Một clock; read snapshot giữ cùng dữ liệu/quyền khi writer khác đang thu hồi quyền; lần đọc tiếp theo bị từ chối. Trigger trên các bảng nghiệp vụ/audit đảm bảo báo cáo không ghi dữ liệu.
- DTO không PII, danh sách không thể sửa, ngày/loại phòng sai bị từ chối và cộng vượt miền decimal có lỗi rõ ràng.

Script kiểm tra chỉ tạo/dọn thư mục GUID trong thư mục tạm; không đọc/ghi đè database quán hoặc gửi dữ liệu đến Aiven.

## Phần còn lại trong plan

Phần UI của bước này được ghi bên dưới sau tích hợp. Số lượt sử dụng từng phòng, tỷ lệ sử dụng theo lịch mở cửa tiêu chuẩn, Booking theo status, doanh thu/top Service và xuất `.xlsx` cùng query/bộ lọc vẫn là scope bắt buộc các bước tiếp theo. Chưa gọi phần doanh thu này là hoàn tất mục 31. Tính tổng Active vẫn thuộc tiền tạm tính nghiệp vụ, không phải báo cáo doanh thu đã thu.

## Giao diện và cách dùng

Đăng nhập Admin hoặc Manager có Report.View → **Báo cáo doanh thu**. Mặc định đầu tháng đến hôm nay (ngày Việt Nam); chọn Từ/Đến ngày, loại phòng hoặc Tất cả, nhóm Theo ngày/Theo tháng/Theo loại phòng rồi bấm **Xem báo cáo**. Kết quả gồm số hóa đơn, tiền phòng, dịch vụ và tổng doanh thu; nhóm không có hóa đơn không tạo hàng 0 giả. Tổng phía trên luôn theo đúng kỳ/loại phòng đã tải.

Đổi một bộ lọc hoặc kiểu nhóm xóa bảng/tổng cũ và yêu cầu tải lại. Service đọc lại quyền hiện hành ở mỗi lần đọc; mất quyền/role/stamp xóa kết quả và filter. Menu mở qua ForReports, không đòi Dashboard.View/Invoice.View. Busy khóa bộ lọc/gửi lặp/đóng, không giữ transaction qua thao tác người dùng. Không ghi audit cho việc xem báo cáo.

RevenueReportWindow tối thiểu 960×640, màu trung tính, tổng có thể xuống dòng, bảng có cuộn ngang khi cần. Số tiền N0 theo vi-VN, tính bằng decimal chứ không chuyển double hoặc cộng TEXT trong SQLite.

Verify-RevenueReportUi đạt Debug và Release, 8 ảnh/build, đã xem full/compact. Gồm menu thật Admin/Manager/Staff/Guest, Manager chỉ Report.View, hóa đơn tạo qua walk-in/gọi món/checkout thật với tiền vượt Int64, binding bộ lọc/ngày/nhóm, empty/invalid/stale, quyền/role/stamp và busy. Các báo cáo khác và Excel ở mục 31 vẫn bắt buộc ở bước tiếp theo; không coi bước này là hoàn tất toàn bộ báo cáo.

## Chốt kiểm tra chung ngày 18/10/2026

Build Debug/Release tích hợp và mặc định đều đạt. 31/31 bộ hồi quy SQLite trước bước này đạt trên VerifyRevenueAvailabilityDebug; thêm RevenueReport và RevenueReportUi đạt cả Debug/Release, tổng 33 bộ SQLite/WPF. AuthenticationUi render 131 ảnh, UI báo cáo mới 8 ảnh/build, ma trận quyền và nhật ký cũ vẫn đạt. InvoicePrinting chỉ xem trước, không gửi máy in.

Nhánh Aiven MySqlAvailability đạt online Debug/Release ở bản test cuối (gồm rollback/clock sau khóa), MySqlFoundation online Debug đạt. Các kiểm tra dùng DB/thư mục tạm, không thay dữ liệu hoặc ảnh thật. App vẫn SQLite; chưa có Excel hoặc các báo cáo còn lại và chưa đổi provider.
