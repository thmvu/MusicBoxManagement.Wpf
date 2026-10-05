# Bước 4d.1 — danh mục khách hàng và chuẩn hóa SĐT

Ngày 05/10/2026. Đối chiếu plan WPF và plan tham chiếu mục 5, 21–22, 32. Giữ WPF .NET Framework 4.7.2 + SQLite. Đây là phần danh mục, chưa hoàn tất lịch sử khách hoặc Guest Lookup.

## Đã làm và cách dùng

Đăng nhập → **Khách hàng**. Staff/Manager/Admin mặc định có Customer.View/Create/Edit. Nhập một phần tên hoặc SĐT đầy đủ vào bộ lọc rồi **Tìm**; cả hai thì lọc đồng thời, cả hai trống thì xem tất cả. Tên tìm bằng .NET OrdinalIgnoreCase để xử lý hoa/thường tiếng Việt; không bỏ dấu, không tìm SĐT một phần.

**Thêm khách hàng** mở form trống; họ tên trim 1–100 ký tự, SĐT bắt buộc. Chọn dòng để sửa. **Bỏ thay đổi** khôi phục dòng đang chọn hoặc xóa form thêm chưa lưu. Sau lưu, xóa bộ lọc và tải lại danh sách để thấy cả khách vừa đổi tên/SĐT. Khi đang tải/lưu, khóa thao tác và chặn đóng cửa sổ.

`PhoneNumberNormalizer.Normalize` loại khoảng trắng/chấm/gạch ngang; đổi +84/84 về 0; chỉ nhận 10 chữ số ASCII bắt đầu bằng 0. Lưu chuỗi giữ số 0 đầu, không kiểm tra nhà mạng hoặc thêm OTP. Ví dụ +84 912.345-678 → 0912345678. Hàm public dùng chung cho đặt/tra cứu/walk-in ở các bước sau, không tạo bản chuẩn hóa riêng cho mỗi luồng.

Tạo trùng SĐT báo dùng khách hiện có; không đổi tên khách cũ khi tạo trùng. Khi triển khai booking/walk-in, tìm thấy số phải tái sử dụng Customer và giữ tên cũ theo mục 21. Thao tác sửa nội bộ cho đổi họ tên/SĐT, số mới phải unique. CustomerId giữ nguyên nên Reservation/RoomSession vẫn liên kết đúng; tra cứu về sau dùng số mới. Không có thao tác xóa.

## Service, quyền và dữ liệu

Schema giữ **v6**, không migration hoặc seed lại Customers đã có từ v5. Search dùng transaction đọc và PermissionService đọc cùng snapshot với quyền trả về cho UI; yêu cầu Customer.View. UI khóa thêm/sửa theo quyền riêng. Save cần Customer.View và Customer.Create/Customer.Edit tương ứng; được một quyền không tự cấp các quyền còn lại.

Save dùng BeginWriteTransaction: đọc lại phiên/quyền hiện hành, validation/chuẩn hóa, đọc khách và so sánh tên/SĐT với snapshot form, kiểm tra số trùng, INSERT/UPDATE và Customer.Create/Customer.Update qua AuditService, rồi commit. UNIQUE trong SQLite là lớp bảo vệ thêm. Hai số nhập tương đương chỉ tạo một khách; hai form cũ chỉ một cập nhật thành công. Lỗi nhật ký rollback khách cùng transaction. Lưu không đổi không thêm log. Nhật ký dùng ActorType Staff/UserId/CustomerId/thời gian UTC.

VM giữ snapshot riêng của khách được chọn. Nếu quyền bị thu hồi trong lúc nhập, Save từ chối và xóa/khóa dữ liệu form; bấm Tìm để kiểm tra lại quyền. Commit thành công nhưng reload lỗi thì báo đã lưu và giữ ID, tránh tạo khách trùng khi thử lại.

## Kiểm tra

- MSBuild Visual Studio Debug/Release: đạt, không lỗi/cảnh báo.
- Verify-Foundation/Authentication/RoomTypes/Rooms/Services: đạt, các luồng cũ vẫn hoạt động.
- Verify-Customers: đạt trên SQLite tạm thật. Thử số 0/+84/84 và khoảng trắng/chấm/gạch ngang, số sai độ dài/prefix/ký tự/Unicode; không thêm quy tắc nhà mạng. Kiểm tra trim tên, giới hạn/required, trùng số và không ghi đè tên, tìm tên tiếng Việt/phone chuẩn hóa, sửa số giữ ID/liên kết Reservation/Session fixture, tìm bằng số mới và không tìm bằng số cũ.
- Kiểm tra Guest bị chặn, mặc định Staff/Manager, quyền View/Create/Edit độc lập, thu hồi khi form đã mở, view-only VM, logout; no-op, rollback create/update khi audit lỗi, hai form sửa đồng thời và hai số tương đương tạo đồng thời, mở lại giữ dữ liệu. Fixture lịch sử là dữ liệu SQL tạm, chưa phải service booking/session hoàn chỉnh.
- Verify-AuthenticationUi ở STA: đạt, render 17 ảnh; Guest ẩn nút Khách hàng, thêm bằng +84, tìm bằng 84, bỏ sửa tên chưa lưu, SĐT sai báo lỗi, sửa tên/số không tạo khách mới, bỏ form thêm chưa lưu. Đã xem ảnh danh mục và màn hình nhân viên; toolbar cho phép xuống dòng khi hẹp.

Tất cả dùng SQLite/thư mục tạm riêng; không mở/xóa database đang dùng. Không có dữ liệu demo tự seed vào dữ liệu thật.

## Phần còn lại và tiếp theo

Chưa có trang chi tiết lịch sử Reservation/Session/Invoice, số lần hoàn tất/lần sử dụng gần nhất. Giữ nguyên mục 21, hoàn thiện khi có các luồng tương ứng trong scope khách hàng/bước 8; không giả lập lịch sử/hóa đơn hoặc coi đồ án đã hoàn thành. Guest hiện vẫn chỉ xem loại phòng: chưa chọn giờ đặt, tra cứu booking theo SĐT, nhận phòng hoặc gọi món.

Tiếp theo bước 5: làm nhỏ từ giờ hoạt động/slot/thời lượng/availability đến đặt/hủy/NoShow và lookup. Phải tái sử dụng PhoneNumberNormalizer/IClock và kiểm tra quyền/Room.IsActive/giờ/overlap trong cùng transaction ghi; lookup Guest chỉ trả booking còn hiệu lực/phiên liên quan đúng mục 22, không công khai danh sách khách hoặc lịch sử hóa đơn.
