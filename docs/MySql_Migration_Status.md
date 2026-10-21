# Bàn giao migration SQLite → MySQL Aiven

Cập nhật 21/10/2026. **Đọc file này trước khi nhận việc migration.** Đây là bảng hiện trạng để người pull repo không làm lại phần đã chuyển; các báo cáo bước cũ là bằng chứng theo thời điểm, không phải danh sách việc còn thiếu mới nhất.

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

Các hàng đã nghiệm thu không cần viết lại service. Khi phần mới đụng luồng đó, chạy hồi quy theo báo cáo và bổ sung test cạnh tranh bằng service thật; fixture không phải nghiệm thu các service còn thiếu.

## Chưa chuyển — danh sách tiếp tục

1. **04e.2 Walk-in** qua MySqlRoomSessionService: giữ số/khách, actual/snapshot, null Reservation/ExpectedEnd và ReturnBy động; quyền riêng, transaction/rollback/concurrency.
2. Đọc phiên nội bộ, gia hạn Staff/Guest; Guest lookup/hủy booking đầy đủ, không giả ActiveSession=null khi chưa port phiên. Giữ cả membership theo SĐT hiện hành và audit Guest.
3. Order/menu/cart/preview/tạo/xác nhận/hủy, billing/tạm tính, checkout/Invoice snapshot/idempotence/in/đọc lịch sử; bổ sung cạnh tranh checkout–gia hạn–order bằng service thật.
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
