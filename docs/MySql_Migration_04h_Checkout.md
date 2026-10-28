# MySQL 04h — Xác nhận thanh toán và tạo hóa đơn

Ngày 28/10/2026. Đối chiếu reference mục 13, 17, 24–28, 46, 54, 55.4–55.6; giữ hợp đồng CheckoutService/Invoice/CheckoutPreview/CheckoutSessionChoice của SQLite và Step7a1_CheckoutFoundation. Đọc [bảng bàn giao](MySql_Migration_Status.md) trước phần kế tiếp. Đây là service MySQL, chưa đổi UI/provider hoặc nhập dữ liệu thật.

## Các đường gọi và quyền

Thêm `MySqlCheckoutService.ListActiveStaff`, `PreviewStaff` và `ConfirmStaff`. Cả ba cần **Session.CheckOut hiện hành**, độc lập Session.View/Order.View/Invoice.View/Report.View/Customer CRUD. Không có Guest endpoint hoặc fallback Staff → Guest. Null actor, mất quyền, account khóa, đổi stamp hoặc logout bị từ chối, kể cả khi replay hóa đơn đã có.

Selector chỉ trả mã phiên Active, mã phòng snapshot và giờ nhận; không CustomerId/tên/SĐT/RoomId/ReservationId. Preview trả SessionBill và các món Completed snapshot để nhân viên kiểm tra thu tiền; đây là phạm vi quyền CheckOut, không mở danh sách lịch sử hóa đơn. Receipt từ Confirm/replay chưa phải API tìm/in Invoice; phần đó sẽ cần Invoice.View/Print ở bước tiếp.

List/Preview dùng **RR read-only**. Quyền, phiên, bill và danh sách món trong cùng snapshot; preview dùng một IClock từ Billing sau khi đọc dữ liệu. Không chốt ActualEndTime, hủy Pending, tạo Invoice/audit, xử lý NoShow hoặc giữ số tiền. InnoDB MVCC cho phép writer Order/Checkout thật commit khi một preview còn mở; bill và danh sách món của preview cũ vẫn phải cùng snapshot, lần đọc sau thấy trạng thái mới.

## Confirm — một transaction, một thời điểm chốt

1. Mở Serializable, Demand CheckOut hiện hành. Khám phá Room/Customer/Reservation từ kết nối riêng không khóa để biết tài nguyên cần khóa.
2. Khóa **Room → Customer → Reservation nguồn nếu có → RoomSession**; đọc lại ID để chặn nguồn thay đổi. Kiểm tra actor/quyền lại sau chờ tài nguyên, kể cả logout trong bộ nhớ. Checkout không tính lịch hoặc NoShow nên không lấy Confirmed candidate locks không liên quan.
3. Đọc Invoice đã có sau khóa: có thì trả đúng bản cũ, không đọc clock/tính lại tiền/đổi PaymentMethod/ghi audit. Replay giữ quyền hiện hành; giá trị payment gửi lại không thay hóa đơn cũ.
4. Nếu chưa có Invoice, yêu cầu Active và PaymentMethod chính xác `Cash` hoặc `BankTransfer`. Khóa các Orders theo ID tăng dần, lấy tên thu ngân từ access trong transaction.
5. Gọi Billing.CalculateActive cùng connection/transaction; đọc actual/HourlyRate snapshot và toàn bộ món rồi mới lấy **checkoutNow** một lần. Tính decimal/AwayFromZero theo 04g, chỉ Completed, không tin tiền/giờ của preview. Không giới hạn checkout theo ca/ExpectedEnd/ReturnBy.
6. Hủy toàn bộ Pending, kiểm tra số dòng đúng số đơn đã khóa; chuyển Reservation nguồn từ CheckedIn → Completed với guard ID/phòng/khách, rồi Session Active → Completed và ActualEndTime=checkoutNow.
7. Tạo Invoice unique theo RoomSessionId/InvoiceNumber, lưu tiền chốt, PaymentMethod, actor/tên thu ngân snapshot và PaidAt=checkoutNow. Tiền dùng parameter decimal và đọc bằng GetDecimal từ **DECIMAL(29,0)**, không dùng cách đọc TEXT của SQLite hoặc double/Int64 làm tổng.
8. Ghi audit Staff cho từng Pending bị hủy, Session.CheckOut và Invoice.Create cùng checkoutNow; commit rồi trả receipt. Không sửa giờ booking/ExpectedEnd/actual start/đơn giá hoặc snapshot món.

Mã hóa đơn giữ mẫu `MB-yyyyMMdd-sessionId` (ID ít nhất sáu chữ số), ngày theo UTC+7 của PaidAt; PaidAt/ActualEndTime lưu UTC giữ ticks. Tên thu ngân lúc thu tiền không đổi khi tài khoản sửa tên sau này. Phòng/khách được giải phóng bằng trạng thái Completed của phiên; không cập nhật một cờ “phòng trống” riêng hoặc tự checkout bằng timer.

## Lỗi, lặp và cạnh tranh

Lỗi tại source/session/invoice/audit rollback toàn bộ: phiên còn Active, nguồn CheckedIn, Pending chưa hủy, không Invoice hoặc audit ghi dở. Retry hợp lệ mới tạo hóa đơn. Không yêu cầu ID tự tăng liên tiếp sau rollback. MySQL 1062/1213/1205 thành thông báo kiểm tra hóa đơn trước khi xác nhận lại, không tự retry vô hạn hoặc coi lỗi là thành công. Lỗi khác giữ nguyên để caller xử lý; không catch-all nuốt exception. Khi COMMIT có kết quả chưa rõ, replay sau kiểm tra quyền đọc Invoice theo phiên giúp tránh tạo hóa đơn thứ hai; không suy diễn “chưa lưu” từ lỗi mạng.

Hai Confirm cùng phiên được serialize bằng khóa Room/Customer/Session; thao tác sau đọc hóa đơn đã có, không lấy clock mới hoặc đổi cách thu tiền. Order/create/confirm/cancel và gia hạn đã dùng cùng thứ tự tài nguyên. Nếu checkout trước: thao tác món/gia hạn chờ rồi bị chặn vì Completed. Nếu thao tác kia trước: checkout đọc dữ liệu mới; món Completed cộng, Pending hủy, Cancelled bỏ; gia hạn không thêm phí riêng hoặc làm tiền dựa ExpectedEnd.

## Kiểm tra và ranh giới bằng chứng

Build VerifyCheckoutMySqlDebug/Release đạt. **Verify-MySqlCheckout online cả hai build đạt (exit 0)**. Log `%TEMP%/MusicBoxMySqlCheckoutDebug_20261028.log` và `%TEMP%/MusicBoxMySqlCheckoutRelease_20261028.log`; không thay code/test sau hai lượt kiểm tra này.

Hồi quy Debug **40/40 bộ đạt**: 13 MySQL online (Billing, Orders, GuestSessions, Sessions, CheckIn, WalkIn, Reservations, Availability, Calendar, Foundation, Authentication, Customers, Services) và 27 SQLite/WPF (Checkout, Billing, Orders, Invoices, InvoicePrinting, Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, Calendar, GuestCalendar, CheckIn, WalkIn, Extension, Sessions, GuestSessions, AuthenticationUi, Dashboard, Workflows). Summary/log/exit code tại `%TEMP%/MusicBoxCheckoutRegression_b662323f67d24e83ad3eb96d9194e24e/results.json`. AuthenticationUi 142 ảnh tại `%TEMP%/MusicBoxUi_73bb52d0b71d459f8c293f48e615c96a`; InvoicePrinting SQLite dùng máy in giả, ảnh `%TEMP%/MusicBoxPrint_839d5ce2c6f34de98a363e90d31e030d`. Không thay UI/xem lại từng ảnh hoặc gửi máy in thật; kết quả in SQLite không nghiệm thu in hóa đơn MySQL. Không tuyên bố đã chạy mọi suite hoặc mọi tổ hợp nghiệp vụ.

`Tests/Verify-MySqlCheckout.ps1` tạo database GUID riêng, dọn bằng tên regex đã kiểm tra. Room/user/role/catalog fixtures dùng SQL hoặc service seed. **Booking → check-in/walk-in → món → checkout → Invoice và trạng thái Completed đều qua service MySQL thật**, không INSERT Invoice hoặc UPDATE Completed để giả thanh toán. SQL nghiệp vụ ngoài service trong bài này chỉ đọc/đổi catalog metadata/quyền, và trigger lỗi hoặc câu UPDATE/DELETE Invoice bị trigger từ chối. Các ca chính:

- CheckOut-only selector/preview/confirm, không cần View/Order/Invoice rights; public selector không PII, quyền hiện hành/revoke/lock/stamp/logout/replay.
- Preview read-only và tạm tính khác Confirm do thêm thời gian/món Completed; actual/giá snapshot/ticks/UTC/cashier snapshot, hai phương thức, invalid method, negative clock, zero charge và qua đóng cửa/qua ngày UTC+7.
- DECIMAL tổng vượt Int64, giữ snapshot khi sửa catalog; Pending/source/session/invoice/audit cùng kết quả, Room/Customer dùng được cho lượt mới.
- Trigger lỗi lần lượt source, session, invoice, Order.Cancel audit, Session.CheckOut audit và Invoice.Create audit: rollback toàn bộ rồi retry thành công.
- Hai checkout đồng thời: cùng InvoiceId/payment/tổng, một invoice và tổng một lần đọc clock; replay không log/tính lại. Trigger cấm UPDATE/DELETE Invoice được giữ.
- **14 ca hai thứ tự**: checkout với CreateGuest/CreateStaff/Confirm/CancelGuest/CancelStaff/ExtendStaff/ExtendGuest bằng writer thật. Không Pending sau Completed, tiền và ExpectedEnd đúng theo bên đi trước.
- Preview đang mở và Order writer thật commit trước khi đóng reader: bill và CompletedOrders không trộn snapshot. Preview đang mở trong lúc checkout thật hoàn tất, sau đó đọc mới bị chặn.
- Chờ Order writer giữ Room rồi checkout lấy clock và món mới; logout khi đang chờ Room không ghi thanh toán. Cuối bài không Completed thiếu Invoice, Pending trong Completed hoặc booking nguồn chưa Completed.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlCheckout.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyCheckoutMySqlDebug
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlCheckout.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyCheckoutMySqlRelease
```

## Tiếp theo

**Đọc/tìm/in Invoice** theo View/Print: tiền chốt/room-order-cashier snapshots, lọc PaidAt Việt Nam/mã/SĐT hiện hành, quyền hiện hành và dữ liệu lịch sử bất biến. Tiếp dashboard/ma trận quyền/audit/báo cáo/Excel/lịch sử khách, worker/provider/UI trên MySQL, backup/import/đối chiếu DB + Content/cutover/phục hồi; giữ ERD tổng thể/từ điển/bản xuất/demo.

App/worker vẫn SQLite v8, đích MySQL v1. Chưa nhập dữ liệu thật/Content, wiring UI hoặc thử máy in thật. Không thay palette/schema/provider; chỉ Compile entry MySqlCheckoutService trong csproj vào commit, giữ định dạng có sẵn riêng.
