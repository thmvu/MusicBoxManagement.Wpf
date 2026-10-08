# Bước 6c.3b — giao diện phiên và gia hạn cho Khách

Ngày 08/10/2026. Đối chiếu mục 14/21/22/38/43/55.4 của Reference_Web_ProjectPlan_v1.3.md; dùng service public 6c.3a, không gọi đường nhân viên.

## Đã làm và cách dùng

Ở chế độ Khách, mở **Tra cứu SĐT**, nhập số đầy đủ rồi Tra cứu:

- Tab **Booking** giữ danh sách Confirmed còn hạn và luồng hủy đúng điều kiện trước/đúng mốc hai giờ.
- Tab **Đang sử dụng** hiện phiên Active đúng SĐT hiện hành: mã phiên/phòng, loại và giá theo giờ đã chốt, giờ nhận thực tế, giờ trả dự kiến hoặc giờ cần trả động của walk-in. Không hiển thị lịch sử Completed/hóa đơn hoặc thông tin khách khác.
- Phiên từ booking chưa quá giờ có lựa chọn 30/60 phút và **Xem trước gia hạn**. Xác nhận ở cột phải hiện giờ trả cũ/mới và giới hạn sử dụng. **Giữ giờ trả** không ghi dữ liệu; **Xác nhận gia hạn** gọi GuestSessionService.Extend.
- Walk-in chỉ hiện hạn trả động, ẩn lựa chọn/nút gia hạn. Booking đã quá giờ không cho gia hạn; khách liên hệ nhân viên.

Giao diện giữ màu trung tính và nút vuông. Hai cột giúp thấy nút xác nhận ngay, kể cả cửa sổ tối thiểu; mỗi vùng có cuộn khi nội dung dài. Không thêm OTP/account/mã truy cập.

## Dữ liệu và bảo vệ thao tác

- GuestReservationLookup bổ sung ActiveSession là DTO public. Booking và Active đọc trong cùng read transaction và cùng mốc giờ, sau bước xử lý NoShow hiện có. GuestSessionService.Lookup độc lập vẫn chỉ đọc; helper nội bộ dùng chung cho đọc Active.
- GuestLookupViewModel lấy service phiên từ cùng database/IClock của GuestReservationService; không dùng StaffSessionService/ExtendStaff.
- Đổi SĐT xóa booking, lựa chọn, phiên và xác nhận cũ. Đang xử lý thì khóa nhập/tra cứu/chọn thời lượng và đóng để không đổi số giữa thao tác. Khi đang xác nhận, khóa tra cứu và các thao tác ghi khác; nút Giữ giờ trả cho phép quay lại.
- Preview so sánh giờ trả mới đọc với giờ trả đang hiển thị; nếu đã thay đổi thì yêu cầu tra cứu lại. Xác nhận dùng số đã tra cứu, ID phiên, observedEnd và thời lượng đã preview; transaction service kiểm tra lại số hiện hành/Active/nguồn booking/giờ/ca/lịch Room và Customer.
- Chặn nhấn xác nhận lặp. Lỗi preview/ghi hoặc phiên stale xóa kết quả cũ. Nếu commit thành công nhưng refresh lỗi, giữ thông báo **Đã gia hạn**, xóa dữ liệu và yêu cầu tra cứu lại, không đề nghị gửi lại xác nhận cũ.
- Giữ nguyên snapshot/Reservation, audit Guest cùng transaction; schema **v6**. Không có tiền tạm tính hoặc phí gia hạn riêng trong bước này.

## Kiểm tra

Build Debug/Release vào bin/VerifyDebug và bin/VerifyRelease; không đóng ứng dụng Debug của người dùng. Toàn bộ kiểm tra dùng database tạm riêng.

16 bộ đạt: GuestSessions, GuestLookup, Sessions, Extension, WalkIn, CheckIn, Availability, Calendar, Reservations, GuestCalendar, Authentication, Foundation, StaffReservations, Rooms, Customers, AuthenticationUi.

```powershell
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
```

AuthenticationUiChecks thêm kiểm tra nút/form thật: đọc cả Confirmed/Active, chuẩn hóa SĐT, giữ actual giây/ticks và giá snapshot, chọn 30/60, preview/giữ/xác nhận, gửi lặp/audit Guest, nhân viên gia hạn sau preview, booking mới sau preview, đổi số trong database trước xác nhận, đổi ô số xóa phiên/xác nhận, Completed stale, walk-in/hạn trả động và lỗi NoShow khi refresh sau commit gia hạn. Kiểm tra vị trí nút xác nhận không cần cuộn ở cửa sổ tối thiểu.

Render **76 ảnh**, gồm 7 ảnh mới: guest-session-active, guest-session-confirm, guest-session-confirm-compact, guest-session-extended, guest-session-conflict, guest-session-walkin-compact, guest-session-refresh-failed. Đã xem ảnh xác nhận ở kích thước thường/tối thiểu và màn booking; chỉnh xác nhận sang cột phải sau lượt xem đầu tiên.

## Phần tiếp theo

Nền tảng gọi món/Order và snapshot OrderItem theo mục 23 cùng quy tắc Guest Pending/Staff Completed/xác nhận-hủy. Tiền tạm tính được bổ sung cùng dữ liệu món và quy tắc tính tiền; checkout/hóa đơn/dashboard/RBAC/báo cáo còn ở plan. Chưa coi Guest Lookup mục 22 hoặc bước vận hành đã hoàn tất. Checkout/gia hạn đồng thời sẽ kiểm tra khi có service checkout thật.
