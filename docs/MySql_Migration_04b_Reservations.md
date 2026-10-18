# MySQL Aiven 4b — Reservation và NoShow

Ngày 18/10/2026. Bổ sung đường service MySQL độc lập, chưa nối App/worker/UI WPF sang MySQL. SQLite v8 và MySQL v1 giữ nguyên; không nhập hoặc sửa dữ liệu thật.

## Cách hoạt động

- `MySqlReservationService.CreateGuest/CreateStaff` giữ cùng hợp đồng với SQLite: chuẩn hóa SĐT chung, tìm hoặc tạo Customer và giữ tên khách cũ; Confirmed, UTC đủ ticks, người tạo Staff hoặc Guest không tài khoản.
- Staff cần `Reservation.Create` hiện hành, độc lập `Reservation.View` và Customer CRUD. `CancelStaff` cần `Reservation.Cancel`, lý do trim 1–500 ký tự, chỉ Confirmed trước đúng StartTime +15 phút và chưa có session nguồn. Không dùng giới hạn hủy trước 2 giờ của Guest cho Staff.
- Writer Serializable khóa Room trước rồi Customer theo SĐT hiện hành. SĐT unique chống hai booking tạo hai Customer. Không cập nhật tên khách hiện có từ tên form. Sau khi có khóa tài nguyên và các booking Confirmed phục vụ NoShow mới lấy một mốc IClock; thời gian/availability/NoShow/CreatedAt/audit cùng mốc.
- `MySqlNoShowService.LockCandidates` khóa các dòng Confirmed theo ID. Worker chờ writer cũ trước khi lấy clock. `ProcessExpiredAt` khóa/đọc trạng thái còn Confirmed, bỏ booking đã có session, đổi NoShow đúng +15 phút; chỉ ghi System audit khi UPDATE thực sự đổi một dòng. Gọi lặp không ghi log lặp.
- Create thực hiện NoShow trong cùng transaction với Customer/Reservation/audit. Từ chối hoặc lỗi audit rollback cả xử lý tồn đọng, không có Customer hoặc NoShow bị commit riêng.
- Cancel khám phá Room/Customer qua kết nối đọc ngắn ngoài transaction nghiệp vụ (IDs bất biến), sau đó khóa Room→Customer→Reservation và đọc lại trạng thái/source trong writer. Clock được đọc sau các khóa, không tin trạng thái từ lần khám phá.

## Đồng thời và giới hạn

Dùng khóa hàng/range InnoDB, không advisory lock nghiệp vụ/toàn ứng dụng và không tự retry vô hạn. NoShow maintenance trong Create cần khóa các Confirmed, do đó có thể chờ các phòng khác; giữ cách xử lý tồn đọng nguyên tử của SQLite cho đồ án nhỏ. Đây không phải tuyên bố hiệu năng quy mô lớn. Deadlock/timeout hoặc tranh unique phone trả lỗi tải lại/kiểm tra lại; transaction rollback đầy đủ.

Các writer session/check-in/walk-in/gia hạn/checkout MySQL tương lai phải dùng Room→Customer rồi Reservation nguồn trước khi thay trạng thái/tạo session. NoShow chỉ lấy Reservation rồi đọc source, không lấy Room/Customer phía sau để tránh đảo thứ tự tài nguyên. `ProcessExpiredAt` là internal, caller phải khóa candidate trước rồi lấy mốc giờ chung.

Chưa port public/staff booking DTO/menu/calendar/lookup/hủy Guest hoặc worker WPF. Chưa có check-in MySQL thật; session trong bài kiểm tra là fixture chỉ kiểm tra guard source. Chưa nghiệm thu cạnh tranh NoShow/check-in bằng hai service thật; phải bổ sung khi port service session. Không đổi provider App chỉ vì kiểm tra service cloud đạt.

## Kiểm tra

`Tests/Verify-MySqlReservations.ps1 -BuildDirectory <build>` tạo database `musicbox_test_<GUID>` riêng và chỉ xóa đúng tên GUID đó trong finally. Không ghi `musicbox/defaultdb` nghiệp vụ hoặc SQLite thật, không đọc/in mật khẩu.

Bài kiểm tra gồm Guest/Staff/quyền Create-only và Cancel-only/thu hồi/stamp, chuẩn hóa và unique SĐT/giữ tên/UTC, ca/slot/thời lượng/ngày/phòng khóa/Room-Customer overlap; Staff hủy/lý do/no replay/nguồn; NoShow ngay trước/đúng grace/System/idempotence; audit rollback Create/Cancel/NoShow và Create lỗi rollback maintenance; hai service booking thật tranh Room/Customer/SĐT mới, hai worker, worker/hủy và hai thứ tự tuần tự; chờ Room và khóa candidate NoShow trước common clock, late start rollback.

Đã chạy đạt online cả Debug và Release trên `bin/VerifyRoomUsageReservationsDebug` và `bin/VerifyRoomUsageReservationsRelease`: Verify-MySqlReservations; MySqlAvailability và MySqlFoundation Debug cũng đạt. Tất cả database GUID riêng đã dọn trong finally. Không sửa nguồn sau build đã kiểm tra. Root chốt hồi quy SQLite và build mặc định chung vì App vẫn SQLite.
