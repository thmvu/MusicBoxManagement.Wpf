# Bước 5d.3 — giao diện lịch Tuần

Ngày: 06/10/2026. WPF .NET Framework 4.7.2 + SQLite, schema **v6**. Đối chiếu mục 20/30/38–39/47 của Reference_Web_ProjectPlan_v1.3.md. Hoàn thành hai chế độ nội bộ Ngày/Tuần; lịch Guest/vận hành vẫn chưa có.

## Đã làm và cách dùng

- Nút chính đổi **Lịch ngày** thành **Lịch phòng**. Cửa sổ mới vẫn mặc định **Ngày**; radio Ngày/Tuần tự tải lại và giữ ngày/phòng đang chọn. Tái dùng CalendarDayViewModel/CalendarDayWindow, không thêm service SQL hoặc đường quyền riêng.
- Chế độ Tuần lấy CalendarRange.Week (thứ Hai 00:00 đến trước thứ Hai kế tiếp theo UTC+7). Chọn bất kỳ ngày trong tuần, cả Chủ nhật, đều lấy cùng khoảng. Status ghi rõ thứ Hai–Chủ nhật; Tuần trước/Tuần sau đổi ngày neo ±7, Hôm nay giữ chế độ. Trở lại Ngày lấy đúng một ngày đang chọn.
- Tuần có 7 cột theo ngày, không phải lịch chỉ ngày lặp lại. Cùng trục 09–23/slot 30 phút/nghỉ 12–13. Cuộn ngang xem đủ ngày cuối; filter Room vẫn hỗ trợ một hoặc tất cả phòng. Mỗi lượt có mã phòng và tooltip/chi tiết; nhiều lượt giao nhau chia lane, lọc phòng giúp đọc rõ khi đông.
- Trạng thái hiện tại của các phòng đang lọc hiện riêng ở khung phải và timestamp trong status; đầu cột Tuần là ngày. Không lấy Occupied lúc này để tô kín ngày mai hoặc cả tuần.

## Vẽ và quyền

Trích DrawColumn dùng chung cho Day/Week. Day tạo cột theo phòng; Week tạo cột theo ngày, gom event của các phòng đang lọc. Mỗi cột chỉ vẽ phần giao [09:00, 23:00) của ngày đó; tọa độ theo thời gian thực, không làm tròn slot. Event raw giữ nguyên Start/End/ExpectedEnd để chọn lượt giao biên tuần/nửa đêm vẫn thấy ngày/giờ gốc. Một session dài có thể có nhiều phần vẽ theo ngày, nhưng vẫn cùng ID nguồn, không thêm khoảng giữ lịch hoặc tạo dữ liệu mới.

Confirmed, thực tế Active, dự kiến Active, walk-in, Completed và quá giờ dùng cùng renderer/màu/cảnh báo của 5d.2. ExpectedEnd không nới khi quá giờ; walk-in chỉ vẽ tới now, không tới hạn cảnh báo trả phòng. Các quy tắc hold/grace đến từ CalendarService/ScheduleRules đã có; bước này không đổi business hoặc schema.

Calendar.View hiện hành vẫn kiểm tra mỗi lượt service đọc; chuyển mode/tải lại xóa lịch/chi tiết cũ, khóa thao tác lúc bận và giữ cơ chế xóa dữ liệu khi mất quyền. Lịch chỉ đọc/manual refresh, không kéo-thả hoặc giữ chỗ. Guest không mở màn hình có thông tin khách nội bộ này.

## Kiểm tra

Build Debug/Release đạt. 14 bộ kiểm tra Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, Calendar, AuthenticationUi đạt trên database/tài khoản/thư mục tạm riêng; không sửa dữ liệu người dùng.

Verify-Calendar bổ sung VM mặc định Ngày, đổi Tuần đúng Monday range/status/nhãn nút, tuần trước/sau ±7, Chủ nhật vẫn cùng tuần, Hôm nay giữ mode, trở lại Day đúng một ngày, mất quyền khi đang xem Week xóa lịch/chi tiết.

AuthenticationUi bổ sung radio/binding/handler thật: đủ 7 cột và 7 nhãn nghỉ; phiên Completed từ Chủ nhật trước giao thứ Hai, và thứ Hai qua thứ Ba được clip đúng, chi tiết giữ timestamp gốc; tất cả phòng có booking Chủ nhật nhưng không có thứ Hai tuần sau; chọn Chủ nhật/lọc phòng/đặt đúng cột cuối; tuần sau chỉ lấy booking mới, tuần trước/Hôm nay/Day; thu hồi quyền ở Week. Render **46 ảnh**; đã xem lịch tuần nhiều phòng, lịch sử giao ngày và Chủ nhật tại kích thước tối thiểu 1050×660. Session fixture vẫn không phải service check-in/walk-in đã triển khai.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Calendar.ps1
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1
```

## Làm tiếp

5d.4 lịch Guest một phòng theo ngày chỉ trống/bận, DTO public không tên/SĐT/CustomerId/booking hoặc session nguồn; vẫn dùng cùng rule, nhắc kiểm tra lại khi submit. Check-in/walk-in/gia hạn/gọi món/checkout/hóa đơn/report/lịch sử khách còn giữ scope. Các ảnh AI chờ người dùng chọn sau, không tự thay dữ liệu.
