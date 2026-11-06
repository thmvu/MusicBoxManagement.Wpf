# Mục lục tài liệu Music Box

Ứng dụng hiện chạy **MySQL/Aiven**. Đọc [README chính](../README.md) để cấu hình và chạy; [plan WPF](../MusicBoxManagement_Wpf_ProjectPlan_v0.1.md) giữ tiến độ và phạm vi đã chốt.

## Làm báo cáo và học code

- [Nghiệp vụ tham chiếu đầy đủ](Reference_Web_ProjectPlan_v1.3.md).
- [Use case và luồng hoạt động](UseCases_And_Workflow_Review.md), [mã nguồn sơ đồ use case](MusicBox_UseCases.puml).
- [ERD 16 bảng và từ điển dữ liệu](ERD_Full_And_DataDictionary.md): [PNG](schema/MusicBox_Full.png), [SVG](schema/MusicBox_Full.svg), [Mermaid](schema/MusicBox_Full.mmd), [DOT](schema/MusicBox_Full.dot).
- [Giải thích MVC/MVVM và Services](Architecture_MVC_Services_Explanation.md), [lần theo lời gọi hàm](Code_Flow_Full_Guide.md): tài liệu học theo mốc 14/10, có chú thích phiên bản.
- [Contracts và wiring MySQL](MySql_Migration_06b_08_ReportsHistoryWiring.md) nối với runtime hiện hành.
- [Rà soát nghiệp vụ 24/10](Business_Workflow_Audit_20261024.md): lưu làm đối chiếu lịch sử, không thay tiến độ mới nhất.

## Chạy demo và bàn giao

- [Demo/vận hành Aiven](MySql_Demo_And_Operations.md).
- [Trạng thái bàn giao MySQL](MySql_Migration_Status.md).
- [Hồ sơ phòng/khách mẫu](MySql_Demo_Seed_20261030.md), [hồ sơ món mẫu](MySql_Service_Seed_20261031.md).
- [Hồ sơ cutover/khôi phục](MySql_Migration_09_Import_Cutover.md) giữ để tra cứu lịch sử; không tự chạy lại import trên dữ liệu đang vận hành.

## Kiểm thử và các sửa đổi gần đây

- [Tổng kiểm tra 05/11: ma trận, log và giới hạn](Flow_Verification_20261105.md), [gallery 117 ảnh](Flow_Test_Gallery_20261105.html).
- [Thông báo nghỉ trưa/đóng cửa khi đặt và nhận](Booking_Shift_Feedback_20261104.md).
- [Thông báo thao tác và SĐT](Ui_Feedback_Validation_20261104.md), [sửa khóa NoShow](MySql_NoShow_Lock_Fix_20261104.md).
- [Lịch phòng và dashboard](Ui_Calendar_Dashboard_20261102.md).
- [Tối ưu khởi tạo MySQL](MySql_Performance_Review_20261102.md), [tối ưu truy vấn theo luồng](MySql_Read_Performance_20261102.md).

## Tài liệu đã dọn

Ngày 06/11 đã gỡ 92 file nhật ký Step, báo cáo port từng phần và plan/spec đã hoàn tất. Không thay code, nghiệp vụ, dữ liệu, ảnh/assets, Tests/Tools hoặc bằng chứng kiểm thử. Liên kết tới file đã gỡ được chuyển sang bản cố định trong Git; [snapshot trước khi dọn](https://github.com/thmvu/MusicBoxManagement.Wpf/tree/72a0fa70cdec42769f00a2bd9d5826de4f2adb53/docs) lưu đầy đủ để phục hồi.
