# Bước 5d.4 — lịch Khách trống/bận theo ngày

Ngày: 06/10/2026. WPF .NET Framework 4.7.2 + SQLite, schema **v6**. Đối chiếu mục 6–7/20/30/38–39/47 của Reference_Web_ProjectPlan_v1.3.md. Guest tại máy demo/quầy không cần tài khoản; không dùng CalendarService có thông tin khách nội bộ.

## Đã làm và cách dùng

Trong form Đặt phòng: chọn phòng/ngày/thời lượng → **Xem trống/bận theo ngày**. Không cần tên/SĐT. Cửa sổ riêng cố định lựa chọn đó; có 28 dòng bắt đầu 09:00–22:30 cách nhau 30 phút. Trạng thái của mỗi dòng là khả năng đặt **toàn bộ khoảng theo thời lượng**, không phải chỉ ô 30 phút: Có thể đặt, Bận, Đã qua, Giờ nghỉ, Không đủ ca. Ngày hôm nay..+30, thời lượng 60/90/120/180 và ca 09–12/13–23 giữ quy tắc đã chốt.

Chọn dòng trống → Chọn giờ này điền giờ vào form, không ghi booking/customer/log. Đóng chưa chọn giữ giờ cũ; đổi ngày/phòng/thời lượng ở form rồi mở lại. Tải lại lịch đọc snapshot mới và bỏ selection; khi phòng bị khóa hoặc lỗi đọc xóa các ô cũ, không giữ kết quả trống cũ. Ô bận/quá khứ/nghỉ/không đủ ca không cho chọn. Form đã gửi thành công bị khóa gửi lặp như trước.

## Dữ liệu public và transaction

PublicRoomDay chỉ có RoomCode/RoomName/Date/DurationMinutes/CheckedAt/Slots. PublicBookingSlot chỉ có Start/State và các nhãn/khả năng chọn; không có tên/SĐT/CustomerId/ID booking/session/creator/reason nội bộ hoặc chi tiết sử dụng. UI chỉ bind các DTO này, không tải đối tượng CalendarService rồi ẩn cột.

GuestCalendarService.Read kiểm tra phòng đang mở, ngày/thời lượng và lấy các ô bằng AvailabilityService.CheckReservationAt trong cùng read transaction/một mốc IClock, customerId=null. Tái dùng BookingHours và ScheduleRules; không SELECT khách hoặc yêu cầu login. Confirmed hết đúng +15 phút không giữ lịch dù worker chưa đổi NoShow. Read chỉ đọc, không ghi NoShow/audit. Bắt đầu đúng now bị chặn nếu Room Active; booking tương lai theo khoảng hold, không cấm mọi giờ do walk-in/quá giờ. Không lấy trạng thái Occupied hiện tại để tô kín ngày mai.

Đây là lịch **phòng**; chưa có SĐT nên không kiểm tra lịch riêng Customer. Màn hình nhắc rõ điều này và chưa giữ chỗ. Preview khi nhập SĐT và CreateGuest/CreateStaff vẫn kiểm tra cả Room/Customer trong transaction ghi. Có người đặt sau khi chọn giờ thì submit bị từ chối và rollback khách mới.

IBookingService thêm ReadDay để form dùng cố định route Guest/Staff. StaffBookingService đọc lại Reservation.Create trong cùng transaction với ReadAt; không cần Calendar.View cho lịch anonymous của form đặt hộ, không gọi Guest thay thế khi mất Create. GuestCalendarViewModel refresh mất quyền xóa ô/chọn, cho đóng; khi đóng trả dấu AccessDenied để form Staff xóa tên/SĐT/phòng và khóa thao tác như đường preview/submit hiện có.

## Kiểm tra

Build Debug/Release đạt. 15 bộ kiểm tra Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, Calendar, GuestCalendar, AuthenticationUi đạt trên dữ liệu tạm riêng, không sửa database người dùng.

Verify-GuestCalendar kiểm tra hợp đồng public bằng reflection (không thuộc tính ngoài whitelist), không cần user/không ghi NoShow/audit; 28 ô, UTC+7/ca/ngày/+30/thời lượng/cuối ca/quá khứ/nghỉ, hold và liền kề, grace trước/đúng +15 phút dù status vẫn Confirmed, Active đúng now, walk-in/quá giờ cho tương lai, ngày mai không bị Occupied, phòng khóa/mã không tồn tại. VM không cần tên/SĐT, không chọn bận, refresh slot vừa bị đặt; submit từ lịch cũ bị chặn và rollback khách mới, khóa phòng xóa ô và không lộ lý do; Staff logout bị chặn. StaffBooking bổ sung Create-only được đọc và mất Create/null/phiên bị thu hồi bị chặn, VM không fallback Guest và xóa ô.

AuthenticationUi dùng nút/modal/binding thật: chưa nhập tên/SĐT vẫn mở; 2 cột giờ/trạng thái không dữ liệu khách; ô bận/nghỉ không chọn, chọn ô trống điền giờ và không ghi dữ liệu; người khác đặt trước thì submit báo trùng/rollback; thời lượng 180 phút đổi điều kiện ca; ngày mai chỉ lịch ngày mai; phòng vừa khóa sau khi mở/tải lại xóa ô và không lộ lý do. Render **50 ảnh**, đã xem lịch public và trạng thái phòng bị khóa. Session fixture kiểm tra availability, chưa phải luồng check-in/walk-in service.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-GuestCalendar.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-StaffBooking.ps1
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1
```

## Làm tiếp

6a.1 service check-in từ Confirmed, Session.CheckIn, thực tế giờ nhận và kết thúc theo thời lượng booking, snapshot giá/phòng, idempotence và kiểm tra NoShow/check-in đồng thời đúng mục 11/38/41/55.4; rồi UI. Walk-in/gia hạn/gọi món/Guest Lookup Active/checkout/hóa đơn/report/lịch sử khách vẫn giữ scope. Ảnh AI chờ chọn sau, không tự seed/thay ảnh.
