# Chuyển Aiven MySQL — bước 3b: phòng và ảnh

Ngày 18/10/2026. Đối chiếu plan WPF, Step4b1/Step4b2a/Step4b2b và mục 5/19/20/32/38/55 của bản nghiệp vụ tham chiếu. Giữ schema MySQL v1/SQLite v8.

## Trạng thái bàn giao

Đã thêm đường `MySqlRoomService` cho danh mục phòng trên MySQL: đọc nội bộ, tạo, sửa tên/mô tả/thay ảnh, đổi loại, khóa/mở có lý do. **App và giao diện đang mở vẫn dùng SQLite.** Chưa nhập dữ liệu thật hoặc đổi worker/UI sang Aiven; không seed phòng/ảnh demo.

**Ảnh vẫn là file local.** Aiven lưu ImageUrl tương đối, không lưu nội dung ảnh. Hàm khởi tạo service bắt buộc nhận thư mục gốc tuyệt đối, do caller cấu hình riêng và ổn định cho từng database:

```text
MySqlRoomService(database, storageRoot[, clock])
  storageRoot/Content/uploads/rooms/<GUID>.png
```

Không lấy đường dẫn file SQLite làm cấu hình mặc định cho MySQL. Không tự sao chép, đổi tên hoặc thay ảnh trong Content hiện hành. Khi chuyển dữ liệu sau này phải sao lưu/nhập cả ảnh và giữ ánh xạ ImageUrl; nếu dùng nhiều máy thì cần giải pháp lưu/chia sẻ ảnh riêng. Chưa triển khai upload ảnh online hoặc đồng bộ giữa máy.

## Cách hoạt động dễ hiểu

Khi thêm phòng, service kiểm tra Room.Manage, đọc/giải mã ảnh trước khi giữ transaction, rồi kiểm tra lại quyền/loại phòng/mã trùng trong transaction MySQL. Sau đó lưu phòng, ghi file PNG và ghi nhật ký, cuối cùng commit. Không có bước xóa phòng.

Khi sửa, service khóa đúng dòng Rooms trước khi đọc thông tin hiển thị kèm loại phòng. Nếu dữ liệu khác bản lúc mở form thì yêu cầu tải lại. Mã phòng và CreatedAt giữ nguyên; nếu không chọn ảnh mới thì giữ ảnh cũ. Ảnh cũ sau thay cũng được giữ lại cho người đang đọc/sao lưu, chưa có bộ dọn ảnh cũ.

Nếu đổi loại hoặc chuyển mở → khóa, service kiểm tra dữ liệu phiên/lịch ngay trong transaction đó:

- Có Active Session thì không đổi loại hoặc khóa; vẫn được sửa chữ/ảnh mà không tự đổi loại/trạng thái.
- Khóa bị chặn bởi mọi Confirmed còn hiệu lực, gồm booking tương lai và đang trong 15 phút nhận phòng.
- Đúng StartTime + 15 phút thì Confirmed không chặn khóa dù worker chưa đổi sang NoShow.
- Completed và các trạng thái booking lịch sử không bị coi là giữ chỗ.
- Khóa cần lý do, mở lại xóa lý do. Cập nhật kèm ảnh/loại/trạng thái vẫn ghi một audit tương ứng Room.Create/Update/Lock/Unlock.

Thời gian lấy qua IClock sau khi mở transaction và khóa/kiểm tra dữ liệu liên quan. Tạo dùng cùng mốc UTC cho CreatedAt và audit; sửa dùng mốc UTC trong transaction cho cutoff grace và audit. Không làm tròn giờ hoặc tự checkout/NoShow.

## Giữ quy tắc và tính nguyên tử

- Room.Manage hiện hành ở cả đọc và ghi, không đòi RoomType.Edit/Reservation.View/Customer CRUD. Guest không gọi được đường quản trị. Mất quyền/stamp/logout bị chặn, không fallback Guest.
- Mã phòng trim, unique không phân biệt hoa/thường và cố định; tên/loại/ảnh bắt buộc, giới hạn độ dài giữ nguyên. Ảnh JPEG/PNG/WebP thật <=5 MiB được chuẩn hóa PNG bằng RoomImages hiện có; tên file GUID, ImageUrl không cho vượt thư mục.
- Read dùng RepeatableRead read-only; writer Serializable ngắn, khóa dòng phòng trước khi so bản gốc và đọc Active/Confirmed. Không khóa ứng dụng toàn cục hoặc dùng schema lock thay validation nghiệp vụ.
- No-op không đổi dữ liệu hoặc ghi audit. Dữ liệu phòng và audit cùng commit; lỗi trước commit rollback database và dọn đúng ảnh mới do thao tác tạo. Lỗi filesystem cũng rollback phòng/audit.
- MySQL LAST_INSERT_ID lấy ID vừa tạo trên cùng connection. Duplicate mã trả lỗi chọn mã khác; deadlock/lock timeout báo tải lại, không retry vô hạn.

## Giới hạn khi file và database nằm ở hai nơi

Filesystem không có transaction chung với MySQL. Nếu process/máy dừng đột ngột sau ghi file có thể còn ảnh mồ côi.

Với lỗi sau khi đã bắt đầu gửi COMMIT, chưa chắc server đã commit hay rollback. Service giữ ảnh mới, báo phải tải lại/đối chiếu trước thao tác lại, không tự gửi lại hoặc xóa ảnh có thể đã được tham chiếu. Cách này có thể giữ ảnh không còn được dùng; chưa có bộ dọn ảnh mồ côi và chưa kiểm thử fault mạng đúng cửa sổ COMMIT. Không tuyên bố rollback filesystem tuyệt đối khi mất mạng/mất điện.

## Kiểm tra và cách chạy

`Verify-MySqlRooms.ps1`/`MySqlRoomChecks.cs` dùng database `musicbox_test_<GUID>` và thư mục GUID trong TEMP; cleanup kiểm tra cả tên database và đường dẫn trước khi dọn. Không sửa database `musicbox`, ảnh thật hoặc mật khẩu trên máy.

Các tình huống gồm quyền/thu hồi/Staff được cấp/logout; mã/tên/loại/mô tả; PNG/JPEG/WebP/ảnh giả/GIF/hỏng/quá dung lượng/path traversal; tạo/sửa/ảnh cũ/giữ Code-CreatedAt/stale/no-op; audit/filesystem rollback; tạo trùng và sửa-thay ảnh đồng thời; Active/Completed, snapshot, Confirmed tương lai/grace đúng tick, lý do/mở lại; cạnh tranh khóa với writer booking mô phỏng đúng hợp đồng transaction.

Writer booking là **fixture**, chưa phải ReservationService MySQL. Các cuộc đua với booking/check-in/NoShow/checkout service MySQL thật phải được bổ sung khi chuyển các service đó; kiểm tra này không xác nhận đã chuyển các luồng vận hành.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlRooms.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyMySql3bDebug
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlRooms.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyMySql3bRelease
```

Windows PowerShell không đọc binding redirects của exe. Load-AivenBuild giải quyết dependency từ đúng build; script kiểm tra ảnh chỉ tắt cảnh báo biên dịch CS1701 do MySqlConnector/SkiaSharp tham chiếu hai phiên bản System.Memory, giữ các lỗi/cảnh báo khác. WPF dùng binding redirects do MSBuild tạo, không tắt kiểm tra TLS hoặc thay package trong bước này.

## Làm tiếp

3c chuyển danh mục dịch vụ, rồi khách hàng/SĐT. Tiếp theo availability/booking/calendar/session/order/billing/checkout/invoice/dashboard/worker. Giữ schema/rules/snapshot/quyền/audit và thêm kiểm tra MySQL tương ứng. Công cụ sao lưu/nhập/đối chiếu dữ liệu + Content và kiểm tra UI riêng phải hoàn thành trước khi đổi App sang Aiven. Ma trận quyền/audit UI, báo cáo/Excel, lịch sử khách, ERD/từ điển dữ liệu/demo vẫn trong phạm vi.

## Kết quả kiểm tra chốt

- Build Debug/Release đạt tại `bin/VerifyMySql3bDebug` và `bin/VerifyMySql3bRelease`.
- **Verify-MySqlRooms đạt online cả Debug/Release**, gồm ảnh thật PNG/JPEG/WebP, dữ liệu/ảnh/audit rollback, filesystem lỗi, quyền/stale/no-op/concurrency, guard Active/Confirmed/grace/Completed/snapshot và writer booking fixture.
- **Verify-MySqlFoundation và Verify-MySqlRoomTypes đạt online trên Debug** của bước 3b. Schema giữ v1; không thay các service xác thực/loại phòng hiện có.
- **7/7 bộ hồi quy SQLite đạt**: Foundation, Authentication, RoomTypes, Rooms, AuthenticationUi, Calendar, Availability. UI render **131 màn hình**; không có thay đổi giao diện trong lượt này.
- Mọi kiểm tra dùng database GUID riêng và ảnh TEMP riêng, cleanup có kiểm tra tên/đường dẫn. Không đổi database/ảnh thật, App/provider, schema hoặc mật khẩu trên máy.
- Chưa thử cắt mạng đúng cửa sổ COMMIT/process crash; giữ giới hạn đã nêu, không coi fixture là service booking MySQL hoàn chỉnh.
