# Bước 8e.5b — Excel phòng, booking và món/top món

Ngày 18/10/2026. Tiếp Excel doanh thu 8e.5a, bổ sung các báo cáo còn lại theo mục 31.4 Reference_Web_ProjectPlan_v1.3. Phần Excel của các báo cáo đang chạy WPF/SQLite đã được bao phủ; chưa đồng nghĩa chuyển provider sang MySQL.

## Cách dùng

Admin hoặc Manager có Report.View + Report.Export → mở báo cáo phòng/booking/món → chọn bộ lọc → Xem báo cáo → Xuất Excel → chọn nơi lưu .xlsx. Đổi bộ lọc phải xem lại trước xuất; View-only không bật nút xuất. Trong báo cáo món, chọn tab Doanh thu hoặc Top 10 trước khi xuất; file chỉ chứa tab đang chọn.

Mỗi file gồm tên báo cáo, kỳ Việt Nam, bộ lọc, thời điểm dữ liệu/xuất UTC+7, nguồn/mốc thời gian, tiêu đề, các dòng và tổng phù hợp. Có freeze header/filter cột; tên dài xuống dòng và tăng chiều cao để không cắt nội dung; không đòi cài Excel, MySQL hoặc thư viện NuGet mới.

## Dữ liệu và tổng

- **Phòng:** cùng RoomUsageReportService.ReadCore, giữ cả phòng khóa, lọc Room hiện hành. Lượt theo ActualEndTime; phút/tỷ lệ theo ca chuẩn đến CheckedAt, gồm phần thực tế Active. Tỷ lệ tổng lấy tổng phút dùng/tổng phút tiêu chuẩn, không trung bình các tỷ lệ phòng. Mẫu số 0 ghi Không có dữ liệu. Phút/tỷ lệ làm tròn hai chữ số như UI; tỷ lệ là ô số với format %, không chuỗi phần trăm giả.
- **Booking:** cùng BookingStatusReportService.ReadCore, lọc StartTime Việt Nam; đủ năm trạng thái và số 0. Confirmed hết đúng +15 phút được biểu diễn NoShow mà không cập nhật booking/audit. Tổng là số booking trong kỳ.
- **Món:** cùng ServiceSalesReportService.ReadCore, chỉ Order Completed của phiên có Invoice, PaidAt trong kỳ, ServiceId và snapshot tên/giá, có danh mục inactive. Tab doanh thu xuất mọi dòng hợp lệ; tab Top 10 giữ đúng thứ tự và chỉ cộng các món thuộc Top 10, có nhãn rõ trong file. Không dùng tổng toàn bộ dịch vụ làm tổng của bảng Top 10.
- **Tiền lớn:** giữ cơ chế 8e.5a, số tiền >15 chữ số ghi Text đầy đủ kèm đ; số bình thường numeric. Không macro/formula/external link hoặc dữ liệu khách/hóa đơn riêng tư. Tên bắt đầu dấu = vẫn là inline text, không thực thi công thức.

## Luồng code và quyền

Các service bổ sung Export(...), gọi ReadCore(...,forExport:true); View gọi cùng ReadCore với false. Quyền role Admin/Manager + Report.View + Report.Export được kiểm tra trong cùng read snapshot với dữ liệu. CanExport trên DTO chỉ phục vụ UI. Không nhận DTO/cache từ client để bỏ kiểm tra quyền.

ReportExcelWriter thay tên RevenueExcelWriter và dùng chung package/style/ô dữ liệu cho các báo cáo; CreateRevenue giữ hợp đồng cũ. ReportExportWorkflow dùng cho ba VM mới: query/kiểm tra trước dialog rồi query/kiểm tra lại trước SaveTo, không giữ transaction trong dialog. Tái sử dụng ReportExcelFile ghi tạm và replace an toàn.

VM khóa busy/filter/gửi lặp/đóng và tab món trong lúc xuất. Hủy dialog không ghi. Mất quyền ở dialog chặn ghi, giữ file đích cũ, xóa báo cáo/filter/danh mục cũ. Tab món được bind SelectedTab rõ ràng, xuất từ filter của báo cáo đã tải; timestamp cho biết dữ liệu mới đọc khi xuất. File dùng query mới, bảng trên màn hình có thể được tải lại bằng Xem báo cáo sau khi xuất.

## Kiểm tra

- Debug/Release VerifyExcelAllDebug/Release build đạt.
- Verify-ReportTablesExcel bản cuối cả hai build đạt: role/View+Export độc lập các quyền khác, cùng query/filter/clock; phòng khóa/weighted total/null denominator/rounding/tên dài; năm trạng thái/grace/no writes; ServiceId/snapshot/inactive/Top10-only totals; text bắt đầu =/UTF-8/XML escaping; package XML/namespace, không PII.
- Verify-RevenueExcel cả hai build đạt sau khi dùng writer chung, giữ grouping/decimal.MaxValue/atomic-file safety của 8e.5a.
- RoomUsageReportUi/BookingStatusReportUi/ServiceSalesReportUi cả hai build đạt, lần lượt 8/7/8 ảnh mỗi build; đã xem nút Excel ở 960×640. Kiểm tra save/cancel/revoke trong dialog bằng callback, busy/gửi lặp/filter/đóng và tab Top10 thực.
- Hồi quy Debug RoomUsageReport/BookingStatusReport/ServiceSalesReport/RevenueReport/Authentication/PermissionMatrix/Foundation/RevenueReportUi đạt. AuthenticationUi đạt 131 ảnh. Không tuyên bố chạy lại toàn bộ mọi bộ SQLite/MySQL trong lượt này.
- Năm file mẫu được đọc lại bằng openpyxl chỉ đọc và import/render artifact-tool read-only; đã xem phòng/tên dài, booking, doanh thu món, món lọc/inactive và Top10. Chưa mở Microsoft Excel/native SaveFileDialog thật trong kiểm tra.

App vẫn SQLite v8/MySQL v1, không thay worker/provider/schema, không nhập hoặc sửa database/ảnh thật, không gửi máy in. MySQL giữ bước 4c.2a; báo cáo/quản trị/Excel cũng cần port trước cutover. Tiếp lịch sử khách hàng và các luồng MySQL còn lại; giữ ERD/từ điển dữ liệu/demo, sao lưu/nhập/đối chiếu database + Content và kiểm tra UI riêng.
