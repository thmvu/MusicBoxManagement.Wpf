# MySQL 04i — Tìm, đọc và in lại hóa đơn

Ngày 28/10/2026. Đối chiếu reference mục 5, 28, 32 và 55, cùng Step7b1_InvoiceReading/Step7b2_InvoiceUi/Step7b3_InvoicePrinting. Đọc [bảng bàn giao](MySql_Migration_Status.md) trước phần kế tiếp. App và worker vẫn dùng SQLite; lượt này chưa cutover hoặc nhập dữ liệu thật.

## Dịch vụ đọc lịch sử

Thêm `MySqlInvoiceService.Search/Read/ReadForPrint`, giữ InvoiceSearch/InvoiceSummary/InvoiceDetails/InvoiceLine. Factory `MySqlStaffReservationService.ForInvoices` kiểm tra Invoice.View riêng; không yêu cầu Reservation.View để mở chức năng hóa đơn.

- Search/Read cần **Invoice.View hiện hành**, độc lập Session/Order/Customer/Checkout/Report/Print. ReadForPrint cần **cả Invoice.View + Invoice.Print** ngay tại lần đọc, không tin CanPrint cũ; Print-only bị chặn theo dependency hiện hành. Không Guest endpoint hoặc fallback.
- Search: mã đầy đủ trim/uppercase, equality có parameter (không LIKE hoặc ghép input vào SQL); khoảng **PaidAt theo ngày Việt Nam UTC+7**, từ đầu ngày from đến trước đầu ngày sau to; SĐT chuẩn hóa hiện hành qua CustomerId. Bộ lọc AND, hỗ trợ khoảng mở, sắp PaidAt/InvoiceId giảm dần. DateTime.Kind và phần giờ của input không đổi ý nghĩa ngày lịch.
- Summary chỉ mã hóa đơn/phòng snapshot/PaidAt/payment/tổng; không tên/SĐT khách. CanPrint và CheckedAt cùng snapshot quyền/dữ liệu.
- Detail đọc Invoice header qua MySqlCheckoutService.ReadInvoice, Session Completed và các Order Completed snapshot trong cùng transaction. DECIMAL(29,0) đọc bằng GetDecimal, không parse TEXT/Int64/double hoặc tính lại bill lịch sử.
- Giữ room/type/rate/actual, tên/giá/số lượng từng món và thu ngân/payment/PaidAt lúc chốt; đổi danh mục/tài khoản/phòng khóa không sửa lịch sử. Không gộp hai dòng thuộc hai Order cũ dù cùng ServiceId. PaidAt phải khớp ActualEnd, actual end không trước start, tổng dòng khớp ServiceCharge và RoomCharge+ServiceCharge khớp TotalAmount; dữ liệu lệch bị chặn.

Tất cả đọc dùng **RR read-only**, quyền và header/items trong cùng snapshot, một IClock sau dữ liệu. Không audit/NoShow, thay trạng thái, tạo Invoice hoặc ghi ngày kiểm tra vào database. InnoDB MVCC cho phép Checkout writer thật commit trong khi Search reader vẫn mở; kết quả đang đọc giữ snapshot cũ, lần sau thấy hóa đơn mới.

## Tái sử dụng đúng luồng WPF để in từ MySQL

Thêm interface nhỏ **IInvoiceService** chỉ có ba phương thức Search/Read/ReadForPrint; InvoiceService SQLite và MySqlInvoiceService cùng thực hiện. InvoicesViewModel và InvoicePrintViewModel nhận interface thay vì cố định SQLite. Đây là hai provider đang dùng thật trong cùng hai consumer, không thêm DI framework hoặc một hệ thống provider mới. Thuật toán SQLite, logic VM, XAML, palette và renderer không đổi; App vẫn truyền InvoiceService SQLite như trước.

Với MySqlInvoiceService được truyền vào, cùng InvoicesWindow → PreparePrintAsync → InvoicePrintWindow/InvoicePrintViewModel → InvoiceDocument/IInvoicePrinter hoạt động trên hóa đơn MySQL thật:

1. Chuẩn bị xem trước gọi ReadForPrint kiểm tra View/Print hiện hành.
2. Bấm in: đọc lại ReadForPrint trước mở hộp chọn máy; transaction kết thúc trước khi chờ người dùng.
3. Hủy hộp chọn máy không gửi lệnh. Đồng ý thì đọc lại cả quyền và snapshot, dựng phiếu theo PrintableArea rồi Send; mất quyền/dữ liệu lệch trong dialog không được gửi.
4. Busy chặn gửi lặp/đóng giữa thao tác; lỗi hàng đợi không báo đã in giấy, xóa bản cũ và hướng dẫn kiểm tra hàng đợi. Không mở toolbar có đường in vượt guard.

Giữ phiếu A4/Segoe UI/vi-VN, giờ UTC+7 có giây, tiền decimal đầy đủ, phân trang theo tên món dài; không có Customer PII. Hóa đơn ít hoặc không món và in lại dùng cùng dữ liệu đã chốt. Việc đổi quyền sau kiểm tra cuối và gửi tới hàng đợi Windows không nằm trong transaction database.

## Kiểm tra

Build VerifyInvoicesMySqlDebug/Release đạt. **Verify-MySqlInvoices và Verify-MySqlInvoicePrinting online đạt ở cả Debug/Release**; dùng tên build trên cho tất cả lượt. Log backend Debug `%TEMP%/MusicBoxMySqlInvoicesDebug_20261028.log`; backend Release cuối `%TEMP%/MusicBoxMySqlInvoicesReleaseDiagnostic_20261028.log`. Log in `%TEMP%/MusicBoxMySqlInvoicePrintingDebug_20261028.log` và `%TEMP%/MusicBoxMySqlInvoicePrintingRelease_20261028.log`.

**44/44 bộ SQLite/WPF Debug đạt**, summary `%TEMP%/MusicBoxRegression_a3b70b707c494e06bc2790c323b83c8e/results.json`; AuthenticationUi 142 ảnh tại `%TEMP%/MusicBoxUi_41921a1e7f7d4dd7a87ce4ca358fa4ea`. Invoices/InvoicePrinting SQLite chạy thêm Release đều đạt; ảnh in Release `%TEMP%/MusicBoxPrint_bb0d1e645b70461d8a6520af014ee45d`. Không thay các assertion cũ để bỏ kiểm tra quyền hoặc nới nghiệp vụ.

Lượt hồi quy 8 bộ MySQL Debug ban đầu: Billing, StaffReservations, Customers, Authentication, Foundation, Services đạt; Orders và Checkout lỗi Connect Timeout. Summary ban đầu `%TEMP%/MusicBoxInvoiceMySqlRegression_419d18e997fc4c2e80546e8dcc37a24c/results.json` giữ nguyên kết quả 6/8, không ghi đè thành đạt. **Chạy lại tuần tự Orders và Checkout đều đạt exit 0**, summary `%TEMP%/MusicBoxInvoiceWriterRetry_579df3c6397d40ed80066b28281cbcde/results.json`; Checkout giữ đủ 14 ca writer hai thứ tự, MVCC và clock/logout sau Room wait. Kết quả mới nhất của 8 bộ liên quan là 8/8 đạt, không phải lượt đầu chạy đạt cả 8.

Backend Invoice Release cũng có các lượt Connect Timeout/Broken trước lượt cuối thành công. Stack nằm ở OpenConnection/reset pooled session/TLS; probe server lúc kiểm tra có Threads_connected=1, max_connections=76, wait_timeout=28800, chưa đủ để kết luận nguyên nhân hạ tầng. Không sửa SSL/pooling/timeout hoặc code nghiệp vụ để che lỗi. Thêm GUID và lỗi trước cleanup trong bài mới, GUID trong hai bài writer để đối chiếu/dọn đúng fixture; schema diagnostic d32a0016f435465280f30d7c3dd9696d đã được dọn riêng bằng GUID ghi trong log. Hai writer chạy lại dọn database của mình thành công trước exit 0; không xóa schema chưa xác định thuộc lượt này.

Hai bài mới tạo database GUID riêng, có kiểm tra tên trước DROP; hóa đơn/Completed tạo bằng MySqlRoomSession/Order/Checkout service thật, không SQL giả thanh toán. SQL seed danh mục/tài khoản, đổi metadata/quyền và cố ý làm lệch actual end/item để kiểm tra guard, sau đó phục hồi fixture. Clock có điều khiển và dữ liệu người dùng giả; không database nghiệp vụ/ảnh thật/máy in thật.

**MySqlInvoices**: View-only/Print grant-revoke/dependency/current stamp/lock/logout/null/CheckOut-only bị chặn; exact code/AND/SĐT hiện hành/UTC+7 midnight/date Kind/open bounds/sort/invalid input; snapshots sau đổi/khóa danh mục/thu ngân/khách; ticks/tiền vượt Int64; mismatch timestamp/items; read-only/schema không đổi; RR search và Checkout thật commit trước khi reader đóng. Có log GUID và lỗi trước cleanup để phân biệt lỗi ca kiểm tra với lỗi dọn database khi mạng gián đoạn.

**MySqlInvoicePrinting**: hai VM/cửa sổ hiện có nhận MySqlInvoiceService; xem trước/in lại/hủy, mất Print trước in/mất View trong dialog, dữ liệu đổi lệch trong dialog, busy/duplicate/close, giả lập lỗi Send/vùng quá nhỏ, nút thật mở preview và View-only ẩn in. Hóa đơn thật **70 dòng trên 4 trang A4** với tổng vượt Int64 được tạo từ một Order 70 dịch vụ fixture và checkout thật; phiếu không món có tiền dịch vụ 0. Toàn bộ chọn/gửi dùng IInvoicePrinter giả, không mở dialog hoặc gửi máy in thật trong test.

Đã xem ảnh Debug: cửa sổ thường/compact và cả 4 trang dài; tiền/tên món/tổng không bị cắt trên trang A4. Cửa sổ compact giữ tài liệu A4 và thanh cuộn hai chiều. 7 ảnh/bản tại `%TEMP%/MusicBoxMySqlPrint_cc7ae3f2feab4d118c624c5c26e1fe08` và `%TEMP%/MusicBoxMySqlPrint_832c1e9b678c4338b096e4259aeaec48`; đã so SHA256 cả 7 cặp Debug/Release giống hệt. Không coi máy in giả hoặc ảnh render là nghiệm thu giấy/driver thật.

```powershell
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlInvoices.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyInvoicesMySqlDebug
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlInvoicePrinting.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyInvoicesMySqlDebug
```

Đổi BuildDirectory sang VerifyInvoicesMySqlRelease để kiểm tra bản Release. Vì thay kiểu service dùng chung của hai VM, hồi quy toàn bộ 44 bộ SQLite/WPF và chạy Invoices/InvoicePrinting thêm Release; không chỉ chạy bài MySQL mới.

## Tiếp theo và giới hạn

Tiếp **Dashboard MySQL** theo Dashboard.View, trạng thái phòng/booking/Active/Pending/cảnh báo; doanh thu PaidAt chỉ Admin hoặc Manager có Report.View, Staff không doanh thu tổng hợp. Giữ ma trận quyền/audit/báo cáo/Excel/lịch sử khách, worker/provider/UI toàn app, backup/import/đối chiếu DB + Content/cutover/phục hồi, ERD/từ điển/bản xuất/demo.

Đã nối và kiểm tra **luồng invoice/print qua MySQL trong fixture riêng**, chưa đổi nguồn mặc định của App/worker hoặc nghiệm thu máy in/driver/giấy thật. Schema SQLite v8/MySQL v1 không đổi. Chỉ hai Compile entry mới trong csproj vào commit; phần định dạng có sẵn giữ riêng.
