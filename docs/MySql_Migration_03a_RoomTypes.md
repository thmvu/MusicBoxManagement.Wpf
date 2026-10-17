# Chuyển Aiven MySQL — bước 3a: loại phòng Standard/VIP

Cập nhật 18/10/2026: đã chuyển tiếp đường phòng/ảnh sang MySQL ở [bước 3b](MySql_Migration_03b_Rooms.md). App vẫn dùng SQLite; mục Làm tiếp dưới đây là thứ tự tại bước 3a.

Ngày 17/10/2026. Đối chiếu plan WPF, Step4a_RoomTypes và bản nghiệp vụ tham chiếu mục 5/18/32/47/55. Giữ MySQL schema v1 và SQLite v8.

## Trạng thái của bước này

Đã thêm đường service MySQL để đọc/sửa danh mục loại phòng. **Giao diện và dữ liệu vận hành của ứng dụng vẫn dùng SQLite.** Bước này không đổi App.OnStartup, không nhập dữ liệu thật, không sửa Standard/VIP trên database `musicbox` đang dùng. Phải hoàn tất các nhóm service và chuyển dữ liệu trước khi đổi nguồn vận hành của toàn ứng dụng.

## Cách hoạt động dễ hiểu

`MySqlRoomTypeService` có ba hàm:

- `List()` đọc danh mục công khai: loại phòng/tên/sức chứa/giá/tiện ích/mô tả. Không có thông tin khách hoặc tài khoản. Hàm chỉ đọc, không tự khởi tạo/seed database; việc khởi tạo thuộc bước thiết lập database.
- `GetForEdit(session, id)` kiểm tra RoomType.Edit hiện hành và đọc loại phòng trong cùng snapshot read-only. Khách không được mở dữ liệu sửa; Staff có quyền này được Admin cấp vẫn được sửa như quy tắc cũ.
- `Update(session, original, changes)` mở transaction, đọc lại quyền và khóa dòng loại phòng, so dữ liệu hiện hành với bản lúc mở form, rồi lưu thay đổi và nhật ký cùng nhau.

Ví dụ: hai người cùng mở giá 120.000đ. Người thứ nhất lưu 145.000đ. Người thứ hai vẫn gửi bản giá cũ sẽ bị yêu cầu tải lại, không ghi đè âm thầm. Nếu ghi nhật ký bị lỗi, lần đổi giá cũng bị rollback.

## Quy tắc giữ nguyên

- Chỉ Standard/VIP đã có; service không có API thêm/xóa hoặc sửa Code/ID.
- Sửa Name, Capacity, PricePerHour, Amenities, Description. Tên/tiện ích bắt buộc, sức chứa và giá nguyên đồng dương; giữ giới hạn độ dài 100/1000/2000. Trim chuỗi, mô tả trắng lưu NULL.
- Code chỉ dùng đối chiếu bản gốc; không nằm trong DTO thay đổi hoặc câu UPDATE. Bản gốc bị sửa Code giả bị từ chối như dữ liệu stale.
- Giá dùng long/BIGINT, không đổi sang float/double. Không thay giá snapshot của phiên hoặc hóa đơn.
- Không thay đổi gì thì không UPDATE hoặc ghi audit thừa.
- Quyền/active/stamp thuộc cùng transaction ghi với cập nhật. Mất quyền sau khi mở form bị chặn khi lưu; không fallback Guest.
- `RoomType.Update` dùng MySqlAuditService với ActorType Staff, user/id/mã loại phòng và giờ UTC.

## Transaction MySQL

Đọc mở form dùng RepeatableRead read-only. Writer dùng Serializable và SELECT FOR UPDATE trên đúng dòng RoomTypes sau khi kiểm tra quyền. So sánh bản gốc và ghi dữ liệu thực hiện trong transaction đó; lỗi thì rollback. Không dùng khóa toàn ứng dụng hoặc khóa khởi tạo schema cho nghiệp vụ.

Deadlock 1213/lock timeout 1205 được trả thành thông báo tải lại rồi thử lại, không retry vô hạn. Không giữ transaction trong thời gian người dùng nhập form. Không tự gửi lại khi mất kết nối làm kết quả commit chưa rõ.

## Kiểm tra và cách chạy

Thêm `Tests/MySqlRoomTypeChecks.cs` và `Tests/Verify-MySqlRoomTypes.ps1`. Bộ kiểm tra chỉ tạo database `musicbox_test_<GUID>` riêng trên Aiven và dọn đúng tên đó.

Các tình huống: quyền đọc/sửa, Manager mặc định và Staff được cấp quyền, validation/trim/NULL, Code cố định, no-op không audit, form cũ/ID không tồn tại/Code giả, giữ giá BIGINT lớn chính xác, quyền bị thu hồi/đăng xuất, rollback lỗi audit, hai form cùng sửa chỉ một lần lưu và một audit.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlRoomTypes.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyMySql3aDebug
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlRoomTypes.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyMySql3aRelease
```

Kết quả chạy chốt được ghi cuối báo cáo. Kiểm tra MySQL không xác nhận giao diện đã dùng Aiven; UI hiện tại vẫn gọi RoomTypeService SQLite.

## Làm tiếp

Bước 3b chuyển phòng và kế hoạch ảnh: thêm/sửa/đổi loại/khóa-mở có lý do, giữ guard Active/Confirmed/grace, stale, quyền, audit, snapshot và rollback filesystem như plan. Sau đó dịch vụ, khách hàng, availability/booking/calendar/session/order/billing/checkout/invoice/dashboard/worker. Sao lưu/nhập/đối chiếu dữ liệu, ảnh và kiểm tra UI riêng phải hoàn thành trước khi chuyển App sang Aiven. Không bỏ ma trận quyền, audit UI, báo cáo/Excel, lịch sử khách hoặc ERD/demo.

## Kết quả kiểm tra chốt

- Build Debug/Release đạt tại `bin/VerifyMySql3aDebug` và `bin/VerifyMySql3aRelease`.
- **MySqlRoomTypes, MySqlFoundation, MySqlAuthentication đều đạt online trên cả Debug/Release**, dùng các database GUID riêng. RoomTypes gồm giá long.MaxValue không mất độ chính xác, Code giả bị chặn và hai form cùng sửa chỉ một cập nhật/audit.
- **4/4 bộ hồi quy SQLite đạt** trên build 3a Debug: Foundation, Authentication, RoomTypes, AuthenticationUi. UI render **131 màn hình**; không có thay đổi giao diện trong lượt này.
- Không đổi schema, App/provider, worker, database `musicbox` thật hoặc dữ liệu/ảnh SQLite đang dùng. Không lưu mật khẩu Aiven trong Git/log.
