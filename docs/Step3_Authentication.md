# Bước 3 — đăng nhập và quyền

Ngày: 03/10/2026. Đối chiếu plan WPF bước 3, thiết kế bước 2 và plan web v1.3 mục 4–5, 33, 35, 53–55. Nền tảng vẫn WPF .NET Framework 4.7.2 + SQLite.

Cập nhật sau bước 4a: schema hiện v3, nhật ký dùng ActorType Staff cho tài khoản nội bộ, giữ nhật ký cũ. UI đã có sửa loại phòng; bài kiểm tra UI hiện render bảy ảnh. Xem `Step4a_RoomTypes.md`; các mô tả schema v2 bên dưới ghi lại bước 3.

## Đã sửa

- Nâng schema v1 lên v2 bằng transaction: thêm AspNetUsers, AspNetRoles, AspNetUserRoles, Permission, RolePermission và AuditLog. Không xóa hoặc seed lại RoomTypes cũ; lỗi migration rollback. Cả database mới và database v1 đều lên v2; từ chối schema >2.
- Seed ba IdentityRole Staff/Manager/Admin và 27 permission; Staff mặc định 19, Manager 24. Không seed tài khoản/mật khẩu và không ghi đè ma trận quyền đã chỉnh khi mở lại.
- ApplicationUser kế thừa IdentityUser; kho SQLite triển khai interface Identity. UserManager xử lý validation/mật khẩu và gán role. Kho lưu gom thay đổi trong bộ nhớ rồi SaveChanges trong transaction, tương tự store tắt tự lưu; băm mật khẩu nằm ngoài transaction ghi.
- UserName được trim và chuẩn hóa hoa invariant vào cột UNIQUE. PasswordHasher dùng PBKDF2-HMAC-SHA256, 600.000 vòng, salt ngẫu nhiên 16 byte và hash 32 byte, định dạng có phiên bản. Không log mật khẩu/hash.
- Thiết lập Admin đầu tiên kiểm tra lại chưa có tài khoản bên trong transaction ghi, lưu user + đúng một role + AuditLog hoặc rollback toàn bộ. Hai lần thiết lập cạnh tranh chỉ một lần thành công.
- Đăng nhập dùng UserManager.CheckPasswordAsync, đọc lại active/stamp/role sau khi kiểm tra mật khẩu rồi ghi nhật ký thành công. Phiên LoginSession chỉ có UserId, SecurityStamp và cờ đăng xuất trong bộ nhớ; không lưu quyền/cookie. Đăng xuất đánh dấu phiên hết hiệu lực và xóa nội dung nhân viên khỏi UI.
- PermissionService đọc user/role/quyền trên một snapshot mỗi thao tác. Admin toàn quyền cố định; Staff không có báo cáo dù bị gán nhầm; ba quyền quản trị chỉ Admin; Export/Print cần View. `Demand` chặn gọi service trực tiếp, có overload dùng cùng kết nối/transaction với nghiệp vụ ghi sau này.
- UI mặc định Khách, vẫn xem danh mục Standard/VIP. Có thiết lập Admin, đăng nhập, đăng xuất và khu vực nhân viên hiển thị quyền hiện hành. Form dùng PasswordBox, xóa mật khẩu sau mỗi lần submit; thao tác SQLite/băm chạy nền, khóa submit khi bận. Hủy form trở về Khách.

## Cách hoạt động để học

Khi ứng dụng mở, RoomTypeService khởi tạo/nâng cấp SQLite rồi đọc danh mục như trước. Nút ở góc trái dưới kiểm tra có tài khoản chưa: chưa có thì mở form thiết lập, đã có thì mở form đăng nhập.

Form không chứa SQL; chỉ gọi AuthenticationService. Identity tạo hash/stamp và kiểm tra mật khẩu qua kho SQLite. Sau khi tạo/đăng nhập, cửa sổ chính giữ LoginSession trong bộ nhớ và gọi PermissionService lấy họ tên, role và quyền hiện hành. Nút kiểm tra lại quyền gọi cùng service. Đăng xuất hủy phiên và hiện lại danh mục Khách.

Với service nghiệp vụ sau này, gọi PermissionService.Demand trước khi trả dữ liệu nội bộ hoặc thay đổi dữ liệu. Khi ghi, dùng overload nhận cùng SQLiteConnection/SQLiteTransaction để quyền và validation thuộc cùng thao tác nguyên tử. UI ẩn/tắt nút theo cùng nguồn quyền; kiểm tra ở UI không thay kiểm tra ở service.

## Kiểm tra đã chạy

- MSBuild Visual Studio: Debug và Release, không lỗi/cảnh báo.
- Verify-Foundation.ps1: database mới, seed Standard/VIP/tiếng Việt, dữ liệu chỉnh được giữ, CHECK/foreign keys, schema mới hơn bị chặn, commit/rollback và writer cạnh tranh.
- Verify-Authentication.ps1: tạo schema v1 thật riêng rồi nâng v2; inject lỗi migration và kiểm tra rollback schema/version; giữ tên/giá; seed role/quyền; không có mật khẩu mặc định; bootstrap validation/một lần; mật khẩu được băm/salt khác nhau/hash lỗi bị từ chối; username không phân biệt hoa thường; mật khẩu sai/khóa user bị chặn; logout không dùng lại phiên.
- Kiểm tra RBAC bằng tài khoản Staff/Manager fixture: quyền thay đổi có hiệu lực ở lần gọi kế tiếp, Manager không kế thừa động Staff, quyền bất biến và View trước Export/Print, chặn gọi Demand trực tiếp và trong transaction. Khởi tạo lại không phục hồi quyền đã tắt.
- Fixture thay stamp/role/hash xác nhận phiên cũ bị chặn và đăng nhập lại đọc dữ liệu mới. Đây là kiểm tra hiệu lực xác thực, không phải tuyên bố UI quản trị tài khoản đã hoàn thành.
- Inject lỗi ghi AuditLog: rollback cả user và membership. Hai task thiết lập Admin cùng database SQLite tạm: đúng một user/Admin/audit được commit. Không thay bằng database giả/in-memory.
- Verify-AuthenticationUi.ps1: kiểm tra component WPF trong process STA với database riêng; Khách trước đăng nhập, xác nhận mật khẩu sai, khóa submit khi băm, Admin 27 quyền, đăng xuất xóa bảng quyền, đăng nhập sai/đúng và hủy form. Render năm ảnh để xem bố cục/tiếng Việt.

Các bài kiểm tra chỉ dùng tên file SQLite ngẫu nhiên trong thư mục tạm và tự xóa file đã tạo. Không mở/nâng cấp/xóa database đang dùng để kiểm tra. Ảnh render UI được giữ trong thư mục tạm, script in đường dẫn.

## Phần chưa làm và bước kế tiếp

Khu vực nhân viên hiện là màn hình xác nhận đăng nhập/quyền. Danh sách quyền không có nghĩa các màn hình tương ứng đã được triển khai.

UI tạo Staff/Manager, khóa user, đổi role, reset mật khẩu, ma trận quyền và xem AuditLog vẫn ở bước 8. Store hiện chỉ SaveChanges cho tài khoản mới; cập nhật/xóa user hoặc tạo/sửa/xóa role cố định bị từ chối để không bỏ qua service quản trị. Khi làm bước 8 cần bổ sung service với kiểm tra User.Manage, không tự khóa/hạ role và bảo vệ Admin active cuối cùng trong transaction; đổi role/reset mật khẩu phải thay SecurityStamp.

Các tính năng Guest đặt/tra cứu SĐT/gia hạn/gọi món giữ nguyên phạm vi và được làm theo milestone nghiệp vụ. Chưa có phòng, booking, session, món, hóa đơn, calendar hoặc báo cáo/Excel.

Tiếp theo: bước 4, bắt đầu từng phần danh mục theo plan gốc, dùng PermissionService cho thao tác nội bộ. Không coi bước đăng nhập là hoàn tất ứng dụng quản lý.
