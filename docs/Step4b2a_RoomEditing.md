# Bước 4b.2a — sửa tên, mô tả và thay ảnh phòng

Ngày: 04/10/2026. Đối chiếu plan WPF bước 4 và plan web v1.3 mục 5, 19–20, 32, 47. Đây là một phần nhỏ của 4b.2. Nền tảng giữ WPF .NET Framework 4.7.2 + SQLite; schema vẫn v4.

## Đã làm và cách dùng

Đăng nhập tài khoản có Room.Manage → Phòng → chọn dòng → **Sửa phòng**. Sửa tên (bắt buộc, trim, 1–100 ký tự), mô tả (tùy chọn, tối đa 2000; trắng lưu NULL). Chọn ảnh mới nếu muốn thay; không chọn thì giữ ImageUrl hiện tại. **Giữ ảnh hiện tại** bỏ lựa chọn ảnh mới. **Lưu thay đổi** tải lại danh sách và chọn lại phòng để hiển thị ảnh/mô tả mới; **Hủy** không lưu.

Mã và loại phòng hiển thị bằng nhãn. DTO RoomEdit chỉ có Name, Description và ReplacementImageFilePath; không có Code, RoomTypeId hoặc IsActive. Thao tác này không thay trạng thái, lý do khóa hoặc CreatedAt, kể cả sửa tên một phòng inactive. Không có phương thức xóa phòng.

## Service, quyền và form cũ

GetForEdit kiểm tra Room.Manage hiện hành trong snapshot đọc trước khi trả dữ liệu form. Update kiểm tra quyền trước khi giải mã ảnh và kiểm tra lại cùng kết nối/BeginWriteTransaction với thay đổi nghiệp vụ. Guest/Staff mặc định bị chặn; Staff được cấp Room.Manage có thể sửa, không kiểm tra cứng chỉ theo tên role. Thu hồi quyền, khóa tài khoản, đổi stamp hoặc logout có hiệu lực ở service theo PermissionService đã có.

Service đọc lại phòng trong transaction và so sánh RoomCode/RoomTypeId/Name/Description/ImageUrl/IsActive/InactiveReason/CreatedAt với bản lúc mở form. Nếu đã thay đổi thì báo đóng form, làm mới và mở lại, không ghi đè âm thầm. Hai form cùng bản cũ chỉ một cập nhật thành công. Tên hiển thị loại phòng không thuộc so sánh vì sửa danh mục RoomType là thao tác khác; RoomTypeId vẫn được giữ. Lưu không thay đổi và không chọn ảnh mới không tạo nhật ký thừa.

UPDATE Name/Description/ImageUrl và Room.Update (ActorType Staff, UserId, EntityId, thời gian UTC) được commit trong cùng transaction. Không giữ transaction khi nhập form hoặc giải mã ảnh. Form khóa lưu/chọn ảnh/hủy và chặn đóng lúc đang lưu. Nút sửa chỉ bật khi có quyền đã kiểm tra, có dòng được chọn và không đang mở form/thao tác dữ liệu.

## Ảnh và rollback

Tái sử dụng kiểm tra nội dung JPEG/PNG/WebP, tối đa 5 MiB, giải mã đầy đủ rồi chuẩn hóa PNG của 4b.1. Ảnh mới có tên GUID, không ghi đè file cũ. Mỗi phòng chỉ có một ImageUrl hiện hành.

Lỗi ghi ảnh/UPDATE/nhật ký làm rollback dữ liệu và dọn file mới thuộc thao tác, giữ nguyên ảnh trước đó. Ảnh cũ sau thay thành công được giữ trên đĩa để không làm hỏng người đọc/sao lưu đang dùng; nó không còn hiển thị qua ImageUrl hiện hành. Chưa có bộ dọn ảnh cũ/mồ côi, nên thay ảnh nhiều lần làm thư mục ảnh tăng dung lượng. Khi sao lưu vẫn giữ database cùng thư mục Content.

Giới hạn filesystem như 4b.1: mất điện/kết thúc process đột ngột giữa ghi file và commit có thể để lại file không được tham chiếu. Lỗi dọn file được Trace và giữ lỗi gốc; không tuyên bố SQLite có transaction chung với filesystem.

## Kiểm tra

- Build Debug/Release đạt, không lỗi/cảnh báo; Verify-Foundation đạt, schema/seed cũ được giữ.
- Verify-Rooms: mở/sửa cần quyền, phòng không tồn tại bị chặn, trim/NULL/độ dài, giữ mã/loại/trạng thái/CreatedAt, giữ ảnh khi sửa chữ, thay lần lượt PNG/JPEG/WebP, từ chối ảnh giả/quá dung lượng, mở lại giữ dữ liệu. Tái sử dụng bộ kiểm tra ảnh hỏng/cắt cụt của 4b.1.
- Kiểm tra form cũ, thay trạng thái sau khi mở form, quyền Manager bị thu hồi, Staff được cấp quyền sửa, logout, no-op không audit; inject lỗi Room.Update audit xác nhận rollback dữ liệu/ảnh mới; hai task sửa và thay ảnh đồng thời chỉ một thành công, thêm đúng một ảnh/audit.
- Verify-AuthenticationUi: component WPF STA với SQLite/ảnh tạm riêng; Admin chọn dòng/mở form, tên trắng báo lỗi, lưu tên/mô tả/thay ảnh và refresh dòng/ảnh, nút giữ ảnh bỏ lựa chọn, Hủy không ghi. Vẫn kiểm tra đăng nhập/Guest, sửa RoomType và thêm phòng. Render 11 ảnh, đã xem form sửa và danh sách sau lưu. Không tự động điều khiển hộp thoại chọn file của Windows.

Tất cả kiểm tra dùng dữ liệu tạm, không mở/xóa/nâng cấp database đang dùng.

## Còn lại trong 4b.2

Đổi RoomType và khóa/mở chưa triển khai. Trước khi mở các thao tác đó cần dữ liệu/kiểm tra Active RoomSession và Reservation Confirmed còn hiệu lực, gồm grace period, trong cùng transaction với đặt phòng. Không thay kiểm tra đó bằng giả định “không có khách/booking”. Không tự cắt chức năng, không coi 4b hoặc toàn bộ bước 4 đã hoàn tất. Sau danh mục phòng vẫn còn dịch vụ/khách hàng và các bước nghiệp vụ theo plan.
