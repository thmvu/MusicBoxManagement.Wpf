# Bước 7b.2 — Giao diện tìm và xem hóa đơn

Hoàn thành ngày **12/10/2026**. Đối chiếu plan WPF bước 7/P02 và tham chiếu mục 5/17/21/27–28. Giữ WPF .NET Framework 4.7.2 + SQLite schema **v8**, dùng InvoiceService 7b.1; không đổi tiền đã chốt hoặc transaction checkout.

## Cách sử dụng

Đăng nhập → **Hóa đơn** trên menu dọc → nhập bộ lọc nếu cần → **Tìm / Cập nhật** → chọn một dòng để đọc chi tiết.

- Mã hóa đơn đầy đủ, có thể nhập chữ thường/khoảng trắng ngoài cùng.
- Từ/đến **ngày thanh toán** theo Việt Nam, bao gồm ngày cuối; bỏ trống để không giới hạn phía đó.
- SĐT hiện hành đầy đủ, chuẩn hóa cùng quy tắc đặt/tra cứu; đổi SĐT khách thì tìm bằng số mới.
- Các bộ lọc kết hợp AND. **Xóa lọc** xóa bộ lọc và kết quả; bấm Tìm để tải toàn bộ lại.

Danh sách ở trên có mã hóa đơn, phòng snapshot, giờ thanh toán UTC+7, tiền mặt/chuyển khoản và tổng đã thanh toán. Chọn dòng tự đọc service chi tiết, không lấy nội dung hóa đơn từ dòng danh sách cũ.

Chi tiết ở dưới: tên quán/mã hóa đơn, phòng/loại, actual nhận/trả, thời lượng, đơn giá, hình thức, thu ngân snapshot và PaidAt. Bên cạnh là tiền phòng/món/tổng đã chốt và các dòng món Completed/số lượng/giá/thành tiền. Phiếu không có món vẫn hiển thị tiền phòng và bảng rỗng bình thường.

## Đã sửa

- MainWindow thêm menu Hóa đơn theo **Invoice.View**; Guest ẩn. Factory `StaffReservationService.ForInvoices` kiểm tra quyền trước mở, dùng chung DB/clock; handler guard mở lặp và cập nhật quyền khi đóng.
- `InvoicesWindow`/`InvoicesViewModel` gọi Search và Read. Người chỉ có Invoice.View vẫn xem được, không đòi quyền phiên/món/khách/checkout/report/print; không fallback Guest.
- Bộ lọc thay đổi xóa ngay danh sách/lựa chọn/chi tiết/món cũ, chờ lần Tìm tiếp. Chọn hóa đơn khác xóa chi tiết trước khi đọc mới. Search cập nhật xóa lựa chọn để không giữ một phiếu từ bộ lọc cũ.
- Busy khóa bộ lọc/chọn/tìm/đóng, chặn yêu cầu lặp. Read có lỗi/stale hoặc dữ liệu nguồn lệch thì xóa nội dung, cho tải lại/đóng; mất quyền còn xóa mã/SĐT/ngày nhập và khóa lựa chọn đến khi đọc được quyền lại.
- Giá/tên/phòng/thu ngân lấy từ snapshot đã chốt qua InvoiceService, không gọi Billing để tính lại. Chỉ đọc, không sửa/xóa/hủy Invoice hoặc ghi audit khi tìm/xem.
- Nhãn PaidAt dùng UTC+7 và CultureInfo vi-VN; tiền decimal/long hiển thị N0 tiếng Việt, không đổi sang double. Placeholder DatePicker dùng “Chọn ngày” trong template riêng của màn hóa đơn, không hiện “Select a date”.
- Bố cục bảng rộng phía trên, metadata và món phía dưới, nền trung tính/không bo góc. Phần dài có cuộn; cửa sổ tối thiểu 1120×700 vẫn nhìn được danh sách/tổng/cột thành tiền.

## Kiểm tra

- Debug/Release build đạt tại bin/VerifyDebug và bin/VerifyRelease.
- **24 bộ kiểm tra đạt** trên SQLite/thư mục tạm: Invoices, Checkout, Billing, Orders, Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, Calendar, GuestCalendar, CheckIn, WalkIn, Extension, Sessions, GuestSessions, AuthenticationUi. Invoices kiểm tra thêm Release.
- Verify-Invoices thêm factory route View-only/thiếu quyền; giữ toàn bộ guard/snapshot/precision/date/phone/current permission/readonly/concurrency ở 7b.1.
- AuthenticationUi kiểm tra menu thật bằng tài khoản chỉ Invoice.View, không có quyền vận hành; Guest ẩn; danh sách rỗng và sidebar cuộn ở kích thước tối thiểu.
- Fixture tạo ba hóa đơn bằng walk-in/orders/checkout thật: 53.000 đ có món, hóa đơn không món lúc đúng 00:00 Việt Nam và tổng vượt Int64. Đổi giá/tên/loại/khóa phòng/ngừng bán món/tên thu ngân không đổi nội dung phiếu cũ trên UI.
- Test mã trim/chữ thường, SĐT +84/đổi số, filters AND, ngày PaidAt và nhãn 00:00 đúng ngày Việt Nam, Xóa lọc, range đảo/SĐT sai không giữ kết quả cũ. Số tiền lớn giữ đúng từng đồng khi hiển thị.
- Chọn phiếu sau khi nguồn bị sửa lệch trong fixture: Read từ chối và xóa dữ liệu. Thu hồi View khi cửa sổ đang mở: Cập nhật chặn/xóa dữ liệu/bộ lọc, vẫn đóng/thử tải lại được.
- Gate clock giữ Read đang chạy: bấm Tìm lặp/đổi filter/đóng bị chặn, chỉ một Read; kết thúc đọc không tạo thêm invoice/audit.
- UI render **116 ảnh** tại `%TEMP%/MusicBoxUi_7b2`; đã xem ảnh detail/compact, kiểm tra cột bảng và tổng tiền trong cửa sổ nhỏ. Không đưa database/ảnh kiểm tra vào Git.

```powershell
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
```

## Tiếp theo

Đã có tìm/xem hóa đơn lịch sử qua WPF. **Chưa có nút in hoặc gửi máy in** ở bước này.

Tiếp theo **7b.3 in hóa đơn WPF**: kiểm tra Invoice.View + Invoice.Print hiện hành qua ReadForPrint, dựng phiếu từ snapshot/decimal/tổng đã chốt, không tự tính lại bằng catalog hoặc bill hiện tại. Kiểm tra bố cục/in lại/nhiều dòng/đổi quyền/hủy dialog trên tài nguyên tạm; không tự in giấy thật khi chạy test. Không sửa/xóa hóa đơn hoặc mở lịch sử cho Guest.

Giữ dashboard hoạt động/doanh thu quán, quản trị tài khoản/role/ma trận RBAC/audit, báo cáo/Excel cùng bộ lọc và lịch sử khách. Quyền xem phiếu vận hành không tự cấp báo cáo doanh thu tổng hợp cho Staff.
