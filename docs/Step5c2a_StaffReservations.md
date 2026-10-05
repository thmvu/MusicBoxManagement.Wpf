# Bước 5c.2a — danh sách/chi tiết/hủy booking nội bộ

Ngày: 05/10/2026. WPF .NET Framework 4.7.2 + SQLite. Đối chiếu mục 8–10/21/38–39/47/55 của `Reference_Web_ProjectPlan_v1.3.md`. Schema giữ **v6**, không thay giờ/overlap/trạng thái hoặc bỏ form đặt hộ, calendar, check-in khỏi scope.

## Đã làm và cách hoạt động

- Khu vực nhân viên có nút **Booking** khi có Reservation.View. Guest không mở dữ liệu nội bộ. Trước khi mở kiểm tra quyền hiện hành; service kiểm tra lại trong read transaction.
- Màn hình danh sách và khung chi tiết: mã/phòng/khách/SĐT/giờ bắt đầu/trạng thái; chọn dòng thấy giờ kết thúc, hạn nhận trước StartTime+15 phút, người đặt/lúc tạo, ID session nguồn nếu có và lý do hủy. Khách/SĐT lấy theo CustomerId hiện hành, không tạo snapshot mới hoặc sửa liên kết lịch sử.
- Lọc ngày từ/đến (hai đầu gồm cả ngày theo Việt Nam UTC+7), SĐT đầy đủ chuẩn hóa, trạng thái. Mặc định hôm nay..+30; bỏ trống ngày để xem toàn bộ lịch sử. Đường nội bộ giữ các trạng thái Cancelled/NoShow/CheckedIn/Completed; đây không phải Guest lookup. Không cần Customer CRUD hoặc Room.Manage để đọc booking khi có Reservation.View.
- Có quyền Reservation.Cancel và Confirmed còn hiệu lực chưa có session nguồn thì được yêu cầu hủy. Nhập lý do → Xác nhận hủy; Giữ booking/đóng trước xác nhận không ghi dữ liệu. Lý do trim, bắt buộc 1–500 ký tự; form khóa đổi filter/selection lúc xác nhận và khóa đóng khi đang bận. Nút xác nhận nằm ngoài phần chi tiết cuộn để luôn nhìn thấy.
- Staff không dùng mốc 2 giờ của Guest: hủy được trong 2 giờ và trong grace trước check-in, nhưng đúng +15 phút đã hết hạn. Không đổi NoShow/Completed/CheckedIn/Cancelled sang Cancelled, không sửa trực tiếp phòng/ngày/giờ/thời lượng hoặc xóa booking.

## Service và transaction

`StaffReservationService.Search` Demand Reservation.View trước bảo trì NoShow. Chuẩn hóa/kiểm tra filter, xử lý NoShow tồn đọng, sau đó read transaction đọc lại quyền và các dữ liệu/khả năng hủy trong cùng snapshot. IsCancellable phụ thuộc status, now < StartTime+15 và chưa có session nguồn; UI kết hợp thêm quyền Cancel. Kết quả là trạng thái tại lúc tải, không tự bảo đảm điều kiện vẫn đúng lúc nhân viên xác nhận.

`ReservationService.CancelStaff` BeginWriteTransaction → kiểm tra Reservation.Cancel hiện hành → kiểm tra lý do → lấy IClock/đọc lại status/giờ/session → UPDATE Cancelled/lý do → AuditLog Reservation.Cancel/ActorType Staff/UserId thực → commit. Audit lỗi rollback; giữ CustomerId/RoomId/StartTime/EndTime và lịch sử. Đường hủy service cần quyền hành động Cancel; UI dùng thêm View để mở/chọn booking.

Hai lượt hủy cùng booking chỉ một lượt ghi dữ liệu/log. Hủy và đặt mới cùng khoảng được serialize với CreateGuest/CreateStaff hiện có: đặt mới chỉ thành công nếu hủy đã commit trước khi kiểm tra lịch; nếu đặt bị từ chối, Customer vừa tạo cũng rollback. Không cam kết giữ một slot thay thế khi hủy booking cũ.

ViewModel xóa danh sách/selection/lý do khi không còn quyền hoặc lỗi đọc; tải lại có thể kiểm tra quyền mới. Hủy commit rồi refresh lỗi vẫn thông báo đã hủy; không báo thao tác thất bại hoặc gợi ý gửi lại lệnh hủy. Confirm thiếu lý do giữ form để nhập; dữ liệu đã đổi/hết grace thì tải lại và bỏ xác nhận cũ.

## Kiểm tra

Build Debug/Release đạt. Chạy 12 bộ kiểm tra: Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, AuthenticationUi. Tất cả database và tài khoản là dữ liệu tạm; không sửa database người dùng.

StaffReservations kiểm tra quyền hiện hành/null/đăng xuất, không cần Customer CRUD, số chuẩn hóa/ngày/trạng thái/người tạo/chi tiết; lý do rỗng/quá dài/trim; Guest chặn dưới 2 giờ nhưng Staff cho phép; hủy trước/đúng +15 phút; các trạng thái không được hủy/session nguồn; audit rollback và actor Staff; hủy lặp/đồng thời, hủy/đặt mới đồng thời và rollback Customer của lượt thua; ViewModel mất quyền xóa dữ liệu, chỉ xem và lỗi NoShow refresh sau commit vẫn báo đã hủy. Guard session dùng fixture, chưa chứng minh luồng check-in service đồng thời vì check-in chưa triển khai.

UI kiểm tra real binding/event handler trên database riêng: nút từ MainWindow/danh sách rỗng, lọc SĐT sai/chuẩn hóa/trạng thái, chi tiết, giữ booking, thiếu lý do, nhập lý do/hủy dưới 2 giờ, xem lại Cancelled/lý do và không gửi hủy lặp, tài khoản chỉ xem và thu hồi View sau mở. Suite render 30 ảnh; đã xem trực quan ảnh xác nhận/chi tiết đã hủy/chỉ xem.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-StaffReservations.ps1
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1
```

## Bước kế tiếp

5c.2b bổ sung form nhân viên đặt hộ theo Reservation.Create, dùng CreateStaff và giữ actor/CreatedByUserId thực; không dùng CreateGuest. Sau đó calendar ngày/tuần rồi check-in/walk-in/gia hạn/gọi món. Customer history/Invoice/report và Guest lookup phiên Active vẫn giữ trong scope, chưa hoàn thành. Các ảnh phòng AI có 5 mẫu trong assets/rooms và chỉ để người dùng chọn sau, chưa tự áp dụng.
