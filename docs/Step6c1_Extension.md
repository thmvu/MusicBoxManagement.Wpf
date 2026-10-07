# Bước 6c.1 — service gia hạn phiên từ booking

Ngày 07/10/2026. WPF .NET Framework 4.7.2 + SQLite, schema giữ v6. Đối chiếu mục 14/38.4/43/55.4 của Reference_Web_ProjectPlan_v1.3.md. Chưa thêm nút/form gia hạn.

## Đã làm và hoạt động

RoomSessionService có hai đường nội bộ:

- `PreviewExtensionStaff(actor, sessionId, minutes)`: kiểm tra Session.Extend hiện hành trong read transaction, đọc phiên/lịch và lấy một mốc IClock. Trả có thể gia hạn hay không, giờ trả hiện tại/đề nghị, mốc tối đa và lý do. Không ghi dữ liệu/NoShow/audit.
- `ExtendStaff(actor, sessionId, observedEnd, minutes)`: lấy quyền ghi trước khi đọc, kiểm tra Session.Extend hiện hành, phiên Active từ Reservation và giờ trả chưa đổi so với observedEnd của form. Đọc clock sau khi lấy writer; cộng từ ExpectedEndTime trong database, kiểm tra lịch/ca, cập nhật ExpectedEndTime và audit Staff cùng transaction.

Quyền gia hạn độc lập Session.View, Reservation.View/Create/Cancel, Room.Manage và Customer CRUD. API chỉ cho nhân viên đã đăng nhập. observedEnd là giá trị form đã đọc để chặn form cũ/gửi lặp; không quyết định giờ gốc đem cộng. Cùng thời điểm có offset khác nhau vẫn được coi là cùng giờ. Gia hạn thêm lần nữa cần đọc giờ trả mới và gửi yêu cầu mới.

Chỉ nhận +30/+60 phút. Đúng ExpectedEndTime còn được gia hạn, muộn một tick thì từ chối. Walk-in, Completed hoặc phiên không tồn tại bị chặn. Giữ giây/ticks, lưu UTC và không ép về slot đặt phòng.

AvailabilityService.CheckExtensionAt dùng khoảng giữ lịch của ScheduleRules để kiểm tra phần thêm cho cả Room và Customer, bỏ qua phiên nguồn; Confirmed hết đúng grace 15 phút không chặn dù worker chưa ghi NoShow. Walk-in không tạo hold tương lai; unique Active Room/Customer đã bảo vệ sử dụng thực tế.

Giới hạn tối đa là cuối ca của phiên hoặc mốc giữ lịch tiếp theo của Room/Customer. Nếu hold đã bắt đầu và còn phủ phần thêm, giới hạn là giờ trả hiện tại. Chạm đúng mốc kế tiếp/12h/23h hợp lệ; vượt mốc hoặc qua nghỉ trưa/sang ngày khác bị từ chối. Không lộ tên/SĐT/mã booking người khác. SessionExtensionException có MaximumEndTime để UI hiển thị giới hạn khi ghi bị từ chối.

Thành công chỉ thay ExpectedEndTime của phiên và ghi Session.Extend. Reservation, ActualStartTime, trạng thái và snapshot tên/phòng/giá giữ nguyên; không tạo phí gia hạn. Audit lỗi rollback cả cập nhật. Không tự retry hoặc cộng lần thứ hai khi kết quả lần trước chưa rõ.

Calendar và Availability đọc ExpectedEndTime nên tự sử dụng khoảng giữ lịch mới. Check-in replay cũng trả giờ trả đã gia hạn.

## Kiểm tra

Debug/Release build đạt vào bin/VerifyDebug và bin/VerifyRelease, dùng IntermediateOutputPath riêng obj/VerifyExtensionDebug và obj/VerifyExtensionRelease. Lần đầu build với obj chung gặp lỗi thiếu phần XAML sinh tự động; build bằng thư mục trung gian riêng đạt, không sửa UI hoặc đóng app người dùng.

11 bộ kiểm tra đạt trên database tạm: Extension, WalkIn, CheckIn, Availability, Calendar, Reservations, GuestCalendar, Authentication, Foundation, StaffReservations và AuthenticationUi. UI hiện có vẫn render 63 ảnh, chưa có ảnh gia hạn mới. Không chạm database thật hoặc ảnh phòng.

Verify-Extension kiểm tra:

- Quyền độc lập, quyền bị thu hồi/khóa user/đăng xuất; preview chỉ đọc; phút sai, phiên thiếu/Completed/walk-in.
- +30/+60/nhiều lần; form cũ bị chặn; UTC/offset/ticks; đúng giờ trả/quá một tick; chạm/vượt cuối ca sáng/tối.
- Trùng Room/Customer, chạm endpoint, hủy booking giải phóng lịch; trước/đúng grace; giới hạn không lộ thông tin khách.
- Reservation/giá/actual/snapshot giữ nguyên; audit Staff đúng actor/thời điểm; audit lỗi rollback.
- Calendar, availability Room/Customer và check-in replay dùng giờ trả mới.
- Hai yêu cầu cùng observedEnd tối đa một commit; gia hạn cạnh tranh booking Room/Customer trùng tối đa một luồng thành công; cạnh tranh hủy/NoShow/khóa phòng qua service thật. Chờ writer qua ExpectedEndTime dùng clock mới và từ chối.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Extension.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
```

Một số fixture thời gian/Completed được ghi trực tiếp để kiểm tra biên đọc; chưa có service checkout để kiểm tra cạnh tranh checkout/gia hạn. Không coi fixture đó là checkout đã hoàn thành.

## Làm tiếp

6c.2 thêm danh sách/chi tiết phiên nội bộ theo Session.View và UI gia hạn theo Session.Extend; preview/xác nhận/observedEnd/khóa gửi lặp/tải lại/mất quyền/stale và lỗi refresh sau commit. Không buộc Reservation.View để xem phiên. Guest Lookup Active và gia hạn qua SĐT vẫn giữ scope để triển khai tiếp, không đưa Guest qua ExtendStaff hoặc tiết lộ phiên của số khác.

Sau đó gọi món/tiền tạm tính, checkout/hóa đơn, dashboard/quản trị RBAC/báo cáo theo plan. Walk-in không có gia hạn.
