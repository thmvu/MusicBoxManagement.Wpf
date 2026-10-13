# Bước 8a.2 — Dashboard trên màn chính và tài liệu luồng code

Ngày **13/10/2026**. Đối chiếu plan WPF bước 8, tham chiếu mục 5/29 và nền tảng [8a.1](Step8a1_DashboardFoundation.md). Giữ .NET Framework 4.7.2 + SQLite **v8**.

## Cách dùng

Đăng nhập → màn chính **Bảng điều hành** nếu có Dashboard.View. Trên cùng có số phòng/booking hôm nay/phiên đang dùng/đơn chờ, doanh thu cho Admin/Manager đủ quyền, giờ cập nhật. Bên dưới là cảnh báo và bốn tab **Phòng, Booking hôm nay, Phiên đang dùng, Đơn món chờ**.

**Làm mới** đọc lại dashboard. **Bảng điều hành** ở menu trái tải lại cả quyền/identity/menu và dữ liệu. Đóng các màn nghiệp vụ như nhận phòng, booking, gia hạn, đơn món, checkout sẽ tải lại màn chính. Thông tin phản ánh thời điểm cập nhật, không tự refresh mỗi phút; worker NoShow vẫn chạy riêng.

Người không có Dashboard.View thấy hướng dẫn chọn nghiệp vụ được phép. Dashboard không cấp thêm quyền cho các menu hoặc thao tác ghi. Nội dung hiện tại chỉ đọc; mở nghiệp vụ bằng menu trái.

## Đã sửa

- Thay bảng quyền kỹ thuật trên MainWindow bằng `DashboardView`; đổi nút “Kiểm tra lại quyền” thành “Bảng điều hành”. Chưa dựng ma trận quyền Admin ở bước này; engine RBAC giữ nguyên.
- `DashboardViewModel` nhận factory `StaffReservationService.ForDashboard`, gọi factory/Read trong Task.Run, map DTO thành dòng tiếng Việt qua State/AlertName/Local, bind collection và summary/revenue/time/status.
- Có busy guard, xóa dữ liệu ngay khi tải lại, version chống kết quả cũ áp dụng sau logout/chuyển actor, lỗi/quyền bị thu hồi xóa snapshot/collection/doanh thu. Report.View bị thu hồi chỉ ẩn doanh thu, vẫn đọc hoạt động khi còn Dashboard.View.
- MainWindow RefreshAccessAsync dùng **SemaphoreSlim** để các lần tải quyền chạy tuần tự. Lần tải sau nghiệp vụ không bị bỏ qua vì lần trước còn chạy; sau chờ lấy lại session hiện hành. Menu bị khóa trong lúc tải; logout vẫn xóa dữ liệu ngay.
- Khi chuyển Guest, Reset(null), ẩn dashboard và xóa identity. Kết quả Task cũ không đưa dữ liệu nhân viên trở lại.
- Bố cục nền trung tính, tab và danh sách có cuộn; không card bo góc/neon. Doanh thu và chuỗi dài có wrap. Không đọc trực tiếp Calendar nội bộ hoặc tính lại doanh thu ở UI; dùng đúng service 8a.1.
- Thêm [Code_Flow_Full_Guide.md](Code_Flow_Full_Guide.md): trình tự gọi hàm khởi động/đăng nhập, dashboard đi sâu từng hàm, 14 luồng nghiệp vụ chính, transaction/quyền/preview/async/binding, vị trí đặt breakpoint. Giữ tài liệu kiến trúc cũ để học khái niệm.

## Kiểm tra

- Build Debug/Release đạt tại bin/VerifyDebug và bin/VerifyRelease.
- **15 bộ kiểm tra liên quan đạt**: Dashboard, Calendar, Availability, Authentication, Checkout, Billing, Orders, Reservations, Sessions, WalkIn, CheckIn, Extension, GuestSessions, Foundation, AuthenticationUi.
- AuthenticationUi render **122 ảnh**: 116 ảnh cũ và 6 ảnh dashboard Admin/Staff/quyền bị thu hồi/giờ đóng cửa/lỗi/cửa sổ nhỏ. Tài nguyên ở `%TEMP%/MusicBoxUi_8a2_Final`; đã xem ảnh dashboard và màn chính/thanh điều hướng. Có lượt kiểm tra UI trên Release ở `%TEMP%/MusicBoxUi_8a2_Release`.
- Dashboard-only Staff không cần quyền Calendar/Session/Order/Invoice và không thấy doanh thu dù bị gán nhầm Report.View; đổi role thành Manager mới đọc doanh thu. Thu hồi Report chỉ ẩn tiền, thu hồi Dashboard xóa tất cả dữ liệu.
- Kiểm tra số liệu/5 cảnh báo từ fixture, tên trạng thái tiếng Việt, UTC+7, doanh thu 0 cho người có quyền, tab phòng khóa/Active, cảnh báo ngoài giờ, dữ liệu và nút ở cửa sổ nhỏ.
- Gate clock kiểm tra tải lặp chỉ gọi factory một lần; Reset/logout khi đang chờ không áp kết quả cũ, đổi actor không giữ doanh thu Admin, retry sau lỗi/reset vẫn hoạt động.
- Kiểm tra màn chính thật sau bootstrap/login, logout xóa snapshot, các menu CheckOut-only/Invoice.View-only còn đúng quyền. Regression cuối phát hiện lần tải quyền bị bỏ qua khi đang tải; chuyển sang SemaphoreSlim và chạy lại UI để kiểm chứng menu cập nhật đúng.
- Tests dùng DB/thư mục tạm. Không thay DB thật, schema hoặc luật nghiệp vụ; engine dashboard/read-only và các guard writer trước đó giữ nguyên.

## Tiếp theo

Bước dashboard đã có nền tảng và UI. Tiếp theo **nền tảng quản trị tài khoản nhân viên**: tạo/sửa/khóa, gán Staff/Manager/Admin, reset mật khẩu, bảo vệ Admin cuối cùng và audit; sau đó nối UI Admin. Giữ ma trận quyền/audit UI, báo cáo/Excel, lịch sử khách, dữ liệu demo và hoàn thiện đồ án. RBAC theo role, không thêm quyền riêng từng user hoặc chi điện/mặt bằng.
