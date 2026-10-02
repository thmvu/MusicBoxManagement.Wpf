# Bước 2 — tài khoản, quyền và transaction WPF/SQLite

Ngày chốt kỹ thuật: 02/10/2026. Đối chiếu plan WPF bước 2–3 và plan web v1.3 mục 4–5, 33, 35, 53–55, 58. Đây là cách triển khai tương đương trên desktop; giữ nguyên phạm vi nghiệp vụ.

**Cập nhật 03/10/2026:** bước 3 đã triển khai đăng nhập theo thiết kế này. Xem `Step3_Authentication.md` về phần đã làm, kiểm tra và phần quản trị còn ở bước 8. Các mô tả schema v1/chưa có tài khoản bên dưới ghi lại thời điểm kết thúc bước 2.

## Tài khoản và đăng nhập (thiết kế cho bước 3)

- Dùng Microsoft.AspNet.Identity.Core 2 với `ApplicationUser : IdentityUser` và `IdentityRole` từ Microsoft.AspNet.Identity.EntityFramework 2 tương thích .NET Framework 4.7.2. Kho lưu tự triển khai các interface Identity trên System.Data.SQLite; không dùng Entity Framework để mở SQL Server, không dùng OWIN/cookie.
- Tiếp tục dùng `UserManager` cho tạo tài khoản, kiểm tra và reset mật khẩu. Kho lưu triển khai IUserStore, IUserPasswordStore, IUserRoleStore, IUserSecurityStampStore và IRoleStore với cùng kết nối/transaction khi thao tác cần nguyên tử. Không có hệ Role thứ hai.
- Schema v2 dự kiến: AspNetUsers (Id string, UserName, NormalizedUserName UNIQUE, PasswordHash, SecurityStamp, FullName, IsActive); AspNetRoles (Id string, Name UNIQUE chỉ Staff/Manager/Admin); AspNetUserRoles (UserId PRIMARY KEY, RoleId FK) bảo đảm tối đa một role. Service tạo/gán role trong transaction bảo đảm đúng một role cho tài khoản đã lưu.
- Permission (Id, Code UNIQUE, Name) và RolePermission (RoleId, PermissionId, khóa chính ghép, foreign keys). Seed đủ 27 mã mục 5.1; Staff có 19 quyền vận hành, Manager thêm 5 quyền danh mục/báo cáo; 3 quyền quản trị chỉ Admin. Seed một lần khi migration, không phục hồi quyền Admin đã tắt cho Staff/Manager khi khởi động.
- PasswordHasher thay thế qua IPasswordHasher: PBKDF2-HMAC-SHA256 bằng Rfc2898DeriveBytes của .NET Framework 4.7.2, tối thiểu 600.000 vòng, salt ngẫu nhiên 16 byte, kết quả 32 byte; định dạng lưu có phiên bản và số vòng; so sánh không thoát sớm. UserManager sử dụng hasher này, không lưu mật khẩu rõ. Tham chiếu [OWASP Password Storage](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html). Kiểm tra hiệu năng trên máy demo khi triển khai, thực hiện băm ngoài transaction ghi.
- Lần đầu chưa có tài khoản: màn hình thiết lập Admin yêu cầu người vận hành tự nhập mật khẩu, kiểm tra lại chưa có user trong transaction rồi tạo user/role/audit nguyên tử. Không seed mật khẩu mặc định. User/AuditLog không hard delete. Sau bootstrap, tạo tài khoản do Admin thực hiện ở bước quản trị.
- Phiên đăng nhập chỉ giữ UserId và SecurityStamp trong bộ nhớ, mất khi đăng xuất/đóng ứng dụng. Không lưu quyền vào phiên. Đổi role/reset mật khẩu thay SecurityStamp; khóa tài khoản bị từ chối ở lần thao tác kế tiếp. Không cho Admin tự khóa/hạ role; không làm mất Admin active cuối cùng.

## Một nguồn kiểm tra quyền

PermissionService đọc lại user active, stamp, role và RolePermission từ SQLite mỗi thao tác. Thứ tự: kiểm tra phiên/user → mã quyền hợp lệ → giới hạn bất biến → Admin toàn quyền hoặc quyền hiện hành trong RolePermission. Không suy quyền từ tên Staff/Manager.

- Staff luôn bị chặn Report.View/Report.Export và widget doanh thu.
- User.Manage, Permission.Manage, Audit.View chỉ Admin.
- Report.Export cần Report.View; Invoice.Print cần Invoice.View.
- Manager không kế thừa động quyền Staff. Admin toàn quyền cố định, không phụ thuộc RolePermission.
- UI ẩn/tắt chức năng theo cùng service; service nghiệp vụ vẫn kiểm tra khi bị gọi trực tiếp. Với thao tác ghi, kiểm tra quyền lại trong transaction dùng cùng kết nối. Kiểm tra ở UI không thay kiểm tra ở service.
- Chế độ Khách không có tài khoản/role nội bộ. Màn hình và service Guest riêng giữ tra cứu SĐT và điều kiện nghiệp vụ mục 4.1; không mở quản trị. Các màn hình nghiệp vụ Guest sẽ làm theo các milestone sau, không coi danh mục hiện tại là đã có đủ Guest.

## Transaction đã chuẩn bị trong bước này

`SqliteDatabase.BeginWriteTransaction(connection)` gọi `BeginTransaction(IsolationLevel.Serializable)`. Với System.Data.SQLite 1.0.119, đây là BEGIN IMMEDIATE; lấy quyền ghi trước các truy vấn validation. Initialize cũng dùng hàm này. Chưa đổi schema v1 hoặc tạo tài khoản.

Service sau này mở một kết nối → bắt đầu transaction → đọc lại quyền/entity/giờ hiện hành → validation → cập nhật dữ liệu liên quan và AuditLog → Commit. Gán Transaction cho mọi command. Nếu có lỗi hoặc không Commit, Dispose rollback. Không giữ transaction khi chờ người dùng, băm mật khẩu, xem tiền tạm tính, in hoặc xử lý ảnh.

Áp dụng transaction ngắn cho booking/hủy/NoShow, check-in/walk-in/gia hạn/khóa phòng, món/checkout và quản trị tài khoản/quyền. SQLite tự tuần tự hóa writer ở mức file; không thêm khóa ứng dụng toàn cục. Truy vấn đọc danh mục/lịch không lấy transaction ghi. Giữ foreign keys, CHECK và unique/partial unique index khi thêm bảng; không dùng unique giờ bắt đầu thay kiểm tra overlap.

Mỗi kết nối hiện có timeout 5 giây. SQLITE_BUSY/LOCKED hoặc constraint do cạnh tranh: rollback và báo dữ liệu đang bận/đã thay đổi, yêu cầu thao tác lại; không retry vô hạn. Check-in/checkout lặp đọc lại kết quả đã lưu theo plan khi đến milestone tương ứng. Nếu kết quả commit chưa rõ, phải đọc lại trước khi tạo thao tác khác.

Không bật WAL hoặc đổi journal mode trong bước này. Database nằm trên máy local; không thiết kế chia sẻ file qua mạng. Không sao chép cơ chế khóa SQL Server vào SQLite. Tham chiếu [SQLite transaction](https://www.sqlite.org/lang_transaction.html) và [SQLite isolation](https://www.sqlite.org/isolation.html); ánh xạ Serializable được đối chiếu XML tài liệu của package System.Data.SQLite đang dùng.

## Kiểm tra và phần tiếp theo

- Build Debug/Release bằng MSBuild Visual Studio.
- Verify-Foundation.ps1: schema/seed/tiếng Việt/constraints như bước 1; thêm commit, rollback khi lỗi và rollback khi Dispose, hai kết nối tranh quyền ghi trước khi ghi dữ liệu, đọc lại sau writer thứ nhất commit.
- Tất cả dùng SQLite tạm riêng, không mở/xóa database đang dùng.
- Đây chỉ là kiểm tra primitive transaction, chưa chứng minh không double-book hoặc checkout đồng thời. Các bài kiểm tra nghiệp vụ mục 53 chạy trên SQLite thật khi có các bảng/service tương ứng.
- Bước 3 kế tiếp: migration v2 và kho Identity, bootstrap Admin, đăng nhập/đăng xuất, PermissionService cùng UI nhân viên/Khách; kiểm tra seed không ghi đè quyền, quyền bất biến và vô hiệu hóa phiên. UI ma trận quyền/quản lý user/audit đầy đủ vẫn ở bước 8, không bị bỏ khỏi phạm vi.
