# Bước 8e.2 — Báo cáo lượt sử dụng và tỷ lệ sử dụng phòng

Ngày thực hiện: 18/10/2026. Đối chiếu mục 31.2–31.3 của bản nghiệp vụ tham chiếu và kế hoạch WPF. Giữ SQLite schema v8, không thay dữ liệu thật hoặc chuyển provider của app.

## Nguồn số liệu và cách đọc

`RoomUsageReportService.Read` dùng `Report.View` hiện hành cho Admin hoặc Manager, độc lập `Dashboard.View`, `Invoice.View` và các quyền vận hành. Staff dù có grant nhầm vẫn bị từ chối. Quyền, danh mục phòng và phiên đọc trong một read transaction; lấy `IClock` đúng một lần sau kiểm tra quyền. Service không ghi audit/NoShow, không sửa phiên hoặc tự checkout.

Hai con số có mốc khác nhau, được trình bày riêng:

- **Lượt hoàn tất:** đếm các phiên `Completed` có `ActualEndTime` từ đầu FromDate đến trước đầu ngày sau ToDate, theo UTC+7. Không phụ thuộc ngày thanh toán, ngày bắt đầu phiên hoặc ca mở cửa. Đây là bộ lọc theo ngày hoàn tất; một phiên kéo dài qua kỳ có thể đóng góp phút vào kỳ nhưng chưa có lượt hoàn tất trong kỳ đó.
- **Tỷ lệ theo lịch mở cửa tiêu chuẩn:** cửa sổ của mỗi phòng bắt đầu ở mốc muộn hơn giữa đầu kỳ và `Room.CreatedAt`; kết thúc ở mốc sớm hơn giữa cuối kỳ và `now`. Mẫu số là phần giao cửa sổ này với ca 09:00–12:00 và 13:00–23:00. Một ngày đủ là 780 phút; hôm nay chỉ tính đến hiện tại, kỳ tương lai không phát sinh phút.
- **Phút sử dụng:** phần thực tế `ActualStartTime–ActualEndTime` của phiên Completed, hoặc `ActualStartTime–now` của phiên Active, giao với đúng cửa sổ và các ca trên. Không dùng giờ dự kiến, thời lượng booking hoặc tiền tạm tính. Các đoạn thực tế chồng nhau trong dữ liệu lịch sử được hợp nhất theo phòng, tránh đếm một phút hai lần; không ép trần tỷ lệ để che sai lệch.

Phòng đang khóa vẫn có lịch sử và vẫn nằm trong bộ lọc. Vì không lưu lịch sử khóa/mở, mẫu số không trừ ngày phòng từng khóa; tên báo cáo là **theo lịch mở cửa tiêu chuẩn**, không phải thời gian mở bán thực tế. Không phân loại lại bằng RoomType hiện tại. Bộ lọc Room dùng RoomId; danh sách lựa chọn gồm cả phòng inactive và được trả trong cùng snapshot, không cần quyền quản lý phòng.

Tổng phút là tổng các phòng; tỷ lệ chung là tổng phút sử dụng / tổng phút tiêu chuẩn × 100, không lấy trung bình các phần trăm. Mẫu số bằng 0 trả `null`, hiển thị “Không có dữ liệu”. Số gốc giữ decimal, nhãn hiển thị hai chữ số thập phân theo vi-VN; số lượt vẫn là số nguyên.

## Độ chính xác và dữ liệu cũ

DateTimeOffset có timezone rõ ràng được đọc theo invariant culture, bao gồm ISO UTC `Z`, offset khác UTC và số chữ số phần giây khác nhau. So sánh biên dùng ticks .NET, không dùng julianday hoặc double để quyết định tính vào kỳ. Thiếu timezone, timestamp không parse được, trạng thái sai, Active có ActualEndTime, Completed thiếu end hoặc end trước start sẽ báo lỗi rõ ràng; không suy ra CreatedAt từ ngày phiên, không tự sửa bản ghi.

Phòng có CreatedAt sau cửa sổ, phiên bắt đầu trong tương lai hoặc đoạn không giao cửa sổ có 0 phút. Số lượt hoàn tất vẫn theo ActualEndTime trong kỳ, độc lập giới hạn phút đến `now`; service không thay giờ thực tế của dữ liệu nguồn. Khoảng phiên ngoài ca bị cắt khi tính tỷ lệ.

Tính phần giao lịch mở cửa bằng hiệu số ngày × 780 phút và số ticks mở cửa của hai ngày biên: O(1) cho một đoạn thời gian, không lặp từng ngày và không thêm giới hạn số ngày ngoài nghiệp vụ. Hợp nhất interval rồi cộng ticks trước khi chia một lần cho số phút trong từng phòng, tránh cộng các số phút đã làm tròn. DTO không chứa khách hàng/SĐT và chỉ có setter nội bộ; danh sách trả read-only.

## Kiểm tra

Chạy `Tests/Verify-RoomUsageReport.ps1 -BuildDirectory <thư mục build>` trên database tạm. Bộ kiểm tra bao gồm walk-in và checkout thật; Room filter/danh mục inactive; quyền Manager chỉ Report.View; Staff grant nhầm; thu hồi quyền, stamp, khóa, đổi role và logout; read transaction/clock duy nhất; snapshot khi thu hồi quyền đồng thời; trigger chặn mọi thao tác ghi nghiệp vụ/audit.

Các ca số liệu gồm ngày đủ 780 phút, hôm nay chưa đủ ngày, nghỉ trưa, ngoài ca, nhiều ngày, Room.CreatedAt giữa ca/nghỉ trưa/tương lai, phiên Completed/Active, khoảng thực tế chồng, lệch 100ns tại biên, hai đoạn tách bởi nghỉ trưa có biên lẻ, UTC/legacy offset, Completed đếm theo [from,until), kỳ tương lai, kỳ dài không giới hạn tự thêm, tổng trọng số và dữ liệu hỏng. Test không sửa database/ảnh đang dùng và không in máy thật.

`Verify-RoomUsageReport` đã đạt cả Debug và Release trên `bin/VerifyRoomUsageReservationsDebug` và `bin/VerifyRoomUsageReservationsRelease`, sau khi ghép production source ngày 18/10/2026. Mỗi lượt dùng database GUID riêng trong thư mục tạm, đã dọn sau kiểm tra. Phần UI và hồi quy chung được ghi bổ sung khi ghép xong. Excel, báo cáo booking/món, lịch sử khách và chuyển các báo cáo sang MySQL vẫn giữ trong kế hoạch tiếp theo.

## Giao diện và cách dùng

Admin hoặc Manager có Report.View → **Báo cáo sử dụng phòng**. Chọn Từ/Đến ngày và Tất cả phòng hoặc một phòng cụ thể, rồi bấm **Xem báo cáo**. Lựa chọn phòng gồm cả phòng đã khóa; nhãn có mã/tên hiện tại và dấu đã khóa. Không cần Room.Manage/Session.View/Invoice.View để dùng báo cáo.

Bảng hiện lượt hoàn tất, phút đã dùng, phút theo lịch tiêu chuẩn và phần trăm cho từng phòng. Tổng phía trên dùng tổng số phút, không lấy trung bình tỷ lệ. Phần giải thích dưới bảng phân biệt mốc hoàn tất với mốc tính tỷ lệ và ghi rõ ca 09–12/13–23, CreatedAt, giới hạn hiện tại. Không có mẫu số thì hiển thị Không có dữ liệu.

Đổi ngày/phòng xóa bảng và tổng cũ, yêu cầu tải lại. Trong lúc tải, khóa bộ lọc/gửi lặp/đóng. Lỗi xóa kết quả và lựa chọn phòng cũ; thu hồi quyền/role/stamp xóa cả bộ lọc. Route menu kiểm tra lại qua ForRoomReports, service vẫn kiểm tra độc lập trong mỗi read snapshot. Window tối thiểu 960×640 có cuộn bảng khi cần và tổng xuống dòng; màu trung tính.

Verify-RoomUsageReportUi đã đạt Debug và Release, 7 ảnh/build. Kiểm tra menu thật Guest/Staff/Manager/Admin và Manager chỉ Report.View; binding ngày/phòng, gồm lịch sử phòng khóa; lượt hoàn tất khác cửa sổ tính phút, mẫu số hôm nay và null ở tương lai; lỗi/unknown/empty/filter cũ, thu hồi quyền/role/stamp, busy/gửi lặp/đóng. Đã xem ảnh full/compact và ảnh cuộn tới cột tỷ lệ ở cửa sổ tối thiểu; xác nhận cột nằm trong vùng nhìn thấy, không chỉ kiểm tra tồn tại control.

## Chốt kiểm tra chung ngày 18/10/2026

Build Debug/Release tích hợp và mặc định đều đạt. 33/33 bộ hồi quy SQLite trước bước này đạt trên VerifyRoomUsageReservationsDebug; hai bộ RoomUsageReport/RoomUsageReportUi mới đạt cả Debug/Release, tổng 35 bộ SQLite/WPF. AuthenticationUi render 131 ảnh, UI phòng mới 7 ảnh/build đã xem full/compact/cuộn tỷ lệ; các UI báo cáo doanh thu/ma trận quyền/nhật ký cũ vẫn đạt. InvoicePrinting chỉ xem trước, không gửi máy in.

MySqlReservations đạt online Debug/Release; MySqlAvailability/MySqlFoundation online Debug đạt trên GUID database riêng và đã dọn. Có cạnh tranh qua hai service booking thật; check-in MySQL vẫn chưa có nên source session trong test chỉ là fixture, giữ phần kiểm tra qua service thật khi port. Không thay database/ảnh thật, schema hoặc provider app.
