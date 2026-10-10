# Bước 6e.2 — Giao diện tiền tạm tính

Hoàn thành ngày 11/10/2026. Đối chiếu mục 17/22/25/26 của bản tham chiếu web và bước 6e/7 trong plan WPF. Giữ WPF .NET Framework 4.7.2, SQLite schema v7; không thay công thức tiền hoặc nghiệp vụ checkout.

## Đã sửa và cách hoạt động

- Khách: **Tra cứu SĐT → Đang sử dụng → Xem tiền tạm tính**. Cửa sổ giữ cố định số đã tra cứu và ID phiên; mỗi lần đọc gọi `BillingService.ReadGuest` kiểm tra lại số hiện hành và Active. Không dùng đường Staff, không mở lịch sử hóa đơn hoặc dữ liệu khách khác.
- Nhân viên: **Phiên sử dụng → chọn Active → Xem tiền tạm tính**. `ReadStaff` kiểm tra `Session.View` hiện hành; không đòi quyền gia hạn, gọi món hoặc xem chi tiết đơn. Completed không có tiền tạm tính. Quyền checkout riêng vẫn dành cho bước 7.
- `SessionBillWindow`/`SessionBillViewModel` dùng chung cách trình bày, nhận hàm đọc của đúng đường truy cập. Hiện phòng/loại snapshot, giờ nhận thực tế, giờ tính UTC+7 đến giây, thời lượng hiển thị hai chữ số thập phân, giá snapshot, tiền phòng, món đã phục vụ và tổng tiền. Thời lượng hiển thị được làm tròn để đọc; phép tính vẫn dùng toàn bộ ticks ở BillingService.
- Hiện số đơn đã phục vụ/chờ/đã hủy để giải thích tổng tiền. Chỉ Completed được cộng; Pending chưa cộng, Cancelled không tính. Không mở danh sách món qua quyền xem phiên.
- Nút **Cập nhật tiền** đọc lại dữ liệu và thời điểm tính. Màn hình ghi rõ tiền chưa chốt, còn thay đổi theo thời gian và món được phục vụ. Không tự thanh toán, kết thúc phiên, hủy Pending, tạo hóa đơn hoặc giữ giá.
- Trong lúc đọc, chặn cập nhật lặp và đóng cửa sổ. Xóa kết quả trước mỗi lần đọc; nếu số/quyền/trạng thái thay đổi hoặc đọc thất bại, xóa cả số tiền và thông tin cũ. Vẫn đóng/thử cập nhật lại được sau lỗi.
- Cửa sổ mở dạng modal: không đổi SĐT/chọn phiên ở cửa sổ cha khi đang xem tiền. Khi đóng, cha tra cứu lại; đổi số/lọc hoặc tải lại xóa phiên chọn cũ. Nội dung dài có cuộn, nút cập nhật/đóng nằm cố định phía dưới.

## Kiểm tra

- Build Debug và Release bằng MSBuild Visual Studio, ra `bin/VerifyDebug` và `bin/VerifyRelease`; tránh ghi đè bản ứng dụng đang mở.
- 22 bộ kiểm tra đạt trên SQLite/thư mục tạm: Billing, Orders, Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, Calendar, GuestCalendar, CheckIn, WalkIn, Extension, Sessions, GuestSessions và AuthenticationUi.
- UI thêm fixture thật qua CreateWalkIn/CreateStaff/CreateGuest/ConfirmStaff/CancelGuest: 90 giây giá 120.000 đ/giờ = 3.000 đ, cộng 50.000 đ món Completed; Pending/Cancelled không tính. Sau 30 giây và phục vụ Pending, tổng thành 104.000 đ. Đổi giá/ngừng bán danh mục không sửa giá snapshot.
- Kiểm tra Guest chuẩn hóa số/đổi membership, Staff chỉ Session.View và thu hồi quyền, fixture Completed, bấm cập nhật lặp khi reader đang chờ, chặn đóng khi busy, mở đúng phiên qua cả hai nút cha, tải lại cha/đổi SĐT xóa lựa chọn cũ, lỗi đọc xóa tổng tiền.
- AuthenticationUi render **100 ảnh**; đã xem `bill-compact.png` ở kích thước tối thiểu. Nút dưới vẫn nhìn thấy, phần dài cuộn được. Ảnh kiểm tra tại `%TEMP%/MusicBoxUi_6e2`; không đưa dữ liệu/ảnh kiểm tra vào Git.

## Cách thử

1. Dùng một phiên Active từ booking hoặc khách trực tiếp; có thể thêm một đơn đã phục vụ và một đơn Pending.
2. Mở tiền tạm tính theo hai đường ở trên, xem tiền phòng/món/tổng và thời điểm tính.
3. Chờ rồi bấm Cập nhật tiền; phục vụ thêm món bằng tài khoản có quyền và cập nhật lại để xem phần món tăng.
4. Đóng cửa sổ tiền, đổi SĐT/lọc/chọn phiên khác; kết quả cũ phải được xóa. Việc xem tiền không trả phòng hoặc tạo hóa đơn.

## Bước tiếp theo

Bước 7: nền tảng checkout/hóa đơn. Kiểm tra `Session.CheckOut` riêng trong writer; tính lại tiền với thời điểm mới sau khi lấy lock, hủy Pending, chốt Session/Reservation/Invoice/audit cùng transaction, Cash/BankTransfer và idempotence. Bổ sung cạnh tranh checkout với phục vụ/hủy món/gia hạn qua service thật; Completed fixture hiện chỉ kiểm tra guard. Sau đó nối UI xác nhận thanh toán/hóa đơn/in WPF. Dashboard, quản trị tài khoản/RBAC, báo cáo/Excel và lịch sử khách vẫn trong scope.
