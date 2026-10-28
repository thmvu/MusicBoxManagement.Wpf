# MySQL 04j — Dashboard vận hành

Ngày 28/10/2026. Đối chiếu reference mục 5/29, giờ/lịch mục 38–39, Step8a1_DashboardFoundation và [bảng bàn giao](MySql_Migration_Status.md). Phần này port nguồn đọc, không đổi nghiệp vụ, layout, palette hoặc nguồn mặc định của App/worker.

## Hợp đồng đọc

Thêm `MySqlDashboardService.Read/ReadRevenueToday` và `MySqlStaffReservationService.ForDashboard`. Factory kiểm tra Dashboard.View độc lập Reservation.View; mỗi lần đọc kiểm tra lại user/active/stamp/role/quyền bằng ReadAccess trong **RR read-only**. Không gọi Demand dành cho writer trong transaction đọc.

- Quyền và tất cả danh sách/doanh thu dùng cùng snapshot InnoDB. Một IClock chuẩn hóa UTC sau ReadAccess, dùng chung để xác định ngày Việt Nam, grace, giờ ca và cảnh báo; không lấy clock riêng cho từng bảng. Clock đặt trước projection như Dashboard SQLite, không phải thời điểm writer chốt tiền.
- DTO DashboardSnapshot hiện có: phòng/trạng thái, booking hôm nay, Active kể cả từ ngày trước, Pending của Active, cảnh báo và CheckedAt. Không trả tên/SĐT/CustomerId hoặc chi tiết món/hóa đơn.
- Trạng thái phòng dùng MySqlScheduleRules chung với Calendar: Inactive trước, sau đó Occupied, Reserved trong grace đã đến giờ, còn lại Available. Booking tương lai không tự biến phòng thành Reserved.
- Booking hôm nay theo StartTime trong `[00:00, 00:00 ngày sau)` UTC+7, đủ trạng thái; Confirmed hết đúng +15 phút chiếu NoShow trong DTO. Không chạy maintenance hoặc UPDATE.
- Phiên booking giữ ExpectedEnd; walk-in dùng ReturnBy động theo ca gốc và lịch còn hiệu lực của cả Room/Customer, không lưu thành ExpectedEnd hoặc tạo hold tương lai.

## Cảnh báo và doanh thu

Giữ bốn loại cảnh báo của SQLite: NearNoShow khi còn `(0,5 phút]`; Overdue khi `now > ExpectedEnd/ReturnBy`; NextBooking theo lượt Confirmed sớm nhất của cùng phòng **hoặc khách**, bỏ booking nguồn; OutsideHours khi còn Active trước 09:00, trong 12:00–13:00 hoặc từ 23:00. Đúng hạn trả chưa quá giờ; sau một tick mới quá giờ. Khi lượt kế tiếp hết grace, ReturnBy và cảnh báo đổi theo lịch còn hiệu lực. Sắp cảnh báo theo thời điểm/phòng/loại như hiện hành.

TodayRevenue là `decimal?`: thiếu quyền trả null, đủ quyền nhưng chưa có thu trả 0. Chỉ Admin hoặc Manager có Report.View hiện hành được nhận tổng; Staff luôn bị chặn cả khi dữ liệu gán nhầm Report.View/Export. `ReadRevenueToday` cũng cần Dashboard.View. Không yêu cầu Invoice.View/Order.View/Customer CRUD.

Nguồn thu là Invoice.TotalAmount đã chốt, lọc PaidAt theo ngày Việt Nam, không cộng bill Active hoặc giá danh mục hiện tại. MySQL DECIMAL(29,0) đọc bằng GetDecimal và cộng bằng .NET decimal; không double/Int64 hoặc parse TEXT. Tràn tổng trả lỗi rõ ràng. Không audit/NoShow/checkout/hủy Pending/tự kết thúc/cache/DDL trong cả hai API.

## Kiểm tra và bằng chứng

Build `bin/VerifyDashboardMySqlDebug` và `bin/VerifyDashboardMySqlRelease` đạt. **Verify-MySqlDashboard bản test cuối online cả Debug/Release đạt, exit 0**, gồm phục hồi trigger và dọn GUID database. Log `%TEMP%/MusicBoxMySqlDashboardFinalDebug_20261028.log` và `%TEMP%/MusicBoxMySqlDashboardFinalRelease_20261028.log`. Production source không đổi sau hai build; lần sửa sau build chỉ ở corruption fixture test.

**15/15 bộ Debug liên quan đạt**: 5 MySQL online (Dashboard mới, Calendar, Authentication, StaffReservations, Foundation) + 10 SQLite/WPF (Dashboard, Calendar, Availability, Authentication, Foundation, StaffReservations, Checkout, Invoices, Workflows, AuthenticationUi). Summary 4 MySQL hồi quy `%TEMP%/MusicBoxDashboardMySqlRegression_cdfdb6f81d554df3bf29d0f69377c896/results.json`; summary 10 SQLite/WPF `%TEMP%/MusicBoxDashboardLocalRegression_ad9c9a9c283b4a138b8a10b5686a7cee/results.json`. AuthenticationUi 142 ảnh tại `%TEMP%/MusicBoxUi_4a8f334ddd144ee4913fa04044186a83`. Không sửa assertion cũ hoặc tuyên bố đã chạy mọi suite. Không thay UI nên không coi ảnh hồi quy SQLite là Dashboard UI đã dùng MySQL; không rerun toàn bộ 14 race Checkout cũ trong lượt này, xem bằng chứng 04h/04i và ca checkout-reader thật mới ở đây.

`Verify-MySqlDashboard` tạo GUID database, có log GUID/lỗi trước cleanup và kiểm tra tên trước DROP. Danh mục/tài khoản seed riêng; booking, hủy, check-in, walk-in, món và tất cả Completed/Invoice qua service MySQL thật. Không INSERT Completed/Invoice giả. Clock có điều khiển để tái hiện booking Confirmed chưa maintenance. Fixture tràn tổng xác nhận UPDATE bị trigger Invoices_NoUpdate từ chối, đọc DDL gốc, rồi chỉ trong GUID database tạm bỏ trigger để cố ý sửa số tiền hai Invoice thật; finally phục hồi tiền và chính DDL đó, xác nhận UPDATE lại bị chặn. Đây là dữ liệu không hợp lệ để kiểm tra lỗi tổng, không phải checkout có thể tạo ra số tiền đó; production trigger/schema không sửa.

Lượt đầu `%TEMP%/MusicBoxMySqlDashboardDebug_20261028.log` dừng ở `Invoice is immutable` khi fixture tràn tổng cố UPDATE hóa đơn; các ca vận hành trước đó đạt nhưng cả suite chưa đạt. Đã sửa cách dựng corruption fixture như trên, giữ guard bất biến và assertion tràn tổng; không nới service/trigger production để qua test. Kết quả bản cuối ghi riêng, không ghi đè log đầu.

Các ca: rỗng/quyền độc lập/Staff bị gán nhầm Report; bốn trạng thái phòng đối chiếu Calendar; booking đầy đủ trạng thái; Active từ hôm trước; Pending-only; DTO không PII; ticks và ReturnBy theo Customer ở phòng khác; đúng 5 phút/hơn một tick/+15 phút/đúng hạn–sau tick; giờ nghỉ/đêm/mở lại; không ghi dữ liệu/audit. Revenue qua thanh toán thật đúng 00:00, tổng vượt Int64, đổi giá không tính lại, offset clock không đổi ngày Việt Nam, tràn decimal. Thu hồi Dashboard/Report, role đổi thành Staff, lock/stamp/logout/null và factory đều kiểm tra hiện hành.

Gate clock dừng reader sau snapshot quyền nhưng trước đọc danh sách. Checkout writer thật phải **commit trước khi reader đóng**; writer metadata và thu hồi Report cũng commit. Reader cũ vẫn thấy quyền/doanh thu/Active/Pending/tên phòng cũ, lần sau thấy thay đổi đồng bộ, không trộn hoặc cache; không áp dụng kỳ vọng SQLite writer phải chờ vào MySQL MVCC.

```powershell
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlDashboard.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDashboardMySqlDebug
```

Đổi BuildDirectory sang VerifyDashboardMySqlRelease để chạy Release. Chỉ database GUID/thư mục tạm; không dữ liệu nghiệp vụ/Content thật hoặc máy in/cloud schema đang dùng của quán.

## Bàn giao tiếp

Tiếp **ma trận quyền Staff/Manager và audit viewer MySQL**, giữ quyền quản trị/current rights/dependency/last Admin/stale/transaction/audit. Còn báo cáo/Excel/history, worker/provider/UI toàn app, backup/import/đối chiếu database + Content/cutover/phục hồi, ERD/từ điển/bản xuất/demo/máy in thật.

App/worker vẫn SQLite v8, đích MySQL v1. Chưa nối DashboardViewModel/UI sang MySQL hoặc nhập dữ liệu thật. Không đổi lớp Dashboard SQLite/shared DTO/VM/XAML; chỉ thêm Compile entry MySqlDashboardService vào commit, định dạng csproj có sẵn giữ riêng.
