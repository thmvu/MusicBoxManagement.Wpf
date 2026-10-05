# Bước 5c.2b — form nhân viên đặt hộ

Ngày: 05/10/2026. WPF .NET Framework 4.7.2 + SQLite. Đối chiếu mục 8–10/21/38–39/47/55 của `Reference_Web_ProjectPlan_v1.3.md`. Schema giữ **v6**; giữ quy tắc giờ/khách/overlap/Confirmed/NoShow và phạm vi calendar/vận hành.

## Đã làm và cách hoạt động

- Màn hình chính có **Đặt hộ** khi có Reservation.Create. Đây là quyền độc lập với Reservation.View và Customer CRUD: tài khoản chỉ có Create vẫn mở được form, không mở danh sách booking nội bộ. Guest không có nút này.
- Trong **Booking**, nút **Đặt hộ** cần View để mở danh sách và Create để tạo. Tài khoản chỉ xem vẫn đọc/lọc, nút tạo bị khóa. Khi đang xác nhận hủy hoặc đang bận cũng không mở lượt tạo mới.
- Dùng chung form phòng đang mở/ảnh/ngày/slot/thời lượng/tên/SĐT với Guest, tiêu đề **Đặt hộ khách hàng**. Thêm IBookingService để truyền cố định đường Guest hoặc Staff vào form; cờ staffMode chỉ đổi chữ hiển thị, không cấp quyền hoặc quyết định đường ghi dữ liệu. GuestBookingViewModel/GuestBookingWindow giữ tên lớp cũ để tránh đổi rộng phạm vi.
- Preview chưa giữ chỗ. Submit gọi CreateStaff: Confirmed ngay, không thu cọc, ghi CreatedByUserId thực và audit ActorType Staff. Giữ tên khách cũ theo SĐT chuẩn hóa; khách mới/booking/log cùng transaction.
- Thành công hiện mã/phòng/giờ/hạn nhận trước StartTime+15 phút và khóa gửi lặp. Đặt lượt mới xóa tên/SĐT và kết quả preview. LastCreatedReservationId giữ mã lượt đã lưu gần nhất khi mở lượt mới rồi đóng nháp, để danh sách chọn đúng booking đã commit.
- Mất quyền hoặc phiên không còn hiệu lực khi tải/preview/lưu: xóa phòng/lựa chọn/tên/SĐT, khóa nhập/đặt mới, vẫn cho đóng. Đường Staff không chuyển sang Guest. Khi đã lưu thành công, đóng form giữ booking; đóng nháp chưa lưu không ghi dữ liệu.
- Mở từ danh sách rồi đóng: chưa tạo thì tải lại với bộ lọc cũ; đã tạo thì bỏ bộ lọc ngày/SĐT/trạng thái cũ, tải lại và chọn dòng vừa lưu. Lỗi tải lại/mất View sau commit vẫn thông báo **đã đặt hộ**, không báo tạo thất bại hoặc tự gửi lại.

## Service và transaction

`StaffReservationService.ForBooking(session)` kiểm tra Reservation.Create và trả StaffBookingService gắn cố định session/IClock. MainWindow kiểm tra session vẫn như lúc yêu cầu trước khi mở form. Các nút chỉ hỗ trợ UI, quyền thực vẫn kiểm tra ở service.

`StaffBookingService.ListRooms` đọc quyền Create và danh sách phòng mở trong cùng read transaction. `Preview` kiểm tra quyền trước xử lý NoShow, chuẩn hóa SĐT, sau bảo trì đọc lại quyền/khách/availability trong read transaction. Tái dùng ReadRooms/PreviewAt của GuestBookingService để giữ một nguồn metadata công khai và quy tắc lịch; không dùng Room.Manage hoặc Customer.Search.

`Create` gọi ReservationService.CreateStaff đã có ở 5b.1: BeginWriteTransaction → quyền Create hiện hành → chuẩn hóa/tìm-tạo khách → NoShow/giờ/phòng mở/overlap phòng và khách với một mốc IClock → INSERT Confirmed/Creator → audit Staff → commit. Quyền/phòng/lịch thay đổi sau preview vẫn bị chặn lúc ghi; lỗi rollback toàn bộ lượt tạo. Không đổi schema, cách lưu ảnh hoặc quy tắc hủy.

## Kiểm tra

Build Debug/Release đạt. Chạy 13 bộ kiểm tra: Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, AuthenticationUi. Tất cả dùng database/tài khoản/thư mục ảnh tạm riêng, không sửa database người dùng.

StaffBooking kiểm tra Create-only không cần View/Customer CRUD; null/logout/mất quyền; catalog/preview bảo vệ và không tạo booking/khách mới; SĐT chuẩn hóa/overlap khách; creator/audit Staff và giữ tên cũ; trùng lịch/khóa phòng sau preview; rollback khách/booking khi audit lỗi; ViewModel gửi lặp/đặt mới/xóa thông tin khi mất quyền/đóng được; parent tải lại/chọn ID/bỏ lọc cũ và báo đã tạo khi mất View sau commit. Các kiểm tra cạnh tranh writer đã có của Reservations tiếp tục đạt.

AuthenticationUi chạy binding/event handler WPF thật trên database tạm: Guest ẩn Đặt hộ; MainWindow tài khoản chỉ Create vẫn mở form khi không có View; chỉ View khóa nút tạo; form Staff ảnh/thông tin/SĐT sai/preview/trùng/creator/Confirmed/gửi lặp/đặt mới/mất quyền; mở modal từ Booking, lưu và chọn dòng mới. Render **36 ảnh**, đã xem trực quan form xác nhận, form mất quyền và danh sách chọn booking vừa tạo.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-StaffBooking.ps1
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1
```

## Bước kế tiếp

5d.1 nền tảng lịch/trạng thái phòng theo thời điểm, sau đó UI calendar Ngày và Tuần từng bước nhỏ; bản cuối phải đủ cả hai theo mục 30. Check-in/walk-in/gia hạn/gọi món/checkout/hóa đơn, Customer history/report và Guest lookup phiên Active vẫn giữ trong scope. Chưa hoàn tất đồ án. Các ảnh AI trong assets/rooms chờ người dùng chọn/áp dụng sau.
