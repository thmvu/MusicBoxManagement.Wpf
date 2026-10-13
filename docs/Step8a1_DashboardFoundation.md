# Bước 8a.1 — Nền tảng dashboard hoạt động

Hoàn thành ngày **13/10/2026**. Đối chiếu plan WPF bước 8, tham chiếu mục **5/29** và quy tắc giờ/lịch ở mục 38–39. Giữ .NET Framework 4.7.2 + SQLite **schema v8**. Bước này có service và kiểm tra; **giao diện dashboard sẽ nối ở 8a.2**.

## Đã sửa

- `DashboardService.Read` đọc theo **Dashboard.View hiện hành**, độc lập với Calendar.View/Reservation.View/Session.View/Order.View/Invoice.View. Factory `StaffReservationService.ForDashboard` kiểm tra quyền khi mở đường này; service đọc vẫn kiểm tra lại.
- `DashboardSnapshot` trả danh sách phòng/trạng thái, booking hôm nay, phiên Active, đơn Pending của phiên Active, cảnh báo và thời điểm cập nhật. Các DTO chỉ có thông tin vận hành cần cho dashboard, không tên/SĐT/CustomerId hoặc nội dung món/hóa đơn lịch sử.
- Quyền, dữ liệu và doanh thu nằm trong **một read transaction**, dùng **một mốc IClock** chuẩn hóa UTC. Ngày hôm nay và các mốc ngày theo Việt Nam UTC+7, không phụ thuộc timezone của máy.
- Trạng thái phòng dùng chung `ScheduleRules.CurrentRoomStatus` với CalendarService: Inactive trước; phòng mở có Active là Occupied; còn Confirmed đã tới giờ/chưa hết grace là Reserved; còn lại Available. Booking tương lai không làm phòng Reserved ngay.
- Booking hôm nay tính theo StartTime trong khoảng `[00:00 hôm nay, 00:00 ngày mai)`, có đầy đủ các trạng thái. Confirmed hết đúng grace +15 phút được chiếu thành NoShow trong DTO dù worker chưa kịp chạy; **không ghi trạng thái vào DB**.
- Phiên Active gồm cả phiên từ ngày trước. Phiên booking giữ ExpectedEnd; walk-in dùng ReturnBy động từ ScheduleRules, theo ca/lịch của cả Room và Customer, không lưu thành ExpectedEnd hoặc tạo hold tương lai.
- Chỉ đọc. Không chạy NoShowService, tính bill, checkout, hủy món/booking hoặc ghi AuditLog khi mở dashboard.

## Các cảnh báo

| Kind | Khi xuất hiện |
| --- | --- |
| NearNoShow | Confirmed còn hiệu lực, thời gian còn lại đến StartTime +15 phút **lớn hơn 0 và tối đa 5 phút**; đúng +15 phút hết cảnh báo và không giữ phòng |
| Overdue | Active từ booking có now > ExpectedEnd; walk-in có now > ReturnBy động; đúng giờ cần trả chưa coi quá giờ |
| NextBooking | Walk-in có lượt kế tiếp của phòng hoặc khách trong giới hạn ca/ReturnBy: nhắc trả trước lượt đó. Phiên booking có ExpectedEnd vượt giờ lượt kế tiếp, hoặc vẫn Active khi đã tới giờ lượt đó |
| OutsideHours | Còn Active trong 12:00–13:00, từ 23:00, hoặc qua đêm/trước 09:00; không tự đóng phiên |

Lượt kế tiếp chỉ lấy Confirmed còn grace, bỏ reservation nguồn của phiên, xét **cùng Room hoặc cùng Customer**, chọn lượt sớm nhất. Cảnh báo walk-in trước khi tới lượt kế tiếp là nhắc hạn trả; không khẳng định đã có xung đột. Khi lượt kế tiếp hết grace, ReturnBy tính lại và cảnh báo cập nhật theo lịch còn hiệu lực. Không nới lịch, dời booking hoặc gia hạn tự động.

## Doanh thu hôm nay

- `TodayRevenue` là **decimal?**: null nếu không được xem; 0 khi có quyền nhưng hôm nay chưa có hóa đơn. Không dùng số 0 để che việc thiếu quyền.
- Chỉ **Admin**, hoặc **Manager có Report.View hiện hành**, được nhận tổng này. Staff luôn nhận null và `ReadRevenueToday` từ chối kể cả RolePermission bị gán nhầm Report.View/Export. API doanh thu cũng đòi Dashboard.View vì thuộc dashboard.
- Nguồn là **Invoice.TotalAmount** đã thanh toán, lọc **PaidAt theo ngày Việt Nam**, bao gồm đầu ngày và loại đầu ngày sau. Không cộng bill tạm tính/đơn chưa thanh toán hoặc tính lại theo giá danh mục.
- Tiền TEXT được đọc và cộng bằng .NET decimal; không dùng SQLite SUM/REAL, double hoặc Int64. Nếu tổng vượt phạm vi decimal, trả lỗi rõ ràng thay vì làm tròn/tràn số.
- ReadAccess kiểm tra role/user/stamp trong transaction. Đổi Manager thành Staff, thu hồi Report.View/Dashboard.View, khóa tài khoản, đổi stamp hoặc logout có hiệu lực ở lần đọc tiếp theo.

## Kiểm tra

- Debug/Release build đạt tại bin/VerifyDebug và bin/VerifyRelease.
- **26 bộ kiểm tra đạt** trên dữ liệu tạm: Dashboard và toàn bộ 25 bộ trước đó, gồm Calendar/Availability/AuthenticationUi sau khi chia sẻ quy tắc trạng thái phòng. Dashboard chạy thêm Release.
- `Verify-Dashboard` kiểm tra dữ liệu rỗng, Dashboard-only không cần các quyền chi tiết, Staff bị gán nhầm quyền báo cáo, Admin/Manager đủ quyền và mất quyền/role/stamp/active/logout.
- Fixture trạng thái/lịch kiểm tra phòng khóa, Active, Reserved, tương lai, Confirmed hết grace nhưng DB chưa đổi; booking hôm nay đủ status, phiên từ ngày trước và chỉ Pending của Active. So sánh trạng thái với Calendar cùng mốc giờ; DTO không PII khách.
- Kiểm tra đúng 5 phút, hơn 5 phút một tick, đúng +15 phút, đúng ExpectedEnd/ReturnBy và sau một tick; giới hạn walk-in theo booking của khách ở phòng khác và đổi động sau NoShow; giờ nghỉ, đúng 23:00, qua đêm, 09:00/13:00 mở lại.
- Revenue dùng hóa đơn tạo qua walk-in/Order/checkout thật: thanh toán đúng 00:00 Việt Nam và tổng vượt Int64, giữ từng đồng; đổi giá hiện hành không ảnh hưởng. Fixture riêng kiểm tra tổng vượt decimal và Staff không đọc tổng đó.
- Gate clock chặn reader rồi chạy checkout thật đồng thời: writer chờ, snapshot đầu vẫn có Active/doanh thu trước checkout; lần đọc sau thấy Completed/doanh thu mới. Không trộn hai thời điểm trong một dashboard.
- Các lần đọc/cảnh báo không tạo invoice/audit, không ghi NoShow hoặc đổi schema. Database/thư mục tạm được dọn; không chạm database thật ở LOCALAPPDATA.
- AuthenticationUi vẫn render **116 ảnh** của giao diện hiện có; InvoicePrinting vẫn dùng bộ giả lập, không gửi máy in thật. Bước này không có ảnh dashboard vì chưa nối UI.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Dashboard.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
```

## Tiếp theo

**8a.2 UI dashboard** trên màn chính, thay bảng kiểm tra quyền kỹ thuật: các số liệu vận hành, cảnh báo, nhãn cập nhật và nút Làm mới. Có quyền Dashboard.View mới đọc/hiện; đổi phiên/mất quyền/lỗi tải xóa dữ liệu cũ. Các thao tác mở màn chi tiết vẫn cần quyền riêng, không dùng dashboard để vượt quyền. Chỉ Admin/Manager đủ quyền thấy doanh thu tổng hợp.

Sau đó giữ phần quản trị tài khoản tạo/sửa/khóa/gán Staff–Manager–Admin/reset mật khẩu/bảo vệ Admin cuối cùng, ma trận Role → Permission và audit; báo cáo/Excel, lịch sử khách, dữ liệu demo, kiểm tra luồng và tài liệu đồ án. Dashboard theo hoạt động quán, không thêm khoản chi điện/thuê mặt bằng.
