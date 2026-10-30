# Bàn giao migration SQLite → MySQL Aiven

## Seed phòng và khách hàng mẫu — 30/10/2026

Người dùng đã thiết lập Admin và yêu cầu seed. Đã thêm vào Aiven musicbox **5 phòng active (3 Standard/2 VIP, có ảnh từ assets) và 6 Khách Demo**, số giả lập0000000101–0000000106. Script quản trị `Tools/Seed-MySqlDemo.ps1` dry-run mặc định, Apply ghi cùng transaction với11 System audit, chạy lại bỏ qua mã/SĐT cũ; không giả phiên Admin hoặc sửa account/quyền/giá. Guest service thật đọc đủ5 phòng/ảnh; lặp Apply không trùng. Hiện Users1/Rooms5/Customers6/AuditLog12, nghiệp vụ và Services vẫn0. Không chạy lại import snapshot29/10 vì sẽ mất dữ liệu mới; ảnh vẫn local. [Chi tiết và kiểm tra](MySql_Demo_Seed_20261030.md). Không đổi code WPF/service/schema hoặc định dạng csproj có sẵn.


## Hoàn tất chuyển MySQL/Aiven — 29/10/2026

Đã port báo cáo phòng/booking/món + Excel và lịch sử khách; nối toàn bộ VM/Views qua service contracts, App và NoShow worker cùng provider. **App.config mặc định MySql / musicbox**, không tự fallback SQLite. Không đổi giao diện/luật/quyền/SSL. [Ports/wiring](MySql_Migration_06b_08_ReportsHistoryWiring.md), [nhập/cutover/khôi phục](MySql_Migration_09_Import_Cutover.md).

Đã nhập nguồn mặc định người dùng xác nhận: SQLite v1 chỉ RoomTypes2, nâng bản sao v8 rồi đối chiếu 15 bảng trên Aiven, SQLite gốc hash không đổi. Roles3/Permission27/RolePermission43 là migration nền tảng; các bảng tài khoản/nghiệp vụ/audit đều0, Content không có ảnh phòng thật. Không tạo tài khoản/doanh thu giả. Mở Release thật thấy Khách/Standard/VIP/**Thiết lập Admin**; người dùng tự tạo Admin lần đầu. Hồ sơ nhập riêng ngoài Git trong AppData Backups, không chạy import lại sau vận hành.

Kiểm tra: runner Debug70/70 (44 local+26 MySQL lúc bắt đầu), Release local44/44; RemainingReads/OperationsUi/Import bản cuối cả Debug/Release đạt riêng, AuthenticationUi Release142 ảnh. Runner hiện72=44+28, không tuyên bố cả72 hai cấu hình. Giữ log/phạm vi trong cutover report; không nới SSL/timeout/assertion. Có [ERD16 bảng/từ điển/SVG/PNG](ERD_Full_And_DataDictionary.md) và [demo/vận hành](MySql_Demo_And_Operations.md).

Còn nghiệm thu người dùng tại quầy/mạng thật và in giấy/driver thật; kiểm tra máy in giả không thay phần này. Ảnh vẫn local, không cloud upload/sync hai chiều; rollback snapshot không chứa dữ liệu MySQL phát sinh sau cutover. Các mốc bên dưới là lịch sử, **không làm lại 01–09 hoặc coi App còn SQLite**. Định dạng csproj có sẵn giữ ngoài commit; cập nhật bàn giao/code/test/README/plan/commit/push cùng thay đổi tiếp theo.


Cập nhật 29/10/2026. **Đọc file này trước khi nhận việc migration.** Đây là bảng hiện trạng để người pull repo không làm lại phần đã chuyển; các báo cáo bước cũ là bằng chứng theo thời điểm, không phải danh sách việc còn thiếu mới nhất.

**Mốc 29/10: 06a doanh thu và Excel doanh thu MySQL đạt online Debug/Release**, hồi quy Debug 11/11 bộ liên quan đạt: PaidAt ngày Việt Nam/decimal/RoomType snapshot, quyền View/Export hiện hành và Staff không tổng hợp. [Kết quả và giới hạn](MySql_Migration_06a_Revenue.md). Tiếp sử dụng phòng; app/worker/UI vẫn SQLite, chưa nhập/cutover.

Trước đó 28/10: 05b tra cứu nhật ký MySQL đạt online Debug/Release (Release retry sau Connect Timeout). Audit.View Admin-only/RR read-only/filters exact/keyset/ticks/current metadata, không ghi khi đọc. [Bằng chứng và giới hạn](MySql_Migration_05b_AuditLog.md). Tiếp báo cáo doanh thu; App/UI vẫn SQLite, chưa import/cutover.

Trước đó 28/10: 05a ma trận quyền MySQL đạt online Debug/Release**, Admin chỉnh Staff/Manager riêng, giữ giới hạn/dependency/raw Revision/cleanup/no-op, khóa role row rồi kiểm tra lại actor/stale/ghi quyền + audit cùng Serializable. Hồi quy Debug mới nhất 17/17 bộ liên quan đạt (5 MySQL + 12 SQLite/WPF), gồm các lượt chạy lại do timeout; log/phạm vi tại [05a](MySql_Migration_05a_PermissionMatrix.md). Tiếp 05b Audit viewer MySQL; App/worker/UI vẫn SQLite, chưa import DB/Content thật hoặc cutover.

Trước đó 28/10: 04j Dashboard MySQL đạt online Debug/Release, Read/ReadRevenueToday và ForDashboard theo Dashboard.View độc lập; room status/today booking/Active/Pending/cảnh báo, RR read-only cùng quyền–dữ liệu/một clock, revenue PaidAt chỉ Admin hoặc Manager đủ Report.View. Hồi quy lượt đó 15/15 bộ Debug liên quan đạt (5 MySQL + 10 SQLite/WPF), UI 142 ảnh. Kết quả/log/giới hạn tại [04j](MySql_Migration_04j_Dashboard.md).

Trước đó 28/10: 04i tra cứu/đọc và luồng in hóa đơn MySQL đã đạt online Debug/Release. Search mã/PaidAt ngày Việt Nam/SĐT hiện hành; View/Print hiện hành, RR read-only, snapshot/decimal/consistency. Hai VM dùng IInvoiceService với MySQL trong fixture WPF, máy in giả, 7 ảnh/bản và phiếu 70 dòng/4 trang; chưa nghiệm thu giấy/driver thật. 44/44 bộ SQLite/WPF Debug và Invoices/InvoicePrinting thêm Release đạt. Hồi quy MySQL và lịch sử lỗi kết nối/chạy lại ghi tại [04i](MySql_Migration_04i_InvoiceHistoryPrinting.md).

Trước đó 28/10: 04h Checkout/Invoice nền tảng đã đạt online Debug/Release, selector/preview CheckOut-only, Confirm chốt tiền/giờ mới và hủy Pending/hoàn tất phiên–nguồn/tạo Invoice/audit cùng transaction, replay/rollback và 14 ca writer thật hai thứ tự. Hồi quy lượt đó 40/40 bộ Debug liên quan đạt (13 MySQL + 27 SQLite/WPF), UI 142 ảnh. Xem [04h và log](MySql_Migration_04h_Checkout.md).

Trước đó 28/10: 04g Billing đạt online Debug/Release, tiền tạm tính Guest/Staff/actual ticks/snapshot/decimal/Completed-only và calculator RR Preview/Serializable Confirm. Hồi quy lượt đó 34/34 bộ đạt; xem [04g](MySql_Migration_04g_Billing.md).

Trước đó 28/10: 04f Order đạt online Debug/Release, gồm menu/giỏ/preview/tạo Guest–Staff/xem/phục vụ/hủy, quyền riêng/Active/current phone/snapshot/transaction/audit. Xem [báo cáo 04f](MySql_Migration_04f_Orders.md).

Hồi quy lượt 04f: 32/32 bộ Debug liên quan đạt (8 MySQL online + 24 SQLite/WPF), AuthenticationUi 142 ảnh; phạm vi và log trong báo cáo. Không tuyên bố toàn bộ MySQL hoặc chuỗi checkout đã hoàn tất.

Trước đó 28/10: 04e.4 Guest đã đạt online Debug/Release, tra cứu Confirmed + Active thật/SĐT hiện hành/hủy/gia hạn/public DTO/audit Guest. Hồi quy lượt đó 26/26 bộ Debug liên quan đạt (8 MySQL online + 18 SQLite/WPF, UI 142 ảnh). Xem [báo cáo 04e.4 và log](MySql_Migration_04e4_GuestSessions.md). Các mục cũ bên dưới là lịch sử, không phải yêu cầu làm lại phần đã nghiệm thu.

Runner Verify-All hiện phát hiện 44 bộ SQLite/WPF và 25 bộ MySQL (thêm MySqlRevenueReport); đây là số bộ có sẵn, không phải tuyên bố đã chạy cả 69 bộ trong lượt 06a.

Ngày 24/10: theo yêu cầu người dùng, rà soát UI/nhập liệu và chạy hồi quy toàn bộ; xem [báo cáo](Ui_Validation_Navigation_Review.md). Không port service mới ở lượt này; MySQL vẫn hoàn tất đến 04e.3, tiếp 04e.4 Guest. App/worker vẫn SQLite v8, đích MySQL v1; không nhập database hoặc Content thật và không cutover.

Hồi quy 24/10: toàn bộ 15 bộ MySQL đã port online Debug đạt trên database GUID. SQLite/WPF 43 bộ Debug đạt; UI cuối Debug/Release đạt 140 ảnh/bản. Không nâng mức nghiệm thu các phần chưa port hoặc đánh dấu nhập thật/provider xong chỉ vì lượt test này.

Bổ sung 24/10: [ma trận kiểm toán luồng](Business_Workflow_Audit_20261024.md) và Verify-Workflows kiểm tra chuỗi service thật trên SQLite, không dùng nó làm bằng chứng MySQL checkout/Guest đầy đủ. Runner nay có 44 bộ SQLite/WPF (thêm Workflow), 15 bộ MySQL hiện có. Không port thêm service ở lượt đối chiếu này; tiếp 04e.4 như bảng bên dưới.

UI 24/10 theo yêu cầu trước khi tiếp port: [theme/bảng vận hành/nút nhanh/form](Ui_Operations_Design.md), giữ business/provider. Build Debug/Release và hồi quy runtime 44 bộ, UI cuối 141 ảnh/bản đạt. Không chạy lại online hoặc thay trạng thái nghiệm thu MySQL; chưa import/cutover. Bước tiếp vẫn 04e.4 Guest.

Phản hồi palette 24/10: phối riêng graphite/olive/sage/brass từ cảm giác chung của tham khảo web; chỉ thay resource màu và trạng thái chọn Tổng quan. Không port thêm hoặc đổi provider. Xem Ui_Operations_Design; tiếp 04e.4 không đổi.

Kiểm tra palette: build Debug/Release đạt, 44/44 bộ SQLite/WPF Debug và UI cuối 142 ảnh/bản đạt. Đây là bằng chứng giao diện/runtime SQLite, không nâng trạng thái nghiệm thu dịch vụ MySQL hoặc import/cutover.

Commit bàn giao gần nhất: chạy `git log -1 -- docs/MySql_Migration_Status.md`. Bảng này được push cùng code/test của bước đó; không cần suy đoán tiến độ từ chat hoặc số bảng trên cloud.

## Quy ước khi bàn giao

1. Pull commit mới trước khi nhận việc; đối chiếu bảng dưới với source/test. Mỗi phần hoàn thành phải cập nhật hàng tương ứng, ngày, báo cáo bước, lệnh/kết quả kiểm tra và bước tiếp theo, rồi commit/push cùng code. Không chỉ ghi tiến độ trong chat.
2. **Có code** chưa đồng nghĩa **đã kiểm tra online đạt**. Nếu DNS/kết nối/test lỗi phải ghi rõ chưa nghiệm thu, không đánh dấu hoàn thành từ việc build được.
3. **Service MySQL đạt** chưa đồng nghĩa **đã nhập dữ liệu thật** hoặc **App đang dùng MySQL**. Theo dõi ba việc này riêng; giữ toàn bộ chức năng SQLite/WPF đã có.
4. Ghi kết quả mới nhất cho phần vừa sửa; không tự nâng ngày nghiệm thu các phần chỉ kế thừa. Không lưu mật khẩu, connection string có mật khẩu, tên/SĐT khách hoặc database dump trong Git.

## Hiện trạng vận hành và dữ liệu thật

| Mục | Trạng thái |
|---|---|
| App.OnStartup, các màn hình và NoShow worker | **MySql mặc định**, cùng ApplicationServices; SQLite chọn rõ khi phục hồi |
| Schema đích | MySQL v1; tương ứng nghiệp vụ SQLite v8, 16 bảng gồm bảng phiên bản |
| Database Aiven `musicbox` | Đã import/verify15 bảng ngày29/10, hồ sơ riêng AppData Backups |
| Dữ liệu nghiệp vụ/tài khoản đang dùng | Nguồn import v1 chỉ2 loại phòng; sau cutover người dùng tạo1 Admin, theo yêu cầu seed5 phòng/6 khách Demo; chưa có booking/thanh toán |
| Ảnh `Content/uploads/rooms` | Root MySql riêng có5 ảnh demo từ assets, đã kiểm qua guest helper; ảnh vẫn local |
| Đổi provider cho WPF | **Đã chuyển** App.config MySql/musicbox; Admin đã thiết lập, seed30/10:5 phòng/6 khách Demo |
| Các database `musicbox_test_<GUID>` | Chỉ fixture kiểm tra online, dọn sau test; không phải dữ liệu đã nhập của quán |

## Phần service đã chuyển — tiếp tục từ hàng chưa hoàn thành

| Bước | Phạm vi/code | Trạng thái kiểm tra | Bằng chứng |
|---|---|---|---|
| 01 | MySqlDatabase, DDL/schema/seed/version/constraint | Đã đạt online theo báo cáo bước | [01](MySql_Migration_01_Foundation.md), Verify-MySqlFoundation |
| 02a | Identity store, MySqlAuthentication/Permission/Audit | Đã đạt online Debug/Release; chưa gồm màn hình ma trận quyền/audit viewer | [02a](MySql_Migration_02a_Authentication.md), Verify-MySqlAuthentication |
| 02b | Quản trị tài khoản, role/lock/reset/last Admin/stamp | Đã đạt online Debug/Release | [02b](MySql_Migration_02b_Accounts.md), Verify-MySqlAccounts |
| 03a | Loại phòng Standard/VIP | Đã đạt online Debug/Release | [03a](MySql_Migration_03a_RoomTypes.md), Verify-MySqlRoomTypes |
| 03b | Phòng/ảnh local/sửa/khóa-mở | Đã có service và nghiệm thu theo báo cáo; ảnh chưa upload online | [03b](MySql_Migration_03b_Rooms.md), Verify-MySqlRooms |
| 03c | Danh mục dịch vụ | Đã có service và nghiệm thu theo báo cáo | [03c](MySql_Migration_03c_Services.md), Verify-MySqlServices |
| 03d | Khách hàng/SĐT/tạo-sửa-tìm | Đã đạt online Debug/Release 18/10; history hoàn tất bước07 | [03d](MySql_Migration_03d_Customers.md), Verify-MySqlCustomers |
| 04a | Availability/ScheduleRules/giờ hoạt động | Đã đạt online Debug/Release; helper phiên không thay service writer | [04a](MySql_Migration_04a_Availability.md), Verify-MySqlAvailability |
| 04b | Tạo Guest/Staff Reservation, Staff hủy, NoShow | Đã đạt online Debug/Release | [04b](MySql_Migration_04b_Reservations.md), Verify-MySqlReservations |
| 04c.1 | Guest/Staff catalog/preview/create/lịch trống public | Đã đạt online Debug/Release; DTO public riêng | [04c.1](MySql_Migration_04c1_BookingReads.md), Verify-MySqlBookingReads |
| 04c.2a | Danh sách/chi tiết/hủy booking nội bộ | Đã đạt online Debug/Release | [04c.2a](MySql_Migration_04c2a_StaffReservations.md), Verify-MySqlStaffReservations |
| 04d | Lịch ngày/tuần nội bộ | **Đã sửa và đạt online Debug/Release 21/10**; lần 20/10 chỉ build vì DNS | [04d](MySql_Migration_04d_Calendar.md), Verify-MySqlCalendar |
| 04e.1 | MySqlRoomSessionService.CheckIn | **Đã đạt online Debug/Release 21/10**, gồm writer thật/replay/Room/Customer/NoShow/clock sau lock | [04e.1](MySql_Migration_04e1_CheckIn.md), Verify-MySqlCheckIn |
| 04e.2 | MySqlRoomSessionService walk-in: list/preview/create/read/own receipt | **Đã đạt online Debug/Release 21/10**, gồm snapshot/receipt/rollback/writer thật/retry NoShow có giới hạn | [04e.2](MySql_Migration_04e2_WalkIn.md), Verify-MySqlWalkIn |
| 04e.3 | MySqlStaffSessionService.Search/Preview/Extend và core gia hạn Staff | **Đã đạt online Debug/Release 22/10**, View/Extend độc lập, stale/overlap/rollback/writer thật/snapshot | [04e.3](MySql_Migration_04e3_SessionsExtension.md), Verify-MySqlSessions |
| 04e.4 | MySqlGuestSessionService và MySqlGuestReservationService: lookup Confirmed/Active, hủy và gia hạn Guest | **Đã đạt online Debug/Release 28/10**, current phone/public DTO/snapshot/Guest audit/rollback/writer races/fresh clock | [04e.4](MySql_Migration_04e4_GuestSessions.md), Verify-MySqlGuestSessions |
| 04f | MySqlOrderService, ForOrders: workspace/menu/giỏ/preview/tạo Guest–Staff/xem/phục vụ/hủy | **Đã đạt online Debug/Release 28/10**, quyền riêng/current phone/snapshot/ca/rollback/races/clock sau Room và Service wait | [04f](MySql_Migration_04f_Orders.md), Verify-MySqlOrders |
| 04g | MySqlBillingService, ForBilling: tiền tạm tính Guest/Staff và calculator chung cho Checkout | **Đã đạt online Debug/Release bản cuối 28/10**, precision/oracle/Completed-only/live View/current phone/RR snapshot/shared reader-writer/rollback/fresh clock | [04g](MySql_Migration_04g_Billing.md), Verify-MySqlBilling |
| 04h | MySqlCheckoutService: selector/preview/Confirm thanh toán và tạo Invoice | **Đã đạt online Debug/Release 28/10**, atomic/source-Pending-session-invoice-audit/rollback/replay/14 ordered writer races/fresh clock/decimal/MVCC | [04h](MySql_Migration_04h_Checkout.md), Verify-MySqlCheckout |
| 04i | MySqlInvoiceService.Search/Read/ReadForPrint, ForInvoices và IInvoiceService cho hai VM xem/in | **Đã đạt online Debug/Release 28/10**, current rights/filters/snapshot/decimal/RR/MVCC; WPF MySQL với máy in giả, 70 dòng/4 trang, revoke/dialog/busy/failure | [04i](MySql_Migration_04i_InvoiceHistoryPrinting.md), Verify-MySqlInvoices/Verify-MySqlInvoicePrinting |
| 04j | MySqlDashboardService.Read/ReadRevenueToday và ForDashboard | **Đã đạt online Debug/Release bản cuối 28/10**, RR current rights/room/booking/Active/Pending/warnings/decimal revenue/MVCC vs checkout thật; đã wiring bước08 | [04j](MySql_Migration_04j_Dashboard.md), Verify-MySqlDashboard |
| 05a | MySqlPermissionManagementService.Read/Update/ValidateSelection và ForPermissions | **Đã đạt online Debug/Release 28/10**, RR reader/Admin-only/raw revision/cleanup/no-op/Serializable role row lock/current actor/atomic audit/stale concurrent writers/real account lock/clock sau chờ; đã wiring bước08 | [05a](MySql_Migration_05a_PermissionMatrix.md), Verify-MySqlPermissionMatrix |
| 05b | MySqlAuditLogService.Search/Read và ForAudit | Đạt online Debug/Release (Release retry), quyền/metadata RR, filters UTC+7/ticks/keyset/read-only, đã wiring bước08 | [05b](MySql_Migration_05b_AuditLog.md), Verify-MySqlAuditLog |
| 06a | MySqlReportService.ReadRevenue/ExportRevenue, ForReports | Doanh thu PaidAt/group/snapshot/decimal và Excel; kết quả online/hồi quy tại báo cáo, đã wiring bước08 | [06a](MySql_Migration_06a_Revenue.md), Verify-MySqlRevenueReport |

Các hàng đã nghiệm thu không cần viết lại service. Khi phần mới đụng luồng đó, chạy hồi quy theo báo cáo và bổ sung test cạnh tranh bằng service thật; fixture không phải nghiệm thu các service còn thiếu.

## Các bước cuối đã hoàn tất

| Bước | Phạm vi | Có code / online | Dữ liệu thật / provider |
|---|---|---|---|
| 06b–d | Báo cáo sử dụng phòng/booking/món + Excel/Top10 | Debug/Release RemainingReads đạt | Đã wiring WPF; production chưa có nghiệp vụ |
| 07 | CustomerHistory theo CustomerId/current phone/quyền Invoice | Debug/Release đạt | Đã wiring WPF |
| 08 | Service contracts/VM/Views/App/NoShow worker/provider | 70/70 Debug +44/44 Release local, OperationsUi cuối hai build | App.config MySql/musicbox, không fallback |
| 09 | Backup/prepare/import/verify/Content/restore | Import cuối hai build đạt, rehearsal nguồn thật đạt | Đã nhập/verify thật15bảng, mở app thật đạt |
| 10 | ERD16 bảng/từ điển/Mermaid/DOT/SVG/PNG/demo | Sinh từ DDL, xem PNG | Không chứa dữ liệu người dùng |

Bằng chứng chi tiết: [ports/wiring](MySql_Migration_06b_08_ReportsHistoryWiring.md), [import/cutover/khôi phục](MySql_Migration_09_Import_Cutover.md), [demo](MySql_Demo_And_Operations.md). Không còn service migration đang dở. Còn nghiệm thu người dùng tại quầy và máy in/driver/giấy thật; không lấy fixture thay các bước thực địa. Runner hiện72=44+28; runner toàn bộ lượt này70=44+26, hai bộ thêm chạy riêng. Các báo cáo cũ giữ nguyên lịch sử lỗi/kết quả.

## Nhật ký nhập dữ liệu thật

| Ngày/mã lượt | Nguồn/đích | Dữ liệu và đối chiếu | Điểm tiếp tục |
|---|---|---|---|
| 29/10, 370ffa184ef4423cb54e67ad28b6d4ba | SQLite mặc định v1 → raw backup → prepared v8 → Aiven musicbox | RoomTypes2/Roles3/Permission27/RolePermission43;11 bảng0. Hash15bảng đạt, FK/ID/decimal/ticks so đúng; sourceSHA256 giữ nguyên. Không có user/booking/invoice/ảnh thật bị bỏ sót | Mặc định MySql, người dùng thiết lập Admin, tạo phòng/dịch vụ; không import lại lên DB đã vận hành |

Chi tiết đường dẫn/hash theo bảng lưu **ngoài Git** `%LOCALAPPDATA%/MusicBoxManagement.Wpf.Backups/actual-import-20261029.json`; raw/prepared và Content bất biến bên cạnh. Đây là toàn bộ nguồn người dùng xác nhận, không coi số0 là dữ liệu nghiệp vụ đã demo. Ảnh fixture có hash và bước InstallContent; nguồn thật Rooms0 nên Content trống. [Runbook](MySql_Migration_09_Import_Cutover.md) có phương án phục hồi bản sao SQLite, không xóa cloud và không đồng bộ ngược dữ liệu phát sinh mới.

## Lượt kiểm tra 21/10/2026

Build tích hợp dùng `bin/VerifyMigrationDebug` và `bin/VerifyMigrationRelease`. Verify-MySqlCalendar và Verify-MySqlCheckIn online hai build đạt; MySqlAvailability/Reservations/Foundation online Debug đạt. Hồi quy SQLite Debug CheckIn/Availability/Reservations/Calendar/Foundation đạt; AuthenticationUi Debug đạt, render 131 ảnh. Chỉ tác động database test GUID/thư mục tạm; ứng dụng/worker và dữ liệu thật vẫn SQLite.

Bổ sung 04e.2: build VerifyWalkInMySqlDebug/Release đạt; Verify-MySqlWalkIn bản cuối online cả hai build đạt. MySqlCheckIn/Calendar/Availability/Reservations/Foundation online Debug đạt. 11 bộ SQLite/WPF Debug WalkIn/CheckIn/Availability/Calendar/Reservations/Rooms/Customers/Authentication/Foundation/GuestCalendar/AuthenticationUi đạt (131 ảnh). Đã kiểm tra conflict 1213 với NoShow: test retry tối đa một lần sau hai writer, không trùng session/log; API không retry vô hạn. Chưa nhập dữ liệu thật hoặc đổi giao diện/provider.

## Lượt kiểm tra 22/10/2026 — 04e.3

Build VerifySessionsMySqlDebug/Release và Verify-MySqlSessions online cả hai build đạt (exit 0). Hồi quy MySqlCheckIn/WalkIn/Availability/Reservations/Calendar/Foundation online Debug đạt. 16 bộ SQLite/WPF Debug Sessions/Extension/WalkIn/CheckIn/Availability/Calendar/Reservations/GuestCalendar/Authentication/Foundation/StaffReservations/Rooms/Customers/GuestSessions/GuestLookup/AuthenticationUi đạt, UI 131 ảnh. Không thay UI/provider hoặc nhập dữ liệu thật.

## Lượt kiểm tra 28/10/2026 — 04e.4

Build VerifyGuestMySqlDebug/Release đạt. Verify-MySqlGuestSessions bản cuối online Debug/Release đạt, log `%TEMP%/MusicBoxMySqlGuestFinalDebug_20261028.log` và `%TEMP%/MusicBoxMySqlGuestFinalRelease_20261028.log`. Hồi quy 26/26 bộ Debug đạt (8 MySQL online + 18 SQLite/WPF); summary `%TEMP%/MusicBoxGuestRegression_6b820d8e8eff48a68a3f879ffab44ec8/results.json`, AuthenticationUi 142 ảnh. Danh sách bộ/ca/ranh giới trong báo cáo 04e.4. Chỉ database test GUID/thư mục tạm; không dữ liệu thật/Content/provider/cutover. Completed fixture không chứng minh checkout MySQL; còn menu/món/tiền và nhóm tiếp theo.

## Lượt kiểm tra 28/10/2026 — 04f

Build VerifyOrdersMySqlDebug/Release và Verify-MySqlOrders online hai build đạt (exit 0), log `%TEMP%/MusicBoxMySqlOrdersDebug_20261028.log` và `%TEMP%/MusicBoxMySqlOrdersRelease_20261028.log`. Hồi quy 32/32 bộ Debug đạt (8 MySQL online + 24 SQLite/WPF), summary `%TEMP%/MusicBoxOrdersRegression_05696225bde5421d8e9a65417cbeadde/results.json`; AuthenticationUi 142 ảnh. Danh sách/ca/giới hạn trong báo cáo 04f. Chỉ database GUID/thư mục tạm, schema/provider/dữ liệu thật giữ nguyên; tiếp Billing rồi Checkout/Invoice. Completed fixture không thay race với checkout MySQL thật.

## Lượt kiểm tra 28/10/2026 — 04g

Build VerifyBillingMySqlDebug/Release và VerifyBillingFinalDebug/Release đạt. Verify-MySqlBilling bản cuối online cả hai Final đạt, log `%TEMP%/MusicBoxMySqlBillingFinalDebug_20261028.log` và `%TEMP%/MusicBoxMySqlBillingFinalRelease_20261028.log`. Hồi quy Debug 34/34 bộ liên quan trên VerifyBillingMySqlDebug đạt (9 MySQL + 25 SQLite/WPF), summary `%TEMP%/MusicBoxBillingRegression_000b37aa9aa048c99131db2e2321db20/results.json`, UI 142 ảnh. Sau điều chỉnh helper mới hỗ trợ RR Preview, Billing được kiểm tra lại hai Final; không thay service SQLite/các service MySQL cũ. Chi tiết/phạm vi trong báo cáo 04g. Chỉ GUID/thư mục tạm, chưa import/provider/cutover; fixture/shared writer không nghiệm thu Checkout MySQL.

## Lượt kiểm tra 28/10/2026 — 04h

Build VerifyCheckoutMySqlDebug/Release và Verify-MySqlCheckout online cả hai build đạt (exit 0); log `%TEMP%/MusicBoxMySqlCheckoutDebug_20261028.log` và `%TEMP%/MusicBoxMySqlCheckoutRelease_20261028.log`. Hồi quy 40/40 bộ Debug đạt (13 MySQL + 27 SQLite/WPF), summary `%TEMP%/MusicBoxCheckoutRegression_b662323f67d24e83ad3eb96d9194e24e/results.json`; AuthenticationUi 142 ảnh, InvoicePrinting SQLite không máy in thật. Danh sách/giới hạn trong 04h. Positive Completed/Invoice đều qua service thật; mới nghiệm thu core Checkout, chưa Invoice history/print MySQL hoặc provider/import/cutover.

## Lượt kiểm tra 28/10/2026 — 04i

Build VerifyInvoicesMySqlDebug/Release đạt; MySqlInvoices/MySqlInvoicePrinting online cả hai build đạt. Backend log Debug `%TEMP%/MusicBoxMySqlInvoicesDebug_20261028.log`, Release cuối `%TEMP%/MusicBoxMySqlInvoicesReleaseDiagnostic_20261028.log`; in `%TEMP%/MusicBoxMySqlInvoicePrintingDebug_20261028.log` và `%TEMP%/MusicBoxMySqlInvoicePrintingRelease_20261028.log`. 44/44 SQLite/WPF Debug đạt, summary `%TEMP%/MusicBoxRegression_a3b70b707c494e06bc2790c323b83c8e/results.json`, UI 142 ảnh; Invoices/InvoicePrinting SQLite thêm Release đạt. MySQL print 7 ảnh/bản, 70 dòng/4 trang, xem ảnh Debug và SHA256 7 cặp giống Release.

Hồi quy 8 bộ MySQL liên quan: Billing/StaffReservations/Customers/Authentication/Foundation/Services đạt ngay, Orders/Checkout ban đầu Connect Timeout (summary `%TEMP%/MusicBoxInvoiceMySqlRegression_419d18e997fc4c2e80546e8dcc37a24c/results.json` giữ nguyên 6/8). Chạy lại tuần tự hai writer đạt exit 0, summary `%TEMP%/MusicBoxInvoiceWriterRetry_579df3c6397d40ed80066b28281cbcde/results.json`; mới nhất 8/8, gồm 14 ca checkout cạnh tranh thật. Backend Invoice Release cũng có timeout/Broken trước lượt cuối đạt; báo cáo 04i giữ lịch sử/diagnostic/giới hạn, không đổi config hoặc assertion để che lỗi. Không tuyên bố đã chạy mọi suite MySQL. Chỉ GUID/thư mục tạm, WPF in dùng máy in giả; App/worker/schema/Content/dữ liệu thật chưa đổi.

## Lượt kiểm tra 28/10/2026 — 04j

Build VerifyDashboardMySqlDebug/Release và Verify-MySqlDashboard bản test cuối online cả hai build đạt exit 0; log `%TEMP%/MusicBoxMySqlDashboardFinalDebug_20261028.log` và `%TEMP%/MusicBoxMySqlDashboardFinalRelease_20261028.log`. Hồi quy Debug 15/15 bộ liên quan đạt: 5 MySQL (gồm Dashboard mới + Calendar/Authentication/StaffReservations/Foundation) và 10 SQLite/WPF (Dashboard/Calendar/Availability/Authentication/Foundation/StaffReservations/Checkout/Invoices/Workflows/AuthenticationUi), UI 142 ảnh. Summary `%TEMP%/MusicBoxDashboardMySqlRegression_cdfdb6f81d554df3bf29d0f69377c896/results.json` và `%TEMP%/MusicBoxDashboardLocalRegression_ad9c9a9c283b4a138b8a10b5686a7cee/results.json`; danh sách/giới hạn tại 04j, không mọi suite.

Lượt đầu dừng ở trigger `Invoice is immutable` của corruption fixture tràn tổng, giữ log riêng; đã sửa fixture GUID để xác nhận trigger/bỏ tạm/phục hồi DDL chính xác và tiền trong finally, không thay production. Completed/Invoice qua service thật, RR reader giữ snapshot cũ dù checkout/quyền/metadata writer commit trước reader đóng. App/worker/UI vẫn SQLite, schema version SQLite v8/MySQL v1; chưa nhập DB/Content hoặc cutover.

## Lượt kiểm tra 28/10/2026 — 05a

Build VerifyMatrixMySqlDebug/Release và Verify-MySqlPermissionMatrix online cả hai build đạt exit 0, log `%TEMP%/MusicBoxMySqlMatrixDebug_20261028.log` và `%TEMP%/MusicBoxMySqlMatrixRelease_20261028.log`. 12/12 bộ SQLite/WPF Debug liên quan đạt, summary `%TEMP%/MusicBoxMatrixLocalRegression_dcbfe9173b194c108d5f5a9977454be9/results.json`; UI 142/MatrixUi 8/AuditUi 6 ảnh. UI giữ SQLite, không xem ảnh hồi quy là bằng chứng wiring MySQL.

Hồi quy MySQL cũ đầu 2/4 (Authentication/Accounts đạt, StaffReservations/Foundation Connect Timeout), summary `%TEMP%/MusicBoxMatrixMySqlRegression_0e20bf3856a34045af3fd21d945fdbe4/results.json` giữ nguyên. Retry `%TEMP%/MusicBoxMatrixMySqlRetry_7f93a81fed1444a8b6dd5c5b466a9776/results.json` đạt Foundation, Staff còn timeout; diagnostic Staff cuối `%TEMP%/MusicBoxMatrixStaffDiagnostic_20261028.log` đạt exit 0 và cleanup. Mới nhất 17/17 bộ Debug liên quan đạt (5 MySQL gồm Matrix + 12 SQLite/WPF), không phải tất cả 67 bộ hoặc lượt đầu 4/4. Chỉ thêm GUID/lỗi trước cleanup vào test cũ; không đổi assertion/SSL/pooling/timeout/quyền, không xóa schema chưa xác định thuộc lượt này. App/worker/schema/dữ liệu thật/Content/provider giữ nguyên, tiếp 05b Audit reader.

## Lượt kiểm tra 28/10/2026 — 05b

Build VerifyAuditMySqlDebug/Release và Verify-MySqlAuditLog online Debug/Release retry đạt. 11/11 bộ Debug liên quan đạt (3 MySQL gồm bài mới + 8 SQLite/WPF), UI 142 ảnh. Summary hồi quy 10 bộ: %TEMP%/MusicBoxAuditRegression_9f0648aa3ae945b496809c6bc1e3469c/results.json. Release đầu Connect Timeout khi mở TCP writer phụ, log đầu/retry giữ riêng tại báo cáo 05b; không đổi assertion/config. Read RR read-only/filters ngày Việt Nam/keyset/ticks/current rights/current metadata được kiểm tra, không wiring UI/provider/import. Tiếp báo cáo doanh thu MySQL; không làm lại 01–05b.

## Lượt kiểm tra 29/10/2026 — 06a
Nghiệm thu 29/10: build VerifyRevenueMySqlDebug/Release và bài MySqlRevenueReport online hai build đều đạt exit 0, log %TEMP%/MusicBoxRevenueMySqlDebug_20261029.log và %TEMP%/MusicBoxRevenueMySqlRelease_20261029.log. Hồi quy Debug 11/11 bộ liên quan đạt: 3 MySQL (bài mới + Authentication/Foundation) và 8 SQLite/WPF (RevenueReport/RevenueExcel/RevenueReportUi/Dashboard/Invoices/Authentication/Foundation/AuthenticationUi). Summary 10 bộ hồi quy %TEMP%/MusicBoxRevenueRegression_e1753feb19464ea0804e8267c9eeaab5/results.json; AuthenticationUi 142 ảnh tại %TEMP%/MusicBoxUi_469ed58f01e946de8a6d193e4ebe8a21. Không sửa assertion cũ/UI/provider/import; không tuyên bố chạy cả 69 bộ có sẵn hoặc đã chuyển các báo cáo/Excel khác.
