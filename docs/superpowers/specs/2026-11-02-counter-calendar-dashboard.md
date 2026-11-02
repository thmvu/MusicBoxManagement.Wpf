# Lịch phòng và dashboard vận hành

User đã chốt ngày/tất cả phòng mặc định, chuyển tuần, lịch tháng nhỏ và dashboard2D rõ ràng; đồng ý triển khai sau đề xuất. Tham khảo bố cục timetable trong ảnh, không sao chép nghiệp vụ trường học hoặc palette xanh web.

Lịch: giờ dọc09–23, khoảng nghỉ12–13, cột phòng ở Ngày; cột thứ2–CN ở Tuần, ưu tiên chọn một phòng khi chuyển Tuần. Khối đúng tỷ lệ giờ, giữ actual/expected/overlap/grace/overdue/locked history. Header và giờ tách vùng cuộn, chọn ngày bằng lịch tháng; chi tiết không che lịch. Không kéo thả để đổi giờ vì nghiệp vụ hiện hành không sửa trực tiếp booking. Không gộp walk-in thành future reservation.

Dashboard: board phòng2D, nhãn+chữ trạng thái, mức sử dụng theo số phòng hiện có (không phải báo cáo occupancy thời gian), cảnh báo/đơn chờ/lịch hôm nay; revenue giữ HasRevenue hiện hành. Không đồ thị doanh thu7ngày từ dữ liệu giả hoặc query mới. Vector WPF cho hình phòng, không3D xoay/animation trang trí. Giữ palette vàSegoeUI, layout thường/compact.

Booking UI: chung mục Đặt phòng cho danh sách+tạo đặt hộ; người Create-only vẫn tạo mà không phải có View; View-only không được cấp Create. Guest giữ Đặt/Tra cứu, check-in từ booking và walk-in riêng về nghiệp vụ. Lịch phòng có lối vào rõ từ màn nhân viên; kiểm quyền qua handler hiện hành. Các nút quick-create dùng tên Tạo đặt phòng nhất quán.

Không chạm backend/schema/SQL/data/provider; không đọc/sửa/chạy thử backend cũ bị người dùng cấm. Không Verify-All hoặc runner pha provider. Chỉ WPF/MySQL fixture GUID, không dữ liệu quán; giữ csproj diff có sẵn171/49 ngoài commit. Không cài dependency.

Alternatives: mô hình3D/kitUI mới tốn package/cấu hình và không phục vụ thao tác đã chốt; giữ danh sách hiện tại không đáp ứng ảnh. Chọn native WPF2D+service/DTO hiện có.
