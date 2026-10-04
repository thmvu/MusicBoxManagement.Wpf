# Bước 4b.2b — đổi loại, khóa và mở phòng

Ngày: 04/10/2026. Đối chiếu plan WPF bước 4 và plan web v1.3 mục 5, 8–9, 13, 19–21, 32, 35, 47, 55.1/55.4. Hoàn thành phần thao tác danh mục phòng; chưa hoàn thành toàn bộ bước 4 và chưa có luồng booking/session.

## Cách dùng và quy tắc

Đăng nhập có Room.Manage → Phòng → chọn dòng → Sửa phòng. Chọn Standard/VIP, bật **Mở phòng để nhận khách** hoặc bỏ chọn để khóa. Khóa cần lý do không trắng, tối đa 1000 ký tự; trim trước lưu. Mở lại đặt InactiveReason=NULL. Có thể sửa tên/mô tả/ảnh trong cùng lần lưu; mã phòng vẫn cố định. Lưu tải lại dòng/ảnh/trạng thái; chọn phòng đã khóa thấy lý do ở cạnh danh sách. Hủy không lưu.

- Đổi RoomType bị chặn khi có RoomSession Active; không chặn chỉ vì có booking tương lai hoặc session Completed. Không thay snapshot của phiên cũ.
- Chuyển mở→khóa bị chặn khi có bất kỳ Active Session hoặc Reservation Confirmed còn hiệu lực, gồm booking ngày mai và booking đang trong grace period.
- Grace period 15 phút: còn hiệu lực khi now < StartTime+15 phút. Đúng mốc thì hết hiệu lực dù worker chưa ghi NoShow. Truy vấn lọc RoomId/Confirmed và StartTime > now−15 phút, không dựa riêng vào trạng thái stored để coi mọi Confirmed là còn hạn.
- Mở lại không yêu cầu hủy lịch sử; Cancelled/NoShow/Completed/CheckedIn không được tính là Confirmed. Session Active vẫn được kiểm tra độc lập.
- Sửa chữ/ảnh không tự khóa/mở hoặc đổi loại khi service caller không truyền các trường mới. UI truyền toàn bộ trạng thái đang chọn; dữ liệu phòng cũ vẫn được so sánh trước khi cập nhật để chặn form cũ.

Quyền hiện hành, validation, dữ liệu sử dụng, cập nhật phòng và AuditLog cùng một BeginWriteTransaction. IClock được truyền vào RoomService (mặc định UTC hệ thống); lấy now trong transaction sau khi lấy writer, để kiểm thử mốc thời gian không cần chờ 15 phút. Các writer booking/check-in về sau phải kiểm tra lại Room.IsActive trong cùng transaction ghi trước khi tạo; không kiểm tra ngoài transaction rồi ghi.

Room.Lock/Room.Unlock ghi khi trạng thái thay đổi, Room.Update cho cập nhật khác. Mọi log có ActorType Staff/UserId/RoomId và lý do khóa nếu có. Cùng một lần lưu nhiều trường chỉ một nhật ký; không thay đổi thì không audit. Các cơ chế ảnh/rollback và ảnh cũ giữ trên đĩa của 4b.2a vẫn áp dụng.

## Schema v5 — chỉ nền tảng dữ liệu sử dụng

Migration thêm Customers, Reservations và RoomSessions thật, không bảng trạng thái giả hoặc dữ liệu demo. Customers có tên/SĐT, SĐT canonical 10 chữ số đầu 0, UNIQUE. Chưa có UI/service chuẩn hóa/tạo/sửa khách hàng; sẽ làm ở phần danh mục khách hàng.

Reservations đủ các trường plan: FK Customer/Room/User, StartTime/EndTime UTC dạng O cố định (7 chữ số phần lẻ, +00:00), CHECK bắt đầu<kết thúc, các trạng thái Confirmed/CheckedIn/Completed/Cancelled/NoShow; index Room/Customer/Status/StartTime. RoomSessions có nguồn booking tùy chọn, thời gian dự kiến/thực tế, snapshot phòng/loại/giá, CHECK giá nguyên dương và mốc thời gian/trạng thái; partial UNIQUE cho một Active mỗi Room/Customer và một session mỗi Reservation.

Mới có schema để service phòng đọc điều kiện sử dụng. Chưa có service đặt/NoShow/check-in/walk-in, chưa kiểm tra giờ mở cửa/overlap/quan hệ session với booking tại service; chưa có Invoice để ràng buộc Completed có hóa đơn. Những kiểm tra đó phải được triển khai tại milestone tương ứng, không coi schema là hoàn thành nghiệp vụ. Không cho người dùng tạo booking/session qua UI hiện tại; fixture trong test chèn dữ liệu riêng để kiểm tra guard.

Migration v4→v5 cùng transaction, giữ Rooms/ảnh, RoomTypes/tài khoản/quyền/nhật ký. Database mới/v1/v2/v3 lần lượt nâng đến v5; từ chối version >5. Không ghi đè dữ liệu seed hoặc tự thêm khách/booking/session trong dữ liệu thật.

## Kiểm tra

- Debug/Release đạt, không lỗi/cảnh báo. Verify-Foundation, Verify-Authentication, Verify-RoomTypes, Verify-Rooms và Verify-AuthenticationUi đạt trên SQLite/thư mục ảnh tạm riêng.
- Fixture v4 với phòng/ảnh/nhật ký rồi inject lỗi sau khi migration tạo Customers: rollback schema/version; chạy lại giữ dữ liệu cũ. Fixture audit v2/v3 được bỏ đúng bảng v5 trước khi hạ version, tránh dựng schema lịch sử sai.
- Active chặn đổi loại/khóa, Completed cho đổi loại, đổi tên không sửa snapshot. Unique SĐT và Active Room/Customer được kiểm tra trên SQLite thật; giữ các bài RBAC/form cũ/ảnh/rollback/concurrency của 4b.1/4b.2a.
- Booking tương lai chặn khóa; trước mốc hết grace một tick vẫn chặn, đúng mốc cho khóa dù trạng thái vẫn Confirmed. Các trạng thái lịch sử không chặn; khóa thiếu/quá dài lý do và loại không tồn tại bị từ chối; mở lại xóa lý do. Inject lỗi Room.Lock audit rollback cả trạng thái và ảnh mới.
- Hai writer cạnh tranh giữa khóa thật và fixture booking (fixture kiểm tra IsActive trong cùng transaction): chỉ một thành công. Đây kiểm tra hợp đồng transaction cần dùng khi viết BookingService, chưa phải kiểm thử luồng đặt phòng hoàn chỉnh.
- UI WPF STA: chọn VIP, khóa thiếu lý do báo lỗi, khóa thành công/refresh trạng thái, mở lại giữ loại và xóa lý do; vẫn thử Guest/auth, RoomType, tạo/sửa/ảnh/Hủy. Render 13 ảnh, đã xem form lỗi khóa và danh sách khóa. Không điều khiển hộp thoại file Windows tự động.

Không mở/xóa/nâng cấp database đang dùng khi kiểm tra. Database thật tự nâng v5 khi người dùng chạy ứng dụng.

## Làm tiếp

Danh mục dịch vụ (4c), rồi UI/service khách hàng (4d) dùng bảng Customers đã có và chuẩn hóa SĐT chung theo mục 21. Sau đó booking/calendar/NoShow và session theo plan, tái sử dụng transaction và quy tắc thời gian/room active; Guest danh sách phòng và toàn bộ luồng nghiệp vụ vẫn giữ. Không coi đồ án hoặc bước 4 đã hoàn tất.
