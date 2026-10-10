# Bước 6d.2b — giao diện gọi món cho Khách

Ngày hoàn thành: 10/10/2026. Nền tảng giữ WPF .NET Framework 4.7.2 + SQLite schema v7. Đối chiếu mục 16, 22–24 và 55.4–55.6 của Reference_Web_ProjectPlan_v1.3.md; không đổi quy tắc nghiệp vụ.

## Cách sử dụng

1. Chế độ Khách → Tra cứu SĐT → nhập số đang sử dụng phòng → tab Đang sử dụng.
2. Chọn **Gọi món / xem đơn**. Nút chỉ hiện khi có phiên Active, áp dụng cả phiên từ booking và walk-in. Màn hình gắn với phiên và số đã tra cứu; cửa sổ tra cứu bị khóa thao tác trong lúc mở cửa sổ con.
3. Menu bên trái chỉ gồm món đang bán. Chọn món, chọn số lượng 1–10, rồi Thêm vào giỏ; thêm lại cùng món gộp số lượng, tổng mỗi món không quá 10. Có thể bỏ dòng đã chọn.
4. Xem trước gửi đơn tải tên/giá hiện hành từ server, hiện giá trị giỏ và xác nhận bên phải. Quay lại không lưu và giữ giỏ. Xác nhận tạo một đơn **Chờ phục vụ (Pending)**; giá/tình trạng bán được kiểm tra lại lúc ghi, không khóa giá theo preview.
5. Đơn đã gửi nằm bên phải. Chọn đơn xem snapshot tên/giá/số lượng. Chỉ đơn Pending có thể xác nhận hủy; Quay lại giữ đơn. Đơn đã phục vụ/đã hủy không đổi tiếp.
6. Tải lại xóa giỏ/xác nhận cũ và tải dữ liệu hiện hành. Đóng màn hình gọi món sẽ tải lại tra cứu. Đổi SĐT ở màn hình tra cứu xóa phiên và nút gọi món cũ.

## Phần đã sửa

- Thêm GuestOrdersWindow/GuestOrdersViewModel với menu, giỏ, xem trước, xác nhận gửi/hủy và danh sách đơn của phiên.
- GuestLookupWindow mở cửa sổ con bằng phiên/SĐT đã tra cứu; GuestReservationService cấp OrderService dùng cùng database và IClock. Không gọi đường Staff hoặc dữ liệu quản trị.
- Mọi lần đọc/preview/ghi dùng route Guest của OrderService. Membership/số hiện hành/trạng thái Active được service kiểm tra lại; writer vẫn kiểm tra ca/dịch vụ/số lượng và chốt snapshot/audit Guest cùng transaction.
- Busy guard khóa input/nút/đóng trong khi xử lý; xác nhận khóa thay giỏ. Bấm xác nhận lặp không gửi thêm. Mỗi lần gửi giỏ mới có chủ ý vẫn tạo đơn mới đúng plan; chưa có token idempotency ở backend.
- Khi stale, đổi SĐT trong database, phiên Completed hoặc món ngừng bán trước lưu: service từ chối, UI xóa dữ liệu và xác nhận cũ. Hủy khi Staff vừa phục vụ cũng bị chặn.
- Nếu ghi đã commit nhưng đọc lại thất bại: giữ thông báo **Đã gửi/Đã hủy**, xóa dữ liệu cũ và yêu cầu tải lại/tra cứu; không báo chưa lưu hoặc giữ xác nhận để gửi lặp.
- Ngoài ca không thêm/gửi giỏ mới, nhưng xem đơn và hủy Pending vẫn được khi phiên Active. Tổng giỏ không phải tổng tiền phiên; Pending chưa tính tiền theo plan.

## Kiểm tra

Build Debug/Release vào bin/VerifyDebug và bin/VerifyRelease để không đụng file ứng dụng người dùng đang chạy. Tất cả dữ liệu kiểm tra nằm trong thư mục tạm, không sửa database/ảnh thật.

21 bộ kiểm tra: Orders, Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, Calendar, GuestCalendar, CheckIn, WalkIn, Extension, Sessions, GuestSessions, AuthenticationUi.

AuthenticationUi bổ sung thao tác thật trên WPF: menu chỉ đang bán; số lượng/gộp/bỏ; preview và quay lại không ghi; giá đổi sau preview; gửi lặp chỉ một đơn; xem/hủy/giữ; ngừng bán/SĐT/Completed/stale đã phục vụ; lỗi refresh sau commit; ngoài ca vẫn hủy Pending; mở cửa sổ từ Tra cứu SĐT và đổi số xóa nút. Render tổng 83 ảnh, trong đó 7 ảnh mới của gọi món. Kiểm tra bố cục xác nhận ở cửa sổ tối thiểu và các cột menu khi thu nhỏ.

```powershell
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
```

## Tiếp theo

6d.2c giao diện nhân viên: xem đơn theo Order.View, xác nhận đã phục vụ theo Order.Confirm, hủy theo Order.Cancel, tạo hộ Completed theo Order.Create độc lập các quyền xem/quản lý dịch vụ. Không fallback Guest khi mất quyền Staff.

Giữ scope Billing/tiền tạm tính, checkout/hóa đơn, dashboard, quản trị tài khoản/RBAC, báo cáo/Excel và lịch sử khách hàng. Guest mục 22 chưa hoàn tất phần tiền tạm tính. Checkout/Order đồng thời sẽ kiểm tra bằng service checkout thật khi triển khai.
