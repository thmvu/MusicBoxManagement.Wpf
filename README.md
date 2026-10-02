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

Đây là bước nền tảng, chưa phải ứng dụng quản lý hoàn chỉnh. Chưa triển khai đăng nhập, phòng, đặt phòng, phiên sử dụng, gọi món, hóa đơn, calendar và báo cáo.

## Cấu trúc để học

| Thư mục | Vai trò |
| --- | --- |
| Models | Dữ liệu một loại phòng |
| Data | Mở kết nối, tạo bảng và dữ liệu ban đầu SQLite |
| Services | Đọc danh mục từ database |
| ViewModels | Dữ liệu và trạng thái được bind lên màn hình |
| Views | Giao diện XAML và sự kiện tải/làm mới |

WPF dùng binding theo hướng MVVM đơn giản. Hai sự kiện giao diện gọi ViewModel; SQL nằm ở Data/Services, không nằm trong code của cửa sổ.

## Kiểm tra nền tảng

Sau khi build, chạy Windows PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Foundation.ps1
```

Bài kiểm tra tạo file SQLite tạm riêng, kiểm tra tiếng Việt, lưu thay đổi qua lần mở lại, seed không ghi đè dữ liệu, giá nguyên đồng và schema version. Nó tự xóa file tạm đã tạo và không chạm database đang dùng.

## Kế hoạch tiếp theo

Đọc `MusicBoxManagement_Wpf_ProjectPlan_v0.1.md`. Người dùng đã chọn giữ chế độ Khách tại máy demo/quầy. Nghiệp vụ lấy từ plan web v1.3; đăng nhập và cơ chế đồng thời phải đối chiếu trước khi triển khai trên SQLite.
