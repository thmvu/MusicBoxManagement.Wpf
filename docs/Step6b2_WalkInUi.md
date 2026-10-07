# Bước 6b.2 — giao diện nhận khách trực tiếp

Ngày 07/10/2026. WPF .NET Framework 4.7.2 + SQLite, schema giữ v6. Đối chiếu mục 12/38/42/55.4 của bản tham chiếu web.

## Đã sửa và cách dùng

Menu trái có **Khách trực tiếp** theo quyền Session.WalkIn. Đăng nhập → Khách trực tiếp → chọn phòng đang mở → nhập tên/SĐT → Nhận khách → đọc giá tham khảo/giờ cần trả → Xác nhận nhận ngay. Chỉnh lại quay về form mà không ghi dữ liệu. Không chọn ngày, giờ hay thời lượng và không tạo booking giả.

RoomSessionService.ListWalkInRooms và PreviewWalkIn kiểm tra Session.WalkIn trong read transaction. Danh sách chỉ phòng mở, không đòi Room.Manage/Customer CRUD/Reservation.Create/View/Session.View. Preview chuẩn hóa SĐT, kiểm tra cả phòng và khách qua AvailabilityService, trả lý do/giá/mốc trả trong cùng snapshot/một IClock; không tìm-tạo khách hoặc ghi NoShow/audit. Confirmed đã hết grace không chặn preview dù worker chậm. Quyền WalkIn bị thu hồi không chuyển sang đường Guest.

Xác nhận gọi CreateWalkIn sẵn có để kiểm tra lại hiện trạng/quyền/giờ/NoShow trong transaction ghi. Giá tham khảo trước đó có thể đổi: kết quả luôn hiển thị snapshot đã chốt, giờ nhận thực tế giữ giây/ticks, mã phiên/phòng và trạng thái Active. Không tự cắt thời lượng hay di chuyển booking khác.

Form khóa input trong lúc xác nhận hoặc gửi; snapshot yêu cầu tách khỏi các trường nhập. Sau thành công không gửi lại cùng form; Nhận lượt mới xóa tên/SĐT/kết quả và tải lại phòng. Nếu tải danh mục lỗi sau commit, vẫn báo đã nhận, không báo thất bại hoặc cho nhận lần nữa. Nếu mất quyền, xóa dữ liệu form; Đóng vẫn hoạt động sau khi thao tác nền kết thúc.

## Giờ cần trả động

Kết quả có nút **Cập nhật** để tính lại giờ cần trả theo cuối ca/booking kế tiếp của phòng và khách qua ScheduleRules, cùng quy tắc Calendar. Hiển thị thời điểm kiểm tra và cảnh báo quá giờ; không ghi mốc này vào ExpectedEndTime, không tự checkout hoặc giữ lịch tương lai.

ReadWalkInReceipt là đường đọc kết quả của chính nhân viên đã tạo phiên: yêu cầu Session.WalkIn hiện hành và đối chiếu AuditLog Session.WalkIn/UserId/RoomSession. Người chỉ có WalkIn không được đọc phiên của nhân viên khác; đường đọc phiên nội bộ chung ReadWalkIn vẫn yêu cầu Session.View. Đây là cập nhật thủ công của kết quả vừa nhận, chưa phải dashboard hoặc danh sách phiên đầy đủ.

## Kiểm tra

- Debug/Release build tại bin/VerifyDebug và bin/VerifyRelease, không đóng bản người dùng đang chạy.
- 11 bộ kiểm tra liên quan: WalkIn, CheckIn, Availability, Calendar, Reservations, Rooms, Customers, Authentication, Foundation, GuestCalendar và AuthenticationUi trên database tạm.
- WalkIn bổ sung danh sách/preview theo quyền độc lập, preview không tạo khách/audit, phòng Active bị chặn, receipt đúng người tạo và không đòi Session.View; giữ các kiểm tra cạnh tranh/rollback/ca/grace/snapshot ở 6b.1.
- UI: menu mở đúng route; phòng khóa không hiện; danh sách rỗng không nhận; SĐT sai; xem trước/xác nhận/chỉnh lại; phòng khóa sau preview rollback cả khách/session; giá đổi chốt giá mới; bấm xác nhận lặp chỉ một phiên/audit; receipt cập nhật theo booking mới; nhận lượt mới xóa dữ liệu; phòng bận bị chặn; mất quyền khi đọc/khi xác nhận và sau commit. Render 63 ảnh và xem trực tiếp form/xác nhận/kết quả.
- Không sửa database thật, ảnh phòng hay schema.

## Tiếp theo

Service gia hạn phiên, rồi UI gia hạn, gọi món, Guest Lookup Active/tiền tạm tính, trả phòng/thanh toán và báo cáo. Dashboard và quản trị nhân viên/RBAC theo role vẫn thuộc bước 8. Chưa coi vận hành hoặc đồ án đã hoàn tất.
