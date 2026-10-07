# Bước 6c.2 — phiên sử dụng và UI gia hạn

Ngày 07/10/2026. WPF .NET Framework 4.7.2 + SQLite, schema giữ v6. Đối chiếu mục 13/14/38/43/48/55.4 của bản tham chiếu web.

## Đã làm và cách dùng

Menu trái có **Phiên sử dụng** theo Session.View. Mở màn hình → xem Active (mặc định) hoặc lọc Completed/Tất cả và SĐT đầy đủ. Chọn một dòng để xem nguồn booking/walk-in, tên/SĐT khách, mã phòng/loại snapshot, giờ nhận/giờ dự kiến hoặc giờ trả thực tế, giá đã chốt và thời điểm cập nhật.

Phiên Active từ booking chưa quá giờ, với quyền Session.Extend, có thể chọn 30/60 phút → Gia hạn → đọc giờ cũ/mới → Xác nhận gia hạn. Để sau không lưu. Thành công tự tải lại danh sách, chọn đúng phiên và hiện giờ trả mới/giá giữ nguyên. Walk-in và Completed không có gia hạn. Phiên quá giờ bị chặn theo dữ liệu lúc đọc; nếu thời gian trôi qua sau đó, preview/writer kiểm tra lại.

StaffSessionService đọc danh sách và thông tin chi tiết trong một read transaction/một IClock. Bộ lọc SĐT dùng PhoneNumberNormalizer, status chỉ Active/Completed hoặc bỏ trống. Không cần Reservation.View/Create/Cancel, Room.Manage hoặc Customer CRUD. Phiên lịch sử vẫn dùng snapshot dù danh mục đổi hoặc phòng đã khóa. Walk-in tính giờ cần trả động qua ScheduleRules cùng snapshot; đọc không ghi NoShow/audit.

Các đường preview/ghi từ màn hình kiểm tra cả Session.View và Session.Extend trong transaction tương ứng, rồi dùng xử lý gia hạn 6c.1. API gia hạn lõi ExtendStaff/PreviewExtensionStaff vẫn chỉ yêu cầu Session.Extend, giữ hợp đồng đã kiểm tra ở 6c.1. Parser RoomSession được dùng chung giữa đọc phiên và các service nhận/gia hạn để không lệch snapshot/thời gian.

## Trạng thái form và lỗi

- Đổi bộ lọc hoặc tải lại xóa danh sách/chi tiết/kết quả cũ; chọn phiên khác xóa xác nhận/kết quả trước.
- Trong xác nhận hoặc gửi, khóa bộ lọc/chọn dòng/thời lượng. Lưu ID/thời lượng/observedEnd riêng, không lấy từ ô đang hiển thị khi gửi.
- Form cũ bị chặn nếu giờ trả đã đổi, phiên đã hoàn tất hoặc có booking mới gây trùng. Trả giới hạn giờ không lộ thông tin khách khác; cần tải lại trước khi gửi tiếp.
- Gửi lặp trong khi đang lưu hoặc dùng lại xác nhận cũ không cộng lần thứ hai. Muốn gia hạn thêm cần một lượt preview/xác nhận mới.
- Mất quyền/phiên hết hiệu lực xóa dữ liệu riêng tư và xác nhận. Quyền chỉ xem vẫn xem được khi tải lại, không được gia hạn. Vẫn đóng cửa sổ được sau khi thao tác nền kết thúc.
- Nếu commit đã thành công nhưng tải lại lỗi/mất View, vẫn báo đã gia hạn, xóa trạng thái cũ và yêu cầu tải lại/đóng; không báo thất bại để người dùng gửi lại.

## Kiểm tra

Debug/Release build vào VerifyDebug/VerifyRelease, intermediate riêng VerifyExtensionDebug/VerifyExtensionRelease. 14 bộ kiểm tra trên database tạm: Sessions, Extension, WalkIn, CheckIn, Availability, Calendar, Reservations, GuestCalendar, Authentication, Foundation, StaffReservations, Rooms, Customers và AuthenticationUi.

Verify-Sessions kiểm tra View độc lập quyền khác, View/Extend hiện hành, chuẩn hóa SĐT/status, một mốc đọc, snapshot/actual/ticks, walk-in deadline động, Completed/phòng khóa, read-only và thu hồi phiên. Đường gia hạn từ UI bị chặn khi mất View, trong khi API lõi vẫn độc lập View.

UI render 69 ảnh; đã xem trực tiếp danh sách/xác nhận/kết quả và menu thu nhỏ. Kiểm tra menu Guest/Admin/empty, mở từ menu, +30 thành công/+60 bị giới hạn, giữ/hủy xác nhận, duplicate, snapshot giá dù danh mục đổi, stale observedEnd, booking mới sau preview, SĐT/status/kết quả cũ, walk-in/Completed, chỉ xem, mất Extend/View và mất View sau commit. Các cạnh tranh/rollback/biên ca/grace của service 6c.1 vẫn đạt.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Sessions.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
```

Không dùng database thật/ảnh phòng. Fixture Completed chỉ để kiểm tra đọc lịch sử, không phải checkout/hóa đơn đã hoàn thành.

## Tiếp theo

Guest Lookup Active và gia hạn theo SĐT: DTO riêng cho đúng số hiện hành, quyền public/quy tắc Guest riêng, không đưa Guest qua StaffSessionService hoặc ExtendStaff. Phần món/tiền tạm tính bổ sung cùng service tương ứng; giữ đầy đủ scope gọi/xác nhận/hủy món, checkout/hóa đơn, dashboard/quản trị RBAC và báo cáo. Không coi vận hành hoặc đồ án hoàn tất.
