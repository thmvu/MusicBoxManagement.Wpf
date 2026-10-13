# Bước 8b.1 — Nền tảng quản trị tài khoản nhân viên

Ngày **14/10/2026**. Đối chiếu plan WPF bước 8 và tham chiếu **5/32/33**: Admin quản lý user, một role cố định, không xóa cứng, không tự khóa/hạ role, luôn còn Admin active. Giữ WPF .NET Framework 4.7.2 + SQLite **schema v8**.

## Đã có

`AccountManagementService` có bốn đường:

| API | Chức năng |
| --- | --- |
| List(actor) | Danh sách username/họ tên/role/active và revision, chỉ User.Manage hiện hành |
| CreateAsync(actor, input, password) | Tạo tài khoản với đúng Staff/Manager/Admin, mật khẩu qua UserManager |
| UpdateAsync(actor, original, input) | Sửa username/họ tên, role, khóa/mở; chặn form cũ |
| ResetPasswordAsync(actor, original, password) | Admin đặt lại mật khẩu qua UserManager, giữ role và trạng thái |

Username trim, 3–100 ký tự chữ không dấu/số/chấm/gạch dưới/gạch ngang, unique không phân biệt hoa thường như bootstrap. Họ tên trim 1–100, mật khẩu 8–128. Không yêu cầu email/SĐT. Đầu vào được sao chép trước xử lý async. Không có Delete hoặc role tùy ý; các lịch sử FK Invoice/AuditLog giữ nguyên.

`UserAccount` là DTO riêng, không trả password/hash/stamp. Revision là fingerprint SHA-256 của bản ghi hiện hành; dùng kiểm tra dữ liệu gốc giống các form danh mục, không là OTP/mật khẩu hoặc quyền truy cập. Các trường sửa/reset/role/stamp/hash đều tham gia kiểm tra form cũ. Đây không phải bộ đếm phiên bản: nếu toàn bộ bản ghi trở lại chính xác giá trị cũ thì fingerprint giống nhau.

**Chưa có giao diện quản trị ở bước này.** Bước kế tiếp sẽ nối cửa sổ Admin vào các API trên.

## Identity và lưu an toàn

- Dùng chung cấu hình UserManager/PasswordValidator/Pbkdf2PasswordHasher với AuthenticationService; không tự gán PasswordHash trong service.
- Chuẩn bị/hash mật khẩu và thay đổi Identity ngoài writer. Store chỉ giữ pending trong bộ nhớ. Reset dùng RemovePasswordAsync rồi AddPasswordAsync trên đối tượng pending; không có trạng thái thiếu mật khẩu được commit ra DB giữa hai lời gọi.
- Đọc quyền User.Manage trước chuẩn bị, rồi đọc lại trong **writer transaction** trước lưu. PermissionService vẫn chặn Staff/Manager kể cả gán nhầm quyền quản trị.
- Đọc bản ghi mới bằng một store khác trong writer để tránh lấy pending đã sửa; so Revision với original sau khi chờ quyền ghi. Kiểm tra username unique lại trong writer.
- `SaveManagedChanges(permissions, actor)` là đường internal dành cho service quản trị, kiểm tra quyền trong cùng transaction. `SaveChanges()` của bootstrap vẫn không cho cập nhật user cũ tùy ý. Chuẩn bị UserManager không tự ghi database.
- Save user và đúng một membership role, kiểm tra còn Admin active, ghi AuditLog User.Create/User.Update/User.ResetPassword rồi Commit. Lỗi audit/constraint/stale/quyền thì rollback cả user/hash/stamp/role. Không log mật khẩu hoặc hash.

## Phiên và bảo vệ Admin

- Đổi role/reset mật khẩu đổi SecurityStamp, phiên cũ bị PermissionService từ chối ở thao tác tiếp theo.
- Khóa/mở cũng đổi stamp để phiên trước khóa không sống lại sau mở. Mở lại giữ mật khẩu; phải đăng nhập mới.
- Chỉ sửa metadata username/họ tên không đổi stamp; quyền/name hiện hành đọc từ DB. Username cũ không đăng nhập được sau đổi tên, còn LoginSession theo UserId vẫn dùng được nếu stamp còn khớp.
- Không cho actor Admin tự đặt IsActive=false hoặc role khác Admin. Có thể sửa tên hoặc reset mật khẩu chính mình; tự reset làm phiên hiện tại mất hiệu lực, UI tương lai phải báo đã lưu rồi yêu cầu đăng nhập mới.
- Sau lưu vẫn kiểm tra số Admin active >=1 trong writer. Hai Admin đồng thời khóa/hạ role nhau: writer tuần tự, thao tác sau kiểm tra lại quyền/stale/stamp; không mất tất cả Admin.

## Kiểm tra

- Debug/Release build đạt tại bin/VerifyDebug và bin/VerifyRelease.
- **27 bộ kiểm tra** gồm Accounts mới và toàn bộ 26 bộ nền tảng/nghiệp vụ/UI trước; Accounts chạy thêm Release. AuthenticationUi giữ 122 ảnh của giao diện hiện có, chưa có ảnh quản trị tài khoản.
- Accounts dùng Identity/login thật để kiểm tra tạo/trim/unique case-insensitive, danh sách DTO không credentials, sửa username/họ tên và stale, role đúng một membership, Manager/Staff không được quản trị dù gán nhầm User.Manage.
- Đổi role/reset/khóa và mở lại: đăng nhập mới theo role/password mới, phiên cũ bị chặn; reset không đổi role, mở lại không đổi mật khẩu; sai role/password/username bị từ chối.
- Admin tự khóa/hạ role bị chặn; hai Admin khóa nhau đồng thời chỉ một thao tác thành công và còn đúng một Admin active. Tạo trùng username hoa/thường đồng thời chỉ một tài khoản được lưu.
- Trigger lỗi audit kiểm tra rollback cập nhật role/active/hash/stamp và reset; mật khẩu cũ sau reset bị rollback vẫn đăng nhập được. Audit không có mật khẩu đầu vào. Không đổi schema.
- Tất cả dữ liệu dùng thư mục GUID riêng trong TEMP và cleanup kiểm tra đường dẫn; không đụng database hoặc ảnh thật ở LOCALAPPDATA.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Accounts.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
```

## Luồng gọi hàm

```text
UI Admin tương lai → Task.Run → AccountManagementService.CreateAsync/UpdateAsync/ResetPasswordAsync
  → PermissionService.Demand(User.Manage)
  → SqliteIdentityStore + AuthenticationService.Manager
  → UserManager chuẩn bị hash/user/role/stamp trong pending
  → BeginWriteTransaction
  → Demand quyền hiện hành + DemandOriginal bản mới + DemandUnique
  → store.SaveManagedChanges
  → DemandAdminRemains + AuditService.WriteStaff
  → Commit → UserAccount DTO mới
```

Sau đó UI tải lại danh sách; nếu tải lại lỗi sau Commit vẫn báo đã lưu. Không tự gửi lại reset hoặc thao tác tạo vì lỗi reader.

## Tiếp theo

**8b.2 UI quản trị tài khoản** cho Admin: danh sách/thêm/sửa/khóa-mở/gán role/reset mật khẩu, form revision/xác nhận/busy/mất quyền, xử lý tự reset và kết quả đã commit. Đổi role theo RBAC, không quyền riêng từng user. Sau đó ma trận quyền/audit UI, báo cáo/Excel, lịch sử khách, demo/kiểm tra/tài liệu đồ án vẫn trong scope.
