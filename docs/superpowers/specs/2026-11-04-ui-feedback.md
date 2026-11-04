# Thông báo thao tác và nhập liệu — 04/11/2026

Yêu cầu trực tiếp: mọi thao tác Khách cần thông báo trên giao diện, lỗi nhập không được làm đóng ứng dụng hoặc chỉ hiện exception trong debugger. Tái hiện từ đặt phòng với SĐT 9 số. Đã có catch và Status nhưng chưa có popup; định dạng điện thoại chỉ được kiểm tra qua service ném exception.

Thiết kế: thêm một hộp thông báo WPF dùng chung, có owner, nội dung tiếng Việt và nút Đã hiểu; dùng tại các lệnh người dùng bấm để đặt/tra cứu/hủy/gia hạn/menu/lưu/xác nhận/tìm dữ liệu. Giữ Status trên form. Không popup khi chọn dòng, thay trường hoặc tải tự động ban đầu. Không đóng form nhập sai. UI command boundary có catch dự phòng và thông báo an toàn; không thêm global handler nuốt lỗi. Các service hiện hành vẫn giữ validation/quyền/transaction.

SĐT: thêm TryNormalize thuần dùng cùng một quy tắc với Normalize, kiểm định dạng trước gọi service ở đặt phòng, tra cứu, walk-in, khách hàng và các bộ lọc SĐT. Giữ 0 +10 chữ số, nhận +84/84, khoảng trắng/dấu chấm/gạch ngang như cũ. Không đổi service contracts, dữ liệu quán hoặc nghiệp vụ. Debugger Break on Thrown vẫn có thể dừng ở lỗi service khác; không sửa cấu hình debugger toàn máy.

Chỉ MySQL/Aiven và UI. Không chạy runner pha provider, không sửa code cũ bị cấm. Csproj chỉ stage Compile entry mới, giữ diff định dạng người dùng ngoài commit. User đã yêu cầu sửa trực tiếp, không yêu cầu lựa chọn thiết kế mới.
