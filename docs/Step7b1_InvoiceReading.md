# Bước 7b.1 — Nền tảng tìm và đọc hóa đơn lịch sử

Hoàn thành ngày **12/10/2026**. Đối chiếu plan WPF bước 7/use case P02, tham chiếu mục 5/17/21/24/27–28/37/55. Giữ WPF .NET Framework 4.7.2 + SQLite schema **v8**, không migration hoặc sửa nghiệp vụ checkout.

## Đã sửa và cách hoạt động

- `InvoiceService.Search` theo **Invoice.View** hiện hành; lọc mã hóa đơn đầy đủ, FromDate–ToDate và SĐT hiện hành. Các bộ lọc kết hợp AND, bỏ trống thì không giới hạn trường đó. Mã trim/chuyển chữ hoa, khớp chính xác, không wildcard; SĐT dùng PhoneNumberNormalizer chung.
- Ngày lọc theo **PaidAt**, không theo giờ đặt hoặc giờ nhận. Khoảng `[đầu FromDate, đầu ngày sau ToDate)` theo UTC+7, bao gồm ngày cuối; DateTime.Kind/giờ lẻ trong input không đổi ý nghĩa ngày. Chặn khoảng đảo/ngày kết thúc ngoài giới hạn DateTime và mã dài hơn 50 ký tự. Dữ liệu UTC chuẩn được so sánh bằng chuỗi ISO cố định; không chuyển tiền sang REAL.
- `InvoiceSummary` trả mã/ID hóa đơn, mã phòng snapshot, PaidAt, hình thức và tổng đã chốt. Sort PaidAt giảm dần rồi InvoiceId giảm dần. Không đưa tên/SĐT/ID khách, RoomId hoặc danh sách khách vào DTO chỉ để chọn hóa đơn; SĐT chỉ là bộ lọc nội bộ qua CustomerId.
- `InvoiceService.Read` trả `InvoiceDetails`: tên cửa hàng cố định Music Box, header Invoice đã chốt, mã/loại/giá phòng snapshot, actual start/end, UsedTicks/UsedMinutes và các dòng món Completed snapshot. Giữ từng dòng theo Order/Item, không tự gộp món từ các lần gọi khác nhau.
- Tiền room/service/total lấy từ Invoice TEXT chuẩn và parse decimal chính xác. Thành tiền dòng = Quantity × UnitPrice bằng decimal; không SUM SQLite trên tiền TEXT hoặc dùng double. Giữ chính xác tổng vượt Int64.
- Không gọi BillingService để tính lại hóa đơn lịch sử. Không đọc RoomType/Service/User hiện tại để thay giá/tên phiếu cũ. Customer đổi SĐT giữ ID: tìm bằng số mới, số cũ không còn tìm được; mã hóa đơn vẫn dùng được.
- Chi tiết kiểm tra phiên Completed/end khớp PaidAt, tổng Completed items khớp ServiceCharge và room + service khớp TotalAmount. Dữ liệu bị lệch thì từ chối, không sửa/bịa lại giá trị đã chốt.
- `CheckoutService.ReadInvoice` chuyển thành reader nội bộ dùng chung; CheckoutService và InvoiceService đều kiểm tra quyền của đường gọi trong cùng transaction. Không thêm đường đọc public bỏ qua Invoice.View.

## Quyền xem và chuẩn bị in

Search/Read/ReadForPrint có read transaction riêng; quyền/tài khoản/stamp đọc hiện hành, header/session/items cùng một snapshot và clock UTC đọc một lần để ghi CheckedAt.

**Invoice.View độc lập** Session.View/Order.View/Customer.View/Session.CheckOut/Report.View. Người chỉ có quyền checkout không được dùng API này để duyệt hóa đơn lịch sử. Không có route Guest hoặc quyền báo cáo tổng doanh thu đi kèm.

Search/Read trả CanPrint để UI hiển thị nút. `ReadForPrint` kiểm tra lại **Invoice.View + Invoice.Print** ngay khi chuẩn bị dữ liệu in; không tin CanPrint cũ. Có Print nhưng mất View thì bị chặn; thu hồi Print vẫn xem được với View, nhưng không đọc đường chuẩn bị in. Đây mới là kiểm tra dữ liệu/quyền cho bước in, chưa mở PrintDialog hoặc gửi máy in.

## Kiểm tra

- Build Debug/Release vào bin/VerifyDebug và bin/VerifyRelease.
- **24 bộ kiểm tra đạt** trên SQLite/thư mục tạm: Invoices, Checkout, Billing, Orders, Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, Calendar, GuestCalendar, CheckIn, WalkIn, Extension, Sessions, GuestSessions, AuthenticationUi. Invoices kiểm tra thêm Release; UI cũ vẫn render **109 ảnh** tại `%TEMP%/MusicBoxUi_7b1`.
- Verify-Invoices tạo phiên/món/checkout qua service thật. 90 giây + ticks và 75.000 đ món Completed → hóa đơn 78.000 đ; không đưa Cancelled hoặc Pending đã hủy vào chi tiết.
- Kiểm tra View-only, Print cần View, thu hồi View/Print, checkout-only không duyệt lịch sử, Guest/null, stamp đổi, tài khoản khóa/đăng xuất; không cần các quyền phụ.
- Mã đủ/trim/chữ hoa-thường, mã không tồn tại/prefix/wildcard/injection không mở rộng kết quả; SĐT chuẩn hóa/đổi số/filters AND; range đảo/max/invalid phone/id bị chặn.
- Biên 00:00 Việt Nam nằm đúng ngày PaidAt; from/to mở một phía và DateTime.Kind Unspecified/Utc/Local giữ nghĩa ngày.
- Đổi tên/giá/loại/khóa phòng, ngừng bán/đổi tên-giá món, đổi tên thu ngân/khách: phiếu cũ vẫn đúng snapshot và các totals/PaidAt cũ. Không mất lịch sử vì danh mục inactive.
- Tổng lớn hơn Int64 vẫn đúng từng đồng trong danh sách/detail/dòng món. Dữ liệu actual end hoặc món bị sửa lệch trong fixture bị từ chối; không sửa ngầm tiền Invoice.
- Search/detail không ghi Invoice/Order/audit/schema. Active chưa checkout không được xem như hóa đơn.
- Search giữ read snapshot khi checkout thật đồng thời: kết quả đang đọc chỉ có bộ hóa đơn trước commit; lần Search sau mới thấy Invoice mới. Không trả bộ header/items hoặc quyền từ hai thời điểm khác nhau.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Invoices.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
```

## Giới hạn và bước tiếp theo

Đã có **service đọc/tìm thật**, chưa có menu/form tra cứu hóa đơn lịch sử hoặc bản in WPF. Checkout vẫn chỉ hiện receipt của thao tác vừa hoàn tất. Không coi P02 đã hoàn tất qua giao diện.

Tiếp theo **7b.2 UI hóa đơn** theo Invoice.View: bộ lọc mã/ngày PaidAt/SĐT, danh sách/chi tiết snapshots/tổng chốt, stale/đổi filter/mất quyền xóa dữ liệu cũ, entry cho View-only độc lập quyền vận hành. Sau đó in WPF theo Invoice.View + Invoice.Print và đọc lại bằng ReadForPrint tại thao tác in; không Guest history, sửa/xóa phiếu hoặc PDF server.

Giữ dashboard hoạt động/doanh thu quán, Admin quản trị tài khoản/role/ma trận/audit, báo cáo + Excel cùng bộ lọc và lịch sử khách. Đọc được hóa đơn không tự cấp báo cáo tổng hợp cho Staff.
