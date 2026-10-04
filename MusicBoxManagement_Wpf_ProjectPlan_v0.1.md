# Music Box WPF — kế hoạch nền tảng v0.1

Ngày: 02/10/2026. Lựa chọn đã chốt: **project riêng, WPF, .NET Framework 4.7.2, SQLite; giữ chế độ Khách tại máy demo/quầy**.

## Phạm vi và nguồn nghiệp vụ

Tham chiếu `docs/Reference_Web_ProjectPlan_v1.3.md` và các quyết định người dùng đã chốt. Đây là bản sao nguyên văn plan web để đối chiếu nghiệp vụ, không phải chỉ dẫn tiếp tục xây dựng ASP.NET/SQL Server trong project WPF. Chỉ thay nền tảng theo yêu cầu; tài liệu này chưa thay thế đầy đủ plan v1.3 và không xác nhận các thay đổi nghiệp vụ.

Giữ các nhóm chức năng: Standard/VIP, phòng/khách/dịch vụ, Reservation → RoomSession → Order → Invoice, walk-in, hủy/NoShow, snapshot, thu tiền mặt/chuyển khoản, lịch ngày/tuần, báo cáo/Excel, Admin/Manager/Staff, ma trận quyền và nhật ký.

## Các bước nhỏ

1. **Nền tảng — đã hoàn thành:** tạo solution WPF, mở cửa sổ, khởi tạo SQLite riêng, đọc hai loại phòng. Debug/Release build đạt; kiểm tra SQLite và giao diện đạt.
2. **Chốt các điểm thay nền tảng — đã hoàn thành:** Guest tại máy demo/quầy; Identity Core + kho SQLite, phiên đăng nhập trong bộ nhớ, quyền hiện hành trong service/UI; transaction ghi IMMEDIATE. Chi tiết ở `docs/Wpf_Authentication_Transactions.md`. Đã kiểm tra commit/rollback và hai kết nối tranh quyền ghi; Debug/Release đạt. Chưa có schema tài khoản hoặc đăng nhập. Không tự bỏ Guest hoặc thay cách tra cứu SĐT.
3. **Đăng nhập và quyền — đã hoàn thành ngày 03/10/2026:** migration v2 giữ dữ liệu v1, kho SQLite cho Identity/UserManager, thiết lập Admin đầu tiên, đăng nhập/đăng xuất, ba role/27 permission, kiểm tra quyền hiện hành ở service/UI. Debug/Release và kiểm tra SQLite/xác thực/quyền/UI đạt. Chi tiết `docs/Step3_Authentication.md`. UI quản trị tài khoản/ma trận quyền/nhật ký đầy đủ vẫn thuộc bước 8.
4. **Danh mục — đang làm:** 4a sửa hai RoomType đã hoàn thành ngày 03/10/2026, quyền RoomType.Edit và nhật ký trong transaction, validation và chặn form cũ; schema v3 chuẩn hóa ActorType Staff. Chi tiết `docs/Step4a_RoomTypes.md`. Ngày 04/10/2026 hoàn thành 4b.1 thêm phòng có một ảnh JPEG/PNG/WebP bắt buộc, danh sách nội bộ, quyền Room.Manage/nhật ký và mã unique/cố định; schema v4 giữ dữ liệu cũ. Debug/Release và các kiểm tra nền tảng/xác thực/loại phòng/phòng/UI đạt. Chi tiết `docs/Step4b1_RoomCreation.md`. Còn 4b.2 sửa/thay ảnh/khóa mở đúng quy tắc booking/session, sau đó dịch vụ và khách hàng. Chưa hoàn tất cả bước 4.
   - **4b.2a hoàn thành 04/10/2026:** sửa tên/mô tả/thay ảnh phòng, quyền Room.Manage và Room.Update trong transaction, chặn form cũ, giữ mã/loại/trạng thái/CreatedAt; schema giữ v4. Debug/Release, nền tảng/phòng/UI đạt. Xem `docs/Step4b2a_RoomEditing.md`. Còn đổi loại/khóa mở cần nền tảng kiểm tra Active Session/Confirmed còn hiệu lực; chưa hoàn thành 4b.2.
   - **4b.2b hoàn thành 04/10/2026:** đổi loại/khóa mở có lý do, chặn Active Session và Confirmed còn hiệu lực đúng mốc +15 phút; quyền/kiểm tra dữ liệu/cập nhật/audit cùng transaction, IClock UTC. Schema v5 thêm nền tảng Customers/Reservations/RoomSessions, giữ dữ liệu cũ; chưa có luồng đặt/nhận phòng. Debug/Release và năm bộ kiểm tra đạt. Xem `docs/Step4b2b_RoomState.md`. Tiếp theo 4c dịch vụ và 4d khách hàng; Guest danh sách phòng/các luồng vẫn giữ trong phạm vi, bước 4 chưa hoàn tất.
5. **Đặt phòng:** lịch ngày/tuần, availability, đặt/hủy/NoShow và kiểm tra trùng.
6. **Vận hành:** check-in, walk-in, gia hạn, gọi/xác nhận/hủy món, tiền tạm tính.
7. **Thanh toán:** transaction trả phòng, hóa đơn bất biến, in bằng WPF.
8. **Quản trị và báo cáo:** dashboard, khách hàng/lịch sử, ma trận quyền, user/audit, báo cáo/Excel.
9. **Hoàn thiện đồ án:** kiểm tra luồng chính bằng SQLite và UI, dữ liệu demo, hướng dẫn và tài liệu bảo vệ.

## Quy tắc dữ liệu cho bước 1

- File riêng tại `%LOCALAPPDATA%\MusicBoxManagement.Wpf\musicbox.db`.
- Bảng `RoomTypes`: mã STANDARD/VIP, tên, sức chứa, giá nguyên đồng, tiện ích, mô tả.
- Schema v1 có CHECK/UNIQUE; seed trong cùng transaction lúc tạo schema lần đầu.
- Mỗi kết nối bật foreign keys; schema mới hơn phiên bản ứng dụng phải được từ chối.
- Tại bước 1 chưa có tài khoản; bước 3 bổ sung schema v2 và Admin do người vận hành tạo. Booking và phiên sử dụng vẫn chưa triển khai.

## Quyết định và điểm cần chốt trước bước 2

Người dùng đã chọn giữ **chế độ Khách tại máy demo/quầy**. Guest xem phòng/lịch, đặt phòng, tra cứu SĐT, gia hạn và gọi món qua giao diện WPF riêng với khu vực nhân viên. Các chức năng và điều kiện nghiệp vụ Guest giữ theo plan gốc; ứng dụng không cần thêm website để chạy chế độ này. Truy cập khu vực nhân viên phải đăng nhập; chế độ Khách không mở dữ liệu quản trị.

ASP.NET Identity/OWIN, Razor, FullCalendar và SQL Server không được sao chép nguyên trạng thành cách triển khai WPF/SQLite. Tái sử dụng quy tắc nghiệp vụ; thiết kế phần truy cập dữ liệu và đăng nhập phù hợp sau khi đối chiếu.
