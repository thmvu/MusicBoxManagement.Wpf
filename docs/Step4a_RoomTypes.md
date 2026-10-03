# Bước 4a — sửa hai loại phòng

Ngày: 03/10/2026. Đối chiếu plan WPF bước 4 và plan web v1.3 mục 5, 18, 32, 47, 55. Đây là phần đầu của danh mục; bước 4 chưa hoàn tất.

## Đã sửa

- Nhân viên có quyền RoomType.Edit được mở danh mục và form sửa Standard/VIP. Guest tiếp tục xem danh mục, không thấy nút sửa.
- Chỉ cho sửa Name, Capacity, PricePerHour, Amenities, Description. Code hiển thị bằng nhãn; DTO cập nhật không chứa Code/ID. Service không có phương thức thêm/xóa loại phòng.
- Name bắt buộc, tối đa 100 ký tự; Capacity nguyên >0; PricePerHour nguyên đồng >0; Amenities bắt buộc, tối đa 1000 ký tự; Description tùy chọn, tối đa 2000 ký tự. Trim chuỗi; mô tả trắng lưu NULL. Giữ giới hạn CHECK của bảng đã có.
- Form nhận giá dạng số nguyên như 145000, báo lỗi với phần lẻ/dấu phân cách/giá âm/0/overflow; không làm tròn dữ liệu nhập. ViewModel xử lý nhập số, service vẫn kiểm tra khi bị gọi trực tiếp.
- Service GetForEdit kiểm tra quyền hiện hành trước khi trả dữ liệu sửa. Update mở transaction ghi, kiểm tra lại quyền/stamp/active bằng cùng kết nối, đọc lại loại phòng rồi validation/cập nhật/nhật ký/Commit. Không giữ transaction lúc người dùng nhập form.
- So sánh dữ liệu hiện hành với bản lúc mở form; nếu có người khác sửa thì từ chối lưu và yêu cầu làm mới. Không ghi đè âm thầm từ form cũ. Lưu khi không có thay đổi không ghi nhật ký thừa.
- Cập nhật xong tải lại bảng. Hủy form không lưu; đang lưu thì khóa nút và chặn đóng cửa sổ. SQL chỉ ở service/data, không ở code cửa sổ.

## Nhật ký và schema v3

Đối chiếu mục 32 phát hiện schema v2 dùng ActorType User, trong khi plan quy định Staff cho mọi tài khoản nội bộ. Migration v3 dựng lại AuditLog với Staff/Guest/System và chuyển User → Staff. Giữ AuditLogId, UserId, Action, EntityName/Id, Description và CreatedAt. Migration chạy trong transaction cùng quản lý version; lỗi rollback schema/dữ liệu.

AuditService dùng chung cho bootstrap, đăng nhập và cập nhật loại phòng. RoomType.Update có UserId, RoomTypeId, mã loại phòng trong mô tả và thời điểm UTC. Ghi log cùng transaction với UPDATE; nếu ghi log lỗi, giá/tên đã UPDATE cũng rollback.

RoomTypes không thay cấu trúc hay seed lại; tài khoản và RolePermission cũ được giữ. Database mới hoặc v1 cũng lần lượt nâng đến v3. Database >v3 bị từ chối.

## Cách dùng

Đăng nhập → Loại phòng → chọn dòng → Sửa loại phòng → sửa thông tin → Lưu thay đổi. Mã STANDARD/VIP giữ nguyên. Bấm Nhân viên để về màn hình quyền, hoặc Đăng xuất về Khách để chỉ xem danh mục.

Manager mặc định có quyền sửa; Staff mặc định không có. Nếu Admin cấp RoomType.Edit cho Staff ở bước ma trận quyền sau này, Staff được sửa theo quyền hiện hành như mục 5. Không chặn cứng chỉ dựa trên tên role. Nếu quyền bị thu hồi khi form đang mở, lần lưu tiếp theo bị service từ chối.

## Kiểm tra

- Build Debug/Release bằng MSBuild Visual Studio: đạt, không lỗi/cảnh báo.
- Verify-Foundation.ps1: seed/tiếng Việt/persistence/constraints/foreign keys/guard schema mới hơn/commit/rollback/competing writers đạt.
- Verify-Authentication.ps1: xác thực, bootstrap, quyền hiện hành, vô hiệu hóa phiên, rollback/bootstrap đồng thời đạt sau thay đổi audit.
- Verify-RoomTypes.ps1: nâng audit v2→v3 giữ ID/thời gian/liên kết UserId và Guest/System; inject lỗi migration xác nhận giữ schema v2 rồi phục hồi; Guest/Staff mặc định không sửa; Manager/Admin sửa được; Staff được cấp quyền cũng sửa được; Code/count giữ nguyên; validation service và form; lưu rồi mở lại giữ dữ liệu.
- Kiểm tra form đang mở bị thu hồi quyền/đăng xuất không lưu được; dữ liệu cũ bị chặn; hai task sửa từ cùng dữ liệu cũ chỉ một thành công và một nhật ký; inject lỗi AuditLog rollback toàn bộ UPDATE.
- Verify-AuthenticationUi.ps1: kiểm tra component WPF STA, ngoài đăng nhập còn kiểm tra Guest ẩn nút sửa, Admin mở form, báo lỗi giá lẻ, lưu cập nhật bảng, Hủy không lưu và logout ẩn nút. Render bảy ảnh; đã xem ảnh form sửa và bảng sau lưu.

Tất cả bài kiểm tra dùng file SQLite ngẫu nhiên trong thư mục tạm; không mở/xóa/nâng cấp database đang dùng. Người dùng chạy ứng dụng sẽ tự nâng database của mình lên v3.

## Phần tiếp theo

Bước 4b: danh mục phòng với mã/loại/ảnh, khóa/mở theo plan; sau đó dịch vụ và khách hàng. UI user/ma trận quyền/audit đầy đủ vẫn ở bước 8.

Chưa có RoomSession/Invoice để kiểm tra snapshot giá. Khi làm nhận phòng/checkout phải lưu snapshot theo plan; không dùng giá danh mục đã đổi để tính lại phiên hoặc hóa đơn cũ. Chưa có các luồng đặt phòng, sử dụng, món hoặc báo cáo/Excel; phạm vi giữ nguyên.
