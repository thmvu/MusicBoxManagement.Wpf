# Bàn giao migration SQLite → MySQL Aiven

Cập nhật 28/10/2026. **Đọc file này trước khi nhận việc migration.** Đây là bảng hiện trạng để người pull repo không làm lại phần đã chuyển; các báo cáo bước cũ là bằng chứng theo thời điểm, không phải danh sách việc còn thiếu mới nhất.

**Mốc mới nhất 28/10: 04g Billing đã đạt online Debug/Release**, tiền tạm tính Guest/Staff, actual ticks/giá snapshot/decimal/Completed-only/RR snapshot và calculator chung RR Preview/Serializable Confirm. Hồi quy 34/34 bộ Debug liên quan đạt (9 MySQL + 25 SQLite/WPF), UI 142 ảnh. Xem [04g và bằng chứng/build/log](MySql_Migration_04g_Billing.md). Tiếp Checkout/Invoice; app/worker vẫn SQLite, chưa import DB/Content thật hoặc cutover.

Trước đó 28/10: 04f Order đạt online Debug/Release, gồm menu/giỏ/preview/tạo Guest–Staff/xem/phục vụ/hủy, quyền riêng/Active/current phone/snapshot/transaction/audit. Xem [báo cáo 04f](MySql_Migration_04f_Orders.md).

Hồi quy lượt 04f: 32/32 bộ Debug liên quan đạt (8 MySQL online + 24 SQLite/WPF), AuthenticationUi 142 ảnh; phạm vi và log trong báo cáo. Không tuyên bố toàn bộ MySQL hoặc chuỗi checkout đã hoàn tất.

Trước đó 28/10: 04e.4 Guest đã đạt online Debug/Release, tra cứu Confirmed + Active thật/SĐT hiện hành/hủy/gia hạn/public DTO/audit Guest. Hồi quy lượt đó 26/26 bộ Debug liên quan đạt (8 MySQL online + 18 SQLite/WPF, UI 142 ảnh). Xem [báo cáo 04e.4 và log](MySql_Migration_04e4_GuestSessions.md). Các mục cũ bên dưới là lịch sử, không phải yêu cầu làm lại phần đã nghiệm thu.

Runner Verify-All hiện phát hiện 44 bộ SQLite/WPF và 18 bộ MySQL (thêm MySqlBilling); đây là số bộ có sẵn, không phải tuyên bố đã chạy cả 62 bộ trong lượt 28/10.

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
| App.OnStartup, các màn hình và NoShow worker | Vẫn dùng SQLite v8 |
| Schema đích | MySQL v1; tương ứng nghiệp vụ SQLite v8, 16 bảng gồm bảng phiên bản |
| Database Aiven `musicbox` | Đã tạo schema và seed nền tảng theo bước 01; chưa coi là bản dữ liệu SQLite đã chuyển |
| Dữ liệu nghiệp vụ/tài khoản đang dùng | **Chưa nhập từ SQLite sang MySQL** |
| Ảnh `Content/uploads/rooms` | Vẫn local; **chưa chuyển/đối chiếu ảnh thật** |
| Đổi provider cho WPF | **Chưa thực hiện**, đợi đủ service/import/đối chiếu/UI |
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
| 03d | Khách hàng/SĐT/tạo-sửa-tìm | Đã đạt online Debug/Release 18/10; **chưa có lịch sử khách MySQL** | [03d](MySql_Migration_03d_Customers.md), Verify-MySqlCustomers |
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

Các hàng đã nghiệm thu không cần viết lại service. Khi phần mới đụng luồng đó, chạy hồi quy theo báo cáo và bổ sung test cạnh tranh bằng service thật; fixture không phải nghiệm thu các service còn thiếu.

## Chưa chuyển — danh sách tiếp tục

1. **Tiếp Checkout/Invoice nền tảng**: Preview/selector CheckOut-only, Confirm kiểm tra quyền/khóa rồi tính lại giờ/tiền, Cash/BankTransfer, hủy Pending + Session/Reservation Completed + Invoice snapshot/PaidAt/audit cùng commit, rollback/replay. 04e.4/04f/04g đã nghiệm thu, không làm lại; Billing đạt chưa có nghĩa đã thu tiền.
2. Giữ kiểm tra cạnh tranh cùng các writer đã có; bổ sung checkout–gia hạn/order bằng service thật khi port checkout, không coi Completed fixture là checkout đã nghiệm thu.
3. Đọc/tìm/in Invoice lịch sử theo quyền View/Print, giữ snapshot/tiền decimal/bất biến; không mở lịch sử public hoặc dùng receipt checkout để bypass quyền.
4. Dashboard, ma trận quyền Staff/Manager và audit viewer, các báo cáo/Excel, lịch sử Customer 8f.1–8f.2; account service 02b không có nghĩa toàn bộ quản trị mới đã port.
5. Worker/provider wiring, kiểm tra UI trên MySQL, import/đối chiếu/ảnh, cutover và phục hồi. Giữ ERD tổng thể/các nhóm còn lại, từ điển dữ liệu/bản xuất/demo.

## Nhật ký nhập dữ liệu thật — hiện chưa có lượt nhập

**Không có run import đã thực hiện trong repo này.** Không điền “đã chuyển dữ liệu” từ việc seed schema hoặc test GUID.

| Nhóm bảng nguồn | Đã nhập/đối chiếu dữ liệu thật? |
|---|---|
| AspNetUsers/AspNetRoles/AspNetUserRoles, Permission/RolePermission | Chưa; seed role/quyền không thay tài khoản/quyền đang dùng |
| RoomTypes/Rooms + Content | Chưa; phải giữ ID/giá sửa/snapshot/đường dẫn ảnh |
| Customers/Reservations/RoomSessions | Chưa; giữ ID/FK/SĐT/UTC ticks/trạng thái/snapshot |
| Services/Orders/OrderItems/Invoices | Chưa; giữ snapshot/tiền decimal/trạng thái/immutability |
| AuditLog | Chưa; giữ actor/ID/thời điểm và liên kết người dùng |

Trước import: tạo snapshot sao lưu SQLite + Content, ghi mã lượt/phiên bản source/commit, destination, kiểm tra ràng buộc và chế độ dừng ghi. Mỗi lượt import sau này phải ghi vào bảng dưới: nhóm bảng, số dòng nguồn/đích, đối chiếu ID/FK/giá/thời gian/snapshot/ảnh và kết quả/điểm tiếp tục. Không dùng bản sao bị ghi tiếp trong lúc đếm. File backup/dump chứa dữ liệu thật phải lưu ngoài Git.

| Ngày/mã lượt | Source snapshot & commit | Đích | Nhóm bảng | Đối chiếu và kết quả | Điểm tiếp tục |
|---|---|---|---|---|---|
| Chưa có | — | — | — | Chưa thực hiện import | Hoàn tất service và công cụ sao lưu/import/đối chiếu trước cutover |

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
