# MySQL 04f — Menu, giỏ món và trạng thái đơn

Ngày 28/10/2026. Đối chiếu reference mục 16, 22–24, 44–45 và 55.4–55.6; giữ hợp đồng OrderService/DTO hiện có, Step6d1_OrderFoundation và các bước UI 6d.2. Đọc [bảng bàn giao](MySql_Migration_Status.md) trước phần kế tiếp.

## Đã chuyển và cách hoạt động

Thêm `MySqlOrderService`, cùng các đường đọc menu/preview/tạo/xem/phục vụ/hủy như SQLite. Thêm factory nội bộ `MySqlGuestReservationService.ForOrders`; App/UI/worker vẫn dùng SQLite, chưa nối provider MySQL. Không thay schema, palette hoặc dữ liệu thật.

| Đường gọi | Điều kiện và kết quả |
|---|---|
| ReadWorkspaceStaff | Order.View hoặc Order.Create hiện hành; Create-only chỉ phiên Active, View đọc thêm lịch sử Completed; metadata quyền cùng snapshot |
| ReadMenuGuest / PreviewGuest | SĐT chuẩn hóa hiện hành, đúng phiên Active; menu chỉ món đang bán; preview không lưu/chốt giá |
| ReadMenuStaff / PreviewStaff | Order.Create độc lập View/Confirm/Session.View/Service.Manage/Customer CRUD |
| CreateGuest | Đúng SĐT/Active/giờ mở cửa; tạo Pending, CreatedByUserId null, audit Guest |
| CreateStaff | Order.Create hiện hành; món **đã phục vụ**, tạo Completed và lưu actor/audit Staff; không cần Order.Confirm hoặc View |
| ListGuest | Chỉ các đơn trong phiên Active đúng SĐT hiện hành; không mở lịch sử phiên đã hoàn tất |
| ListStaff | Order.View độc lập quyền khác; đọc cả phiên Completed và giữ snapshot |
| ConfirmStaff | Order.Confirm; chỉ Pending trong Active → Completed sau phục vụ |
| CancelStaff / CancelGuest | Order.Cancel hoặc đúng SĐT hiện hành; chỉ Pending trong Active → Cancelled |

Không suy diễn actor null là Guest cho API Staff: Staff thiếu quyền/phiên hết hiệu lực bị từ chối; Guest là các API riêng có SĐT. DTO OrderMenu/OrderPreview/ServiceOrder/OrderSessionChoice không chứa CustomerId/tên/SĐT, RoomId/ReservationId/RoomSessionId/creator. Selector chỉ có mã phiên, mã phòng snapshot, trạng thái và giờ bắt đầu cần cho thao tác.

Giỏ đầu vào chỉ ServiceId/Quantity. Clone input, kiểm tra ít nhất một món, mỗi dòng 1–10; gộp dòng trùng, tổng một món không quá 10. Tên/giá phải đọc từ Services, từ chối thiếu/ngừng bán, lưu ServiceNameSnapshot/UnitPrice khi tạo. Preview tính decimal, giữ chính xác kể cả giá BIGINT tối đa nhân 10; không nhận tổng tiền/tên/giá từ client. Giá/tên thay sau preview được kiểm tra lại lúc gửi. Ngừng bán không hủy Pending cũ hoặc sửa snapshot, vẫn phục vụ/hủy được theo trạng thái phiên.

Tạo mới chỉ trong ca 09–12 và 13–23 UTC+7; đúng 12h/23h từ chối. Menu ngoài ca vẫn đọc được nhưng CanCreate=false và có lý do; preview/tạo bị chặn. Pending cũ được phục vụ/hủy ngoài ca nếu phiên còn Active. Quá ExpectedEnd/ReturnBy không tự đóng phiên; trong giờ mở cửa vẫn có thể nhận món. Completed/Cancelled không đổi tiếp, không có sửa item/số lượng hoặc xóa đơn. Mỗi lần gửi hợp lệ tạo một Order mới; không tự coi hai giỏ giống nhau là cùng request. UI chống bấm lặp là hợp đồng hiện có, không thêm khóa idempotency giả từ nội dung giỏ.

## Snapshot, transaction và thứ tự khóa

Đọc dùng RR read-only: quyền hiện hành/membership/phiên và dữ liệu trong cùng snapshot. Menu, preview, workspace dùng một clock; List đọc header/items trong cùng snapshot, không cần clock riêng. Không ghi audit, xử lý NoShow hoặc thay trạng thái khi đọc. Helper internal ReadOrders dùng lại cho Billing sau này; không là API bỏ qua guard Guest/Staff.

Writer Serializable kiểm tra quyền Staff trong transaction. Khám phá Room/Customer/Reservation/Session ID bằng kết nối riêng không khóa, rồi **Room → Customer → Reservation nguồn nếu có → RoomSession → Order hiện có khi đổi trạng thái**. Đọc lại ID, membership SĐT và Active sau khóa; đổi trạng thái còn đọc lại Order.RoomSessionId và Pending. Orders không đọc lịch/Confirmed candidates hoặc xử lý NoShow, nên không lấy candidate locks không liên quan; thứ tự các tài nguyên dùng chung giữ tương thích với writer booking/extension và checkout tương lai. Không advisory lock toàn app.

Tạo đơn đọc/khóa các Services theo ID tăng dần bằng SELECT của transaction Serializable, rồi mới lấy business clock và kiểm tra ca. Đây là điểm cần giữ khi port: không lấy giờ trước lúc chờ sửa menu, nếu không có thể nhận đơn tại 23h bằng giờ 22:59 cũ. Preview dùng cùng hàm đọc giỏ trong RR read-only nên không khóa menu hoặc chốt giá.

Header, toàn bộ items và audit cùng commit; lỗi item thứ hai hoặc audit rollback toàn bộ. Chuyển trạng thái dùng UPDATE có guard OrderId/RoomSessionId/Status=Pending, kiểm tra đúng một dòng và ghi audit cùng transaction/một now. Confirm/Cancel đồng thời chỉ một thao tác chuyển trạng thái; lần sau báo tải lại. MySQL 1213/1205 báo kiểm tra lại, không retry vô hạn; lỗi khác không bị catch-all nuốt mất. Transaction không kéo dài qua màn xác nhận của người dùng.

## Kiểm tra

Build VerifyOrdersMySqlDebug/Release đạt. **Verify-MySqlOrders online cả hai build đạt (exit 0)**. Log `%TEMP%/MusicBoxMySqlOrdersDebug_20261028.log` và `%TEMP%/MusicBoxMySqlOrdersRelease_20261028.log`. Không thay code/test sau hai lượt kiểm tra này.

Hồi quy Debug **32/32 bộ đạt**: 8 MySQL online (GuestSessions, Sessions, Services, Customers, Authentication, Foundation, CheckIn, WalkIn) và 24 SQLite/WPF (Orders, Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, Calendar, GuestCalendar, CheckIn, WalkIn, Extension, Sessions, GuestSessions, AuthenticationUi, Billing, Checkout, Workflows). Summary/exit code/log từng bộ tại `%TEMP%/MusicBoxOrdersRegression_05696225bde5421d8e9a65417cbeadde/results.json`. AuthenticationUi đạt 142 ảnh tại `%TEMP%/MusicBoxUi_392f545a78624eb4bd171e470953f45c`; không thay UI hoặc tuyên bố đã xem lại từng ảnh trong lượt port service này. Không chạy toàn bộ 44 bộ local hoặc mọi bộ MySQL; không dùng kết quả Billing/Checkout SQLite làm nghiệm thu MySQL Billing/Checkout.

`Tests/Verify-MySqlOrders.ps1` tạo database GUID riêng, dọn bằng tên regex đã kiểm tra. Phòng/user fixture dùng SQL, danh mục/sửa tên giá/phone/check-in/walk-in/booking và Order dùng service thật. Completed fixture chỉ kiểm tra guard/history; **chưa phải nghiệm thu checkout**. Clock có điều khiển dùng cho các ca biên riêng, không tuyên bố là một timeline vận hành liên tục. Các nhóm kiểm tra:

- Create/View/Confirm/Cancel độc lập, quyền hiện hành/revoke/stamp, Staff null không Guest fallback, public DTO.
- Menu chỉ đang bán, preview/cart read-only, gộp 1–10/invalid/null/missing/inactive, giá server thay sau preview và BIGINT/decimal.
- Guest Pending/Staff Completed, creator/audit, snapshot sau đổi/ngừng bán; hai giỏ gửi mới không tự dedup.
- Ca 09/12/13/23 với ticks, xử lý Pending ngoài ca, Active booking quá giờ vẫn gọi được trong ca; Completed/history/terminal states.
- Lỗi item/audit rollback header/items/trạng thái/log; FK/unique/quantity/price guard.
- Ba lượt ConfirmStaff–CancelGuest cạnh tranh, một chuyển trạng thái/một log; catalog writer thật chờ đơn đã khóa giỏ, giữ snapshot cũ.
- RR preview/menu/workspace khi đổi giá/phone/quyền; lần đọc sau thấy dữ liệu mới.
- Chờ Room qua 23h và Service qua 12h lấy giờ mới; đổi SĐT khi tạo/hủy đang chờ Room phải kiểm tra lại.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlOrders.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyOrdersMySqlDebug
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlOrders.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyOrdersMySqlRelease
```

## Tiếp theo và ranh giới

Tiếp **Billing/tiền tạm tính**: actual ticks/HourlyRate snapshot, decimal/AwayFromZero đến đồng một lần, chỉ món Completed, đọc không đóng phiên hoặc giữ giá. Sau đó Checkout/Invoice và các ca cạnh tranh order/confirm/cancel/extension với **checkout MySQL thật**; không dùng fixture Completed để đánh dấu xong. Giữ dashboard/ma trận quyền/audit/báo cáo/Excel/lịch sử, backup/import/đối chiếu DB + Content/UI/cutover/ERD/từ điển/bản xuất/demo.

Schema giữ SQLite v8/MySQL v1; chưa nhập DB/Content thật hoặc đổi App/worker/UI. Chỉ Compile entry cho MySqlOrderService vào commit, giữ phần định dạng csproj có sẵn riêng.
