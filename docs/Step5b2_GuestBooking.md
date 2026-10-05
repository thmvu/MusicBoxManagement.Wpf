# Bước 5b.2 — danh sách phòng public và form Khách đặt phòng

Ngày 05/10/2026. Đối chiếu WPF plan và mục 6–9, 19–23, 38–40, 48 của bản tham chiếu. WPF .NET Framework 4.7.2 + SQLite, schema giữ v6.

## Đã làm và cách dùng

Ở chế độ Khách bấm **Đặt phòng**. Cửa sổ gộp danh sách phòng, ảnh/chi tiết và form chọn ngày/giờ/thời lượng/tên/SĐT. Danh sách chỉ có phòng IsActive; hiển thị giá theo giờ/sức chứa/loại/tiện ích hiện hành và mô tả phòng. Không dùng dữ liệu quản trị hoặc lộ lý do khóa/khách/lịch sử. Phòng không được tự seed; nếu chưa có phòng đang mở, hiển thị liên hệ nhân viên và tắt gửi.

Ngày chọn giới hạn hôm nay..+30 theo giờ Việt Nam tại lúc mở form; mặc định chọn slot sớm nhất chưa qua với thời lượng 60, hết ca thì ngày mai. Chỉ hiện slot bắt đầu có thể chứa tối thiểu 60 phút trong ca, nên không hiện 11:30/22:30. Các duration dài hơn vẫn được kiểm tra với giờ đã chọn; không cho vắt qua nghỉ trưa/cuối ngày. DatePicker chỉ hỗ trợ nhập; service vẫn kiểm tra giờ/ngày hiện hành khi xem trước/lưu, kể cả form mở qua ngày mới.

Nhập tên/SĐT rồi **Kiểm tra giờ** để xem kết quả và thời điểm kiểm tra. Preview dùng SĐT chuẩn hóa để tìm Customer, kiểm tra cả lịch Room/Customer; không tạo khách/booking. Khách mới chưa có Customer thì chỉ kiểm tra lịch phòng. Preview xử lý NoShow tồn đọng trước khi lấy snapshot đọc. Không trả danh sách/chi tiết booking của khách khác.

**Xác nhận đặt** gọi ReservationService.CreateGuest, kiểm tra lại trong transaction rồi lưu Confirmed; không yêu cầu login/OTP/mã bí mật/cọc. Không cần preview thành công trước mới cho gửi vì service luôn kiểm tra lại. Trùng giờ sau preview báo lỗi, giữ form để chọn lại. Đổi dữ liệu làm kết quả preview cũ hết hiệu lực hiển thị. Chỉ dùng tên nhập để tạo Customer mới, không ghi đè tên cũ.

Thành công hiển thị mã booking/phòng/giờ/Confirmed và hạn đến nhận trước +15 phút. Khóa form/gửi lặp; **Đặt lượt mới** xóa tên/SĐT và tải lại phòng. Nút làm mới trước xác nhận đọc danh sách mới và giữ phòng đã chọn nếu còn mở. Đóng form chưa xác nhận không ghi; đã thành công thì booking vẫn được lưu. Khi busy khóa thao tác/chặn đóng. Không hiển thị CustomerId hoặc tên cũ của khách tìm được.

Ảnh dùng đường dẫn GUID đã kiểm tra trong thư mục ảnh cạnh DB, BitmapImage OnLoad rồi đóng stream. Mô tả dài có cuộn. Tải/lưu SQLite ở luồng nền. Form có scroll khi thu nhỏ và giao diện trung tính.

## Cấu trúc và kiểm tra

PublicRoom chỉ chứa metadata public, không Actor/User/InactiveReason/Customer/Reservation fields. GuestBookingService cung cấp ListRooms/Preview/Create, không gọi RoomService.ListForManagement hoặc CustomerService.Search. Chỉ tái sử dụng GetImagePath làm helper kiểm tra đường dẫn. GuestBookingViewModel quản lý lựa chọn/busy/kết quả và gửi request; SQL không ở cửa sổ. Staff hiện chưa có UI đặt hộ, service CreateStaff của 5b.1 vẫn giữ quyền riêng.

- Debug/Release build đạt, không warning/error.
- Verify-GuestBooking trên SQLite tạm: danh sách phòng mở/DTO không field nội bộ/không seed; Preview không tạo khách/booking, tìm customer bằng +84/84, overlap khách ở phòng khác, xử lý NoShow; VM validation/xác nhận/khóa gửi lặp/reset; không tạo Guest account, giữ v6.
- Verify-AuthenticationUi STA: Guest mở nút Đặt phòng với danh mục rỗng và bị chặn gửi; form riêng với ảnh PNG thật/thời gian giả định, phòng khóa không xuất hiện; chọn DatePicker/ComboBox/duration/name/phone qua control thực, lỗi SĐT và ca nghỉ, preview không ghi.
- Preview thấy trống rồi Create của người khác chiếm chỗ: Submit bị chặn trùng. Chọn giờ khác submit thành công, giữ tên Customer cũ, không yêu cầu account; gửi lặp không tăng booking, đặt mới xóa thông tin, đóng chưa gửi không ghi. Render 21 ảnh (thêm rỗng/form/trùng/thành công), đã xem form và xác nhận.
- Verify-Foundation/Authentication/RoomTypes/Rooms/Services/Customers/Availability/Reservations đều đạt. Tổng mười bộ kiểm tra đạt, tất cả SQLite/ảnh/thư mục tạm riêng; không mở/xóa dữ liệu đang dùng.

## Phần còn lại

Đã đặt phòng được từ UI Khách; chưa có Guest Lookup/hủy, Day/Week calendar, trạng thái/phần lịch chi tiết phòng, UI danh sách/chi tiết/đặt hộ nhân viên, check-in/walk-in hoặc checkout. Giữ nguyên phạm vi; màn hình hiện mới xem trước cho khoảng được chọn, chưa phải timeline lịch ngày/tuần. Tiếp theo **5c — tra cứu SĐT và hủy đúng mốc 2 giờ**, sau đó các UI nội bộ/calendar theo plan. Phiên Active/gia hạn/gọi món trong lookup làm tiếp cùng luồng phiên; không coi toàn bộ Guest hoàn tất.
