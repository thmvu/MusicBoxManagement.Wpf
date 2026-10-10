# Bước 6d.2c — giao diện đơn món cho nhân viên

Ngày: 10/10/2026. Giữ WPF .NET Framework 4.7.2, SQLite schema v7. Đối chiếu mục 5, 16, 23–24, 55.4–55.6 của docs/Reference_Web_ProjectPlan_v1.3.md trước khi nối UI.

## Sử dụng

Đăng nhập → **Đơn món** → chọn phiên/phòng trong ô trên cùng.

- Với Order.View: xem đơn của phiên Active hoặc lịch sử phiên Completed, chọn đơn để xem snapshot tên/đơn giá/số lượng và thời điểm gửi.
- Với Order.View + Order.Confirm: chọn đơn chờ → Đã phục vụ → Xác nhận sau khi giao món. Quay lại không đổi trạng thái.
- Với Order.View + Order.Cancel: chọn đơn chờ → Hủy đơn chờ → Xác nhận. Chỉ Pending được xử lý và phiên phải còn Active; đơn đã phục vụ/đã hủy giữ nguyên.
- Với Order.Create: chọn phiên Active → Ghi món đã phục vụ → chọn menu/số lượng/thêm-bỏ giỏ → Xem trước ghi đơn → Xác nhận đã phục vụ. Đơn hộ được tạo Completed ngay, chỉ dành cho món đã giao. Với yêu cầu Guest đã có Pending, xác nhận đơn cũ để tránh ghi hai đơn.

Quyền tạo độc lập với Order.View/Order.Confirm/Session.View/Service.Manage và Customer CRUD. Nhân viên chỉ có Order.Create vẫn thấy menu Đơn món và phiên Active; màn hình không tải danh sách đơn cũ, chỉ nhận receipt từ lần tạo của mình. Khi tạo xong và còn quyền, có thể chọn giỏ mới; Tải lại xóa giỏ/receipt cũ. Không tự lặp gửi khi lỗi.

## Thay đổi và quyền

- OrderService.ReadWorkspaceStaff đọc quyền và lựa chọn phiên trong cùng read transaction với một IClock. Chấp nhận Order.View **hoặc** Order.Create; nếu chỉ Create thì chỉ trả Active. DTO lựa chọn chỉ ID phiên/mã phòng snapshot/trạng thái/giờ nhận, không tên/SĐT/ID khách/RoomId/ReservationId. Không dùng StaffSessionService hoặc đường Guest.
- Menu Đơn món trên sidebar kiểm tra quyền hiện hành, có guard mở lặp. StaffReservationService.ForOrders dùng chung database/clock và kiểm tra quyền chọn phiên trước mở.
- StaffOrdersWindow/StaffOrdersViewModel đọc Order.View; xác nhận phục vụ/hủy dùng ConfirmStaff/CancelStaff với quyền riêng. API writer vẫn độc lập View như nền tảng 6d.1; chọn đơn trên UI cần View để đọc danh sách.
- StaffOrderCreateWindow/StaffOrderCreateViewModel chỉ dùng ReadMenuStaff/PreviewStaff/CreateStaff. Giỏ gộp mỗi ServiceId 1–10; preview đọc giá hiện tại, không ghi hoặc khóa giá. Writer kiểm tra lại quyền, Active, ca, dịch vụ đang bán và snapshot/audit Staff cùng transaction.
- Đổi phiên xóa đơn/chi tiết/xác nhận cũ. Tải lại đọc quyền và dữ liệu hiện hành. Xác nhận khóa lựa chọn; busy khóa input/nút/đóng và chặn lần bấm lặp. Stale hoặc mất quyền ở thao tác service xóa dữ liệu/xác nhận, vẫn đóng được; không fallback Guest.
- Khách vừa hủy hoặc nhân viên khác xử lý trước: thao tác còn lại bị chặn theo trạng thái trong transaction. Dịch vụ ngừng bán không chặn phục vụ đơn Pending cũ; tên/giá snapshot cũ giữ nguyên.
- Nếu writer đã commit mà tải lại lỗi/mất quyền: giữ thông báo Đã xác nhận/Đã hủy/Đã ghi, xóa dữ liệu và xác nhận cũ; tải lại trước thao tác tiếp. Không báo thay đổi chưa lưu.
- Completed history chỉ xem. Ngoài ca chặn tạo mới, vẫn cho xử lý Pending của Active. UI tải lại thủ công, không tự checkout hoặc thay trạng thái phiên.

## Kiểm tra

Debug/Release build vào bin/VerifyDebug và bin/VerifyRelease. 21 bộ kiểm tra đạt: Orders, Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, Calendar, GuestCalendar, CheckIn, WalkIn, Extension, Sessions, GuestSessions, AuthenticationUi. Chỉ dùng SQLite/thư mục tạm, không sửa database/ảnh thật hoặc đóng ứng dụng người dùng đang mở.

Verify-Orders bổ sung workspace Create-only/View/history, một clock, DTO tối thiểu, thiếu quyền/signed-out/null và không ghi dữ liệu (các guard phiên đăng nhập dùng PermissionService hiện hành).

AuthenticationUi bổ sung thao tác thật: menu/sidebar, xem Pending, giữ/xác nhận phục vụ/hủy, snapshot dịch vụ ngừng bán, stale Guest cancel, Completed history, View-only/Create-only, thu hồi quyền, tạo hộ Completed đúng actor, giỏ/gộp/bỏ/preview/Keep, giá đổi/inactive/Completed/đổi clock qua giờ đóng, duplicate, refresh lỗi sau commit và mở form tạo từ nút thật. Render 94 ảnh tổng cộng, 11 ảnh mới; kiểm tra bố cục tại cửa sổ tối thiểu cho hai màn hình.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Orders.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
```

## Tiếp theo

6e.1 nền tảng BillingService theo mục 17/25/26/55.2: tiền phòng từ actual và giá snapshot, tính cả giây/ticks bằng decimal, làm tròn đồng AwayFromZero một lần; chỉ cộng Order Completed, Pending/Cancelled không cộng. Cùng công thức cho tiền tạm tính và checkout; không khóa giá theo màn hình hoặc tự hủy Pending khi preview. Sau đó nối tiền tạm tính Guest/Staff và checkout/hóa đơn theo từng bước nhỏ.

Giữ scope dashboard/doanh thu quán, quản trị tài khoản/role/ma trận RBAC/audit, báo cáo/Excel và lịch sử khách hàng. Checkout/Order và gia hạn/checkout đồng thời kiểm tra khi có service checkout thật; fixture Completed hiện chỉ kiểm tra guard/history, chưa chứng minh đã thanh toán.
