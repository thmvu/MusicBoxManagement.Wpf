# Bước 4c — danh mục dịch vụ

Hoàn thành ngày 04/10/2026 trên WPF .NET Framework 4.7.2 + SQLite, sau khi đối chiếu WPF plan và mục 23 của `Reference_Web_ProjectPlan_v1.3.md`.

## Đã làm và cách dùng

- Khu vực nhân viên có nút **Dịch vụ**, chỉ hiện khi có quyền `Service.Manage`. Admin/Manager mặc định có; Staff mặc định không có. Guest không mở danh mục quản trị.
- Danh sách và form cùng cửa sổ. **Thêm dịch vụ** mở form trống; chọn dòng để sửa. **Lưu** tạo/cập nhật; **Bỏ thay đổi** khôi phục dữ liệu đã chọn hoặc xóa form thêm chưa lưu. **Làm mới** đọc lại danh sách và mở form thêm mới.
- Các trường: tên bắt buộc 1–100 ký tự sau trim, nhóm cố định **Đồ uống / Đồ ăn / Khác**, giá số nguyên đồng dương, mô tả tùy chọn tối đa 2000 ký tự, trạng thái **Đang bán**.
- Bỏ chọn **Đang bán** rồi lưu để ngừng bán; chọn lại để bán tiếp. Không xóa vật lý, không quản lý tồn kho. Danh sách nội bộ hiển thị cả dịch vụ ngừng bán.
- Không seed món mẫu hoặc tài khoản thử vào database sử dụng.

## Dữ liệu và transaction

Schema **v6** thêm bảng Services gồm ServiceId/Name/Category/Price/Description/IsActive. SQLite CHECK bảo vệ nhóm, giá nguyên dương, độ dài và trạng thái. Migration từ v5 nằm trong transaction khởi tạo chung, giữ các bảng dữ liệu hiện có; lần khởi tạo lại không ghi đè dịch vụ.

`ServiceCatalogService` đọc danh sách có kiểm tra quyền cùng transaction đọc. Khi lưu, transaction ghi IMMEDIATE lấy quyền ghi trước, rồi kiểm tra phiên/quyền hiện hành, validation và snapshot form cũ; sau đó mới cập nhật và ghi `Service.Create`/`Service.Update` với ActorType Staff. Lỗi ghi nhật ký rollback cả dữ liệu. Hai form cùng sửa, chỉ form đầu thành công; form sau cần làm mới. Lưu không đổi dữ liệu không ghi thêm nhật ký.

UI tải/lưu ở luồng nền, khóa thao tác và chặn đóng cửa sổ khi đang xử lý. Giá thập phân, dấu phân cách, số âm/0 và tràn kiểu long bị từ chối. Mất quyền khi lưu khiến form bị khóa/xóa dữ liệu đã tải. Nếu commit đã thành công nhưng tải lại thất bại, báo rõ đã lưu và giữ ID để tránh tạo trùng khi thử lại.

Chưa có Orders/OrderItems hoặc UI gọi món. Quy tắc mục 23 vẫn giữ cho bước gọi món: chỉ nhận dịch vụ đang bán cho món mới; món cũ giữ snapshot tên/giá và không thay đổi khi danh mục sửa/ngừng bán. Bước này chưa thể kiểm tra snapshot OrderItem vì luồng đó chưa triển khai.

## Kiểm tra

- Build Debug và Release bằng MSBuild Visual Studio: đạt, không warning/error.
- `Verify-Foundation.ps1`, `Verify-Authentication.ps1`, `Verify-RoomTypes.ps1`, `Verify-Rooms.ps1`: đạt sau cập nhật mốc schema/migration fixture lên v6.
- `Verify-Services.ps1`: đạt trên SQLite tạm. Kiểm tra v5→v6 giữ giá loại phòng/tài khoản/nhật ký, migration thất bại do bảng xung đột giữ version/data; thêm/sửa/bán tiếp/ngừng bán, ba nhóm, validation service/VM/CHECK, persistence, Guest/Staff bị chặn, cấp/thu hồi quyền, logout, form cũ và hai cập nhật đồng thời, rollback khi audit lỗi, lưu không đổi không tăng log.
- `Verify-AuthenticationUi.ps1` ở STA: đạt, render 15 ảnh. Phần mới kiểm tra Guest ẩn nút, mở danh mục, giá `15000.5` bị chặn, thêm và sửa tên/giá/nhóm, ngừng bán và bỏ form thêm chưa lưu. Đã xem ảnh danh mục để kiểm tra bố cục/chữ.

Tất cả dùng file/thư mục tạm riêng, không mở hoặc xóa database đang dùng ở `%LOCALAPPDATA%\MusicBoxManagement.Wpf`. Tiếp theo **4d — khách hàng**, chuẩn hóa SĐT dùng chung đúng mục 21. Booking/session/gọi món/checkout/báo cáo và các luồng Guest còn trong phạm vi đồ án.
