# MySQL 4c.1 — danh sách phòng, preview và lịch public

Ngày 18/10/2026. Tiếp nối 4a Availability và 4b Reservation/NoShow, bổ sung ba service `MySqlGuestBookingService`, `MySqlStaffBookingService`, `MySqlGuestCalendarService`. Hai đường Guest/Staff triển khai đầy đủ `IBookingService`: ListRooms, Preview, Create, ReadDay và GetImagePath. Chưa nối vào ứng dụng WPF; app và worker đang dùng SQLite v8, schema MySQL giữ v1. Không nhập hoặc sửa dữ liệu thật.

## Cách hoạt động

- Caller khởi tạo database trước; không chạy Initialize hoặc DDL khi đọc danh sách. Chỉ phòng mở, DTO PublicRoom với giá/loại/sức chứa/tiện ích/mô tả/ảnh; không dùng danh sách quản trị phòng.
- Staff cần đúng `Reservation.Create` hiện hành, độc lập Reservation.View/Customer CRUD/Room.Manage. ListRooms/Preview/ReadDay kiểm tra user, stamp, role và quyền trong cùng transaction RepeatableRead chỉ đọc. Preview kiểm tra quyền trước xử lý NoShow rồi kiểm tra lại sau đó; mất quyền không đi đường Guest. Create gọi đúng CreateStaff, Guest gọi CreateGuest; writer 4b vẫn kiểm tra lại toàn bộ điều kiện và chốt actor/audit.
- Preview chuẩn hóa SĐT chung, xử lý NoShow trước đọc, tra Customer theo số hiện hành, cùng một snapshot cho Customer/Room/lịch và một IClock của snapshot. Preview không tạo khách, giữ chỗ hoặc khóa giá; chỉ phần xử lý NoShow được phép ghi trạng thái hết hạn với audit System.
- Lịch một phòng/ngày có 28 ô 09:00–22:30, slot 30 phút, nghỉ 12–13, ngày Việt Nam hôm nay..+30, thời lượng 60/90/120/180. Cùng MySqlAvailabilityService và một clock cho mọi ô; phân biệt Available/Busy/Past/Break/Closed. Không trả khách/SĐT/ID booking/phiên, không ghi NoShow, không khóa slot. Đúng grace +15 phút không còn giữ lịch dù worker chậm. Walk-in chặn nhận ngay, không giữ mọi slot tương lai hoặc ngày mai.
- GetImagePath dùng helper của MySqlRoomService, bắt buộc storageRoot tuyệt đối rõ ràng; chỉ `Content/uploads/rooms/<GUID-N>.png`. Ảnh vẫn nằm local, không upload Aiven hoặc dùng thư mục database SQLite ngầm. Không tạo thư mục/ảnh khi đọc.

## Kiểm tra

`Tests/Verify-MySqlBookingReads.ps1 -BuildDirectory <build>` chỉ dùng database `musicbox_test_<GUID>`, dọn đúng database đó; không dùng database musicbox/defaultdb làm dữ liệu thử. Kiểm tra đầy đủ interface, catalog/DTO không PII, ảnh local và traversal, Staff Create-only/mất quyền/stamp/thu hồi giữa maintenance và protected read, snapshot quyền/phòng dù phòng bị khóa sau lần SELECT quyền, preview canonical/current phone/Room-Customer overlap, CreateGuest/CreateStaff actor, lịch đủ slot/thời lượng/ca/ngày/giây-ticks/grace/walk-in, read-only/no audit/no NoShow, và writer thật chặn slot đã stale.

Đã chạy online đạt với bản test cuối trên cả `bin/VerifyBookingReportsReadsDebug` và `bin/VerifyBookingReportsReadsRelease`. Hồi quy `Verify-MySqlReservations`, `Verify-MySqlAvailability`, `Verify-MySqlFoundation` online Debug đều đạt trên database GUID riêng. Không sửa database SQLite, database musicbox hoặc ảnh thật. Fixture phòng khóa có lý do đúng CHECK; không bỏ constraint để kiểm tra.

## Phần còn lại

Chưa port Guest lookup/hủy và danh sách/chi tiết Staff booking; không tạo API Lookup giả với ActiveSession rỗng. Guest Active/menu/order/billing cần các service phiên tương ứng, giữ nguyên scope. Tiếp tục calendar nội bộ, check-in/walk-in/gia hạn/order/checkout/invoice cùng các feature quản trị/báo cáo mới, công cụ sao lưu/nhập/đối chiếu dữ liệu và Content, rồi kiểm tra UI trước đổi provider. Lớp IBookingService hiện đã đầy đủ nhưng chưa đồng nghĩa app đã dùng MySQL.
