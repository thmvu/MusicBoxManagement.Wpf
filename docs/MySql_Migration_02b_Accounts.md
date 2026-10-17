# Chuyển Aiven MySQL — bước 2b: quản trị tài khoản

Ngày 17/10/2026. Đối chiếu plan WPF, bước 8b.1/8b.2 và bản nghiệp vụ tham chiếu mục 5/32/33/55.3–55.4. Giữ schema MySQL v1/SQLite v8 và toàn bộ quy tắc tài khoản đã chốt.

## Cách hoạt động dễ hiểu

Ở bước 2a, MySQL đã có đường đăng nhập và kiểm tra quyền. Bước này thêm service để Admin quản lý nhân viên trên MySQL:

- Tạo user mới với đúng một vai trò Staff/Manager/Admin.
- Sửa tên đăng nhập/họ tên, đổi vai trò, khóa/mở tài khoản.
- Đặt lại mật khẩu bằng Identity; không có mật khẩu mặc định hoặc email/OTP.
- Không xóa cứng user vì còn nhật ký và hóa đơn liên quan.

Mỗi lần sửa giữ một dấu nhận diện bản ghi (`Revision`). Nếu người khác đã sửa trước thì yêu cầu tải lại, không ghi đè từ form cũ. Đổi role/reset mật khẩu/khóa-mở thay SecurityStamp để phiên đăng nhập cũ bị từ chối ở lần thao tác sau. Chỉ sửa tên/họ tên giữ phiên và đọc tên mới từ database.

Admin không được tự khóa hoặc tự hạ vai trò. Transaction còn kiểm tra luôn có ít nhất một Admin đang hoạt động. Hai Admin thao tác đồng thời phải được kiểm tra trên dữ liệu thật trong transaction; deadlock hoặc lock timeout trả thông báo yêu cầu tải lại, không retry vô hạn.

## Luồng lưu dữ liệu

```text
Service kiểm tra User.Manage hiện hành
  → Identity chuẩn bị user/role/mật khẩu/stamp trong bộ nhớ
  → mở transaction ghi Serializable
  → đọc lại quyền và tài khoản gốc sau khi chờ khóa
  → kiểm tra Revision và username unique
  → lưu user + đúng một role
  → kiểm tra còn Admin active
  → lấy IClock + ghi AuditLog
  → commit
```

Lỗi ở bất kỳ bước lưu nào sẽ rollback dữ liệu, mật khẩu, role, stamp và audit cùng nhau. Việc băm mật khẩu thực hiện ngoài transaction để tránh giữ khóa lâu. Không giữ transaction khi chờ người dùng xác nhận. Lỗi mất kết nối lúc commit không được tự gửi lại thao tác.

## Mã và ranh giới

- `MySqlAccountManagementService.List/CreateAsync/UpdateAsync/ResetPasswordAsync` dùng lại DTO `UserAccount/AccountEdit`, PasswordHasher và UserManager của 2a. DTO không có mật khẩu/hash/stamp; danh sách có Revision để chặn bản ghi cũ.
- `List` đọc quyền và tài khoản trong một transaction RepeatableRead read-only. Staff/Manager bị chặn kể cả gán nhầm User.Manage. Writer đọc lại quyền trong Serializable cùng cập nhật và audit.
- `MySqlIdentityStore.SaveManagedChanges` là đường internal có kiểm tra User.Manage trong transaction; service giữ quy tắc self/last Admin/stale. `SaveChanges` của bootstrap vẫn không cập nhật user cũ; store không cho xóa user hoặc thay role cố định.
- Không có quyền riêng từng user, không thêm role tùy ý. Không log mật khẩu/hash.
- Không thay `App.OnStartup` hoặc nối UI sang MySQL. Màn quản trị đang dùng vẫn chạy service SQLite; đây là bước chuẩn bị provider MySQL, chưa chuyển dữ liệu thật.

## Kiểm tra

Các bộ MySQL chỉ tạo database `musicbox_test_<GUID>` và dọn đúng database đó, không chỉnh `musicbox` hoặc SQLite đang dùng.

- `Verify-MySqlFoundation`: schema/seed/constraints/ticks/decimal/bất biến.
- `Verify-MySqlAuthentication`: bootstrap đồng thời/login/quyền/stamp/rollback/snapshot và phiên đúng database.
- `Verify-MySqlAccounts`: tạo/sửa/reset, quyền Admin hiện hành, DTO riêng tư, username unique không phân biệt hoa thường, đúng một role, stale/no-op, đổi role/reset/khóa-mở vô hiệu hóa phiên, self/last Admin, hai Admin khóa nhau, hai form sửa cùng tài khoản, tạo trùng đồng thời, rollback lỗi audit khi tạo/sửa/reset và chặn store bootstrap ghi user cũ.
- Hồi quy SQLite theo AGENTS; kết quả chốt ở cuối báo cáo.

Chạy sau khi build:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlAccounts.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyMySql2bDebug
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlAccounts.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyMySql2bRelease
```

## Làm tiếp

Chuyển danh mục sang MySQL theo các bước nhỏ: loại phòng trước, rồi phòng/ảnh, dịch vụ và khách. Mỗi service phải giữ quyền hiện hành, validation, stale, snapshot và audit cùng transaction. Tiếp theo mới chuyển availability/booking/calendar/session/order/billing/checkout/invoice/dashboard/worker. Ảnh cần kế hoạch lưu/chuyển riêng; không coi MySQL online là ảnh đã online.

Công cụ sao lưu SQLite + Content, nhập/đối chiếu dữ liệu và kiểm tra UI trên DB riêng vẫn bắt buộc trước khi đổi App. Ma trận quyền/audit UI, báo cáo/Excel, lịch sử khách, ERD/từ điển dữ liệu và demo cuối vẫn nằm trong phạm vi.

## Kết quả kiểm tra chốt

- Build Debug/Release đạt tại `bin/VerifyMySql2bDebug` và `bin/VerifyMySql2bRelease`.
- **MySqlFoundation, MySqlAuthentication và MySqlAccounts đều đạt online trên cả Debug/Release.** Authentication đã chạy lại với Identity store mới của 2b. Accounts gồm cả chặn store bootstrap cập nhật user cũ, đường managed từ chối Staff, no-op edit, stale edit đồng thời và rollback khi tạo user bị lỗi audit.
- Tất cả database cloud trong các bộ kiểm tra là tên GUID riêng và được dọn sau kiểm tra. Không đổi `musicbox` thật, database/ảnh SQLite đang dùng hoặc cấu hình mật khẩu.
- **27/27 bộ hồi quy SQLite đạt** trên build 2b Debug: Accounts, Authentication, AuthenticationUi, Availability, Billing, Calendar, CheckIn, Checkout, Customers, Dashboard, Extension, Foundation, GuestBooking, GuestCalendar, GuestLookup, GuestSessions, InvoicePrinting, Invoices, Orders, Reservations, Rooms, RoomTypes, Services, Sessions, StaffBooking, StaffReservations, WalkIn.
- AuthenticationUi render **131 màn hình**; InvoicePrinting dùng giả lập, không gửi giấy thật. Không thay đổi UI hoặc nghiệp vụ SQLite trong lượt này.
