# Chỉnh giao diện chính — 07/10/2026

## Yêu cầu và thay đổi

Người dùng yêu cầu tận dụng thanh trái đang trống, chuyển các nút chen nhau bên phải thành menu dọc và nới phần nội dung.

- Menu nhân viên gồm Loại phòng, Phòng, Dịch vụ, Khách hàng, Booking, Đặt hộ, Lịch phòng; mỗi mục vẫn hiện theo quyền hiện hành. Kiểm tra lại quyền nằm cuối nhóm này.
- Chế độ Khách có Đặt phòng, Tra cứu SĐT và Làm mới danh mục. Khi nhân viên mở danh mục loại phòng, các thao tác sửa/làm mới/quay lại cũng nằm ở thanh trái.
- Menu cuộn độc lập khi cửa sổ thấp; đăng nhập/đăng xuất luôn ở phần cuối thanh.
- Cửa sổ mặc định 1280 × 760, giới hạn theo vùng màn hình làm việc; kích thước tối thiểu vẫn 960 × 560. Giảm lề nội dung, tăng khoảng cách hàng trong bảng quyền và bỏ hàng nút ngang.
- Giữ nền xám sáng/thanh trái màu slate, nút vuông, chữ sáng trên menu và phản hồi khi rê chuột hoặc dùng bàn phím.

## Hoạt động

Bấm mục bên trái mở đúng màn hình qua các handler/service sẵn có. Đăng nhập đổi menu Khách sang nhân viên; đăng xuất xóa dữ liệu nhân viên và trả menu Khách. Khi quyền bị thu hồi, kiểm tra hiện hành vẫn chặn thao tác và cập nhật menu như trước.

Đã đối chiếu plan WPF và bản tham chiếu web về menu/quyền. Không thay database, transaction hoặc quy tắc nghiệp vụ. Bảng quyền là màn hình tạm hiện có; dashboard hoạt động quán/doanh thu và quản trị RBAC theo vai trò vẫn thuộc bước 8. Bước nghiệp vụ tiếp theo vẫn là 6b.2 UI khách trực tiếp.

## Kiểm tra

- Build Debug và Release bằng MSBuild Visual Studio, dùng thư mục VerifyDebug/VerifyRelease để không ghi đè ứng dụng người dùng đang chạy.
- Verify-Authentication: bootstrap, đăng nhập/đăng xuất, quyền hiện hành và thu hồi phiên trên database tạm.
- Verify-AuthenticationUi: các luồng UI hiện có, kiểm tra menu dọc không chồng nhau, tách Khách/nhân viên, nội dung không chồng thanh trái, đăng nhập/đăng xuất còn nhìn thấy ở kích thước tối thiểu. Render 57 ảnh, gồm Khách/Admin thường và thu nhỏ; xem trực tiếp ảnh để kiểm tra bố cục và màu chữ.
- Không sử dụng hoặc chỉnh sửa database thật. Để thấy bản mới trong Visual Studio, dừng phiên chạy cũ rồi chạy lại bằng F5.
