# Bước 7b.3 — Xem trước và in hóa đơn WPF

Hoàn thành ngày **13/10/2026**. Đối chiếu bước 7 của plan WPF và mục **5/28** của bản tham chiếu. Bản tham chiếu quy định nội dung phiếu và dữ liệu snapshot; nền tảng này dùng `PrintDialog` của WPF thay cho trang in Razor. Giữ .NET Framework 4.7.2 + SQLite **schema v8**.

## Cách dùng

1. Đăng nhập bằng tài khoản có **Invoice.View + Invoice.Print**.
2. Vào **Hóa đơn**, tìm và chọn một hóa đơn đã thanh toán.
3. Bấm **Xem trước / In** để xem phiếu A4. Phiếu dài có **Trang trước / Trang sau**; có thể cuộn để đọc hết trang khi cửa sổ nhỏ.
4. Bấm **Chọn máy in / In**, chọn máy và thiết lập in trong hộp thoại Windows rồi xác nhận.
5. Hủy hộp thoại thì chưa gửi lệnh. Khi gửi thành công, màn hình báo **Đã gửi lệnh in**; có thể in lại hóa đơn cũ.

Người chỉ có Invoice.View vẫn xem hóa đơn, không có nút in. Quyền in độc lập với quyền checkout, gọi món, danh mục hoặc báo cáo. Guest không có đường xem/in lịch sử hóa đơn.

## Đã sửa và hoạt động ra sao

- `InvoicesWindow` có nút mở bản xem trước. `InvoicesViewModel.PreparePrintAsync` gọi **ReadForPrint** để kiểm tra cả View và Print hiện hành, đọc dữ liệu chốt mới trước khi dựng phiếu; không dùng cờ CanPrint cũ làm căn cứ cấp quyền.
- `InvoiceDocument` dựng `FlowDocument` với giấy A4, font Segoe UI, nền trắng/chữ đen. WPF tự xuống dòng và phân trang. Bảng món có tên, số lượng, đơn giá và thành tiền; phiếu không món hiện thông báo và tiền dịch vụ bằng 0.
- Phiếu có tên quán cố định, mã hóa đơn, mã/loại phòng snapshot, giờ nhận/trả thực tế, thời lượng và đơn giá phòng, món Completed, tiền phòng/dịch vụ/tổng đã thanh toán, hình thức, thu ngân snapshot và PaidAt. Giờ Việt Nam UTC+7; tiền N0 theo vi-VN, dùng decimal/long và không tính lại tiền lịch sử.
- `InvoicePrintWindow` chỉ có các nút xem trang, in và đóng; không có toolbar mặc định tạo đường in bỏ qua kiểm tra quyền. Khi mất quyền hoặc dữ liệu nguồn không nhất quán, bản xem trước bị xóa và không gửi lệnh.
- `InvoicePrintViewModel.PrintAsync` kiểm tra ReadForPrint trước khi mở hộp chọn máy và **kiểm tra lại sau khi người dùng xác nhận**. Mỗi lần đọc kết thúc transaction trước khi chờ người dùng; không giữ khóa SQLite trong hộp thoại Windows.
- Sau khi chọn máy, phiếu được dàn lại theo **vùng in** mà PrintDialog trả về. Vùng quá nhỏ bị từ chối với hướng dẫn chọn khổ lớn hơn. Bản xem trước A4 có thể khác số trang của máy chọn nếu khổ/vùng in khác.
- Busy chặn gửi lặp và đóng giữa thao tác. Lỗi hàng đợi không báo đã in thành công; hướng dẫn kiểm tra hàng đợi trước khi thử lại vì một lỗi gửi không luôn chứng minh máy chưa nhận lệnh.
- `IInvoicePrinter` tách đúng ranh giới hộp thoại/gửi lệnh; bản chạy thật dùng `InvoicePrinter`, bài kiểm tra dùng bộ giả lập. Thêm tham chiếu framework System.Printing và ReachFramework; không thêm package, schema hoặc ghi invoice/audit khi xem/in.

Thông báo thành công xác nhận đã gửi lệnh tới hệ thống in, **không xác nhận giấy đã ra khỏi máy**. Các thay đổi quyền xảy ra sau lần kiểm tra cuối và việc xử lý hàng đợi Windows không nằm trong một transaction SQLite.

## Kiểm tra

- Build **Debug/Release** đạt tại `bin/VerifyDebug` và `bin/VerifyRelease`.
- **25 bộ kiểm tra đạt** trên SQLite/thư mục tạm: InvoicePrinting, Invoices, Checkout, Billing, Orders, Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, Calendar, GuestCalendar, CheckIn, WalkIn, Extension, Sessions, GuestSessions, AuthenticationUi.
- InvoicePrinting chạy thêm trên **Release**. Tất cả thao tác chọn/gửi trong bài này dùng bộ giả lập; không mở hộp máy in thật và không gửi giấy thật.
- Fixture dùng walk-in, Order và checkout thật để tạo hóa đơn. Sau khi đổi danh mục/thu ngân, bản in hóa đơn đầu vẫn giữ tên/giá gốc, giây/ticks, tiền phòng 3.000 đ + món 50.000 đ = 53.000 đ.
- In lại hai lần giữ dữ liệu; hủy dialog không gửi; thu hồi Print trước thao tác không mở dialog; thu hồi View trong dialog không gửi. Dữ liệu nguồn bị sửa lệch trong dialog cũng bị chặn. View vẫn đọc được khi chỉ mất Print.
- Gate clock kiểm tra busy, gửi lặp và đóng giữa thao tác: chỉ một lệnh đi qua. Giả lập lỗi gửi và khổ quá nhỏ không báo thành công hoặc gửi nội dung bị cắt.
- Hóa đơn **70 dòng**, tên dài và tiền vượt Int64 chia **4 trang** A4; số tiền đầy đủ đến từng đồng. Phiếu không món có tổng dịch vụ 0. Các trang đã render và xem ảnh để kiểm tra bố cục.
- Test mở bản xem trước từ **nút thật của InvoicesWindow**, đóng mà không in; thu hồi quyền trước mở và tài khoản View-only bị chặn. Không phát sinh invoice/audit hoặc đổi schema trong luồng in.
- AuthenticationUi giữ **116 ảnh** tại `%TEMP%/MusicBoxUi_74674abbb8144a5a9f1ed44aaeb8672a`. Bài in tạo **7 ảnh** tại `%TEMP%/MusicBoxPrint_7b3` gồm cửa sổ thường/tối thiểu, 4 trang và cửa sổ phiếu dài; bản Release ở `%TEMP%/MusicBoxPrint_7b3_Release`. Các tài nguyên kiểm tra không đưa vào Git.

Chạy kiểm tra in riêng sau build:

```powershell
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-InvoicePrinting.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
```

Chất lượng giấy và thiết lập riêng của driver **chưa được kiểm tra trên máy in vật lý**. Anh có thể thử thủ công bằng một hóa đơn nhỏ qua hộp thoại Windows; các bài tự động chỉ kiểm tra nội dung, phân trang, quyền và việc gọi/hủy gửi.

## Tiếp theo

Bước 7 đã có checkout, hóa đơn bất biến, tìm/xem và in lại qua WPF. Tiếp theo **8a.1 nền tảng dashboard hoạt động** theo mục 29: trạng thái phòng, booking hôm nay, phiên đang dùng, Pending Order và các cảnh báo; doanh thu hôm nay chỉ cho Admin/Manager đủ quyền, Staff luôn bị chặn phần tổng hợp.

Giữ các phần bắt buộc tiếp theo: quản trị tài khoản tạo/sửa/khóa/gán Staff–Manager–Admin/reset mật khẩu, bảo vệ Admin cuối cùng, ma trận Role → Permission và audit; báo cáo/Excel, lịch sử khách hàng, dữ liệu demo và kiểm tra luồng bảo vệ đồ án. Không thêm khoản chi điện/thuê mặt bằng.
