# Bước 6c.3a — service phiên Active và gia hạn cho Khách

Ngày 08/10/2026. Đối chiếu mục 14/21/22/38/43/55.4 của bản tham chiếu nghiệp vụ. Đây là bước service; màn hình Tra cứu SĐT hiện vẫn chỉ có booking/hủy booking. Nối giao diện ở 6c.3b.

## Đã sửa và cách hoạt động

- Thêm `GuestSessionService`, đường public riêng, không gọi `StaffSessionService` hoặc `ExtendStaff`, không cần tài khoản hoặc quyền nhân viên.
- `Lookup(phoneNumber)` chuẩn hóa SĐT chung, chỉ đọc phiên Active gắn với số **hiện hành** của Customer; không tìm theo số cũ, không trả Completed. Không có kết quả thì trả null. Read transaction dùng một mốc `IClock`, không ghi NoShow/audit hoặc tự checkout.
- DTO `GuestSession` chỉ có mã phiên dùng khi gửi thao tác, snapshot mã phòng/loại/giá, giờ nhận, giờ trả dự kiến/giới hạn trả, mốc đọc và khả năng gia hạn. Không trả CustomerId, RoomId, ReservationId, tên/SĐT khách, entity nội bộ hoặc lịch sử hóa đơn.
- Booking Active có giờ trả dự kiến; walk-in có giới hạn trả động từ ca và lịch phòng/khách qua `ScheduleRules`. Phiên quá giờ vẫn đọc được nhưng không cho gia hạn. Không tự kéo dài hold.
- `PreviewExtension(phoneNumber, sessionId, minutes)` đọc lại quan hệ phiên–khách–SĐT và Active trong read transaction, tái sử dụng `AvailabilityService.CheckExtensionAt`. Chỉ trả khả năng gia hạn, mốc giờ và lý do/giới hạn, không thông tin khách khác. ID không tồn tại hoặc của số khác có cùng thông báo.
- `Extend(phoneNumber, sessionId, observedEnd, minutes)` lấy writer transaction rồi đọc lại số hiện hành/Active/nguồn booking/ExpectedEndTime. `observedEnd` chặn xác nhận cũ hoặc gửi lặp. Sau khi lấy writer mới đọc clock; chỉ +30/+60 trước hoặc đúng giờ trả, phần thêm cùng ca và không chồng lịch Room hoặc Customer. Walk-in không gia hạn.
- Cập nhật ExpectedEndTime và audit `Session.Extend`, ActorType Guest/UserId null trong cùng transaction. Lỗi audit rollback cả giờ trả. Giữ nguyên Reservation, actual và snapshot giá/phòng; không có phí gia hạn riêng. Kết quả trả DTO public của phiên đã cập nhật.

Schema giữ **v6**, không migration. Không dùng database thật hoặc tự seed phòng thật.

## Kiểm tra

Build Debug/Release vào `bin/VerifyDebug` và `bin/VerifyRelease`, dùng intermediate riêng để không ảnh hưởng ứng dụng Debug đang mở.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-GuestSessions.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
```

`GuestSessionsChecks.cs` dùng database tạm riêng, kiểm tra:

- Chuẩn hóa SĐT, số hiện hành, ID phiên của số khác, Active/Completed, DTO không lộ các trường nội bộ, read chỉ đọc/một clock.
- Actual/Expected giữ giây và ticks, snapshot không đổi khi danh mục đổi giá/tên; walk-in trả hạn động theo booking của khách ở phòng khác.
- +30/+60, gia hạn nhiều lần với giờ trả mới, stale/gửi lặp, đúng giờ trả và quá một tick, giới hạn ca/nghỉ trưa/23 giờ, trùng Room hoặc Customer, đúng grace không ghi NoShow.
- Audit Guest và rollback khi trigger làm audit thất bại; Reservation giữ EndTime cũ.
- Ba vòng cạnh tranh Guest/Staff gia hạn cùng observedEnd và gia hạn/booking mới theo Room hoặc Customer: chỉ một writer thành công.
- Chờ writer qua giờ trả: clock chỉ được đọc sau lock và từ chối gia hạn quá giờ.

Đã chạy đạt 16 bộ: GuestSessions, Sessions, Extension, WalkIn, CheckIn, Availability, Calendar, Reservations, GuestCalendar, GuestLookup, Authentication, Foundation, StaffReservations, Rooms, Customers, AuthenticationUi. UI cũ render 69 ảnh; không thêm màn hình trong bước này.

## Tiếp theo

6c.3b nối vào Tra cứu SĐT: hiện phiên Active, chọn +30/+60, preview/xác nhận/giữ, chặn gửi lặp, đổi số xóa kết quả cũ và tải lại sau commit. Không gọi đường nhân viên hoặc thêm OTP/account/mã truy cập. Tiền tạm tính, menu/order, hủy Pending, checkout/hóa đơn/dashboard/RBAC/báo cáo vẫn giữ trong scope và triển khai cùng các luồng tương ứng; chưa hoàn tất Guest Lookup mục 22 hoặc bước vận hành.
