# Bước 4b.1 — thêm phòng và xem danh sách

Ngày: 04/10/2026. Đối chiếu plan WPF bước 4 và plan web v1.3 mục 5, 19–20, 32, 35, 47, 55. Đây là phần đầu của 4b, chưa hoàn thành sửa phòng/thay ảnh/khóa mở. Giữ WPF .NET Framework 4.7.2 + SQLite; database online sẽ được xem xét riêng khi người dùng chốt.

Cập nhật cùng ngày: 4b.2a đã có sửa tên/mô tả/thay ảnh, schema giữ v4; bài kiểm tra UI hiện render 11 ảnh. Xem `Step4b2a_RoomEditing.md`; nội dung bên dưới ghi lại thời điểm 4b.1.

## Đã làm và cách dùng

Đăng nhập → **Phòng** → **Thêm phòng** → nhập mã/tên, chọn loại Standard/VIP, chọn một ảnh, nhập mô tả tùy chọn → **Thêm phòng**. Sau khi lưu, danh sách được tải lại; chọn dòng để xem ảnh/mô tả. Hủy không lưu. Database mới không tự thêm phòng demo.

- Mã phòng bắt buộc, trim, 1–50 ký tự, unique theo SQLite NOCASE (không phân biệt hoa/thường ASCII). Trigger chặn sửa mã sau khi tạo. Tên bắt buộc 1–100 ký tự; mô tả tùy chọn tối đa 2000, trắng lưu NULL. Loại phòng phải tồn tại, có foreign key.
- Một ảnh bắt buộc, nhận nội dung JPEG/PNG/WebP hợp lệ, file nguồn >0 và không quá 5 MiB (5 × 1024 × 1024 byte). Kiểm tra bằng giải mã đầy đủ, không tin phần mở rộng; từ chối ảnh giả/hỏng/cắt cụt hoặc định dạng khác. File nguồn được đọc một lần, không bị ghi đè.
- Dùng [SkiaSharp 3.119.4](https://www.nuget.org/packages/SkiaSharp/3.119.4) tương thích .NET Framework để đọc cả WebP. [SKCodec](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skcodec) kiểm tra định dạng/kết quả giải mã. Chuẩn hóa ảnh đầu vào thành PNG để WPF hiển thị nhất quán, tên GUID do ứng dụng tạo. Mỗi phòng giữ một ImageUrl; không thêm album nhiều ảnh.
- ImageUrl là đường dẫn tương đối `Content/uploads/rooms/<GUID>.png`. Trên desktop, thư mục này nằm cạnh database, mặc định `%LOCALAPPDATA%\MusicBoxManagement.Wpf\Content\uploads\rooms`, để không phụ thuộc thư mục cài/chạy app. Sao lưu/chuyển máy cần giữ cả database và thư mục Content.
- Phòng mới IsActive=true, InactiveReason=NULL, CreatedAt UTC. Không lưu Room.Status hay tự gán Available/Occupied. Cột “Mở / khóa” chỉ phản ánh IsActive của danh mục; trạng thái vận hành/lịch sẽ tính theo mục 20/38/39 khi có booking/session.

## Quyền, transaction và ảnh

UI hiện nút Phòng theo PermissionService, Guest không thấy quản lý phòng. Service đọc danh sách và tạo phòng đều kiểm tra Room.Manage. Manager/Admin mặc định được dùng; Staff mặc định bị chặn nhưng được dùng nếu Admin cấp quyền này sau đó. Không kiểm tra cứng tên role thay cho permission.

Create kiểm tra quyền trước khi đọc ảnh; giải mã ngoài transaction ghi. Trong BeginWriteTransaction, kiểm tra lại phiên/quyền bằng cùng kết nối, loại phòng hiện hành và mã trùng, INSERT phòng, ghi file ảnh, ghi Room.Create bằng AuditService rồi Commit. Hai thao tác cùng mã chỉ một được lưu. Không giữ transaction khi người dùng chọn ảnh/nhập form.

Lỗi ghi ảnh/nhật ký/database làm rollback phòng và nhật ký. File vừa tạo được dọn khi rollback; không xóa ảnh đã commit. Nếu dọn file thất bại do quyền/filesystem, ghi Trace và giữ lỗi gốc. SQLite và filesystem không có transaction chung: mất điện/kết thúc process đột ngột giữa ghi file và Commit có thể để lại ảnh không được tham chiếu; chưa có bộ dọn ảnh mồ côi. Không tuyên bố rollback filesystem có tính nguyên tử qua sự cố process. File đã có trước thao tác không bị ghi đè/xóa.

Form khóa lưu/chọn ảnh/hủy và chặn đóng lúc đang lưu. Danh sách đọc lại quyền mỗi lần làm mới; khi bị từ chối thì xóa dữ liệu danh sách cũ và báo lỗi. Xem ảnh PNG bằng BitmapImage OnLoad, đóng file sau đọc; mô tả dài có cuộn.

## Schema v4 và kiểm tra

Migration v3→v4 thêm Rooms cùng CHECK/UNIQUE/FK/trigger mã cố định trong transaction. Giữ RoomTypes đã chỉnh, tài khoản, role/quyền và nhật ký. Database mới/v1/v2 nâng lần lượt tới v4; từ chối schema >4. Không tạo tài khoản/phòng dùng thử trong database sử dụng.

- Build MSBuild Debug/Release đạt, không lỗi/cảnh báo.
- Verify-Foundation.ps1, Verify-Authentication.ps1 và Verify-RoomTypes.ps1 đạt sau nâng schema. Fixture audit v2 được dựng lại đúng bằng cách bỏ bảng Rooms của v4 trước khi hạ version để kiểm tra migration.
- Verify-Rooms.ps1: database tạm thật, migration lỗi sau CREATE TABLE rollback cả schema/version, giữ dữ liệu cũ, danh sách rỗng, quyền Guest/Staff/Manager/Admin và quyền Staff được cấp, thu hồi quyền khi form đã mở, logout, seed không phục hồi quyền đã tắt.
- Kiểm tra tên/mã/loại/mô tả/ảnh bắt buộc; JPEG/PNG/WebP thật; ảnh giả/GIF/cắt cụt/quá 5 MiB; đường dẫn ảnh giới hạn thư mục; mã unique/cố định, FK và khóa cần lý do ở CHECK. Giữ dữ liệu sau mở lại, nhật ký Staff/UTC, rollback khi lỗi AuditLog hoặc filesystem, hai task tạo cùng mã chỉ một phòng/ảnh/audit.
- Verify-AuthenticationUi.ps1: component WPF STA với dữ liệu/ảnh giả lập riêng; Guest ẩn quản lý, Admin mở danh sách/form, thiếu ảnh báo lỗi, lưu có ảnh, khóa submit lúc bận, tải lại danh sách/ảnh và Hủy không lưu; đồng thời kiểm tra lại đăng nhập và sửa loại phòng. Render chín ảnh; xem form thêm và danh sách để kiểm tra bố cục/tiếng Việt. Đây là kiểm tra component, chưa tự động điều khiển hộp thoại chọn file của Windows.

Mọi database/ảnh thử đều nằm trong thư mục tạm riêng. Không mở/nâng cấp/xóa database đang dùng khi kiểm tra. Khi người dùng chạy ứng dụng, database của ứng dụng tự nâng lên v4.

## Phần kế tiếp vẫn giữ

4b.2: sửa thông tin phòng, thay một ảnh, khóa/mở có lý do; không xóa vật lý, không sửa mã. Trước khi có luồng vận hành phải kiểm tra đổi loại khi Active Session và khóa khi Active Session/Confirmed còn hiệu lực trong cùng transaction với booking. Chưa có các bảng đó nên bước này chưa cung cấp thao tác sửa/khóa để tránh coi kiểm tra nghiệp vụ là đã hoàn tất.

Sau đó dịch vụ/khách hàng theo bước 4. Danh sách phòng public, đặt/tra cứu/gia hạn/gọi món Guest, calendar ngày/tuần, vận hành, snapshot, checkout/invoice, quản trị user/ma trận quyền/audit UI và báo cáo/Excel vẫn giữ toàn bộ trong phạm vi đồ án.
