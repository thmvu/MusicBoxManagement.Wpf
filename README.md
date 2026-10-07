# Music Box Management — WPF

Project desktop riêng cho đồ án: WPF, .NET Framework 4.7.2, SQLite.

## Chạy ứng dụng

1. Mở `MusicBoxManagement.Wpf.sln` trong Visual Studio có workload **.NET desktop development** và .NET Framework 4.7.2 Developer Pack.
2. Restore NuGet packages (Visual Studio thường tự thực hiện khi build).
3. Chọn `MusicBoxManagement.Wpf` làm Startup Project rồi nhấn F5.

Ứng dụng tự tạo file `%LOCALAPPDATA%\MusicBoxManagement.Wpf\musicbox.db` khi đọc danh mục lần đầu. Không cần cài SQL Server hay chạy `Update-Database`. Thư viện dùng là [System.Data.SQLite.Core 1.0.119](https://www.nuget.org/packages/System.Data.SQLite.Core/1.0.119).

## Đã làm ở bước nền tảng

- Solution và project WPF truyền thống, target .NET Framework 4.7.2.
- Khởi tạo SQLite bằng transaction, quản lý phiên bản schema bằng `PRAGMA user_version`.
- Hai loại phòng Standard/VIP theo plan cũ; giá là số nguyên đồng, sức chứa và giá phải lớn hơn 0.
- Cửa sổ đọc danh mục thật từ SQLite, nút làm mới; đọc database ở luồng nền để tránh treo giao diện.
- Khởi tạo lại không chèn trùng hoặc ghi đè giá/tên đã chỉnh.

Ứng dụng đã có nền tảng, đăng nhập, danh mục phòng/dịch vụ/khách hàng, NoShow, giao diện Khách đặt/tra cứu/hủy/lịch trống-bận và nhân viên đặt hộ/xem/chi tiết/hủy booking, lịch Ngày/Tuần nội bộ; chưa phải ứng dụng quản lý hoàn chỉnh. Schema hiện **v6**. Đã có service và UI nhận phòng từ booking, service walk-in; chưa có UI walk-in, gia hạn, gọi món, hóa đơn và báo cáo.

## Cấu trúc để học

Giao diện chính đã chuyển chức năng sang menu dọc bên trái theo yêu cầu ngày 07/10/2026. Menu hiện theo chế độ/quyền, cuộn được khi cửa sổ thấp; nút đăng nhập/đăng xuất nằm ở cuối thanh. Vùng nội dung bên phải rộng hơn, không còn hàng nút chen nhau. Xem [báo cáo giao diện](docs/Ui_MainSidebar.md). Dashboard nghiệp vụ vẫn thuộc bước 8.

Có 5 ảnh phòng AI minh họa Standard/VIP tại [assets/rooms](assets/rooms/README.md), theo phong cách Canon 50mm f/1.8 và ánh sáng tự nhiên. Ba mẫu thêm gồm Standard sofa thẳng, VIP buổi tối và Standard tông gỗ sáng. Người dùng sẽ chọn/áp dụng sau. Để dùng, đăng nhập → Phòng → thêm/sửa → Chọn ảnh → chọn file trong thư mục này → Lưu. Ảnh chỉ là tài nguyên để nhập qua luồng quản lý phòng; không tự seed phòng hoặc thay dữ liệu đang dùng.

| Thư mục | Vai trò |
| --- | --- |
| Models | Loại phòng, ApplicationUser của Identity và phiên đăng nhập |
| Data | Kết nối, nâng cấp schema và kho lưu Identity trên SQLite |
| Services | Danh mục, đăng nhập, băm mật khẩu và kiểm tra quyền |
| ViewModels | Dữ liệu và trạng thái được bind lên màn hình |
| Views | Giao diện XAML và sự kiện tải/làm mới |

WPF dùng binding theo hướng MVVM đơn giản. Sự kiện cửa sổ gọi ViewModel/service; SQL nằm ở Data/Services. PasswordBox được đọc và xóa trong sự kiện của cửa sổ đăng nhập; không bind mật khẩu lên ViewModel.

## Kiểm tra nền tảng

Sau khi build, chạy Windows PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Foundation.ps1
```

Bài kiểm tra tạo file SQLite tạm riêng, kiểm tra tiếng Việt, lưu thay đổi qua lần mở lại, seed không ghi đè dữ liệu, giá nguyên đồng và schema version. Nó tự xóa file tạm đã tạo và không chạm database đang dùng.

## Đã làm ở bước 2

- Chốt thiết kế Identity Core + kho SQLite, phiên đăng nhập trong bộ nhớ và kiểm tra quyền hiện hành ở service/UI trong [tài liệu triển khai](docs/Wpf_Authentication_Transactions.md).
- Thêm `SqliteDatabase.BeginWriteTransaction`: lấy quyền ghi trước khi đọc để kiểm tra và cập nhật nghiệp vụ; chưa thay schema v1.
- Kiểm tra thêm commit, rollback toàn bộ khi lỗi, rollback tường minh và hai kết nối tranh quyền ghi trên SQLite thật. Debug/Release build và Verify-Foundation đạt.

Thiết kế trên đã được triển khai cho đăng nhập ở bước 3. Kiểm tra transaction nền tảng chưa thay thế kiểm tra booking/checkout đồng thời ở các bước sau.

## Bước 3 — đăng nhập và quyền

1. Mở ứng dụng: mặc định **chế độ Khách**, xem Standard/VIP không cần đăng nhập.
2. Lần đầu, bấm **Thiết lập Admin** ở góc trái dưới. Nhập tên đăng nhập (3–100 ký tự: chữ không dấu, số, `.`, `_`, `-`), họ tên và mật khẩu 8–128 ký tự, nhập lại mật khẩu rồi bấm **Tạo Admin**. Không có mật khẩu mặc định.
3. Sau khi tạo, ứng dụng vào khu vực nhân viên và hiển thị quyền hiện hành. Bấm **Đăng xuất về Khách** để kết thúc phiên.
4. Các lần sau dùng **Đăng nhập nhân viên** với tài khoản đã tạo. **Kiểm tra lại quyền** đọc lại SQLite, không dùng quyền cũ trong phiên.

SQLite tự nâng từ v1 lên v2 trong transaction, giữ các loại phòng/giá đã chỉnh. Tài khoản dùng Identity Core 2.2.4/UserManager và kho SQLite; không chạy ASP.NET/OWIN/SQL Server. Mật khẩu dùng PBKDF2-SHA256, salt ngẫu nhiên, 600.000 vòng. Phiên chỉ ở bộ nhớ, không tự đăng nhập sau khi mở lại.

Ba role và 27 permission đã được seed. Staff/Manager đọc quyền hiện hành; Staff luôn bị chặn báo cáo, quyền quản trị chỉ Admin, Export/Print cần View. Admin toàn quyền cố định. Khi tài khoản bị khóa hoặc SecurityStamp thay đổi, service từ chối phiên cũ ở thao tác kế tiếp.

Hiện UI tài khoản chỉ thiết lập Admin đầu tiên và đăng nhập/đăng xuất. Tạo Staff/Manager, khóa/đổi role/reset mật khẩu, ma trận quyền và màn hình nhật ký đầy đủ sẽ làm ở bước 8. Tài khoản Staff/Manager trong bài kiểm tra là dữ liệu thử ở database tạm, không được seed vào database sử dụng. Chế độ Khách hiện có danh mục; các chức năng đặt/tra cứu/gia hạn/gọi món sẽ làm ở các bước nghiệp vụ.

Chi tiết cách hoạt động và giới hạn kiểm tra: [báo cáo bước 3](docs/Step3_Authentication.md).

Sau khi build, kiểm tra thêm bằng Windows PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Authentication.ps1
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1
```

Bài kiểm tra UI chạy các cửa sổ WPF với dữ liệu thử riêng, hiện kiểm tra thêm phòng/ảnh/loại/khóa mở/dịch vụ/khách hàng/worker NoShow và Khách đặt phòng, lưu 21 ảnh render trong thư mục tạm. Các bài kiểm tra không mở/xóa database đang dùng.

## Bước 4a — sửa Standard/VIP

1. Đăng nhập Admin (hoặc tài khoản có quyền sửa loại phòng).
2. Trong khu vực nhân viên, bấm **Loại phòng**, chọn Standard/VIP rồi bấm **Sửa loại phòng**.
3. Sửa tên, sức chứa, giá mỗi giờ, tiện ích và mô tả. Nhập giá nguyên đồng như `145000`, sức chứa là số nguyên dương; tên và tiện ích bắt buộc.
4. Bấm **Lưu thay đổi** để cập nhật bảng. **Hủy** không lưu. Đăng xuất trở về danh mục Khách chỉ xem.

Chỉ sửa hai loại đã có; không thêm/xóa hoặc sửa Code. Quyền `RoomType.Edit` được kiểm tra trong service cả khi mở form và khi lưu bằng cùng transaction với cập nhật/nhật ký. Form cũ bị chặn nếu người khác đã sửa dữ liệu; hãy đóng, làm mới rồi mở lại.

Schema hiện là **v3**: nâng từ v2 để sửa ActorType của nhật ký nhân viên từ `User` thành `Staff` đúng plan; giữ nội dung, ID, thời gian và liên kết user của nhật ký cũ. Phần sửa loại phòng dùng bảng RoomTypes hiện có, không seed lại giá/tên.

Kiểm tra phần này:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-RoomTypes.ps1
```

[Báo cáo bước 4a](docs/Step4a_RoomTypes.md) giải thích validation, transaction, nhật ký và kiểm tra đồng thời. Bước 4 còn danh mục phòng/ảnh/khóa mở, dịch vụ và khách hàng.

## Bước 4b.1 — thêm phòng có ảnh

1. Đăng nhập tài khoản có quyền `Room.Manage` (Admin/Manager mặc định), bấm **Phòng** → **Thêm phòng**.
2. Nhập mã phòng riêng, tên, chọn Standard/VIP, chọn một ảnh JPEG/PNG/WebP hợp lệ tối đa 5 MB; mô tả tùy chọn.
3. Bấm **Thêm phòng** rồi chọn dòng vừa tạo để xem ảnh. **Hủy** không lưu. Mã phòng cố định sau khi tạo; phòng mới đang mở.

Schema hiện **v4**, thêm Rooms, giữ dữ liệu loại phòng/tài khoản/quyền/nhật ký cũ. Ảnh bắt buộc, được giải mã rồi lưu PNG với tên do ứng dụng tạo trong `%LOCALAPPDATA%\MusicBoxManagement.Wpf\Content\uploads\rooms`. Khi sao lưu/chuyển máy, giữ cả database và thư mục Content. Không tự seed phòng demo.

Quyền, mã trùng, loại phòng và nhật ký được kiểm tra/ghi trong cùng transaction. Service chặn Guest/phiên hết hiệu lực; hai lần tạo cùng mã chỉ một thành công. Lỗi ghi ảnh/nhật ký rollback dữ liệu. Chi tiết và giới hạn khi process dừng đột ngột: [báo cáo bước 4b.1](docs/Step4b1_RoomCreation.md).

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Rooms.ps1
```

## Kế hoạch tiếp theo

Bước 4b.2a đã có: **Phòng → chọn dòng → Sửa phòng**, sửa tên/mô tả và chọn ảnh mới nếu cần; không chọn thì giữ ảnh hiện tại. Hủy không lưu. Service kiểm tra Room.Manage khi mở/lưu, chặn form cũ và lưu nhật ký cùng transaction. Schema giữ v4. Ảnh cũ sau thay được giữ trên đĩa; hiện chưa có bộ dọn ảnh cũ. Xem [báo cáo 4b.2a](docs/Step4b2a_RoomEditing.md). Kiểm tra bằng Verify-Rooms.ps1 và Verify-AuthenticationUi.ps1 ở trên.

**4b.2b đã hoàn thành:** trong form sửa, chọn Standard/VIP và bật/tắt **Mở phòng để nhận khách**. Khóa cần lý do; phiên Active chặn đổi loại/khóa, booking Confirmed còn hiệu lực (kể cả tương lai) chặn khóa. Đúng mốc StartTime+15 phút thì booking hết hạn dù worker chưa ghi NoShow. Mở lại xóa lý do. Quyền/kiểm tra dữ liệu/cập nhật/nhật ký cùng transaction; schema hiện **v5**. Xem [báo cáo 4b.2b](docs/Step4b2b_RoomState.md).

V5 thêm nền tảng Customers/Reservations/RoomSessions, giữ dữ liệu cũ; chưa có UI/service đặt hoặc nhận phòng. Không tạo dữ liệu demo trong database đang dùng. Phần danh mục khách hàng 4d.1 ở dưới; tiếp theo các luồng booking/session theo plan. Guest hiện vẫn chỉ xem loại phòng; danh sách phòng và các luồng Guest giữ trong phạm vi.

## Bước 4c — danh mục dịch vụ

1. Đăng nhập Admin/Manager hoặc tài khoản được cấp `Service.Manage`, bấm **Dịch vụ**.
2. Bấm **Thêm dịch vụ**, nhập tên, chọn **Đồ uống / Đồ ăn / Khác**, nhập giá nguyên đồng dương như `15000`, mô tả tùy chọn, rồi **Lưu**.
3. Chọn một dòng để sửa. Bỏ chọn **Đang bán** rồi lưu để ngừng bán; chọn lại để bán tiếp. Dịch vụ vẫn nằm trong danh mục, không xóa vật lý.
4. **Bỏ thay đổi** trả form về dữ liệu của dòng đang chọn; với form thêm mới, xóa nội dung chưa lưu. **Làm mới** tải lại danh sách và trở về form thêm.

Schema v6 bổ sung Services trong transaction, giữ dữ liệu cũ và không seed món demo. Quyền hiện hành, validation, kiểm tra form cũ, cập nhật và nhật ký cùng transaction ghi. Form cũ bị chặn nếu có người sửa trước; làm mới rồi chọn lại. Staff mặc định chưa có quyền này, Guest không mở danh mục quản trị.

Gọi món chưa triển khai. Khi làm OrderItem phải dùng snapshot tên/giá, chỉ nhận dịch vụ đang bán cho món mới và giữ món cũ theo mục 23 của plan tham chiếu. Xem [báo cáo 4c](docs/Step4c_Services.md).

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Services.ps1
```

## Bước 4d.1 — khách hàng và chuẩn hóa SĐT

1. Đăng nhập nhân viên có `Customer.View`, bấm **Khách hàng**. Staff/Manager/Admin mặc định có quyền xem/thêm/sửa.
2. Nhập một phần tên hoặc SĐT đầy đủ rồi bấm **Tìm**; nhập cả hai để lọc đồng thời. Để trống hai ô để xem tất cả. Tìm tên không phân biệt hoa/thường, giữ dấu tiếng Việt.
3. **Thêm khách hàng**: nhập họ tên, SĐT rồi **Lưu**. Chọn dòng để sửa; **Bỏ thay đổi** không lưu.
4. SĐT được bỏ khoảng trắng/chấm/gạch ngang, đổi `+84`/`84` thành `0` rồi kiểm tra đúng 10 chữ số đầu 0. Ví dụ `+84 912.345-678` lưu thành `0912345678`.

SĐT unique: tạo/sửa sang số đã thuộc khách khác bị chặn, không tự ghi đè tên khách cũ. Đổi số giữ CustomerId và các liên kết lịch sử; các lần tìm sau dùng số mới. Không xóa khách hàng. Quyền thêm/sửa tách riêng và cần quyền xem để dùng màn hình; tài khoản chỉ xem có form bị khóa. Khi lưu xong, bộ lọc được xóa để thấy dòng vừa sửa.

Schema giữ **v6**, dùng bảng Customers có từ v5. Hàm chuẩn hóa dùng chung đã sẵn sàng cho các luồng sau. **Đây là tìm khách hàng nội bộ, chưa phải Guest tra cứu phòng đã đặt.** Trang lịch sử Reservation/Session/Invoice, số lần hoàn tất/lần sử dụng gần nhất còn cần hoàn thiện khi có luồng dữ liệu tương ứng; không bỏ khỏi scope. Xem [báo cáo 4d.1](docs/Step4d1_Customers.md).

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Customers.ps1
```

## Bước 5a — nền tảng giờ và phòng trống

Đã có `BookingHours` và `AvailabilityService` dùng chung cho bước đặt phòng kế tiếp. Phần này chưa thêm nút/form đặt phòng và chưa ghi Reservation qua ứng dụng.

- Tính ngày theo giờ Việt Nam UTC+7; nhận hôm nay đến +30 ngày, không đặt trong quá khứ.
- Bắt đầu ở phút 00/30, không giây lẻ; thời lượng ban đầu 60/90/120/180 phút.
- Nằm hoàn toàn trong ca 09:00–12:00 hoặc 13:00–23:00; được kết thúc đúng cuối ca.
- Kiểm tra overlap cả phòng và khách; đặt sát nhau được. Confirmed còn hiệu lực và phiên Active nguồn booking giữ khoảng lịch; walk-in không chặn mọi booking tương lai. Đặt bắt đầu đúng now cần không có phiên Active ở phòng/khách.

Kết quả kiểm tra chỉ là xem trước. Service đặt phòng tương lai phải kiểm tra quyền/khách rồi chạy lại cùng transaction ghi với INSERT và nhật ký. Chưa có worker ghi NoShow, nhưng Confirmed hết đúng 15 phút đã không giữ lịch. Xem [báo cáo 5a](docs/Step5a_Availability.md).

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Availability.ps1
```

## Bước 5b.1 — lưu booking và NoShow

Đã có service tạo đặt phòng cho Guest/Staff; chưa thêm form đặt phòng nên hiện chưa dùng thao tác này từ giao diện. Staff cần quyền `Reservation.Create`; đường Guest riêng không cần tài khoản. Cả hai dùng cùng quy tắc giờ/phòng/khách/overlap và tạo **Confirmed ngay**, không thu cọc.

SĐT chuẩn hóa tìm Customer cũ, giữ nguyên tên đã lưu; chưa có thì tạo Customer cùng booking và nhật ký trong transaction. Nếu lỗi/trùng lịch, hoàn tác cả khách mới. Hai lượt đồng thời đặt cùng phòng hoặc cùng khách ở hai phòng khác nhau chỉ một lượt thành công. Staff đặt hộ không cần mở màn hình quản trị khách để tìm-tạo khách của booking.

`NoShowService` chuyển Confirmed đã hết hạn 15 phút thành NoShow và ghi log System; không chuyển booking đã có session/CheckedIn. WPF chạy tác vụ khi khởi động và mỗi phút, xử lý ở luồng nền, dừng timer khi thoát. Khi app đóng không có worker; lần mở sau xử lý tồn đọng. Tạo booking cũng xử lý NoShow trong transaction. Lịch trống vẫn loại booking hết hạn ngay cả khi worker chậm; không cam kết chuyển status đúng từng giây.

Build Debug/Release và chín bộ kiểm tra đạt, tất cả bằng dữ liệu tạm. Xem [báo cáo 5b.1](docs/Step5b1_Reservations.md). Tiếp theo form chọn phòng/ngày/giờ, rồi lookup/hủy và calendar.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Reservations.ps1
```

## Bước 5b.2 — Khách đặt phòng từ giao diện

1. Ở **chế độ Khách**, bấm **Đặt phòng**. Nếu chưa có phòng, đăng nhập Admin → Phòng để thêm phòng có ảnh, rồi đăng xuất về Khách. Không tự seed phòng demo.
2. Chọn phòng đang mở, xem ảnh/loại/sức chứa/giá/tiện ích/mô tả. Danh sách này là phòng nhận đặt; phòng đang có khách vẫn có thể nhận lịch tương lai theo plan.
3. Chọn ngày (hôm nay đến +30), giờ, thời lượng 60/90/120/180 phút, nhập tên và SĐT.
4. Bấm **Kiểm tra giờ** để xem khoảng đã chọn có đặt được không. Đây chỉ là xem trước, chưa giữ chỗ.
5. Bấm **Xác nhận đặt**: service kiểm tra lại và lưu Confirmed. Thành công hiện mã booking, phòng, giờ bắt đầu/kết thúc và hạn đến nhận phòng trước StartTime+15 phút. Không thu cọc.
6. Sau thành công form/nút gửi được khóa. **Đặt lượt mới** xóa tên/SĐT để nhập lượt khác. Đóng khi chưa xác nhận không lưu; đóng sau thành công giữ booking đã lưu.

SĐT 0/+84/84 dùng chung chuẩn hóa; số cũ giữ tên khách cũ. Nếu vừa xem trống nhưng có người đặt trước, lần xác nhận báo trùng và không ghi thêm khách/booking. Sửa lựa chọn hoặc thông tin sẽ bỏ kết quả preview cũ. Khi bận không đóng/gửi lặp; schema giữ v6 và các transaction của 5b.1.

Guest tra cứu/hủy sau khi đóng form đã có ở bước 5c.1 bên dưới. UI Staff đặt hộ đã có ở 5c.2b; calendar ngày/tuần là phần kế tiếp. Chi tiết [báo cáo 5b.2](docs/Step5b2_GuestBooking.md).

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-GuestBooking.ps1
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1
```

## Bước 5c.1 — Khách tra cứu và hủy booking

1. Ở **chế độ Khách**, bấm **Tra cứu SĐT**, nhập SĐT đầy đủ rồi **Tra cứu**. Số dạng `0`, `+84`, `84` dùng cùng quy tắc chuẩn hóa với đặt phòng.
2. Danh sách chỉ có booking **Confirmed còn hiệu lực** của SĐT hiện hành, gồm mã booking/phòng và giờ bắt đầu/kết thúc theo UTC+7. Không cần tài khoản/OTP/mã truy cập.
3. Chọn dòng để xem điều kiện hủy. Trước hoặc **đúng mốc 2 giờ** đến giờ đặt, bấm **Hủy booking đã chọn** → **Đồng ý hủy**. **Giữ booking** hoặc đóng cửa sổ chưa xác nhận không ghi thay đổi.
4. Còn dưới 2 giờ, màn hình hướng dẫn liên hệ cửa hàng. Dù kết quả tra cứu cũ còn cho phép hủy, lần xác nhận vẫn kiểm tra lại SĐT/trạng thái/giờ trong transaction ghi.

Hủy chuyển trạng thái thành Cancelled, ghi lý do `Customer cancelled online` và nhật ký Guest cùng transaction; không xóa booking hoặc sửa lịch/phòng. Booking đã hủy không giữ chỗ nữa. Khi đổi SĐT khách ở màn hình nội bộ, các lần tra cứu/hủy dùng số mới; sửa ô SĐT trên màn hình Guest xóa ngay kết quả/selection cũ. Lookup xử lý NoShow tồn đọng và loại booking hết đúng +15 phút.

Schema giữ v6. Debug/Release và 11 bộ kiểm tra đạt trên dữ liệu tạm tại bước 5c.1; kiểm tra UI render 25 ảnh. Chi tiết [báo cáo 5c.1](docs/Step5c1_GuestLookup.md). Phần tra cứu phiên Active/tiền tạm tính/gia hạn/gọi món sẽ bổ sung ở bước vận hành khi có các service tương ứng; Guest Lookup chưa hoàn tất toàn bộ mục 22. Danh sách/chi tiết/hủy nội bộ và UI đặt hộ đã có ở bước 5c.2a/5c.2b bên dưới; calendar ngày/tuần còn làm tiếp.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-GuestLookup.ps1
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1
```

## Bước 5c.2a — nhân viên xem và hủy booking

1. Đăng nhập tài khoản có `Reservation.View`, bấm **Booking**. Guest không có nút này; Staff/Manager/Admin mặc định xem được.
2. Chọn khoảng ngày (mặc định hôm nay..+30), SĐT đầy đủ nếu cần, trạng thái rồi **Tải danh sách**. Bỏ trống hai ngày để xem toàn bộ lịch sử booking. SĐT dùng chuẩn hóa 0/+84/84 chung; cả ngày đến được tính theo giờ Việt Nam.
3. Chọn dòng để xem tên/SĐT khách, phòng, giờ bắt đầu/kết thúc, hạn nhận, người đặt/lúc tạo và lý do hủy nếu có. Cancelled/NoShow/CheckedIn/Completed vẫn xem được nội bộ.
4. Có `Reservation.Cancel` và booking Confirmed còn hạn, chưa có phiên nguồn: **Hủy booking đã chọn** → nhập lý do → **Xác nhận hủy**. **Giữ booking**/đóng trước xác nhận không lưu. Staff được hủy trong vòng 2 giờ và trong grace trước khi nhận phòng; đúng StartTime+15 phút đã hết hạn.

Hủy đọc lại quyền/trạng thái/giờ/nguồn session trong transaction với lý do và nhật ký Staff; không xóa booking hoặc sửa lịch/phòng. Mất quyền sau khi mở form bị chặn và xóa dữ liệu chi tiết cũ. Tài khoản chỉ có quyền xem vẫn lọc/xem được, không hủy. Nếu hủy đã lưu nhưng tải lại lỗi, màn hình vẫn báo đã hủy.

Schema giữ v6. Build Debug/Release và 12 bộ kiểm tra đạt tại bước 5c.2a trên dữ liệu tạm, UI render 30 ảnh. Chi tiết [báo cáo 5c.2a](docs/Step5c2a_StaffReservations.md). Form đặt hộ đã có ở 5c.2b bên dưới; calendar ngày/tuần còn làm tiếp. Check-in/walk-in và phần phiên chưa có, không coi chi tiết booking là hoàn tất vận hành.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-StaffReservations.ps1
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1
```

## Bước 5c.2b — nhân viên đặt hộ từ giao diện

1. Đăng nhập tài khoản có `Reservation.Create`, bấm **Đặt hộ** ở màn hình chính. Nếu có thêm `Reservation.View`, cũng mở được **Booking → Đặt hộ**. Tài khoản chỉ có quyền xem không được đặt hộ; quyền tạo độc lập với quyền xem và Customer CRUD.
2. Chọn phòng đang mở, ngày/giờ/thời lượng, nhập tên/SĐT như form Khách. **Kiểm tra giờ** chỉ xem trước, chưa giữ chỗ; giờ/ca/slot/overlap giữ quy tắc đã chốt.
3. **Xác nhận đặt** gọi CreateStaff và đọc lại quyền/phòng/khách/lịch trong transaction. Booking Confirmed lưu đúng tài khoản người đặt và nhật ký Staff; SĐT cũ giữ tên khách đã lưu, khách mới được tạo cùng booking. Có lỗi thì rollback cả khách mới/booking/nhật ký.
4. Thành công hiện mã/giờ/hạn nhận, khóa gửi lặp. **Đặt lượt mới** xóa tên/SĐT; đóng trước xác nhận không lưu. Mất quyền sau khi mở form bị chặn, xóa dữ liệu form, vẫn đóng được; không chuyển sang đường Guest.
5. Mở từ Booking rồi lưu/đóng sẽ tải lại, bỏ bộ lọc cũ và chọn booking vừa tạo. Lỗi tải lại sau commit vẫn báo đã đặt hộ để tránh gửi lại booking.

Schema giữ v6. Debug/Release và 13 bộ kiểm tra đạt trên dữ liệu tạm, UI render 36 ảnh. Chi tiết [báo cáo 5c.2b](docs/Step5c2b_StaffBooking.md). Tiếp theo 5d.1 nền tảng lịch/trạng thái phòng, rồi giao diện Ngày và Tuần. Ảnh AI vẫn chờ người dùng chọn; chưa thay ảnh phòng đang dùng.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-StaffBooking.ps1
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1
```

## Bước 5d.1 — nền tảng lịch và trạng thái phòng

Đã có CalendarService cho lịch nội bộ, cần quyền `Calendar.View` hiện hành; chưa thêm nút/màn hình lịch. CalendarRange.Day lấy ngày Việt Nam 00:00 đến trước 00:00 hôm sau; Week lấy thứ Hai đến trước thứ Hai kế tiếp. Đọc tất cả phòng hoặc lọc một Room, có cả phòng khóa để xem lịch sử.

- Trạng thái hiện tại ưu tiên Inactive → Occupied → Reserved → Available. Chỉ Confirmed đang tới lượt và còn grace mới làm Reserved; booking ngày mai không đổi trạng thái hôm nay.
- Khoảng giữ lịch dùng chung quy tắc với AvailabilityService. Phiên nguồn booking chỉ giữ tới ExpectedEndTime, CheckedIn không vẽ thêm booking cũ. Quá giờ cảnh báo nhưng không tự nới hold.
- Dữ liệu thực tế giữ nguyên giây/phút: Active đến now, Completed đến ActualEndTime, không làm tròn theo ô 30 phút. Walk-in không giữ lịch tương lai; hạn trả phòng tính động theo ca và booking kế tiếp của phòng/khách.
- Lịch là kết quả đọc tại một mốc thời gian, không giữ chỗ hoặc tự cập nhật NoShow. Mọi thao tác ghi vẫn kiểm tra lại trong transaction. Đây là dữ liệu nội bộ có thông tin khách, chưa phải đường lịch Guest.

Schema giữ v6. Debug/Release và 14 bộ kiểm tra đạt trên database tạm; kiểm tra WPF cũ vẫn render 36 ảnh. Fixture session kiểm tra cách đọc lịch, chưa có service/UI check-in hoặc walk-in. Chi tiết [báo cáo 5d.1](docs/Step5d1_CalendarFoundation.md). Tiếp theo 5d.2 giao diện lịch **Ngày**, rồi **Tuần** và lịch Guest chỉ trống/bận, giữ đủ phạm vi plan.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Calendar.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Availability.ps1
```

## Bước 5d.2 — lịch Ngày từ giao diện

1. Đăng nhập tài khoản có `Calendar.View` → **Lịch phòng** (mặc định Ngày). Guest không có nút nội bộ này; không cần thêm quyền xem booking hoặc quản lý phòng/khách.
2. Chọn ngày và phòng (hoặc tất cả), bấm **Tải lịch**. **Ngày trước / Hôm nay / Ngày sau** đổi ngày và tải lại. Chưa có phòng thì màn hình hướng dẫn thêm ở danh mục Phòng.
3. Lưới giờ Việt Nam 09:00–23:00, ô 30 phút, đánh dấu nghỉ 12:00–13:00. Mỗi cột có mã/tên phòng và trạng thái **hiện tại**, còn khối lịch thuộc ngày đã chọn. Cuộn dọc xem giờ muộn, cuộn ngang xem thêm phòng.
4. Chọn khối để xem mã booking/phiên, tên/SĐT, giờ thực tế/dự kiến/hạn trả và cảnh báo. Dự kiến dùng nền sáng/viền nét đứt; Completed màu xám; quá giờ viền nâu. Walk-in chưa chốt kết thúc, chỉ vẽ tới lúc tải và có hạn trả động. Các lượt chồng do phiên trước quá giờ được vẽ cạnh nhau.

Giờ thực tế như 10:37 giữ nguyên vị trí, không làm tròn slot. Phần vẽ cắt theo khung 09–23, chi tiết giữ giờ gốc. Lịch chỉ đọc, không kéo-thả sửa booking và không giữ chỗ. Tải lại để cập nhật; không có timer tự tải ở màn hình này. Đổi ngày/phòng xóa lịch/chi tiết cũ; bị thu hồi quyền khi tải lại sẽ xóa cả dữ liệu/canvas, vẫn cho đóng.

Schema giữ v6. Debug/Release và 14 bộ kiểm tra đạt trên dữ liệu tạm; UI render 42 ảnh. Xem [báo cáo 5d.2](docs/Step5d2_CalendarDay.md). Tiếp theo 5d.3 UI lịch **Tuần**, rồi lịch Guest một phòng chỉ trống/bận. Session/walk-in trong bài kiểm tra là fixture; service nhận phòng/vận hành vẫn chưa triển khai.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Calendar.ps1
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1
```

## Bước 5d.3 — lịch Tuần

1. Đăng nhập → **Lịch phòng**, chọn **Tuần**. Chuyển chế độ tự tải lại, giữ ngày/phòng đang chọn; mở cửa sổ mới vẫn mặc định Ngày.
2. Chọn bất kỳ ngày nào trong tuần muốn xem rồi **Tải lịch**. Tuần bắt đầu thứ Hai, kết thúc Chủ nhật theo giờ Việt Nam; status ghi rõ ngày đầu/cuối. **Tuần trước/Tuần sau** dịch 7 ngày; **Hôm nay** giữ chế độ đang dùng.
3. Có đủ 7 cột ngày, cùng trục 09–23/ô 30 phút/nghỉ 12–13. Cuộn ngang để xem các ngày cuối, lọc một phòng nếu nhiều lượt chồng nhau. Mỗi lượt có mã phòng, chọn/tooltip xem đầy đủ thông tin.
4. Phiên giao qua nửa đêm vẽ phần giao với từng ngày trong tuần; phần trước/sau khung không bị đổi giờ gốc. Chi tiết vẫn có ngày và giờ thực tế tới giây. Trạng thái phòng hiện tại hiện riêng ở bên phải, không tô kín lịch tuần.

Tuần dùng cùng CalendarService/quyền/NoShow-grace/giữ lịch với Ngày, chỉ đọc và tải lại thủ công. Không kéo-thả/sửa giờ hoặc giữ chỗ từ hình lịch. Schema v6; Debug/Release và 14 bộ kiểm tra đạt tại bước 5d.3 trên dữ liệu tạm, UI render 46 ảnh. Xem [báo cáo 5d.3](docs/Step5d3_CalendarWeek.md). Lịch Guest đã có ở 5d.4 bên dưới; service/UI vận hành vẫn chưa triển khai.

## Bước 5d.4 — Khách xem trống/bận theo ngày

1. Chế độ Khách → **Đặt phòng**, chọn phòng/ngày/thời lượng → **Xem trống/bận theo ngày**. Chưa cần nhập tên/SĐT. Form nhân viên đặt hộ cũng dùng được nút này theo quyền `Reservation.Create`.
2. Danh sách có 28 giờ bắt đầu cách nhau 30 phút từ 09:00 đến 22:30. Ô **Có thể đặt** màu xanh nhạt; ô **Bận**, **Đã qua**, **Giờ nghỉ**, **Không đủ ca** không được chọn. Trạng thái tính cho cả khoảng theo thời lượng đang chọn, không chỉ 30 phút của ô.
3. Chọn dòng trống → **Chọn giờ này** chỉ điền giờ vào form. Đóng hoặc xem/tải lại lịch không tạo booking/khách. Đổi ngày/phòng/thời lượng ở form và mở lại để xem lựa chọn khác.
4. **Tải lại lịch** lấy snapshot mới; phòng bị khóa/lỗi sẽ xóa các ô cũ. Màn hình không hiển thị tên/SĐT, mã booking/phiên hoặc thông tin khách khác.

Lịch kiểm tra **phòng**, chưa kiểm tra lịch riêng của bạn vì chưa có SĐT. Nhập tên/SĐT rồi Kiểm tra giờ/Xác nhận đặt như trước; submit vẫn kiểm tra cả phòng và khách trong transaction. Lịch không giữ chỗ, lịch trống có thể đổi khi người khác đặt trước. Walk-in/quá giờ không tự tô bận tương lai; bắt đầu đúng now vẫn bị chặn nếu phòng có Active.

Schema v6; Debug/Release và 15 bộ kiểm tra đạt trên dữ liệu tạm, UI render 50 ảnh. Chi tiết [báo cáo 5d.4](docs/Step5d4_GuestCalendar.md). Tiếp theo 6a.1 nền tảng **check-in từ booking** theo quyền Session.CheckIn; chưa có luồng nhận phòng thực tế ở bước này.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-GuestCalendar.ps1
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1
```

## Bước 6a.1 — service nhận phòng từ booking

Có RoomSessionService.CheckIn theo quyền Session.CheckIn hiện hành. Nhận sớm khi đủ khoảng trống, giờ thực tế giữ nguyên giây/ticks; dự kiến trả sau đầy đủ thời lượng booking. Kiểm tra grace/ca/Room và Customer/overlap bỏ nguồn trong transaction, chốt giá và loại phòng lúc nhận; session, CheckedIn và nhật ký Staff cùng commit. Nhận lặp trả phiên đã lưu, không tạo phiên hoặc log thứ hai.

Schema v6, Debug/Release và 16 bộ kiểm tra đạt trên database tạm, gồm NoShow/check-in cả hai thứ tự writer, hủy/khóa phòng, snapshot và rollback. UI cũ render 50 ảnh. **Chưa có nút nhận phòng trên UI**; tiếp theo 6a.2 thêm thao tác ở chi tiết booking. Chi tiết [báo cáo 6a.1](docs/Step6a1_CheckIn.md).

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-CheckIn.ps1
```

## Bước 6a.2 — UI nhận phòng

Đăng nhập → Booking → chọn Confirmed còn hạn → Nhận phòng → xem giờ/giá tham khảo → Xác nhận nhận phòng. Cần Reservation.View để mở danh sách và Session.CheckIn để nhận; không cần quyền đặt/hủy hoặc Customer CRUD. Để sau chưa lưu. Service kiểm tra lại và chốt giờ thực tế/giá; kết quả hiện mã phiên, phòng/loại, giờ nhận/dự kiến trả và giá chốt. Gửi lặp/mất quyền/booking bị hủy đều được xử lý; lỗi tải lại sau commit vẫn báo đã nhận. Chi tiết [báo cáo 6a.2](docs/Step6a2_CheckInUi.md).

Debug/Release build vào bin/VerifyDebug và bin/VerifyRelease vì bản Debug đang chạy trong Visual Studio. 7 bộ kiểm tra liên quan đạt trên dữ liệu tạm, UI 55 ảnh. Bản đang chạy chưa tự cập nhật; dừng phiên chạy rồi build/F5 lại để dùng thay đổi. Tiếp theo 6b.1 walk-in.

Quản trị tài khoản và ma trận quyền có trong bước 8: Admin tạo/sửa/khóa, gán Staff/Manager/Admin và chỉnh quyền theo role. Người dùng đã chốt giữ RBAC theo role; dashboard hoạt động/doanh thu quán sẽ thay phần bảng/nút kiểm tra quyền tạm ở màn hình chính, không cần module khoản chi điện/thuê mặt bằng.

## Bước 6b.1 — service nhận khách trực tiếp

RoomSessionService.CreateWalkIn theo Session.WalkIn nhận phòng/tên/SĐT, không chọn thời lượng hoặc tạo booking. Kiểm tra ca/phòng mở/Room và Customer không Active/booking đã tới giờ còn grace; lưu session Active, giờ thực tế/snapshot giá-phòng/customer/audit trong transaction. Giờ cần trả phòng tính động theo ca và booking kế tiếp của Room/Customer, không lưu thành ExpectedEndTime hoặc chặn booking tương lai vô hạn. ReadWalkIn theo Session.View chỉ đọc và cập nhật cảnh báo theo lịch hiện tại.

Schema v6, Debug/Release và 11 bộ kiểm tra liên quan đạt trên dữ liệu tạm, UI cũ 55 ảnh. Chưa có nút/form walk-in; tiếp theo 6b.2 UI nhận khách trực tiếp. Chi tiết [báo cáo 6b.1](docs/Step6b1_WalkIn.md).
