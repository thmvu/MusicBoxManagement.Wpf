# Nhập dữ liệu, chuyển nguồn và khôi phục — 29/10/2026

## Kết quả thực tế

App.config mặc định **MySql / musicbox**. App, Guest/Staff/Admin và NoShow worker cùng provider; lỗi Aiven báo lỗi, không tự ghi sang SQLite. Giữ WPF .NET Framework 4.7.2, MySqlConnector và SSL VerifyFull/certificate hiện có. Không dùng Supabase SDK hoặc cài MySQL Server tại máy.

Người dùng xác nhận không có SQLite khác. File mặc định `%LOCALAPPDATA%\MusicBoxManagement.Wpf\musicbox.db` thực tế là **v1, chỉ RoomTypes 2 dòng**. Vì vậy không có tài khoản/booking/khách/hóa đơn đang dùng để chuyển. Không tạo tài khoản hoặc doanh thu giả vào database thật.

Đã sao lưu nguyên bản DB+Content, nâng **bản sao riêng** lên v8 bằng migration SQLite hiện có, diễn tập vào GUID rồi nhập Aiven `musicbox`. Đích trước nhập chỉ có seed, không có dữ liệu nghiệp vụ. Giao dịch nhập và đối chiếu sau commit đều đạt 15 bảng. Không đổi schema MySQL v1 hoặc sửa trigger Invoice.

| Nhóm | Số dòng nguồn chuẩn bị / đích |
|---|---:|
| RoomTypes | 2 / 2 |
| AspNetRoles | 3 / 3 |
| Permission | 27 / 27 |
| RolePermission | 43 / 43 |
| 11 bảng còn lại | 0 / 0 mỗi bảng |

Role/quyền được tạo khi nâng bản sao v1 lên v8; không gọi đó là tài khoản cũ đã chuyển. Rooms=0 nên không có ảnh phòng thực tế cần chuyển. Content runtime riêng đã cài tại `%LOCALAPPDATA%\MusicBoxManagement.Wpf.MySql`; ảnh vẫn local, máy khác phải nhận Content và cấu hình root riêng.

Mã nhập `370ffa184ef4423cb54e67ad28b6d4ba`, UTC `2026-10-29T05:02:51.1801818+00:00`; mã nền code `0eeb25d` cộng thay đổi của task này (commit triển khai xem Git log). Hồ sơ riêng ngoài Git: `%LOCALAPPDATA%\MusicBoxManagement.Wpf.Backups\actual-import-20261029.json`, chứa đường dẫn raw/prepared/storage, số dòng/hash từng bảng và cờ kích hoạt provider. Không đưa backup hoặc password hash vào Git.

SHA256 SQLite gốc trước/sau: `E350DCF6F5B5A60AC91666347010B3E96BC9157D3FA82884C17A6856611B5335`. Nguồn vẫn v1, không bị nâng trực tiếp. Đã mở bản Release dùng config thật: Standard/VIP, chế độ Khách và **Thiết lập Admin**. Chưa tạo Admin trên database thật; người dùng tự chọn tài khoản/mật khẩu lần đầu. UI automation đọc được cây accessibility; thao tác đóng bị lỗi activate của công cụ, không xem đó là lỗi nghiệp vụ hay đã thử nhập liệu trên production.

## Chạy và cấu hình

Build solution Debug/Release trong Visual Studio, chạy project WPF bình thường. Các biến người dùng Aiven/password/certificate đã cấu hình trước vẫn cần trên mỗi máy; không chia sẻ password trong repo. Mở lại Visual Studio sau khi đổi biến môi trường. Xem `Data/AivenDatabase.cs` cho tên biến kết nối hiện hành.

ApplicationServices đọc App.config rồi cho phép biến process kế thừa ghi đè: `MUSICBOX_PROVIDER`, `MUSICBOX_MYSQL_DATABASE`, `MUSICBOX_SQLITE_PATH`, `MUSICBOX_STORAGE_ROOT`. Chỉ nhận provider `MySql` hoặc `SQLite`. Không fallback sau lỗi online. Kiểm tra biến override khi máy dùng sai DB.

Sau khi mở, chọn **Thiết lập Admin**, tự đặt tài khoản. Tạo phòng/dịch vụ/nhân viên trong UI theo quyền. Chế độ Khách tại quầy vẫn dùng được; nguồn hiện chưa có Rooms nên cần tạo phòng trước khi đặt. Trạng thái tải loại phòng dùng cùng VM của hai provider, không dùng riêng lời nhắn UI để kết luận database nào đang chạy.

## Công cụ migration

Đóng mọi instance ứng dụng trước backup/import/cài Content/chuyển nguồn. Thư mục snapshot là bất biến, tuyệt đối không dùng nó làm storage runtime. Backup giữ writer lock SQLite trong lúc Backup API và copy Content; công cụ không thể khóa phần mềm bên ngoài tự sửa file ảnh. Chạy từ root repo bằng Windows PowerShell, không dùng PowerShell Core để nạp WPF/.NET Framework.

```powershell
# Read-only, không in dữ liệu từng người hoặc mật khẩu
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File Tools/Inspect-MigrationState.ps1

# Các mode trả đối tượng/đường dẫn; lưu kết quả bên ngoài repo.
$build = 'D:\vu\hoctap\MusicBoxManagement.Wpf\MusicBoxManagement.Wpf\bin\Release'
$sourceRoot = Join-Path $env:LOCALAPPDATA 'MusicBoxManagement.Wpf'
$backupRoot = Join-Path $env:LOCALAPPDATA 'MusicBoxManagement.Wpf.Backups'
$raw = & Tools/Invoke-MySqlMigration.ps1 -Mode Backup -SourcePath (Join-Path $sourceRoot 'musicbox.db') -StorageRoot $sourceRoot -BackupParent $backupRoot -BuildDirectory $build
$prepared = & Tools/Invoke-MySqlMigration.ps1 -Mode PrepareV8 -SnapshotDirectory $raw.DirectoryPath -BuildDirectory $build
# Chỉ chạy Import cho đích trống đã kiểm tra, không chạy lại trên musicbox đã vận hành.
# & Tools/Invoke-MySqlMigration.ps1 -Mode Import -SnapshotDirectory $prepared.DirectoryPath -DatabaseName <database-trong> -BuildDirectory $build
# & Tools/Invoke-MySqlMigration.ps1 -Mode Verify -SnapshotDirectory $prepared.DirectoryPath -DatabaseName <database-trong> -BuildDirectory $build
```

Các lệnh gọi trực tiếp `.ps1` trong ví dụ phải nằm trong phiên `powershell.exe -STA`, tương tự runner. `PrepareV8` tạo bản sao mới, không sửa raw. `Import` từ chối đích có nghiệp vụ; không merge/remap ID, không xóa DB, không tự retry. `Verify` so toàn bộ giá trị/NULL/ID/hash từng bảng trong RR read-only. Sau khi đăng nhập sẽ có User.Login audit hoặc dữ liệu mới: Verify với snapshot cũ **phải báo lệch**, không được xóa dữ liệu mới để ép khớp.

Preflight kiểm integrity/FK/version, manifest SHA256 và ảnh GUID PNG tồn tại, chuẩn hóa timestamp UTC O giữ ticks, tiền nguyên decimal; kiểm role/phone và trạng thái session/source/invoice/PaidAt/Completed-only món. Không tái tính giá phòng lịch sử hoặc đổi CustomerId. Nhập theo FK trong một Serializable transaction, thay seed chỉ khi nghiệp vụ trống, giữ ID/password hash/stamp/snapshot/grants/audit; rollback khi bất kỳ insert/audit/verify lỗi. Content được cài riêng qua InstallContent vào root chưa có Content; lỗi cài ảnh không phải transaction xuyên DB+filesystem. Chỉ kích hoạt provider khi cả hai đạt.

## Khôi phục có kiểm soát

Giữ Aiven nguyên trạng. Dừng app/worker, lấy đường dẫn **PreparedV8** từ hồ sơ nhập, phục hồi vào thư mục mới. Raw v1 cũng được giữ nguyên để đối chiếu; chọn prepared khi cần SQLite đủ schema.

```powershell
$record = Get-Content (Join-Path $env:LOCALAPPDATA 'MusicBoxManagement.Wpf.Backups\actual-import-20261029.json') -Raw | ConvertFrom-Json
$restoreRoot = Join-Path $env:LOCALAPPDATA ('MusicBoxManagement.Wpf.Restore_' + [Guid]::NewGuid().ToString('N'))
& Tools/Invoke-MySqlMigration.ps1 -Mode RestoreSqliteCopy -SnapshotDirectory $record.PreparedV8 -DestinationStorageRoot $restoreRoot -BuildDirectory $build
$env:MUSICBOX_PROVIDER = 'SQLite'
$env:MUSICBOX_SQLITE_PATH = Join-Path $restoreRoot 'musicbox.db'
# SQLite services hiện có lấy Content cạnh file DB.
& (Join-Path $build 'MusicBoxManagement.Wpf.exe')
```

Không ghi đè nguồn/backup. Bản phục hồi là dữ liệu **tại thời điểm snapshot**, không gồm dữ liệu mới phát sinh trên MySQL. Không có đồng bộ hai chiều hay reverse import MySQL→SQLite; nếu đã vận hành online phải đối chiếu/backup dữ liệu mới trước quyết định quay lại. Để trở lại MySQL, đóng app, bỏ biến override SQLite trong phiên rồi chạy lại với App.config. Không tự xóa cloud database.

## Bằng chứng

- Runner Debug 70/70 (44 SQLite/WPF +26 MySQL ở lúc khởi chạy), summary `%TEMP%/MusicBoxRegression_b2fa8ba726ff47279fb493ab97ee939b/results.json`.
- Release SQLite/WPF 44/44, summary `%TEMP%/MusicBoxRegression_52edb3e1ec074c3ea19fa8cecfba55e4/results.json`.
- RemainingReads cuối Top10/12 món: Debug `MusicBoxRemainingFinalDebug_20261029.log`; Release summary `MusicBoxFinalDeltaRelease_6896da52fe3c48d18e039292fc4e34b2/results.json` (3/3 cùng Import bản v1 và AuthenticationUi 142 ảnh).
- OperationsUi cuối gồm worker MySQL: Debug `MusicBoxOperationsFinalDebug_20261029.log`; Release `MusicBoxCompletionRelease_4dd058dbfe7845cfbdb18a5010fbbc63/results.json` (3/3). 5 ảnh/build; đã xem Dashboard có Invoice MySQL.
- Import cuối thêm business preflight/corrupt PaidAt/restore: `MusicBoxImportShipDebug_20261029.log` và `MusicBoxImportShipRelease_20261029.log`, cả hai exit 0. Dùng checkout/Identity/ảnh/quyền thật trong GUID, trigger ép rollback, từ chối đích bẩn và manifest bị sửa, v1→copy v8 và restore không ghi đè.
- Nguồn thực rehearsal `MusicBoxActualRehearsal_20261029.log`, import `MusicBoxActualImport_20261029.log`, verify cuối bằng Release `MusicBoxActualVerifyShip_20261029.log`, 15 bảng đạt. Tất cả log trên nằm TEMP trừ hồ sơ nhập riêng AppData.

Runner hiện có 72 bộ =44+28, hai bộ thêm sau khi runner70 bắt đầu được chạy riêng. Không tuyên bố 72 bộ đều chạy hai cấu hình. Lỗi dựng fixture đầu (Add-Type/constructor, Verify sau User.Login) được sửa test theo timeline, không nới service/SSL/timeout/assertion. Một lệnh cuối truyền sai đường dẫn build dừng trước chạy; lệnh đúng đạt. Máy in giả đã kiểm tra ở 04i; **chưa in giấy/driver thật**, chưa nghiệm thu vận hành thực tế với người dùng, không giả số liệu quán trong database thật.
