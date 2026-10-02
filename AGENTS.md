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
- Bước 2 đã chốt kỹ thuật tại `docs/Wpf_Authentication_Transactions.md`: Identity Core + kho SQLite, quyền hiện hành và phiên trong bộ nhớ. Có BeginWriteTransaction; đã build Debug/Release và kiểm tra commit/rollback/hai kết nối tranh quyền ghi trên database tạm. Schema vẫn v1.
- Bước 3 đã có schema v2 (nâng cấp giữ dữ liệu v1), Identity/UserManager + kho SQLite, thiết lập Admin đầu tiên, đăng nhập/đăng xuất, PermissionService đọc quyền hiện hành và UI Khách/nhân viên. Đã build Debug/Release và kiểm tra nền tảng, xác thực/quyền, rollback/bootstrap đồng thời, UI WPF trên database tạm. Chi tiết `docs/Step3_Authentication.md`.
- UI quản lý user/reset mật khẩu/đổi role/ma trận quyền/audit đầy đủ vẫn ở bước 8; kho Identity hiện chỉ lưu tài khoản mới cho bootstrap, không cho cập nhật tài khoản bỏ qua service quản trị.
- Chưa có quản lý phòng, đặt phòng, phiên sử dụng, gọi món, checkout/hóa đơn và báo cáo. Guest hiện chỉ có danh mục, chưa đủ các luồng nghiệp vụ Guest.
- Database nằm ở `%LOCALAPPDATA%\MusicBoxManagement.Wpf\musicbox.db`; không ghi đè/xóa database đang dùng khi kiểm tra.

## Làm tiếp

Đọc README, plan, `docs/Wpf_Authentication_Transactions.md` và `docs/Step3_Authentication.md` trước. Bước tiếp theo là bước 4 — danh mục (RoomType, phòng/ảnh/khóa mở, dịch vụ, khách hàng), từng phần nhỏ sau đối chiếu plan gốc. Tái sử dụng PermissionService trong service và UI; thao tác ghi kiểm tra quyền/dữ liệu bằng cùng kết nối/transaction. Không giả định phần còn lại của ứng dụng desktop đã hoàn thành.

## Kiểm tra và Git

- Build solution `MusicBoxManagement.Wpf.sln` bằng MSBuild của Visual Studio có workload .NET desktop development.
- Sau thay đổi nền tảng dữ liệu, chạy Windows PowerShell: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Foundation.ps1`.
- Khi sửa xác thực/quyền, chạy thêm `Tests/Verify-Authentication.ps1`; khi sửa UI đăng nhập, chạy `powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1`.
- Commit tiếng Việt. Đây là Git repository độc lập, chưa có remote; không dùng remote của project web.
