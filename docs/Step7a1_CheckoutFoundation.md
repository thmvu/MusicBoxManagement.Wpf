# Bước 7a.1 — Nền tảng checkout và hóa đơn

Hoàn thành ngày **11/10/2026**. Đối chiếu plan WPF bước 7, use case P01 và tham chiếu mục 13/17/24–28/32/46/54/55.4–55.6. Giữ WPF .NET Framework 4.7.2 + SQLite. Schema hiện **v8**.

## Đã sửa

- `InvoiceSchema` thêm bảng Invoices: unique RoomSessionId/InvoiceNumber, FK phiên/người xử lý, Cash/BankTransfer, tên người xử lý snapshot, tổng tiền và PaidAt UTC. Có index PaidAt để truy vấn sau này; trigger chặn sửa/xóa hóa đơn.
- Nâng cấp v7 → v8 trong transaction khởi tạo chung, giữ tài khoản/quyền/phòng/ảnh/khách/booking/phiên/đơn món và snapshot cũ. Không tạo hóa đơn hoặc doanh thu giả cho dữ liệu Completed cũ. Không seed phòng, không sửa database thật để thử.
- `Invoice` là kết quả thanh toán đã chốt; `CheckoutPreview` chứa bill hiện tại và danh sách Order Completed snapshot để UI xác nhận ở bước sau.
- `CheckoutService.PreviewStaff` theo **Session.CheckOut**, độc lập Session.View/Order.View/Invoice.View. Đọc bill và món Completed cùng read transaction/một clock, trả số Pending trong bill; không ghi ActualEndTime, hủy món, tạo invoice/audit hoặc giữ giá.
- `CheckoutService.ConfirmStaff(actor, sessionId, paymentMethod)` chỉ nhận hình thức thanh toán và ID phiên. Không nhận tổng tiền, thời lượng, đơn giá hoặc bản preview từ UI. Không có PreviewId/cache/thời hạn giữ giá.

## Confirm hoạt động như thế nào

Trong một writer transaction ngắn:

1. Kiểm tra Session.CheckOut hiện hành; tài khoản phải active, stamp đúng và chưa đăng xuất.
2. Nếu phiên đã có Invoice, trả lại hóa đơn đó. Vẫn cần quyền hiện hành; không đổi phương thức/người xử lý/PaidAt/tiền hoặc ghi audit lần nữa. Replay không đọc clock hoặc kiểm tra lại payment của yêu cầu mới vì dữ liệu đã chốt được giữ nguyên.
3. Với lượt mới, chỉ nhận đúng `Cash` hoặc `BankTransfer`; đọc lại phiên Active/orders và lấy thời điểm mới qua BillingService **sau writer lock**.
4. Tính tiền phòng từ actual/giá snapshot/giây-ticks, chỉ cộng Order Completed. Không cắt tiền ở ExpectedEndTime, ReturnBy walk-in hoặc giờ đóng cửa.
5. Hủy tất cả Pending; booking nguồn nếu có phải CheckedIn và chuyển Completed. Walk-in không có booking nguồn.
6. Ghi Session Completed/ActualEndTime và tạo Invoice đã thanh toán với PaidAt bằng chính ActualEndTime; snapshot tên người xử lý hiện hành.
7. Ghi Order.Cancel cho mỗi Pending bị hủy, Session.CheckOut và Invoice.Create cùng timestamp/transaction; commit rồi trả Invoice.

Mã hóa đơn hiện có dạng `MB-yyyyMMdd-sessionId` với ngày Việt Nam và ID phiên tối thiểu 6 chữ số, ví dụ `MB-20261011-000001`. ID phiên duy nhất cùng unique index chống trùng; không dùng số đếm hóa đơn trên UI.

Nếu validation/ghi Invoice/audit thất bại, rollback cả món/booking/phiên/hóa đơn/log. Phiên vẫn Active và Pending chưa bị hủy một phần. Không giữ lock khi nhân viên xem preview hoặc chờ khách trả tiền. Tổng cuối có thể tăng so với preview do thời gian chờ hoặc món mới được phục vụ.

Sau checkout, các service hiện có đọc được Completed trong lịch sử phiên; Guest Lookup không còn trả Active/booking nguồn đã hoàn tất. Unique Active Room/Customer được giải phóng, có thể nhận khách tiếp theo nếu các quy tắc khác hợp lệ.

## Lưu tiền trong SQLite

Đơn giá phòng/món vẫn là SQLite INTEGER như trước. Billing tính bằng .NET decimal; tổng hóa đơn lưu thành **TEXT số nguyên đồng chuẩn**, parse về decimal khi đọc. Không dùng SQLite REAL hoặc tham số decimal bị provider chuyển sang số thực, để tránh sai tiền và giữ chính xác tổng vượt Int64 trong bộ kiểm tra giá lớn.

CHECK chỉ nhận chuỗi số không âm, không thập phân/ký hiệu khoa học/zero thừa, trong phạm vi decimal. Tổng ba cột do service tính bằng decimal và lưu cùng transaction. Không dùng phép cộng/`SUM` SQLite trên các chuỗi tiền để lập báo cáo sau này vì có thể chuyển sang số thực; InvoiceService/ReportService phải đọc và tính tổng decimal chính xác. Đây là lựa chọn lưu trữ, không đổi nghiệp vụ VND.

## Kiểm tra

- Build Debug/Release tại `bin/VerifyDebug` và `bin/VerifyRelease`, giữ bản ứng dụng đang mở.
- **23 bộ kiểm tra đạt** trên SQLite/thư mục tạm: Checkout, Billing, Orders, Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, Calendar, GuestCalendar, CheckIn, WalkIn, Extension, Sessions, GuestSessions và AuthenticationUi.
- Checkout/Billing kiểm tra thêm trên Release. UI cũ render **100 ảnh** tại `%TEMP%/MusicBoxUi_7a1`; chưa thêm màn hình checkout.
- Verify-Checkout tạo booking/check-in/walk-in/orders bằng service thật; 90 giây + món Completed = 53.000 đ; preview giữ Pending. Sau thêm 30 giây và phục vụ Pending, Confirm chốt 104.000 đ; thay giá/tên danh mục không đổi snapshot cũ. Kiểm tra actual UTC ticks và tên thu ngân snapshot.
- Kiểm tra CheckOut-only không đòi View/Confirm/Invoice.View; chỉ có các quyền View không được checkout; thu hồi quyền, stamp đổi, khóa/đăng xuất chặn cả replay. Payment sai bị chặn với phiên chưa thanh toán.
- Cash/BankTransfer, zero duration không tính tối thiểu, walk-in tới sau 23:00 vẫn tính actual, món Pending bị hủy và audit đúng thời điểm. Checkout xong nhận walk-in tiếp theo bằng cùng Room/Customer qua service thật.
- Giá lớn hơn Int64 khi cộng món/thời lượng giữ nguyên toàn bộ đồng qua lưu/đọc/replay; constraint/FK/unique/UTC và trigger bất biến.
- Gây lỗi khi insert Invoice hoặc audit cuối cùng: toàn bộ source/session/Pending/invoice/log rollback. Booking nguồn sai trạng thái, clock trước actual, ID không tồn tại và Completed fixture không có hóa đơn đều từ chối; không bịa doanh thu cũ.
- Hai checkout cùng phiên trả một Invoice. Checkout tranh với Confirm Order, Cancel Guest, Extend Staff, CreateGuest Order và CreateStaff Order theo **cả hai thứ tự**. Writer đầu tiên quyết định; sau Completed không được phục vụ/hủy/gia hạn/tạo món thêm. Checkout đọc dữ liệu và clock mới sau khi chờ writer lock.
- Nâng cấp v7 có tài khoản/khách/booking/Active/Pending/Completed Order, kiểm tra giữ dữ liệu/snapshot, gọi Initialize lặp và rollback khi bảng Invoices xung đột.
- Các fixture hạ schema của Orders/Rooms/RoomTypes/Services đã bỏ Invoices trước bảng nguồn; assertions phiên bản hiện hành dùng CurrentSchemaVersion. Giữ assertions version cũ khi kiểm tra migration thất bại.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Checkout.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
```

## Giới hạn và bước kế tiếp

Đây là **service checkout thật**, chưa phải chức năng đã bấm được trên WPF. Chưa có selector/preview/xác nhận Cash–BankTransfer, màn xem/tìm/in hóa đơn, dashboard hoặc báo cáo doanh thu. Không coi UI bước 7 hoàn tất vì tests gọi service thành công.

Tiếp theo **7a.2 giao diện checkout**: entry theo Session.CheckOut hiện hành, hỗ trợ người chỉ có CheckOut độc lập Session.View; preview/tính lại/Completed items/cảnh báo Pending sẽ hủy, xác nhận đã nhận tiền bằng Cash/BankTransfer, busy/duplicate/stale/mất quyền/rollback/refresh lỗi sau commit và replay. Kết quả phải giữ Invoice đã chốt, không tự gửi lại thanh toán sau lỗi tải UI. Đường xem/in hóa đơn tách Invoice.View/Invoice.Print ở bước sau.

Giữ dashboard hoạt động/doanh thu quán, Admin quản trị tài khoản/role/ma trận RBAC/audit, báo cáo/Excel và lịch sử khách. Các Completed fixture cũ vẫn là fixture; Verify-Checkout mới là bằng chứng service hoàn tất thực sự.
