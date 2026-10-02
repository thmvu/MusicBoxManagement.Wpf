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

Ứng dụng đã có nền tảng và đăng nhập ở bước 3, chưa phải ứng dụng quản lý hoàn chỉnh. Chưa triển khai phòng, đặt phòng, phiên sử dụng, gọi món, hóa đơn, calendar và báo cáo.

## Cấu trúc để học

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

Bài kiểm tra UI chạy các cửa sổ WPF với dữ liệu thử riêng, lưu năm ảnh render trong thư mục tạm và in đường dẫn. Các bài kiểm tra không mở/xóa database đang dùng.

## Kế hoạch tiếp theo

Tiếp theo là bước 4 — danh mục: đối chiếu plan web v1.3 rồi làm RoomType, phòng/ảnh/khóa mở, dịch vụ và khách hàng từng phần nhỏ. Các service nội bộ cần Permission tương ứng, kiểm tra lại trong transaction khi ghi. Giữ chế độ Khách tại máy demo/quầy và toàn bộ phạm vi đã chốt.
