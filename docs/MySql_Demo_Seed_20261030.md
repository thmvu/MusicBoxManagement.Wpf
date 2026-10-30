# Seed phòng và khách hàng mẫu — 30/10/2026

Theo yêu cầu người dùng sau khi đã tự thiết lập Admin, đã thêm dữ liệu demo vào Aiven `musicbox`. Trước seed: 1 tài khoản Admin, 0 phòng, 0 khách, 1 nhật ký bootstrap. Không chạy lại import SQLite hoặc thay account/quyền/giá loại phòng.

| Mã phòng | Tên | Loại | Sức chứa / giá giờ hiện tại |
|---|---|---|---|
| DEMO-S01 | Phòng Mộc (Demo) | Standard | 4 khách / 120.000 đ |
| DEMO-S02 | Phòng Nắng (Demo) | Standard | 4 khách / 120.000 đ |
| DEMO-S03 | Phòng Ấm (Demo) | Standard | 4 khách / 120.000 đ |
| DEMO-V01 | Phòng Aurora (Demo) | VIP | 6 khách / 200.000 đ |
| DEMO-V02 | Phòng Hoàng Hôn (Demo) | VIP | 6 khách / 200.000 đ |

Tất cả active. Giá/sức chứa lấy từ RoomTypes hiện hành, không ghi đè khi seed. Có 6 khách `Khách Demo 01` đến `Khách Demo 06`, SĐT giả lập `0000000101`–`0000000106`; dùng cho thử nghiệm, không coi là thông tin người thật hoặc số để liên hệ. Tên có nhãn Demo để phân biệt dữ liệu vận hành sau này. Không tạo dịch vụ/booking/phiên/món/thanh toán hoặc doanh thu mẫu ngoài yêu cầu.

Ảnh dùng 5 file đã gen trong `assets/rooms`, không gen lại: oak/daylight/canon Standard và canon/evening VIP. Tái sử dụng đúng `RoomImages.ReadPng` trong build bằng reflection để kiểm byte/giới hạn 5 MB/normalize PNG; chép thành 5 file GUID tại `%LOCALAPPDATA%\MusicBoxManagement.Wpf.MySql\Content\uploads\rooms`. Ảnh vẫn local, máy khác cần nhận thư mục Content; pull Git chỉ có ảnh nguồn và script, không tự seed database khi mở app.

## Script và cách kiểm tra

`Tools/Seed-MySqlDemo.ps1` là công cụ quản trị chủ động dùng credential Aiven có trên máy, không phải route public của ứng dụng. Không tạo/giả LoginSession hoặc mượn identity Admin. Script yêu cầu database đã có Admin active; các câu SQL tham số hóa, room type và phone dùng hợp đồng hiện có, một Serializable transaction cho 11 bản ghi và 11 nhật ký **System / DemoSeed.Create / UserId NULL**. Dữ liệu mới có ID do DB cấp, CreatedAt UTC O. Không sửa service/nghiệp vụ/SSL/schema/provider.

Mặc định chỉ dry-run, không ghi dữ liệu/audit hoặc file. `-Apply` mới ghi; gặp RoomCode/SĐT đã tồn tại thì bỏ qua, không sửa tên/ảnh hoặc tạo trùng. Ảnh dùng CreateNew, rollback xác nhận mới dọn file thuộc lượt mình; nếu kết quả commit không rõ thì giữ ảnh và báo lỗi để đối chiếu, không tự retry/xóa file đang được tham chiếu. Không xóa dữ liệu để seed lại.

```powershell
# Từ root repo, dùng Windows PowerShell (.NET Framework)
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File Tools/Seed-MySqlDemo.ps1
# Chỉ khi muốn bổ sung những mã/SĐT mẫu còn thiếu:
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File Tools/Seed-MySqlDemo.ps1 -Apply
```

Có tham số DatabaseName/StorageRoot/BuildDirectory cho máy demo riêng; mặc định musicbox và storage MySql hiện hành. Cần kiểm đúng đích trước Apply. Script không chạy cùng App startup, không đưa secret/DB dump vào repo.

Đã thực hiện dry-run, Apply và Apply lần hai: lần đầu tạo 5 phòng +6 khách; lần hai cả11 `SkippedExisting`, không thêm ảnh/nhật ký. Kiểm bằng `MySqlGuestBookingService.ListRooms/GetImagePath` thật: cả5 xuất hiện trong catalog Khách và đủ5 ảnh; SQL read-only xác nhận6 khách, 11 System audit, tổng AuditLog12; tài khoản vẫn1, Reservations/RoomSessions/Services/Orders/OrderItems/Invoices vẫn0. Không cần rebuild hoặc chạy lại toàn bộ suite vì không đổi code WPF/service/schema; đây là kiểm tra seed, không nghiệm thu luồng thanh toán/in thật.

Log máy chạy tại TEMP: `MusicBoxBeforeSeed_20261030.log`, `MusicBoxSeedDryRun_20261030.log`, `MusicBoxSeedApply_20261030.log`, `MusicBoxSeedRepeat_20261030.log`, `MusicBoxAfterSeed_20261030.log`, `MusicBoxSeedVerify_20261030.log`. Lượt dry-run đầu lỗi reflection do PowerShell PSObject trước mở DB; đổi đối số sang string rõ kiểu rồi dry-run/Apply đạt. Không nới decoder hoặc validation.

Mở lại màn Phòng/Khách hàng hoặc bấm tải lại để thấy dữ liệu; Guest chọn Đặt phòng để thấy ảnh phòng. Snapshot cutover29/10 là trước Admin và trước seed; Verify toàn bảng với snapshot đó nay phải lệch, không dùng import/restore để xóa những cập nhật mới. Xem [bàn giao](MySql_Migration_Status.md), [cutover/khôi phục](MySql_Migration_09_Import_Cutover.md).
