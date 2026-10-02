# Music Box WPF

## Yêu cầu đã chốt với người dùng

- Đây là project riêng: WPF trên .NET Framework 4.7.2 và SQLite.
- Giữ chế độ Khách tại máy demo/quầy. Khu vực nhân viên sẽ cần đăng nhập.
- Đồ án nhỏ, làm từng bước dễ hiểu; báo cáo bằng tiếng Việt về phần đã làm, cách hoạt động và kiểm tra.
- Giao diện màu trung tính có tương phản, hạn chế card bo góc và màu neon.
- Trước khi thay nghiệp vụ, đối chiếu `MusicBoxManagement_Wpf_ProjectPlan_v0.1.md` và `docs/Reference_Web_ProjectPlan_v1.3.md`; không tự cắt chức năng hoặc đổi quy tắc.
- Bản tham chiếu web chỉ là nguồn nghiệp vụ. Không dùng ASP.NET MVC, SQL Server hay OWIN cookie cho nền tảng project này.

## Hiện trạng

- Bước 1 đã có: solution WPF truyền thống, SQLite schema v1/RoomTypes, Standard/VIP, màn hình đọc danh mục và nút làm mới.
- Đã build Debug/Release, kiểm tra database bằng `Tests/Verify-Foundation.ps1` và thử giao diện.
- Chưa có đăng nhập, quản lý phòng, đặt phòng, phiên sử dụng, gọi món, checkout/hóa đơn và báo cáo.
- Database nằm ở `%LOCALAPPDATA%\MusicBoxManagement.Wpf\musicbox.db`; không ghi đè/xóa database đang dùng khi kiểm tra.

## Làm tiếp

Đọc README và plan trước. Bước tiếp theo là chốt triển khai tài khoản/role/permission và transaction SQLite, rồi làm đăng nhập. Tái sử dụng quy tắc nghiệp vụ phù hợp; không giả định phần còn lại của ứng dụng desktop đã hoàn thành.

## Kiểm tra và Git

- Build solution `MusicBoxManagement.Wpf.sln` bằng MSBuild của Visual Studio có workload .NET desktop development.
- Sau thay đổi nền tảng dữ liệu, chạy Windows PowerShell: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Foundation.ps1`.
- Commit tiếng Việt. Đây là Git repository độc lập, chưa có remote; không dùng remote của project web.
