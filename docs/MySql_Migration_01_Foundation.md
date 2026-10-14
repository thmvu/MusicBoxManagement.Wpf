# Chuyển Aiven MySQL — bước 1: schema và kết nối

Ngày 14/10/2026. Người dùng yêu cầu chuyển Music Box sang MySQL Aiven sau khi WPF .NET Framework 4.7.2 đã kết nối thành công. Giữ chế độ Khách tại quầy, Identity/RBAC, toàn bộ nghiệp vụ theo plan WPF và Reference_Web_ProjectPlan_v1.3. Đây là bước đầu của migration, chưa chuyển service hoặc dữ liệu SQLite.

## Đã triển khai

- `AivenDatabase.CreateConnection` dùng chung cấu hình TLS VerifyFull, chứng chỉ và mật khẩu biến User Windows. Không lưu secret vào Git hoặc log.
- `MySqlDatabase` mở kết nối, bật strict SQL mode và múi giờ UTC, khởi tạo schema MySQL v1 tương ứng nghiệp vụ SQLite v8. Tên database được kiểm tra trước khi dùng làm identifier.
- `MySqlSchema.sql` embedded resource tạo 15 bảng nghiệp vụ; thêm `_MusicBoxSchemaVersion` quản lý phiên bản. Bao gồm danh mục, Identity/role/permission/audit, khách, Reservation/RoomSession, dịch vụ/Order/OrderItem, Invoice.
- Seed đúng Standard/VIP, ba role, 27 permission và quyền mặc định Staff/Manager từ PermissionCatalog. Admin vẫn toàn quyền cố định qua service, không tạo ma trận Admin riêng. Không seed tài khoản hoặc phòng giả.
- Database `musicbox` đã được tạo trên Aiven: 16 bảng, hai loại phòng, 27 quyền. `defaultdb` không bị thay schema; SQLite đang dùng không bị thay đổi.

## Giữ độ chính xác và ràng buộc

- UTC vẫn là chuỗi round-trip 33 ký tự, 7 chữ số phần giây; không dùng DATETIME(6) vì mất một chữ số tick. CHECK kiểm tra định dạng UTC, khoảng phiên và trạng thái.
- Giá dùng BIGINT; tiền hóa đơn dùng DECIMAL(29,0), giới hạn theo decimal.MaxValue của .NET. Không dùng FLOAT/DOUBLE.
- InnoDB/FK/CHECK, SĐT unique và chuẩn hóa ở service; mã phòng unique không phân biệt hoa/thường và trigger cấm thay đổi mã.
- Cột generated ActiveRoomId/ActiveCustomerId + unique index thay partial unique index của SQLite: chỉ một Active trên mỗi Room/Customer, nhiều Completed được giữ lại. Reservation nguồn unique; walk-in giữ ReservationId/ExpectedEndTime null.
- Giữ snapshot tên/giá món, ràng buộc quantity 1–10/unique(OrderId,ServiceId), hóa đơn unique theo phiên và trigger chặn sửa/xóa.

## Khởi tạo và lỗi

MySQL DDL có implicit commit, không thể hứa rollback cả CREATE TABLE như SQLite. Version giữ 0 tới khi toàn bộ DDL/trigger và transaction seed thành công. DDL dùng IF NOT EXISTS; metadata và khóa GET_LOCK riêng cho **khởi tạo schema** cho phép chạy lại sau gián đoạn. Khóa này không phải khóa ghi cho nghiệp vụ và không thay thế thiết kế transaction/concurrency khi chuyển service.

Chặn database có bảng nhưng chưa có metadata Music Box và phiên bản mới hơn ứng dụng; không tự xóa/đổi schema cũ. Initialize lặp lại phiên bản đã hoàn tất không seed lại hoặc ghi đè dữ liệu.

## Kiểm tra đã chạy

- Build Debug và Release đạt.
- Verify-MySqlFoundation đạt trên **cả Debug và Release**, dùng database tên `musicbox_test_<GUID>` trên Aiven. Có kiểm tra seed lặp/UTF-8/RBAC/CHECK/FK/rollback/ticks/Active unique/null walk-in/snapshot/quantity/decimal.MaxValue/hóa đơn bất biến/version guard. Database tạm được xóa sau kiểm tra, kiểm tra tên cố định trước DROP.
- Verify-Foundation SQLite đạt trên dữ liệu tạm.
- Khởi tạo `musicbox` thật qua Tools/Initialize-Aiven.ps1 đạt; kiểm tra 16 bảng/2 RoomTypes/27 Permission. Chưa có dữ liệu khách/booking từ SQLite được đưa lên cloud.

Chạy lại:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlFoundation.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Initialize-Aiven.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug -DatabaseName musicbox
```

Tools/Load-AivenBuild.ps1 giải quyết dependency trong Windows PowerShell vì host này không đọc binding redirects của WPF exe; ứng dụng WPF vẫn dùng cấu hình redirect do MSBuild tạo.

## Các bước còn lại

1. Chuyển transaction/quyền/audit và kho Identity sang MySQL, kiểm tra bootstrap/đăng nhập/current permission/rollback/concurrency.
2. Chuyển danh mục rồi availability/booking/session/order/billing/checkout/dashboard/quản trị, giữ các bộ kiểm tra hiện có và bổ sung trên MySQL.
3. Công cụ sao lưu/đọc snapshot SQLite và nhập dữ liệu MySQL, kiểm tra ID/FK/snapshot/tiền/thời gian/tài khoản và ảnh. Không tự bỏ dữ liệu hoặc reset mật khẩu.
4. Chọn provider MySQL cho app sau khi service và kiểm tra đạt; thử UI trên database riêng trước dữ liệu thật.

Hiện App.OnStartup vẫn tạo SqliteDatabase. Không thay bằng MySqlDatabase đơn giản vì các service đang sử dụng kiểu/câu SQL/transaction SQLite. Ảnh vẫn nằm cạnh SQLite trong Content; chưa triển khai lưu ảnh online. Không phát hành tài khoản avnadmin cùng ứng dụng cho khách.
