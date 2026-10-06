# Bước 6a.1 — service nhận phòng từ booking

Ngày: 06/10/2026. WPF .NET Framework 4.7.2 + SQLite, schema **v6**. Đối chiếu mục 11/17/35/38/41/55.4 của Reference_Web_ProjectPlan_v1.3.md; dùng transaction ghi SQLite đã chốt tại Wpf_Authentication_Transactions.md.

## Đã làm và cách hoạt động

Có `RoomSessionService.CheckIn(LoginSession actor, int reservationId)` trả về `RoomSession`. Đây là đường nhân viên theo quyền **Session.CheckIn** hiện hành, độc lập Reservation.View/Create và Customer CRUD. Chưa gắn nút trên giao diện; 6a.2 sẽ thêm thao tác nhận ở chi tiết booking.

Service lấy quyền và dữ liệu mới trong BeginWriteTransaction trước khi quyết định. Booking phải Confirmed; `now < StartTime + 15 phút`, đúng mốc 15 phút đã hết hạn. Phòng phải mở; cả Room và Customer đều không có phiên Active, kể cả walk-in hoặc phiên quá giờ.

Giờ bắt đầu thực tế là một mốc IClock lấy **sau khi chiếm quyền ghi**, lưu UTC giữ nguyên giây/ticks. ExpectedEndTime = now + (Reservation.EndTime − Reservation.StartTime). Không dùng ValidateReservation, không yêu cầu slot 30 phút, không giới hạn nhận sớm 30 phút. Booking 20–22 nhận lúc 19:37:12 thì dự kiến trả 21:37:12; nhận 20:10 thì trả 22:10 nếu đủ điều kiện.

BookingHours.ValidateSessionInterval kiểm tra toàn bộ khoảng thực tế dự kiến trong một ca 09–12 hoặc 13–23, cho phép kết thúc đúng 12/23. AvailabilityService.CheckCheckInAt kiểm tra lịch Room **và** Customer qua ScheduleRules dùng chung; bỏ chính Reservation nguồn. Confirmed hết grace không giữ lịch dù worker chậm, CheckedIn không chặn thêm ngoài session. Walk-in/quá giờ vẫn bị chặn bởi kiểm tra Active trực tiếp. Nhận muộn đụng lượt sau hoặc qua giờ nghỉ bị từ chối; không cắt thời lượng hoặc đẩy booking khác.

## Snapshot, transaction và nhận lặp

Trong cùng transaction: tạo RoomSession Active, chốt HourlyRate/RoomCodeSnapshot/RoomTypeCodeSnapshot/RoomTypeNameSnapshot theo danh mục **lúc nhận**, cập nhật Reservation CheckedIn và ghi Session.CheckIn/AuditLog với actor Staff, đúng UserId/mốc giờ. Reservation giữ nguyên StartTime/EndTime. Sửa danh mục sau đó không đổi snapshot phiên.

Booking đã có session trả lại đúng phiên đó, kể cả phiên đã Completed; vẫn kiểm tra quyền hiện hành trước khi trả. Không tạo phiên/log thứ hai hoặc cập nhật lại giá/giờ. Unique ReservationId và unique Active Room/Customer hiện có tiếp tục bảo vệ dữ liệu.

Check-in không tự ghi NoShow khi bị từ chối do hết hạn: thất bại giữ dữ liệu nguyên trạng để worker xử lý theo luồng riêng. Check-in và NoShow cùng BeginWriteTransaction, đọc lại nguồn sau khi chiếm quyền ghi; NoShow chỉ cập nhật Confirmed chưa có session. Vì vậy check-in thắng thì worker không đổi thành NoShow, worker thắng thì check-in bị từ chối. Không có retry vô hạn hoặc tự chuyển walk-in.

Bất kỳ lỗi ghi session/status/audit nào đều rollback cả nghiệp vụ. Schema vẫn v6, không migration và không sửa database đang dùng.

## Kiểm tra

Build Debug/Release đạt. **16 bộ kiểm tra** Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, Calendar, GuestCalendar, CheckIn, AuthenticationUi đạt trên database tạm. UI cũ render 50 ảnh; bước này không thay UI.

Verify-CheckIn kiểm tra:

- Chỉ Session.CheckIn vẫn nhận được, null/mất quyền/tài khoản khóa/đăng xuất bị chặn, kể cả nhận lặp; booking không tồn tại/trạng thái không hợp lệ bị từ chối.
- Giờ UTC/giây/ticks và đủ thời lượng, nhận sớm hơn 30 phút, nhận muộn hợp lệ; trước/đúng grace; ca/nghỉ/qua cuối ca/kết thúc đúng 12.
- Room/Customer trùng booking tương lai, walk-in Active, phòng khóa; bỏ nguồn và bỏ Confirmed quá hạn; không dịch hoặc sửa giờ booking sau.
- Giá/tên/loại hiện hành khi nhận, snapshot sau đổi danh mục và replay Active/Completed; lịch nội bộ chỉ còn một hold theo session thực tế dự kiến.
- Lỗi audit và lỗi cập nhật status rollback session/booking/log.
- Hai lần nhận cùng booking trả một session/log; hai booking cạnh tranh cùng Room hoặc Customer chỉ một phiên Active.
- Ép cả hai thứ tự writer NoShow/check-in; chờ writer qua mốc grace dùng giờ mới sau lock, một lần đọc clock. Nhận cạnh tranh hủy chỉ một bên thành công; khóa phòng không vượt qua Confirmed còn hạn hoặc Active.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-CheckIn.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Availability.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Reservations.ps1
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1
```

## Làm tiếp

6a.2 UI nhận phòng trên chi tiết booking theo Session.CheckIn, xác nhận thời gian/giá, trả kết quả phiên và tải lại danh sách; mất quyền hoặc dữ liệu thay đổi phải được xử lý rõ. Walk-in/gia hạn/gọi món/Guest Lookup Active/tiền tạm tính/checkout/hóa đơn/report/lịch sử khách vẫn giữ scope. Ảnh AI vẫn chờ người dùng chọn.
