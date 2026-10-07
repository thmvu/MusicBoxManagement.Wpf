# Bước 6a.2 — nhận phòng trên chi tiết booking

Ngày: 07/10/2026. WPF .NET Framework 4.7.2 + SQLite, schema v6. Đối chiếu mục 11/17/35/38/41/55.4 của bản tham chiếu; dùng RoomSessionService.CheckIn đã hoàn thành ở 6a.1.

## Cách sử dụng

Đăng nhập → Booking → chọn booking Confirmed còn hạn/chưa có session → **Nhận phòng**. Màn hình danh sách cần Reservation.View; thao tác nhận cần Session.CheckIn, độc lập quyền tạo/hủy booking hoặc Customer CRUD.

Panel xác nhận hiện phòng, booking, giờ nhận/dự kiến trả theo đồng hồ tại lúc mở xác nhận, giá tham khảo hiện tại. **Để sau** chưa ghi dữ liệu. **Xác nhận nhận phòng** gọi service kiểm tra lại quyền/trạng thái/giờ/ca/Room/Customer/overlap trong transaction. Giờ nhận và giá được chốt lúc xác nhận, không dựa vào giá/giờ xem trước. Không cần lý do hủy để nhận phòng.

Thành công hiện mã phiên, trạng thái, mã/loại phòng snapshot, giờ nhận thực tế tới giây, giờ dự kiến trả và giá chốt. Danh sách tải lại, chọn booking vừa nhận nếu còn trong bộ lọc; filter Confirmed có thể không còn chứa booking CheckedIn nhưng kết quả phiên vẫn được hiển thị sau read có quyền. Giữ giờ booking gốc riêng với giờ session; không làm tròn actual theo slot.

Trong xác nhận khóa chọn booking, bộ lọc/tải lại và thao tác khác. Trong lúc gửi khóa gửi lặp/đóng; sau thành công không xác nhận lại. Đổi booking/tải danh sách xóa kết quả phiên cũ. Đã có nguồn session thì nút nhận không bật; idempotence của service vẫn bảo vệ các lần gọi lặp/concurrent ngoài UI.

Mất quyền/phiên đăng nhập bị thu hồi xóa danh sách, chi tiết, confirmation và kết quả. Booking bị hủy/đổi trạng thái/hết hạn/trùng lịch sau khi mở form bị service từ chối, báo tải lại. Lỗi SQLite Busy/Locked có thông báo thử lại; lỗi ghi rollback service. Nếu đã commit nhưng tải lại lỗi, thông báo vẫn ghi **Đã nhận phòng**, không báo thao tác chưa lưu. Mất Reservation.View sau commit cũng xóa dữ liệu riêng tư.

## Triển khai

- StaffReservationSearch thêm CanCheckIn theo quyền hiện hành, StaffReservation thêm CurrentHourlyRate từ danh mục lúc đọc để tham khảo.
- StaffReservationService.CheckIn chuyển tiếp RoomSessionService dùng cùng IClock; không tự ghi trạng thái/session trong UI, không cần Reservation.Cancel/Create.
- ReservationsViewModel quản lý confirmation/result/busy, gọi service và tải lại sau commit.
- ReservationsWindow thêm nút/panel xác nhận/kết quả trong cột chi tiết, cùng giao diện màu trung tính hiện có.

Không nâng schema. Database thật và ảnh đang dùng không được thay đổi khi kiểm tra.

## Kiểm tra

Build Debug/Release vào bin/VerifyDebug và bin/VerifyRelease đạt. Thư mục riêng được dùng vì ứng dụng Debug đang mở trong Visual Studio giữ file executable; không đóng process người dùng.

7 bộ kiểm tra CheckIn, Availability, Reservations, Calendar, StaffReservations, StaffBooking, AuthenticationUi đạt trên dữ liệu tạm. AuthenticationUi bổ sung 5 ảnh nhận phòng, tổng 55 ảnh: button/confirmation/Để sau không ghi/giờ thực tế/giá đổi trước confirm/kết quả & refresh/gửi lặp/booking vừa hủy/mất quyền/View-only/post-commit refresh lỗi. Đã xem ảnh xác nhận và kết quả.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-CheckIn.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
```

## Làm tiếp

6b.1 service nhận khách trực tiếp (walk-in), rồi UI. Gia hạn, gọi món, Guest Active/tiền tạm tính, checkout/hóa đơn, dashboard, quản trị tài khoản/ma trận quyền/báo cáo vẫn trong scope.

Ngày 07/10 người dùng xác nhận giữ RBAC theo vai trò Staff/Manager/Admin; không cần quyền riêng cho hai nhân viên cùng role. Admin gán một role cho mỗi tài khoản và chỉnh quyền Staff/Manager theo plan. Dashboard theo dõi hoạt động/doanh thu quán, không thêm điện/thuê mặt bằng hoặc module khoản chi. UI nút/bảng kiểm tra quyền hiện tại là phần nền tảng; mục tiêu bước quản trị là dashboard nghiệp vụ làm màn hình chính, ma trận quyền đặt trong khu vực Admin. Service vẫn đọc quyền hiện hành khi thực hiện thao tác.
