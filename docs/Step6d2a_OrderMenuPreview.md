# Bước 6d.2a — menu và xem trước giỏ món

Ngày 09/10/2026. Đối chiếu mục 16/22–24/44–45/47/55.4 của Reference_Web_ProjectPlan_v1.3.md. Bước nhỏ chuẩn bị đường dữ liệu cho giao diện; chưa có màn hình gọi món.

## Đã làm và cách hoạt động

- OrderService.ReadMenuGuest(phoneNumber, sessionId) chuẩn hóa SĐT, kiểm tra phiên Active thuộc số hiện hành, trả các món đang bán. ReadMenuStaff(actor, sessionId) kiểm tra Order.Create hiện hành và Active; không cần Service.Manage/Session.View/Order.View/Order.Confirm. Staff không fallback Guest khi mất quyền.
- DTO OrderMenuItem chỉ có ID món/tên/nhóm/giá/mô tả. OrderMenu có danh sách, CheckedAt, CanCreate và Reason; không có khách/creator hoặc entity phiên. Ngoài ca vẫn đọc được menu của phiên Active nhưng CanCreate=false; preview/gửi mới bị chặn. Menu rỗng không seed món hoặc tạo đơn.
- PreviewGuest/PreviewStaff kiểm tra lại membership/quyền/Active/giờ mở cửa, đọc tên/giá đang bán và gộp giỏ theo ServiceId. Đầu vào chỉ ServiceId/Quantity; ít nhất một món, từng số lượng hợp lệ và tổng mỗi món 1–10.
- OrderPreview có Items, CheckedAt và Amount là tổng giá trị **giỏ món đang chọn**. Đây chưa phải tiền tạm tính của phiên: không có tiền phòng và không cộng các Order đã hoàn tất. Tính bằng decimal để tổng quantity × giá không tràn long.
- Menu và preview dùng read transaction/một IClock, không tạo Order/OrderItems, không audit/NoShow hoặc giữ chỗ/giá. Không tạo PreviewId/cache/token xác nhận.
- Tách ReadCart và DemandOrderHours dùng chung với Create. Lúc gửi CreateGuest/CreateStaff vẫn mở writer transaction, kiểm tra lại quyền/số hiện hành/Active/clock sau lock/giờ/dịch vụ/số lượng và lấy snapshot mới; không dùng giá/tên từ preview làm nguồn lưu. Món ngừng bán sau preview bị từ chối; giá/tên đổi thì đơn mới lấy dữ liệu hiện hành.

Schema giữ **v7**, không migration. Không đổi quy tắc trạng thái 6d.1: Guest Pending; Staff tạo Completed cho món đã phục vụ; Confirm/Cancel chỉ Pending+Active, đơn cũ giữ snapshot.

## Kiểm tra

Build Debug/Release vào bin/VerifyDebug và bin/VerifyRelease với intermediate riêng, chỉ database tạm. Verify-Orders bổ sung VerifyMenu:

- SĐT chuẩn hóa/số hiện hành/phiên khác/Completed, DTO không PII; Create-only Staff đọc menu/preview, mất quyền bị chặn và không dùng Guest thay thế.
- Chỉ món đang bán, menu rỗng không seed; read-only không tạo đơn/audit, một mốc clock giữ UTC giây/ticks.
- Giỏ trống/số lượng >10/tổng dòng trùng >10/món ngừng bán, gộp số lượng và Amount đúng giá server.
- Giờ nghỉ/23:00: menu báo không nhận mới và preview bị chặn.
- Giá/tên đổi sau preview: writer dùng giá/tên mới; món ngừng bán sau preview không tạo đơn. Giá long.MaxValue × 10 không tràn long khi preview.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Orders.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
```

21 bộ kiểm tra đạt: Orders, Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, Calendar, GuestCalendar, CheckIn, WalkIn, Extension, Sessions, GuestSessions, AuthenticationUi. UI hiện có render 76 ảnh; chưa thêm màn hình bước này.

## Tiếp theo

6d.2b nối giao diện Guest từ Tra cứu SĐT/phiên Active: menu/giỏ/số lượng, xem trước/xác nhận gửi Pending, xem đơn và hủy Pending, chống gửi lặp/đổi số/phiên thay đổi/refresh lỗi sau commit. Tiếp đó 6d.2c giao diện Staff theo quyền riêng để xem/xác nhận đã phục vụ/hủy và tạo hộ Completed. Không cắt phần Staff hoặc hoàn tất Guest mục 22 chỉ bằng menu. Giữ Billing/tiền tạm tính/checkout/hóa đơn/dashboard/RBAC/báo cáo trong plan.
