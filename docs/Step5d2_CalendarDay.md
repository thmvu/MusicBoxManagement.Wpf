# Bước 5d.2 — giao diện lịch Ngày

Ngày: 06/10/2026. WPF .NET Framework 4.7.2 + SQLite, schema **v6**. Đối chiếu mục 20/30/38–39/47 của Reference_Web_ProjectPlan_v1.3.md; triển khai Day trước, giữ Week và lịch Guest trong scope. Dùng CalendarService/ScheduleRules đã kiểm tra ở 5d.1.

## Cách dùng và bố cục

Đăng nhập → **Lịch ngày** khi có Calendar.View. Chọn ngày/phòng hoặc tất cả, bấm Tải lịch; Ngày trước/Hôm nay/Ngày sau đổi ngày và tải lại. Date mặc định theo Việt Nam UTC+7. Nút ẩn ở Guest/không có quyền; kiểm tra lại quyền trước mở và khi service đọc. Tài khoản chỉ Calendar.View vẫn đọc được, không cần Reservation.View/Room.Manage/Customer CRUD.

CalendarDayWindow có trục giờ dọc 09–23, đường lưới mỗi 30 phút, nghỉ 12–13 tô nền. Cột mã/tên phòng và trạng thái hiện tại riêng; mỗi khối luôn có mã phòng, nhãn lượt và tooltip chi tiết. Cuộn dọc/ngang khi cần. Phần phải xem chi tiết lượt đã chọn, có cả mã booking/phiên, tên/SĐT, giờ raw tới giây, ExpectedEnd hoặc hạn trả walk-in và cảnh báo quá giờ. Trạng thái hiện tại không dùng để tô kín ngày đang xem.

Màu trung tính có tương phản, khối vuông: Confirmed xanh nhạt; thực tế xanh đậm; dự kiến nền sáng/viền nét đứt; Completed xám; quá giờ viền nâu và chữ cảnh báo. Lượt có đoạn ngắn có thể chỉ đủ nhãn đầu, chọn hoặc trỏ chuột/điều hướng bàn phím để xem đầy đủ. AutomationProperties.Name chứa thông tin lượt.

## Vẽ và dữ liệu

CalendarDayViewModel tải CalendarService trên luồng nền, tạo lựa chọn phòng và lọc Room được chọn; mỗi lần tải đều kiểm tra Calendar.View trong service. CalendarService đọc tất cả phòng trong cùng snapshot để cập nhật bộ lọc, kết quả đưa lên canvas chỉ gồm phòng đang chọn. Không gọi đường Guest, không ghi booking/customer/NoShow/audit hoặc thay schema.

Confirmed vẽ Start–End, Active nguồn booking vẽ ActualStart tới mốc lớn hơn giữa now/ExpectedEnd, nhưng phân biệt phần đã dùng và dự kiến; ExpectedEnd không tự kéo dài. CheckedIn cũ không thêm khối khác. Walk-in chỉ vẽ ActualStart–now, nhãn chưa chốt kết thúc/hạn trả phòng, không dùng ReturnBy để giữ lịch tương lai. Completed vẽ ActualStart–ActualEnd. Chỉ phần giao khung 09–23 được vẽ; chi tiết giữ đầy đủ thời gian gốc. Tọa độ theo số phút thực có phần lẻ, không làm tròn 30 phút. Các lượt giao nhau (ví dụ phiên trước quá giờ và booking sau) được chia lane để không che nhau; đây là cách hiển thị, không thay quy tắc giữ lịch.

Lịch là snapshot lúc tải; trạng thái cột kèm nhãn hiện tại và status cuối màn hình ghi thời điểm. Không có kéo-thả/resize để sửa booking, nút nhận phòng hoặc timer tự tải tại bước này. Mọi thao tác ghi sau này vẫn phải kiểm tra trong transaction, không dựa vào hình lịch.

Đổi ngày/phòng xóa canvas/selection/chi tiết cũ và hướng dẫn tải. Bắt đầu tải cũng xóa dữ liệu cũ, khóa filter/nút và chặn đóng/gửi lặp khi đang bận. Không chọn ngày thì báo lỗi, không giữ hình cũ. Mất quyền/phiên hoặc lỗi đọc xóa dữ liệu riêng tư; thu hồi quyền xóa cả danh sách chọn phòng, vẫn cho đóng hoặc thử tải lại. Chọn event cũ không thuộc dữ liệu đang hiển thị bị bỏ qua.

## Kiểm tra

Debug/Release build đạt. Chạy đủ 14 bộ kiểm tra Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, Calendar, AuthenticationUi; tất cả đạt trên database/tài khoản/ảnh tạm, không sửa dữ liệu người dùng.

Verify-Calendar bổ sung ViewModel: tải danh sách/filter, chi tiết giờ thật và cảnh báo, thay filter xóa dữ liệu, ngày sau/Hôm nay, ngày rỗng, mất quyền xóa room choices/schedule/details và mở lại điều khiển. Kiểm tra service/grace/hold/actual/expected vẫn giữ từ 5d.1.

UI chạy binding và event handler thật: MainWindow ẩn Guest/quyền thiếu, mở Lịch ngày/rỗng; tài khoản chỉ Calendar.View thấy 4 phòng và 6 lượt đúng; tọa độ 10:37 không bị làm tròn; nghỉ 12–13; chọn lượt quá giờ và thông tin; lọc Walk-in/hạn trả từ booking khách ở phòng khác; ngày sau không tô kín theo Occupied; ngày trước; lịch sử Completed phòng khóa; ngày rỗng/Hôm nay; mất quyền xóa canvas/chi tiết. Render **42 ảnh**; đã xem lịch nhiều phòng, Walk-in và lịch sử tại cửa sổ tối thiểu 1050×660.

Session trong các bài kiểm tra là fixture schema v5, chưa phải luồng check-in/walk-in đã triển khai. Các bộ kiểm tra booking/hủy/xác thực/UI cũ tiếp tục đạt.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Calendar.ps1
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1
```

## Bước tiếp theo

5d.3 UI lịch Tuần theo thứ Hai, cùng nguồn quy tắc và quyền; mặc định vẫn Day. Sau đó lịch Guest một phòng chỉ trống/bận qua DTO public riêng, không tên/SĐT/ID người khác. Vận hành/checkout/hóa đơn/report/khách lịch sử vẫn chưa có; ảnh AI chờ người dùng chọn sau, không tự áp dụng.
