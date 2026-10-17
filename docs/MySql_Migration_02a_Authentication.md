# Chuyển Aiven MySQL — bước 2a: xác thực, quyền và transaction

Cập nhật sau khi người dùng bật lại Aiven ngày 17/10/2026: **Verify-MySqlFoundation và Verify-MySqlAuthentication đã chạy đạt online trên cả Debug/Release**, dùng database GUID riêng. Lỗi DNS ghi trong nhật ký dưới đây đã được khắc phục. Phần quản trị tài khoản được chuyển tiếp ở [bước 2b](MySql_Migration_02b_Accounts.md). App vẫn dùng SQLite.

Ngày 17/10/2026. Đối chiếu plan WPF và bản nghiệp vụ tham chiếu mục 5/32/33/53/55.3–55.4. Đây là phần nền tảng của migration bước 2; chưa chuyển giao diện hoặc dữ liệu vận hành sang MySQL.

## Giải thích dễ hiểu

Ở bước trước, Aiven đã có các bảng nhưng ứng dụng vẫn đọc/ghi SQLite. Bước này bổ sung đường xử lý tài khoản trên MySQL:

1. Identity kiểm tra tên và băm mật khẩu bằng cùng thuật toán đang dùng ở SQLite. Việc băm tốn thời gian nên thực hiện trước khi giữ transaction ghi.
2. Khi tạo Admin đầu tiên, mở transaction, đọc lại xem đã có tài khoản chưa, rồi lưu tài khoản, vai trò và nhật ký cùng nhau. Nếu một phần lỗi thì hủy toàn bộ.
3. Khi đăng nhập, kiểm tra mật khẩu rồi đọc lại trạng thái tài khoản và SecurityStamp trong transaction trước khi ghi nhật ký. Tài khoản bị khóa hoặc phiên cũ sau đổi mật khẩu/role sẽ bị chặn.
4. Mỗi thao tác đọc lại quyền từ database. Admin luôn toàn quyền; Staff không được báo cáo; quyền quản trị chỉ Admin; in/xuất cần quyền xem tương ứng.
5. Phiên MySQL gắn với tên database. Không nhận phiên SQLite hoặc phiên của database MySQL khác dù trùng ID/stamp; đường SQLite cũng từ chối phiên MySQL.

Các lớp mới có tiền tố MySql để không vô tình chuyển một phần giao diện đang dùng SQLite sang cloud. Khi đủ các service MySQL và kiểm tra đạt mới đổi nguồn vận hành của App.

## Mã đã thêm

- `Data/MySqlIdentityStore.cs`: thực hiện interface Identity, gom thay đổi trong bộ nhớ và chỉ lưu tài khoản mới trong transaction Serializable. Không cho cập nhật user cũ/xóa tài khoản/sửa role cố định qua store. Service quản trị tài khoản MySQL cần triển khai riêng ở bước sau để giữ self/last Admin/stale/audit.
- `Services/MySqlAuthenticationService.cs`: NeedsSetup, SetupAdminAsync, LoginAsync, Logout; không seed tài khoản hoặc mật khẩu mặc định. Deadlock 1213/lock timeout 1205 được trả thành lỗi yêu cầu đọc lại/thử lại, không retry vô hạn. Chỉ trả phiên khi commit thành công; mất kết nối lúc commit không tự gửi lại.
- `Services/MySqlPermissionService.cs`: snapshot tài khoản/role/quyền, Demand dùng cùng kết nối và transaction ghi Serializable. Không cache quyền trong phiên.
- `Services/MySqlAuditService.cs`: Staff/Guest/System dùng cùng transaction ghi với nghiệp vụ; thời gian UTC round-trip. Hiện bootstrap/login dùng đường Staff; Guest/System sẽ dùng khi chuyển service tương ứng.
- `MySqlDatabase.BeginWriteTransaction`: Serializable cho transaction nghiệp vụ ngắn; InnoDB bảo vệ dữ liệu/phạm vi đọc. Không dùng khóa khởi tạo schema hoặc khóa ứng dụng toàn cục thay kiểm tra nghiệp vụ. Thời gian chờ khóa InnoDB là 5 giây, command timeout vẫn có giới hạn.
- `MySqlDatabase.BeginReadTransaction`: RepeatableRead + read-only để các SELECT thấy cùng snapshot, thay vì ReadCommitted vốn có thể thấy các thời điểm commit khác nhau giữa các SELECT trên MySQL.
- `LoginSession.DatabaseScope` và guard tương ứng ở hai provider.

Schema MySQL vẫn v1, SQLite vẫn v8. Không tạo/sửa schema hoặc tài khoản trên database `musicbox` thật trong bước này. Không sửa App.OnStartup, worker hoặc UI.

Giải thích kỹ thuật về [isolation của InnoDB](https://dev.mysql.com/doc/refman/8.0/en/innodb-transaction-isolation-levels.html) và [locking read](https://dev.mysql.com/doc/refman/8.0/en/innodb-locking-reads.html). Serializable có thể phát sinh deadlock khi hai transaction cùng kiểm tra vùng chưa có tài khoản; một transaction phải rollback. Không tuyên bố đạt cạnh tranh chỉ từ việc chọn isolation: cần chạy integration test thật.

## Kiểm tra và giới hạn

- Build Debug/Release đạt tại `bin/VerifyMySqlDebug` và `bin/VerifyMySqlRelease`.
- Thêm `Tests/Verify-MySqlAuthentication.ps1` và `MySqlAuthenticationChecks.cs`. Chế độ `-PreflightOnly` đạt cả Debug/Release: kiểm tra đầu vào và chặn Guest/phiên SQLite/phiên MySQL database khác trước khi kết nối mạng; đường SQLite cũng chặn phiên MySQL trên file GUID tạm. Đây không phải kiểm tra integration cloud.
- **Sự cố trước khi bật lại Aiven (đã khắc phục):** lúc chạy ngày 17/10/2026, hostname đang cấu hình `musicbox-mysql-baitapnet.j.aivencloud.com` báo DNS không tồn tại; kết nối thất bại trước khi khởi tạo database tạm. Biến mật khẩu User Windows và chứng chỉ vẫn tồn tại. Điều này không phủ nhận kết nối thành công đã được người dùng xác nhận trước đây.
- Script mới dự kiến kiểm tra: bootstrap/rollback khi audit lỗi/hai kết nối cạnh tranh; username chuẩn hóa/mật khẩu sai/logout; quyền hiện hành và bất biến/dependencies; stamp/khóa/đổi role; transaction rollback và Dispose; snapshot đọc/read-only; bảo vệ quyền tới commit và chặn phiên khác database. Chỉ tên `musicbox_test_<GUID>` được phép dọn; không dùng database vận hành. Các tình huống này đã chạy đạt sau khi bật lại Aiven; xem cập nhật đầu tài liệu.
- Hồi quy SQLite: xem kết quả chốt cuối báo cáo; không chạm database người dùng.

Chạy khi Aiven truy cập được:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlAuthentication.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyMySqlDebug
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlAuthentication.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyMySqlRelease
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlFoundation.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyMySqlDebug
```

Không gửi mật khẩu qua chat hoặc lưu vào Git. Nếu host/port thay đổi, cập nhật thông tin kết nối được người dùng xác nhận; giữ TLS VerifyFull.

## Làm tiếp

1. Khôi phục kết nối Aiven; chạy kiểm tra nền tảng/xác thực trên Debug và Release, xử lý lỗi phát hiện được. **Đã chạy đạt sau khi Aiven hoạt động lại; xem cập nhật đầu tài liệu.**
2. Chuyển AccountManagementService với UserManager, quyền Admin hiện hành, revision/self/last Admin/reset/đổi stamp/rollback và kiểm tra đồng thời; không mở đường cập nhật trực tiếp qua store.
3. Chuyển lần lượt danh mục, availability/booking/calendar/session/order/billing/checkout/invoice/dashboard và worker; giữ kiểm tra overlap/phòng/khách/snapshot/clock sau transaction và quyền hiện hành.
4. Sao lưu SQLite + Content, nhập/đối chiếu dữ liệu và kiểm tra UI riêng rồi mới chuyển App. Ma trận quyền, audit UI, báo cáo/Excel, lịch sử khách và ERD vẫn trong phạm vi.

## Kết quả chốt lượt triển khai

- **27/27 bộ kiểm tra SQLite đạt** trên build Debug riêng: Accounts, Authentication, AuthenticationUi, Availability, Billing, Calendar, CheckIn, Checkout, Customers, Dashboard, Extension, Foundation, GuestBooking, GuestCalendar, GuestLookup, GuestSessions, InvoicePrinting, Invoices, Orders, Reservations, Rooms, RoomTypes, Services, Sessions, StaffBooking, StaffReservations, WalkIn.
- AuthenticationUi render **131 màn hình**. InvoicePrinting dùng bộ giả lập, không gửi lệnh máy in thật. Các kiểm tra dùng dữ liệu tạm riêng.
- Build mã cuối Debug/Release đạt; MySQL `-PreflightOnly` đạt cả hai build.
- Ban đầu integration bị DNS; sau khi người dùng bật lại Aiven, Foundation/Authentication đã đạt online Debug/Release. Chưa thay nguồn dữ liệu của app.
