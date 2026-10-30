# Seed món/dịch vụ — 31/10/2026

Theo yêu cầu người dùng để thử gọi món, đã thêm 8 dịch vụ vào Aiven `musicbox`, tất cả `IsActive=1`. Giá dưới đây là giá demo theo phần/đơn vị, người dùng có thể sửa qua Danh mục dịch vụ.

| Tên | Nhóm | Giá (đ) |
|---|---|---:|
| Nước suối (Demo) | Đồ uống | 10.000 |
| Coca-Cola (Demo) | Đồ uống | 20.000 |
| Trà đào (Demo) | Đồ uống | 30.000 |
| Nước cam (Demo) | Đồ uống | 35.000 |
| Snack (Demo) | Đồ ăn | 20.000 |
| Khoai tây chiên (Demo) | Đồ ăn | 35.000 |
| Đĩa trái cây (Demo) | Đồ ăn | 60.000 |
| Khăn lạnh (Demo) | Khác | 5.000 |

## Script

`Tools/Seed-MySqlServices.ps1` là script quản trị chủ động, không gắn vào startup. Dùng kết nối Aiven/VerifyFull và credential hiện có, không giả LoginSession hoặc sửa tài khoản/quyền. Không cần mật khẩu Admin ứng dụng. Mặc định dry-run; chỉ `-Apply` mới ghi. Yêu cầu có Admin active; kiểm category theo MySqlServiceCatalogService.Categories, tên 1–100 ký tự, giá BIGINT nguyên dương. Chỉ INSERT dữ liệu cố định và audit System/UserId NULL/DemoSeed.Create/Service cùng Serializable transaction bằng SQL tham số hóa, không đổi luật service/DDL/provider/UI. Dispose rollback khi lỗi; không tự retry khi commit không rõ kết quả.

Services không có unique name theo nghiệp vụ hiện hành. Script bỏ qua bất kỳ tên demo trùng hiện có, không ghi đè giá/nhóm/tên/trạng thái đã sửa, không thêm ràng buộc mới. Serializable bảo vệ lượt kiểm tên/insert; writer cạnh tranh có thể bị từ chối, không coi script là cơ chế dedup đơn món của app.

```powershell
# Root repo, Windows PowerShell (.NET Framework)
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File Tools/Seed-MySqlServices.ps1
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File Tools/Seed-MySqlServices.ps1 -Apply
```

Có tham số DatabaseName/BuildDirectory; kiểm đúng đích trước Apply. Tên cột SeedPrice trong output là giá dự kiến của seed, không khẳng định giá hiện hành của bản ghi được bỏ qua. Không import lại snapshot29/10 để seed, không xóa dữ liệu vận hành.

## Kết quả kiểm tra

Trước lượt này: Users1, Rooms5, Customers7, Reservations1, Services0, AuditLog16; RoomSessions/Orders/OrderItems/Invoices0. Khách/booking mới của người dùng đã có, không lấy số liệu30/10 để ghi đè.

Dry-run cả8 WouldCreate → Apply cả8 Created (ServiceId1–8) → Apply lần hai cả8 SkippedExisting. Read-only xác nhận đúng tên/nhóm/giá/active, Services8 và8 audit System cho Service, tổng AuditLog24. Users/Rooms/Customers/Reservations và các bảng phiên/đơn/hóa đơn giữ nguyên số dòng. Không tạo booking/phiên/đơn/hóa đơn giả để thử menu. Không chạy lại build hoặc72 suites vì không đổi production C#/XAML/schema; phạm vi lượt này là seed/đối chiếu/lặp không trùng.

Log ngoài Git trong TEMP: `MusicBoxBeforeServiceSeed_20261031.log`, `MusicBoxServiceSeedDryRun_20261031.log`, `MusicBoxServiceSeedApply_20261031.log`, `MusicBoxServiceSeedRepeat_20261031.log`, `MusicBoxAfterServiceSeed_20261031.log`, `MusicBoxServiceSeedVerify_20261031.log`; tất cả lệnh hoàn tất exit0. Không lưu secret/dump/thông tin khách thật trong repo.

Trong WPF, tải lại danh mục Dịch vụ để thấy món. Cần **nhận phòng trước để có phiên Active**, rồi vào gọi món. Guest gửi đơn Pending; nhân viên xác nhận sau phục vụ; chỉ món Completed được cộng vào tiền. Catalog đang bán đã được kiểm bằng SQL; chưa gọi ReadMenuGuest/Staff trên production vì hiện chưa có phiên Active, không nới guard để đọc menu.

Xem [seed phòng/khách](MySql_Demo_Seed_20261030.md), [bàn giao](MySql_Migration_Status.md) và [demo nghiệp vụ](MySql_Demo_And_Operations.md).
