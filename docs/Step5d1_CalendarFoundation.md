# Bước 5d.1 — nền tảng lịch và trạng thái phòng

Ngày: 06/10/2026. WPF .NET Framework 4.7.2 + SQLite, schema **v6**. Đối chiếu mục 12/20/30/38–39/47 của Reference_Web_ProjectPlan_v1.3.md. Đây là bước service/data, chưa thêm màn hình lịch hoặc service vận hành.

## Đã làm và cách hoạt động

CalendarRange.Day/Week xác định khoảng xem theo giờ Việt Nam UTC+7. Ngày là [00:00, 00:00 hôm sau); tuần bắt đầu thứ Hai và kết thúc trước thứ Hai kế tiếp. Không áp giới hạn đặt hôm nay..+30 cho lịch nội bộ xem lịch sử. Không nhận ngày biên không chuyển UTC an toàn.

CalendarService.Read(session, range, roomId tùy chọn) kiểm tra Calendar.View hiện hành trong cùng read transaction với danh sách phòng, trạng thái và dữ liệu lịch; lấy IClock một lần. Quyền độc lập với Reservation.View/Room.Manage/Customer CRUD. Đọc tất cả phòng hoặc một phòng, giữ phòng inactive để xem lịch sử. Null/đăng xuất/mất quyền bị chặn. Kết quả có thông tin khách nội bộ; không cung cấp đường public/Guest ở bước này.

Trạng thái hiện tại ưu tiên Inactive → Occupied → Reserved → Available. Reserved chỉ khi Confirmed đang tới lượt (StartTime <= now < EndTime) và now < StartTime+15 phút. Booking tương lai không làm Reserved; Active vẫn Occupied khi quá giờ, dù khoảng giữ lịch dự kiến đã kết thúc. Trạng thái là dữ liệu hiện tại, không tái dựng lịch sử khóa/mở và không tô kín ngày mai theo Occupied lúc này.

RoomSchedule trả hai phần riêng:

- **Holds**: khoảng giữ booking theo mục 38, lấy SQL ScheduleRules dùng chung với AvailabilityService. Confirmed còn grace giữ Start–End; Active nguồn Reservation giữ ActualStart–ExpectedEnd; không đếm thêm CheckedIn. Cancelled/NoShow/Completed/Confirmed hết grace không giữ lịch; walk-in không tạo hold tương lai.
- **Events**: Confirmed còn hiệu lực và phiên có khoảng thực tế/dự kiến giao với ngày/tuần đang xem. Active có Start là ActualStartTime và End là now, ExpectedEnd riêng; Completed đến ActualEndTime, không giữ lịch. Giữ giờ raw (cả giây), không cắt theo biên ngày hoặc làm tròn slot; UI tương lai phải clip phần vẽ theo range, phân biệt thực tế/dự kiến. Khoảng Completed độ dài 0 không vẽ event. Overdue đánh dấu khi now vượt ExpectedEnd, không nới hold.

Walk-in Active có End=now, ExpectedEnd không cam kết và không giữ chỗ tương lai. ReturnBy là mốc sớm nhất giữa cuối ca lúc phiên bắt đầu và Confirmed còn hiệu lực kế tiếp của Room hoặc Customer (có thể ở phòng khác). Mốc tính lại khi đọc, đổi khi booking mới/hủy; nếu phiên kéo qua hết ca thì cảnh báo mốc ca đã qua, không cấp thêm ca hoặc tự checkout. Đây là cảnh báo, không phải ExpectedEndTime.

Service lịch chỉ đọc: Confirmed quá hạn bị loại ngay cả khi worker chưa đổi status, không ghi NoShow/audit hoặc làm tác vụ bảo trì khi mở lịch. NoShowService/worker vẫn là đường ghi hiện có. Snapshot lịch không giữ chỗ; CreateStaff/CreateGuest và các writer tương lai vẫn kiểm tra lại quyền/giờ/Room/Customer/overlap trong transaction ghi.

## Kiểm tra

Build Debug/Release đạt. 14 bộ kiểm tra đạt: Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, Calendar, AuthenticationUi. Database/tài khoản/ảnh đều trong thư mục tạm riêng; không sửa database người dùng. UI cũ render 36 ảnh, chưa có ảnh UI calendar mới.

Verify-Calendar kiểm tra UTC+7/ngày/tuần thứ Hai (cả Chủ nhật), phòng không tồn tại/range sai; chỉ Calendar.View vẫn đọc được, Guest/null/mất quyền/đăng xuất bị chặn; Reserved đúng grace trước/đúng +15 phút; ưu tiên Inactive/Occupied/Reserved; booking tương lai/Cancelled/NoShow/Confirmed hết hạn; CheckedIn không đếm đôi; ActualStart không khớp slot; ExpectedEnd đã qua không nới hold; walk-in không tô bận ngày mai, ReturnBy xét lịch khách ở phòng khác và cập nhật sau hủy/ca; Completed giao qua nửa đêm và lịch sử phòng khóa. Đối chiếu với Availability cho phiên quá giờ: booking tương lai hợp lệ nhưng bắt đầu ngay bị chặn. Xác nhận đọc lịch không đổi Confirmed hết hạn/log NoShow/schema.

Session trong kiểm tra là fixture schema v5; chưa chứng minh luồng check-in/walk-in/gia hạn/checkout đầy đủ. Kiểm tra cạnh tranh và nghiệp vụ ghi của Availability/Reservations vẫn đạt sau trích ScheduleRules dùng chung.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Calendar.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Availability.ps1
```

## Làm tiếp

5d.2 giao diện lịch Ngày mặc định, slot 30 phút/09–23/đánh dấu nghỉ 12–13/lọc Room/nhãn phòng, không kéo-thả sửa booking. Sau đó Tuần và lịch Guest một phòng chỉ trống/bận qua DTO public riêng; không đưa CustomerName/PhoneNumber/ID nguồn từ CalendarService ra Guest. Cả Day và Week giữ trong MVP. Luồng vận hành/hóa đơn/báo cáo/lịch sử khách và ảnh AI chờ chọn vẫn giữ trong phạm vi, chưa hoàn tất đồ án.
