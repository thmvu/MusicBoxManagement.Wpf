# MySQL 04e.4 — Tra cứu, gia hạn và hủy dành cho Khách

Ngày 28/10/2026. Đối chiếu reference mục 9–10, 14, 22, 38, 43 và 55.4; giữ hợp đồng SQLite trong GuestSessionService/GuestReservationService và DTO public hiện có. Đọc [bảng bàn giao](MySql_Migration_Status.md) trước khi làm phần kế tiếp.

## Phạm vi và cách hoạt động

`MySqlGuestSessionService.Lookup(phone)` trả phiên Active thuộc **SĐT hiện hành đã chuẩn hóa**, hoặc null khi không có. DTO GuestSession chỉ chứa mã phiên, snapshot phòng/loại/giá, giờ thực tế/dự kiến, giờ cần trả và thời điểm kiểm tra; không chứa CustomerId/RoomId/ReservationId/tên/SĐT khách hoặc RoomSession nội bộ. Completed không được trả cho Guest. Walk-in vẫn được tra cứu, ReturnBy tính động từ ca gốc và lịch Room/Customer; không tự checkout.

`PreviewExtension(phone, sessionId, minutes)` là đọc thuần, RR read-only, cùng snapshot membership/phiên/lịch, một clock; không giữ chỗ, không xử lý NoShow hoặc ghi audit. `Lookup` riêng của phiên cũng đọc thuần. Không đòi quyền nhân viên, không gọi service Staff với actor giả, không tạo khách khi tra cứu số chưa có. Mã phiên của người khác và mã không tồn tại nhận cùng thông báo khi preview/gia hạn.

`Extend(phone, sessionId, observedEnd, minutes)` chỉ nhận +30/+60 cho Active từ booking, trước hoặc đúng ExpectedEnd. Cộng từ giờ trong database, giữ giây/ticks và chấp nhận observedEnd khác offset nhưng cùng thời điểm. Gia hạn tiếp dùng observedEnd mới; bản cũ/gửi lặp bị chặn. Availability chung kiểm tra cùng ca, lịch Room/Customer, bỏ nguồn và Confirmed hết grace; chỉ trả giới hạn giờ, không công khai khách khác. Walk-in không gia hạn, quá giờ không cộng vào mốc đã qua. Không sửa actual, snapshot, Reservation hoặc tạo phụ phí.

`MySqlGuestReservationService.Lookup(phone)` trả GuestReservationLookup gồm Confirmed còn hiệu lực **và ActiveSession thật**, không gán null giả. Booking sắp theo StartTime/ID, metadata CanCancel theo mốc Start−2h. Đây là hợp đồng lookup hiện có: gọi NoShow.ProcessExpired trong **writer riêng trước đọc**, audit System của maintenance không thuộc transaction đọc. Sau đó pin RR snapshot bằng truy vấn membership, lấy một read clock cho cả booking/Active/ReturnBy; lọc grace lần nữa theo clock đó kể cả có booking vừa hết hạn sau maintenance. Tổng cộng một clock cho maintenance và một clock cho snapshot đọc. Lỗi maintenance được trả về, không nuốt lỗi và giả kết quả rỗng.

`Cancel(phone, reservationId)` kiểm tra membership hiện hành, Confirmed còn hạn và chưa có session nguồn. Đúng Start−2h được hủy; sau một tick bị chặn và hướng dẫn liên hệ quán. Lưu Cancelled, lý do cố định `Customer cancelled online`, audit Guest/UserId null. Không biến CheckedIn/NoShow/Completed/Cancelled về trạng thái khác; không tự chạy NoShow khi hủy thất bại. Mã booking của người khác và mã không tồn tại cùng thông báo.

## Transaction, khóa và cạnh tranh

Writer khám phá ID ngoài transaction khóa, chỉ để biết tài nguyên cần khóa; dữ liệu đó không thay thế kiểm tra lúc lưu. Thứ tự Serializable: **Room → Customer → Confirmed candidates theo ID → Reservation nguồn → RoomSession (gia hạn)**. Cùng quy ước các writer đã port; không lấy khóa session trước Room/Customer. Candidate locks có thể khiến thao tác ở phòng khác phải chờ; không dùng advisory lock toàn app.

Sau khóa, đọc lại SĐT hiện hành/ID/status/observedEnd rồi mới lấy clock. Do vậy đổi SĐT trong lúc chờ không còn quyền dùng số cũ; chờ qua hạn hủy/ExpectedEnd không dùng giờ preview cũ. UPDATE có guard trạng thái và giờ cũ, kiểm tra đúng một dòng; thay đổi và audit Guest cùng commit. Lỗi audit rollback toàn bộ. MySQL 1213/1205 thành thông báo tra cứu lại, không retry vô hạn hoặc coi là thành công. Lỗi khác giữ nguyên để caller xử lý, không dùng catch-all nuốt lỗi.

Hai request cùng observedEnd, hoặc Guest/Staff cùng gia hạn, chỉ một lần cộng được ghi. Gia hạn và booking mới cùng Room hoặc Customer dùng writer thật để kiểm tra không giữ lịch chồng. Hủy đồng thời check-in chỉ một trạng thái được lưu. Hủy/NoShow kiểm tra cả hai thứ tự lấy khóa với writer thật.

## Kiểm tra và bằng chứng

Build `VerifyGuestMySqlDebug` và `VerifyGuestMySqlRelease` đạt. **Verify-MySqlGuestSessions online bản test cuối đạt ở cả Debug và Release (exit 0)**. Log tại `%TEMP%/MusicBoxMySqlGuestFinalDebug_20261028.log` và `%TEMP%/MusicBoxMySqlGuestFinalRelease_20261028.log`. Trước đó lượt Debug đầu cũng đạt; bản cuối thêm assertion dữ liệu sau race Room/Customer và thông báo từng nhóm ca, không đổi production code.

Hồi quy Debug **26/26 bộ đạt**: 8 MySQL online (Sessions, CheckIn, WalkIn, Reservations, Availability, Calendar, Foundation, Customers) và 18 SQLite/WPF (GuestSessions, GuestLookup, Extension, Sessions, CheckIn, WalkIn, Availability, Reservations, Calendar, GuestCalendar, Foundation, Authentication, Customers, StaffReservations, GuestBooking, StaffBooking, AuthenticationUi, Workflows). Tóm tắt/exit code/log từng bộ: `%TEMP%/MusicBoxGuestRegression_6b820d8e8eff48a68a3f879ffab44ec8/results.json`. AuthenticationUi render 142 ảnh tại `%TEMP%/MusicBoxUi_a512ea4d0e0b4c83bf758b70f0286574`; không có thay đổi giao diện ở lượt này. Không tuyên bố đã chạy lại toàn bộ 44 bộ SQLite/WPF hoặc mọi bộ MySQL.

`Tests/Verify-MySqlGuestSessions.ps1` tạo database `musicbox_test_<GUID>` riêng, dọn bằng tên đã kiểm tra regex. Booking/check-in/walk-in, Staff extension, sửa SĐT và NoShow đi qua service MySQL thật. SQL dùng tạo phòng fixture/đọc đối chiếu/reset giữa ca, thay danh mục snapshot và trigger lỗi audit. Một fixture Completed chỉ kiểm tra không lộ/không gia hạn; **không phải kiểm tra checkout**. Các ca chính:

- SĐT chuẩn hóa/hiện hành/số lạ, DTO public, mã nguồn người khác và mã không tồn tại.
- Lookup kết hợp Confirmed/Active cùng snapshot; đổi SĐT giữa các SELECT và lần đọc kế tiếp.
- +30/+60, offset/ticks, giữ actual/booking/snapshot, stale/repeat, exact end/overdue, ca 12h/23h, Room/Customer overlap và giới hạn không PII.
- Walk-in Active/ReturnBy động theo Room và Customer, từ chối gia hạn; Completed không lộ.
- Pure read không NoShow/audit; lookup kết hợp maintenance riêng, exact +15 và không log lặp.
- Hủy exact −2h và muộn một tick; trạng thái/lý do/audit Guest, rollback khi audit lỗi, đổi SĐT và CheckedIn.
- Duplicate Guest, Guest/Staff, booking Room/Customer, hủy/check-in, hai thứ tự hủy/NoShow bằng writer thật.
- Chờ Room qua ExpectedEnd/hạn hủy; đổi SĐT trong lúc chờ gia hạn/hủy.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlGuestSessions.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyGuestMySqlDebug
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlGuestSessions.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyGuestMySqlRelease
```

## Ranh giới và bước tiếp

Không đổi UI/App/worker/provider, SQLite v8/MySQL v1, không nhập database/Content thật. Chỉ thêm hai Compile entry tương ứng trong csproj; giữ phần định dạng có sẵn của người dùng ngoài commit.

Phần này chuyển **tra cứu booking/Active, hủy và gia hạn Guest**, chưa phải toàn bộ trang phiên Guest trên MySQL: menu/giỏ/Order và tiền tạm tính phải port ở nhóm Order/Billing tiếp theo, không trả tiền hoặc danh sách món giả để lấp chỗ. Cạnh tranh với checkout thật bổ sung khi port Checkout. Sau đó Invoice/dashboard/ma trận quyền/audit viewer/báo cáo/Excel/lịch sử khách, backup/import/đối chiếu database + Content/UI/cutover; ERD tổng thể/từ điển/bản xuất/demo vẫn giữ trong plan.
