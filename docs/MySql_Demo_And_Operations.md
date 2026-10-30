# Demo và vận hành Music Box trên Aiven

30/10: theo yêu cầu mới, đã seed5 phòng/6 khách Demo trên musicbox sau khi người dùng tự tạo Admin. Xem [lượt seed](MySql_Demo_Seed_20261030.md); không còn coi production Rooms/Customers trống. Các bước dưới là kịch bản thao tác, chưa tự tạo booking/thanh toán mẫu.

Thực hiện trong database demo riêng nếu cần diễn tập, không bơm fixture vào `musicbox`. Đọc [cutover/backup/khôi phục](MySql_Migration_09_Import_Cutover.md) và [quy tắc nghiệp vụ](Reference_Web_ProjectPlan_v1.3.md). Fixture kiểm tra tạo GUID và tự dọn; không phải bộ dữ liệu quán sẵn có.

## Kịch bản trình bày

1. Admin lần đầu tự thiết lập tài khoản; tạo phòng Standard/VIP với ảnh local, món/dịch vụ và nhân viên Staff/Manager. Gán vai trò rồi chỉnh ma trận theo quyền; giữ Admin cố định, Staff không xem doanh thu tổng hợp. Đăng nhập nhân viên mở Tổng quan trước.
2. Về Khách tại quầy, chọn phòng/ngày/giờ hợp lệ và SĐT. Đặt thành công, tra cứu bằng SĐT; thông tin công khai không lộ khách khác. Booking tương lai chưa tạo phiên sử dụng hoặc hóa đơn.
3. Nhân viên tìm booking và check-in đúng cửa sổ cho phép; thao tác lặp không tạo hai phiên. Trường hợp khách đến trực tiếp dùng Walk-in, không cần booking nguồn. ReturnBy thay đổi theo ca và lịch phòng/khách; walk-in không có giờ kết thúc dự kiến để gia hạn.
4. Phiên Active: Khách gửi món Pending, nhân viên xác nhận sau phục vụ hoặc hủy. Nhân viên tạo món đã phục vụ theo quyền. Chỉ Completed được cộng vào tiền; giá/tên món giữ snapshot. Gia hạn booking +30/+60 trước hoặc đúng ExpectedEnd nếu không xung đột; kiểm tra lại sau chờ khóa.
5. Xem tiền tạm tính rồi checkout Cash/BankTransfer. Confirm tính lại giờ/tiền, hủy Pending, hoàn tất booking/phiên và Invoice cùng transaction. Bấm lại trả hóa đơn cũ; lỗi ghi không để lại thanh toán nửa chừng. Không tự checkout vì quá giờ.
6. Tra cứu/in lại hóa đơn với quyền riêng; thay danh mục không sửa lịch sử. Báo cáo doanh thu theo PaidAt ngày Việt Nam; sử dụng phòng theo actual/ca/CreatedAt; booking theo StartTime/effective NoShow; món theo snapshot đã thanh toán. Xuất Excel cần quyền Export hiện hành.
7. Lịch sử khách theo CustomerId với SĐT hiện hành; hóa đơn chỉ hiện khi đủ Invoice.View. Admin xem nhật ký, chỉnh quyền/khóa tài khoản; cửa sổ cũ bị thu quyền phải làm mới/xóa dữ liệu hoặc từ chối ghi.

## Ca từ chối nên trình bày

- Thiếu trường bắt buộc: form báo thiếu, không ghi hoặc thoát app. Visual Studio dừng first-chance Exception có Continue/F5 khác với ứng dụng crash.
- Hai booking trùng phòng/khách hoặc đặt ngoài ca: từ chối. Confirmed hết grace được NoShow, reader không tự ghi trừ route maintenance được quy định.
- Hủy Guest sau mốc Start−2h; gia hạn quá giờ, walk-in hoặc trùng booking tiếp: từ chối.
- Khách không truy cập màn nội bộ; Staff không có tổng doanh thu; Manager không tự quản trị tài khoản/quyền/audit Admin-only.
- Mất mạng hoặc writer deadlock: hiển thị lỗi nghiệp vụ/kết nối, tra cứu trạng thái trước thử lại; không tự chuyển SQLite hoặc retry vô hạn.

## Nghiệm thu tại máy quầy

Chạy Release trên mạng sử dụng thật, kiểm certificate và quyền Windows trên Content. Máy khác cần cấu hình Aiven và sao chép Content, database cloud không lưu ảnh. Chọn máy in thật, kiểm khổ giấy/PrintableArea, hóa đơn dài/nhiều trang và hủy dialog; fixture máy in giả không thay thế kiểm tra driver/giấy. Đối chiếu thu tiền thực tế sau ca với hóa đơn; backup DB cloud bằng cơ chế vận hành phù hợp, giữ file Content tương ứng. Công cụ import của repo là chuyển SQLite→MySQL và snapshot SQLite, không phải hệ thống backup định kỳ MySQL.

## Tài liệu báo cáo

- [ERD đầy đủ 16 bảng và từ điển](ERD_Full_And_DataDictionary.md), [SVG](schema/MusicBox_Full.svg), [PNG](schema/MusicBox_Full.png).
- Mermaid biểu diễn cardinality/identifying; DOT/SVG/PNG biểu diễn hướng FK, tên cột/kiểu/PK/FK. UNIQUE/CHECK/transaction đọc ở từ điển và DDL, không suy từ mũi tên DOT.
- `python Tools/Export-SchemaDocs.py` sinh lại Markdown/Mermaid/DOT từ DDL trong repo. SVG được render Graphviz và PNG từ SVG, chỉ chứa schema. Không cần MySQL Workbench để đọc bản xuất; Workbench là lựa chọn nếu muốn reverse-engineer trực tiếp database bằng tài khoản phù hợp.
- [Bàn giao hiện hành](MySql_Migration_Status.md), [ports và WPF wiring](MySql_Migration_06b_08_ReportsHistoryWiring.md), [nhập/cutover](MySql_Migration_09_Import_Cutover.md).
