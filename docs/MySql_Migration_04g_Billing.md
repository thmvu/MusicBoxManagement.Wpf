# MySQL 04g — Tiền tạm tính cho Khách và nhân viên

Ngày 28/10/2026. Đối chiếu reference mục 17, 22, 25–26, 55.2–55.4 và hợp đồng BillingService/SessionBill hiện có. Đọc [bảng bàn giao](MySql_Migration_Status.md) trước phần kế tiếp. Đây là port service tiền tạm tính, chưa phải checkout/hóa đơn hoặc đổi provider của ứng dụng.

## Đã chuyển và cách hoạt động

Thêm `MySqlBillingService.ReadGuest(phone, sessionId)` và `ReadStaff(actor, sessionId)`, giữ DTO SessionBill. Factory ForBilling nội bộ từ MySqlGuestReservationService và MySqlStaffSessionService giữ cùng database/IClock cho bước wiring sau.

- Guest: chuẩn hóa SĐT, chỉ đọc phiên Active thuộc **SĐT hiện hành**, số cũ/người khác/phiên không tồn tại/Completed bị chặn. Mã phiên khác người và mã không tồn tại cùng thông báo khi cùng SĐT. Không thêm account/OTP hoặc gọi đường Staff.
- Staff: cần **Session.View hiện hành**, độc lập Order.View/Service.Manage/Customer CRUD/Invoice.View/CheckOut. Staff null, bị khóa, đổi stamp, thu hồi quyền hoặc logout không được đọc; không fallback Guest.
- SessionBill chỉ có mã phiên, snapshot phòng/loại/giá, actual/giờ tính, ticks/phút thực tế, tiền phòng/món/tổng và số đơn từng trạng thái. Không có CustomerId/tên/SĐT/RoomId/ReservationId/creator/danh sách đơn/hóa đơn lịch sử. Session.View cho xem số tổng của phiên, không mở đường đọc chi tiết món khi thiếu Order.View.

## Công thức giữ nguyên

Giờ tính lấy một lần từ IClock **sau khi đọc phiên và các đơn** trong cùng transaction, chuyển UTC và giữ giây/ticks. Chặn đồng hồ trước ActualStartTime. Không dùng giờ đặt hoặc ExpectedEndTime, hạn trả walk-in, ca nghỉ/đóng cửa để cắt tiền. Phiên Active sau giờ dự kiến hoặc qua ngày vẫn tiếp tục tính đến now; không tự checkout.

```text
UsedTicks = BillingEndTime - ActualStartTime
RoomCharge = làm tròn(HourlyRate snapshot × UsedTicks / ticks mỗi giờ), AwayFromZero đến đồng
ServiceCharge = tổng Quantity × UnitPrice snapshot của Order Completed
TotalAmount = RoomCharge + ServiceCharge
```

RoomCharge làm tròn **một lần**, không block/phút nguyên/tiền tối thiểu/phí gia hạn/VAT/phụ thu. Công thức phần nguyên/phần dư được giữ giống hệt SQLite để tránh tràn rate×ticks và sai mốc nửa đồng khi giá lớn. Production dùng decimal; BigInteger chỉ dùng làm phép tính đối chiếu độc lập trong test. UnitPrice được đổi sang decimal trước nhân số lượng và cộng, không dùng Int64/double làm tổng tiền.

Ví dụ giá 120.000đ/giờ: 80 phút = 160.000đ, 147 phút = 294.000đ, 90 giây = 3.000đ, đúng giờ nhận = 0đ tiền phòng. Hai món đã phục vụ giá snapshot 25.000đ cộng 50.000đ; Pending/Cancelled không cộng. Đổi danh mục giá/tên/ngừng bán không thay snapshot hoặc tiền của phiên/đơn cũ. Trả riêng CompletedOrderCount/PendingOrderCount/CancelledOrderCount.

## Snapshot và hợp đồng checkout sau này

ReadGuest/ReadStaff dùng **RR read-only** trên MySQL: quyền/membership, header phiên và toàn bộ header/items đơn trong cùng snapshot; không cache giữa các lần đọc. InnoDB MVCC cho phép Order writer thật commit khi một bill reader chưa kết thúc. Bill đang đọc giữ snapshot cũ; lần đọc mới thấy dữ liệu/quyền/SĐT đã commit. Không sao chép assertion khóa reader của SQLite sang MySQL.

Đọc bill không ghi audit/NoShow, tạo Order/Invoice, hủy Pending, chốt ActualEndTime hoặc giữ giá/tổng tiền. Không giữ transaction qua màn xác nhận của khách. Billing chỉ đọc số tiền phiên, không cộng thành doanh thu đã thu.

`CalculateActive(connection, transaction, sessionId)` là helper **internal** cho cả Preview và Confirm Checkout, cùng hợp đồng dùng chung của SQLite: yêu cầu transaction RR hoặc Serializable đang thuộc đúng connection và database của service; từ chối ReadCommitted vì MySQL không giữ một snapshot cho nhiều SELECT ở mức đó. Preview caller phải đọc **Session.CheckOut hiện hành trong cùng RR read-only** rồi tính tiền. Confirm caller phải Demand CheckOut trong Serializable, lấy khóa Room → Customer → Reservation nguồn → RoomSession → Orders rồi gọi calculator trong cùng writer. Không gọi ReadStaff để đòi thêm Session.View cho người chỉ có CheckOut. Calculator đọc dữ liệu cùng transaction (kể cả thay đổi chưa commit của caller), lấy một BillingEndTime sau dữ liệu và không tự Commit/Rollback. Confirm dùng giờ đó làm checkoutNow, không tin giờ hoặc tiền preview do client giữ.

Helper không tự cấp quyền hoặc lấy thay toàn bộ khóa cho caller. Guard isolation/scope không thay thế authorization/locking của Checkout. Tests shared reader/writer chỉ kiểm tra hợp đồng đọc/cùng transaction/clock, không chứng minh đã thu tiền hoặc hoàn thành checkout.

## Kiểm tra

Build VerifyBillingMySqlDebug/Release và VerifyBillingFinalDebug/Release đạt. **Verify-MySqlBilling bản cuối online Debug/Release đạt (exit 0)**, gồm helper dùng chung cho RR Preview và Serializable Confirm. Log `%TEMP%/MusicBoxMySqlBillingFinalDebug_20261028.log` và `%TEMP%/MusicBoxMySqlBillingFinalRelease_20261028.log`. Các lệnh dưới dùng build cuối.

Hồi quy Debug **34/34 bộ đạt** trên VerifyBillingMySqlDebug: 9 MySQL online (Orders, GuestSessions, Sessions, Customers, Authentication, Foundation, Services, CheckIn, WalkIn) và 25 SQLite/WPF (Billing, Orders, Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, Calendar, GuestCalendar, CheckIn, WalkIn, Extension, Sessions, GuestSessions, AuthenticationUi, Checkout, Invoices, Workflows). Summary/log/exit code tại `%TEMP%/MusicBoxBillingRegression_000b37aa9aa048c99131db2e2321db20/results.json`. AuthenticationUi đạt 142 ảnh tại `%TEMP%/MusicBoxUi_a5f9310db7f844ae86626623d444f60c`. Sau đối chiếu Preview checkout, điều chỉnh cuối chỉ mở helper mới cho RR cùng snapshot và thêm ca tương ứng; Billing được chạy lại online cả hai build Final, không sửa service SQLite hoặc các service MySQL cũ trong lượt điều chỉnh này. Không dùng các bài checkout SQLite hoặc writer fixture làm nghiệm thu checkout MySQL, không tuyên bố chạy mọi suite.

`Tests/Verify-MySqlBilling.ps1` dùng database `musicbox_test_<GUID>` riêng và regex kiểm tra tên trước dọn. Walk-in/booking/check-in, tạo/phục vụ/hủy món, đổi catalog/loại phòng/SĐT đi qua service MySQL thật. SQL fixture tạo phòng/tài khoản, đổi HourlyRate để thử số học, grant/revoke/lock/stamp và Completed guard. Writer fixture đổi Order trong transaction để kiểm tra calculator/rollback, **không giả Invoice hoặc gọi đó là checkout thật**. Clock có điều khiển dùng cho các ca độc lập, không tuyên bố là một timeline quán liên tục.

- Zero/1 tick/60–80–147 phút/90 giây, offset/UTC/actual ticks và một clock.
- Mốc nửa đồng ±1 tick, rate 1/120000/Int64.Max/Max−1, thời lượng đến DateTimeOffset.MaxValue; so sánh BigInteger oracle.
- Snapshot phòng/món sau đổi danh mục, Completed-only/status counts, tổng món vượt Int64, negative clock, không cap ở deadline/đóng cửa/ExpectedEnd.
- Current phone/public DTO/View-only/CheckOut-only không View/revoke/active/stamp/logout; không bypass Order.View bằng chi tiết đơn.
- Read-only giữ Pending/ActualEnd/NoShow/audit/order/invoice; chỉ phiên được yêu cầu, walk-in/booking/Completed guards.
- RR read đang mở và Order writer thật commit, bill cũ/bill mới; sửa SĐT/quyền khi reader đang mở và lần đọc kế tiếp.
- Helper đúng connection/database/RR hoặc Serializable, từ chối ReadCommitted; preview CheckOut-only trong cùng read snapshot, writer thấy thay đổi chưa commit rồi rollback/không tự commit, chờ Room rồi đọc thời điểm/món mới.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlBilling.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyBillingFinalDebug
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlBilling.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyBillingFinalRelease
```

## Tiếp theo

**Checkout/Invoice**: Confirm tính lại dữ liệu/giờ/tiền bằng calculator trong writer đã kiểm tra quyền/khóa, Cash/BankTransfer, hủy Pending + Session/Reservation Completed + Invoice snapshot/PaidAt/audit cùng commit; rollback/replay một hóa đơn và race checkout–order/confirm/cancel/extension bằng service MySQL thật. Sau đó đọc/in hóa đơn, dashboard/ma trận quyền/audit/báo cáo/Excel/lịch sử, backup/import/đối chiếu DB + Content/UI/cutover, ERD/từ điển/bản xuất/demo.

App/worker/UI vẫn SQLite v8, đích MySQL v1, chưa nhập dữ liệu thật/Content hoặc đổi palette/provider. Chỉ Compile entry MySqlBillingService trong csproj vào commit; định dạng có sẵn giữ riêng.
