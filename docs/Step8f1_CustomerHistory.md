# Bước 8f.1 — Service lịch sử khách hàng

Ngày 19/10/2026. Đối chiếu mục 21 và 37 của Reference_Web_ProjectPlan_v1.3; bổ sung phần còn lại của 4d.1. Đây là nền tảng đọc nội bộ, chưa thêm giao diện lịch sử.

## Dữ liệu và cách tính

`CustomerHistoryService.Read(actor, customerId)` trả Customer hiện hành, lịch sử Reservation, RoomSession và tóm tắt Invoice. Mọi truy vấn liên kết bằng CustomerId; đổi tên/SĐT hoặc người khác dùng lại số cũ không chuyển lịch sử sang người đó. Phòng khóa vẫn có lịch sử. Reservation dùng mã phòng hiện hành (không có snapshot mã riêng); phiên và hóa đơn dùng mã phòng snapshot.

- `CompletedSessionCount`: số RoomSession Completed, gồm booking và walk-in; không cộng thêm Reservation Completed hoặc Invoice nên không đếm trùng một lần sử dụng.
- `LastUsedAt`: ActualStartTime gần nhất của phiên Active/Completed. Booking tương lai, hủy hoặc NoShow không tính là đã sử dụng. Khách chưa có phiên nhận null và số lần hoàn tất 0.
- Reservation xếp theo StartTime giảm dần, session theo ActualStartTime, invoice theo PaidAt; cùng thời điểm xếp ID giảm dần. Thời gian đọc/so sánh bằng DateTimeOffset; giữ ticks, không làm tròn.
- Booking hiển thị trạng thái đã lưu. Đọc lịch sử không chạy worker NoShow, không ghi audit hoặc cập nhật nghiệp vụ. UI sau này cần ghi rõ trạng thái lưu để phân biệt với báo cáo trạng thái hiệu lực.
- Invoice chỉ là tóm tắt số hóa đơn, mã phòng snapshot, PaidAt, phương thức và tổng tiền decimal. Chưa có thao tác mở/in từ lịch sử; khi nối UI vẫn phải qua InvoiceService để kiểm tra quyền hiện hành.

## Quyền và transaction

Customer.View hiện hành là bắt buộc; không đòi Customer.Create/Edit, Reservation.View hoặc Session.View để xem lịch sử thuộc trang khách hàng theo mục 21. Dữ liệu hóa đơn tiếp tục tôn trọng Invoice.View: thiếu quyền này trả CanViewInvoices=false và danh sách hóa đơn rỗng; UI phải hiển thị thiếu quyền, không diễn giải thành khách chưa có hóa đơn. Không thêm permission hoặc đường public/Guest.

Quyền/tài khoản/stamp, Customer, ba nhóm lịch sử và một mốc IClock cùng read transaction. Không gọi các service đọc khác để tách snapshot. ID không hợp lệ/không tồn tại được báo sau kiểm tra quyền. Các danh sách chỉ đọc, không có writer hoặc cache.

## Kiểm tra và làm tiếp

Build Debug/Release VerifyHistoryDebug/Release đạt; Verify-CustomerHistory cả hai build đạt trên SQLite GUID tạm: khách rỗng/cách đếm/actual ticks/walk-in/snapshot/phòng khóa, liên kết ID khi đổi và tái sử dụng SĐT, dữ liệu khách khác, quyền Customer.View độc lập/Invoice.View thêm-thu hồi, session stamp, một clock, không NoShow/audit/schema writes. Invoice/phiên trong bài là fixture đọc lịch sử, không thay kiểm tra checkout thật.

Hồi quy Debug Verify-Customers/Authentication/Foundation đạt. Verify-AuthenticationUi đạt, render 131 ảnh của các màn hình hiện có; chưa có UI lịch sử khách mới để kiểm tra. Không đụng database/ảnh đang sử dụng, không gọi cloud hoặc máy in.

Tiếp theo 8f.2: nút Lịch sử tại danh sách khách, summary và tab booking/phiên/hóa đơn; guard busy/chọn khách/đổi filter/mất quyền/xóa dữ liệu cũ và kiểm tra cửa sổ nhỏ. Bước này chưa cho người dùng bấm xem lịch sử. Runtime vẫn SQLite v8, migration MySQL vẫn 4c.2a; service mới cũng cần port trước cutover. Giữ scope ERD/từ điển dữ liệu/demo và sao lưu/nhập/đối chiếu database + Content.
