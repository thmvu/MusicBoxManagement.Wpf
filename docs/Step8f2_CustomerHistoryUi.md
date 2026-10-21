# Bước 8f.2 — Giao diện lịch sử khách hàng

Ngày 21/10/2026. Nối service 8f.1 vào trang Khách hàng, đối chiếu mục 21/37 và quyền mục 5 của plan tham chiếu. Không thay quy tắc, schema hoặc provider.

## Cách dùng

Đăng nhập có Customer.View → Khách hàng → tìm/chọn một dòng → **Xem lịch sử**. Nút ở đầu vùng thông tin khách, dùng được với tài khoản chỉ xem, không cần Customer.Create/Edit. Chưa chọn khách hoặc đang tải/lưu thì nút tắt.

Cửa sổ riêng có tên/SĐT hiện hành, số phiên Completed và lần nhận phòng thực tế gần nhất, ba tab:

- **Booking:** mã, phòng hiện hành, bắt đầu/kết thúc, trạng thái đã lưu và lý do hủy. Không sửa booking hoặc chạy NoShow từ lịch sử.
- **Phiên sử dụng:** mã phiên, booking nguồn hoặc Walk-in, mã/loại/giá phòng snapshot, giờ nhận/trả dự kiến/trả thực tế và trạng thái. Walk-in không có giờ trả dự kiến; thời gian thiếu hiện dấu —. Bảng có cuộn ngang để xem các cột còn lại.
- **Hóa đơn:** số hóa đơn, phòng snapshot, giờ thanh toán, phương thức và tổng tiền. Chỉ trả dữ liệu khi có Invoice.View hiện hành; thiếu quyền báo rõ, khác với khách chưa có hóa đơn. Đây là tóm tắt; xem chi tiết/in vẫn dùng chức năng Hóa đơn riêng và kiểm tra quyền riêng.

Thời gian hiển thị UTC+7, có giây; số tiền theo vi-VN, không đổi sang double. Chọn **Tải lại** để lấy dữ liệu và quyền mới. Đổi SĐT không thay CustomerId nên giữ các lượt cũ. Khách chưa dùng có 0 phiên hoàn tất và Chưa sử dụng.

Lịch sử đọc dữ liệu đã lưu, không tự lưu form sửa khách đang nhập. Đóng lịch sử sẽ tải lại danh sách và xóa lựa chọn/form cũ, kể cả trường hợp quyền bị thu hồi khi cửa sổ con đang mở. Muốn giữ thay đổi trong form, bấm Lưu trước khi mở lịch sử.

## Luồng code

`CustomersWindow.History_Click` lấy EditingId của khách đang chọn → CustomerHistoryWindow → sự kiện Loaded → CustomerHistoryViewModel.ReloadAsync → Task.Run(CustomerHistoryService.Read) → trả DTO cùng snapshot quyền/dữ liệu → PropertyChanged cập nhật tên, thống kê và ItemsSource của ba bảng. HistoryTimeConverter chỉ chuyển giờ hiển thị, không sửa DTO. Service tạo từ CustomerService.ForHistory nên dùng đúng database của trang cha.

Mỗi lần tải xóa DTO/tên/thống kê/bảng cũ trước khi đọc; lỗi, mất quyền hoặc ID không còn tồn tại không giữ dữ liệu trước đó. Busy khóa tải lặp và đóng cửa sổ trong lúc đọc. Trang cha dùng modal và historyOpen để chặn mở lặp; đóng xong SearchAsync kiểm tra lại quyền/tải lại danh sách. Tìm lại/đổi kết quả/chọn khách khác không giữ lịch sử khách trước; không có cache hoặc đường Guest.

## Kiểm tra

- Debug/Release VerifyHistoryUiDebug/Release build đạt.
- Verify-CustomerHistoryUi cả hai build đạt: mở modal từ khách chọn, chỉ Customer.View, nút/chọn/filter, summary/UTC+7/snapshot, Invoice.View cấp-thu hồi, Customer.View thu hồi xóa cả cửa sổ con và trang cha, đổi SĐT, khách rỗng/ID không tồn tại, tải lại xóa dữ liệu cũ, busy/duplicate/close, cửa sổ nhỏ. Render 11 ảnh/build, đã xem nút lịch sử ở trang cha 900×540 và các tab cửa sổ con 900×580/full, trạng thái thiếu quyền/rỗng/mất quyền.
- Hồi quy Debug CustomerHistory/Customers/Authentication/Foundation đạt. AuthenticationUi đạt, render 131 ảnh của các màn hình cũ.
- Fixture phiên/hóa đơn chỉ kiểm tra đọc và UI, không thay kiểm tra checkout thật. Không gửi máy in, không chạy MySQL online hoặc thay dữ liệu/ảnh thật.

Phần lịch sử khách 4d/bước 8 nay có service và UI trên SQLite v8. MySQL giữ các bước đã pull: 4c.2a đã nghiệm thu, 4d Calendar có code/test nhưng lần trước chưa nghiệm thu online do DNS. Tiếp các service MySQL/kiểm tra online theo plan, port cả lịch sử/quản trị/báo cáo/Excel trước cutover; giữ ERD tổng thể/từ điển dữ liệu/bản xuất/demo và sao lưu/nhập/đối chiếu database + Content/UI riêng.
