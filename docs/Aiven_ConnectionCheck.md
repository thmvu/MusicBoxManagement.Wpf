# Kiểm tra kết nối Aiven từ WPF — 14/10/2026

Người dùng đã thử kết nối MySQL Aiven thành công bằng project riêng và yêu cầu đưa đoạn kiểm tra vào project WPF thật. Đây là bước chuẩn bị kết nối, chưa chuyển schema hoặc nghiệp vụ khỏi SQLite.

## Cách hoạt động

- MySqlConnector 2.6.2 đã được người dùng thêm vào project.
- `Data/AivenDatabase.cs` đọc mật khẩu từ biến **User** của Windows `MUSICBOX_AIVEN_PASSWORD`. Không lưu mật khẩu trong source, connection string log hoặc Git.
- Host `musicbox-mysql-baitapnet.j.aivencloud.com`, port 12628, user `avnadmin`, database `defaultdb` là thông tin người dùng cung cấp cho kiểm tra trên máy demo. Tài khoản quản trị này chưa phải cấu hình phát hành cho khách.
- Chứng chỉ tại `D:\MusicBoxSecrets\ca.pem`, TLS `VerifyFull` kiểm tra chứng chỉ/tên server. Timeout kết nối và truy vấn 15 giây.
- Chỉ mở kết nối và chạy `SELECT VERSION()`, không tạo bảng hoặc ghi dữ liệu.
- Trong Debug, `App.OnStartup` gọi kiểm tra bất đồng bộ sau khi hiện cửa sổ chính. Hiện thông báo thành công/lỗi; không hiện thông báo nếu cửa sổ đã đóng. Release không tự gọi kiểm tra.
- `SqliteDatabase`, worker NoShow, xác thực, RBAC và mọi service hiện tại vẫn dùng SQLite. Database/ảnh đang dùng được giữ nguyên.

## Tự kiểm tra

1. Tạo biến **User** `MUSICBOX_AIVEN_PASSWORD` trong Windows bằng mật khẩu Aiven; không gửi mật khẩu qua chat.
2. Kiểm tra file chứng chỉ đúng đường dẫn và service Aiven đang Running.
3. Chạy project MusicBoxManagement.Wpf ở Debug. Thông báo “WPF đã kết nối Aiven thành công!” kèm phiên bản MySQL xác nhận đường kết nối.
4. Nếu thiếu biến hoặc chứng chỉ, thông báo chỉ ra cấu hình thiếu. Không tắt xác minh TLS để xử lý lỗi.

Chuyển schema SQLite sang MySQL và từng service/transaction là bước sau, phải giữ nguyên nghiệp vụ của plan WPF và bản tham chiếu web.

## Kết quả kiểm tra triển khai

- Build Debug và Release đạt; Verify-Foundation đạt trên database tạm.
- Gọi trực tiếp `TestConnectionAsync` trong bản Debug báo thiếu biến User `MUSICBOX_AIVEN_PASSWORD`. Chứng chỉ tồn tại; chưa xác nhận kết nối online từ project thật ở môi trường này. Người dùng đã xác nhận project Console riêng kết nối được.
- Không chạy startup trên database thật trong kiểm tra này.
