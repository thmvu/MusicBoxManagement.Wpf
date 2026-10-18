# Bước 8e.4 — Doanh thu dịch vụ và top món

Ngày 18/10/2026. Đối chiếu mục 31.1–31.2 của Reference_Web_ProjectPlan_v1.3: món đã hoàn tất của phiên có hóa đơn, lọc PaidAt theo ngày Việt Nam, nhóm theo ServiceId. App vẫn dùng SQLite v8; không thêm bảng Report hoặc thay quy tắc gọi món/checkout.

## Cách sử dụng

Admin hoặc Manager có Report.View → menu **Doanh thu và top món**. Chọn Từ/Đến ngày và dịch vụ, bấm Xem báo cáo. Có hai tab:

- Doanh thu theo món: số lượng và tổng Quantity × UnitPrice snapshot, giảm dần theo doanh thu.
- Top 10 theo số lượng: giảm dần theo số lượng, rồi doanh thu, rồi mã dịch vụ tăng dần để ổn định khi bằng nhau.

Danh sách lọc gồm dịch vụ ngừng bán và hiển thị mã để phân biệt món trùng tên. Tên dòng báo cáo là snapshot của lần thanh toán gần nhất trong kỳ; không thay tên/giá lịch sử bằng danh mục hiện tại. Cùng ServiceId được gộp dù đã đổi tên, ServiceId khác nhau giữ riêng dù trùng tên. Không có giao dịch hợp lệ thì bảng rỗng/tổng 0, không dựng doanh thu giả từ danh mục.

## Luồng code

MainWindow.ShowServiceReports_Click → StaffReservationService.ForServiceReports → ServiceSalesReportWindow/ServiceSalesReportViewModel.SearchAsync → Task.Run(ServiceSalesReportService.Read).

Service kiểm tra role/quyền hiện hành trong cùng read transaction với danh mục và món; chỉ Admin/Manager Report.View, không đòi Dashboard/Service.Manage/Order.View/Invoice.View. Staff gán nhầm quyền vẫn bị chặn. Một IClock/read snapshot, chỉ đọc, không maintenance hoặc audit.

Query Invoices → Orders Completed → OrderItems, lọc [đầu FromDate, đầu ngày sau ToDate). Không cộng Pending/Cancelled hoặc món của phiên chưa có hóa đơn, không lọc theo CreatedAt. SQLite julianday chỉ dùng cửa sổ thô; quyết định vào kỳ dùng DateTimeOffset/ticks chính xác, hỗ trợ timestamp legacy có offset/Z, từ chối timestamp thiếu timezone nếu gặp trong tập ứng viên. Tiền tính bằng decimal, không SUM(double/TEXT); số lượng long có kiểm tra overflow. DTO/setter không public, collection kết quả chỉ đọc và không PII/ID khách/phiên/hóa đơn.

UI đổi bộ lọc xóa bảng/top/tổng cũ; lỗi xóa kết quả/lựa chọn cũ; mất quyền xóa cả kỳ. Busy khóa gửi lặp/thay filter/đóng; không giữ transaction trong lúc người dùng xem. Các giá trị hiển thị vi-VN, min 960×640 có vùng bảng cuộn. Không thêm Excel giả; xuất .xlsx theo bộ lọc vẫn là bước tiếp theo.

## Kiểm tra

- Verify-ServiceSalesReport: dữ liệu chính qua walk-in/Order/checkout thật; Active chưa invoice/Pending loại khỏi báo cáo; rename/inactive/snapshot/trùng tên/group theo ID; lọc Service/empty/Top10/tie; PaidAt biên ±1 tick/legacy offset/thiếu timezone; decimal vượt Int64; Staff/Manager View-only/revoke/stamp; snapshot quyền khi writer cạnh tranh, không ghi audit. Fixture sửa PaidAt riêng bằng cách bỏ trigger trên DB tạm để kiểm tra biên, không dùng trên DB thật.
- Verify-ServiceSalesReportUi: menu thật Guest/Staff/Manager/Admin; binding filter dịch vụ ngừng bán, hai tab, giá snapshot, empty/invalid/stale/revoked/busy/duplicate/close, full/compact; 7 ảnh/build, kiểm tra PNG và xem ảnh thực tế.
- Debug/Release build và hai bộ mới đạt cả hai cấu hình; hồi quy liên quan ghi tại AGENTS.md/plan. Tests dùng DB/ảnh tạm, không gửi máy in.

Chưa triển khai Excel hoặc service báo cáo này trên MySQL. Giữ các báo cáo cũ, lịch sử khách, ERD/từ điển dữ liệu/demo trong scope; không đổi provider hoặc nhập dữ liệu thật.
