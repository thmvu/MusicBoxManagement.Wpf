# Music Box Management — WPF

Ứng dụng quản lý phòng Music Box bằng **WPF .NET Framework 4.7.2 + MySQL/Aiven**. Khách sử dụng tại quầy/demo; nhân viên đăng nhập và được phân quyền theo vai trò Admin/Manager/Staff.

## Chạy project

1. Mở `MusicBoxManagement.Wpf.sln` trong Visual Studio có workload **.NET desktop development** và **.NET Framework 4.7.2 Developer Pack**.
2. Restore NuGet packages, chọn `MusicBoxManagement.Wpf` làm Startup Project.
3. Cấu hình biến môi trường **User** của Windows `MUSICBOX_AIVEN_PASSWORD` bằng mật khẩu Aiven. Không ghi mật khẩu vào source hoặc gửi kèm ZIP.
4. Đặt CA Certificate của Aiven tại `D:\MusicBoxSecrets\ca.pem`. Ứng dụng kiểm tra TLS bằng `VerifyFull`.
5. Đóng/mở lại Visual Studio nếu vừa đổi biến môi trường, rồi Build và nhấn F5.

`App.config` đang mặc định `DatabaseProvider=MySql`, `MySqlDatabase=musicbox`. Host/port/user và đường dẫn CA hiện được cấu hình trong `MusicBoxManagement.Wpf/Data/AivenDatabase.cs`; máy nhận project cần cấu hình kết nối phù hợp. Chỉ chọn **Thiết lập Admin** khi database chưa có tài khoản; tài khoản của quán không được reset bằng dữ liệu test.

Ảnh phòng lưu local, database cloud không tự đồng bộ ảnh. Khi chuyển máy cần sao chép Content trong thư mục dữ liệu tương ứng; xem [hướng dẫn demo/vận hành](docs/MySql_Demo_And_Operations.md).

## Chức năng

- Khách xem phòng/ảnh/lịch, đặt phòng, tra cứu theo SĐT, hủy theo điều kiện, xem phiên/gia hạn, gọi món và xem tiền tạm tính.
- Nhân viên đặt hộ, nhận phòng từ booking hoặc walk-in, quản lý phiên/món, trả phòng và hóa đơn.
- Lịch ngày/tuần, dashboard phòng/cảnh báo, doanh thu và báo cáo/Excel theo quyền.
- Admin quản lý tài khoản, vai trò/quyền và nhật ký thao tác.

Giờ phục vụ: **09:00–12:00 và 13:00–23:00**. Booking và khoảng sử dụng dự kiến phải nằm trong một ca. Nhận phòng giữ đủ thời lượng; lượt kết thúc đúng 12:00/23:00 có nhắc đến sớm.

## Tài liệu nên đọc

Bắt đầu tại [mục lục docs](docs/README.md), gồm:

- [Plan WPF và tiến độ](MusicBoxManagement_Wpf_ProjectPlan_v0.1.md), [nghiệp vụ tham chiếu](docs/Reference_Web_ProjectPlan_v1.3.md).
- [Use case/luồng hoạt động](docs/UseCases_And_Workflow_Review.md).
- [ERD đầy đủ và từ điển dữ liệu](docs/ERD_Full_And_DataDictionary.md), [ảnh ERD](docs/schema/MusicBox_Full.png).
- [Kiến trúc MVC/MVVM/Services](docs/Architecture_MVC_Services_Explanation.md), [luồng code để học](docs/Code_Flow_Full_Guide.md). Hai tài liệu học có ghi rõ mốc phiên bản; wiring MySQL hiện hành được dẫn trong mục lục.
- [Báo cáo kiểm thử 05/11/2026](docs/Flow_Verification_20261105.md), [gallery 117 ảnh](docs/Flow_Test_Gallery_20261105.html).

## Kiểm tra MySQL

Lượt tổng kiểm tra 05/11: **34/34 nhóm Release đạt**, thêm Hours UI Debug18 popup và Feedback UI Debug36 popup đạt. Đây là số nhóm, không phải mọi tổ hợp đầu vào/race; xem phạm vi và các lượt lỗi bộ chạy trong báo cáo. Máy in giả chưa thay nghiệm thu in giấy thật.

Chạy suite được chọn trên database GUID thử riêng, cần quyền tạo/dọn database test ở Aiven:

```powershell
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File Tests/Verify-MySqlFlows.ps1 -Suite BookingHoursUi -BuildDirectory MusicBoxManagement.Wpf/bin/Debug
```

Không chạy runner tổng hoặc runner pha provider. Không dùng fixture để ghi đè dữ liệu quán.

## Cấu trúc và đóng gói

| Thành phần | Nội dung |
|---|---|
| `MusicBoxManagement.Wpf/` | Source WPF, Models, Views/ViewModels, Services, Data và project `.csproj` |
| `assets/` | Ảnh phòng minh họa để nhập qua form quản lý phòng |
| `Tests/` | Các bài kiểm tra hiện có; chỉ sử dụng các suite MySQL được phép |
| `Tools/` | Công cụ quản trị hiện có; chọn đúng công cụ, không tự chạy import/seed lại |
| `docs/` | Tài liệu học, ERD, nghiệp vụ, vận hành và bằng chứng kiểm thử |

Nén **folder ngoài** chứa `.sln`, project con, assets và docs. Có thể bỏ `.git`, `.vs`, `bin`, `obj` khỏi bản ZIP; giữ bản Git làm việc và không đóng gói mật khẩu thật.

Ngày 06/11/2026 đã dọn 92 tài liệu từng bước/nháp/lịch sử đã được thay thế, đồng thời giữ báo cáo và ảnh kiểm thử. Tài liệu đã gỡ vẫn có trong [snapshot Git trước khi dọn](https://github.com/thmvu/MusicBoxManagement.Wpf/tree/72a0fa70cdec42769f00a2bd9d5826de4f2adb53/docs), có thể lấy lại từ lịch sử Git.
