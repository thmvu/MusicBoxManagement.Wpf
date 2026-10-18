# Bước 8e.5a — Xuất báo cáo doanh thu Excel

Ngày 18/10/2026. Tiếp bước báo cáo món 8e.4, triển khai phần đầu Excel mục 31.4 trong Reference_Web_ProjectPlan_v1.3. Chỉ báo cáo doanh thu ngày/tháng/loại phòng có xuất Excel ở bước này; phòng, booking và món/top món vẫn giữ đầu việc xuất riêng tiếp theo.

## Cách dùng

Admin hoặc Manager có Report.View và Report.Export → Báo cáo doanh thu → chọn kỳ/loại phòng/cách nhóm → Xem báo cáo → Xuất Excel → chọn nơi lưu file .xlsx. View-only vẫn xem được báo cáo nhưng nút Xuất Excel không bật. Đổi bộ lọc xóa kết quả và khóa xuất cho đến khi xem lại báo cáo.

File có tên báo cáo, kỳ Việt Nam, loại phòng snapshot, cách nhóm, thời điểm dữ liệu/xuất UTC+7, tiêu đề cột, chi tiết và tổng. Chỉ xuất cách nhóm đang xem (ngày/tháng/loại phòng), không tạo nhiều tab không yêu cầu. Có freeze header/filter cột, kiểu tiền và cột rộng đủ đọc; kết quả rỗng vẫn có tổng 0.

## Luồng code và quyền

RevenueReportWindow.Export_Click → RevenueReportViewModel.ExportAsync → ReportService.ExportRevenue → cùng ReadRevenueCore với màn hình → RevenueExcelWriter → ReportExcelFile.SaveTo.

- ReadRevenueCore giữ nguyên query PaidAt/ticks/RoomType snapshot/decimal. Bản xuất kiểm tra role Admin/Manager, Report.View và Report.Export trong cùng transaction read snapshot với dữ liệu; không đòi Dashboard/Invoice.View. Staff gán nhầm vẫn bị chặn.
- CanExport trên DTO chỉ phục vụ nút UI. Service công khai ExportRevenue luôn kiểm tra quyền hiện hành; không xuất từ DTO/cache được truyền vào.
- Trước mở SaveFileDialog, query/kiểm tra quyền; sau dialog query/kiểm tra lại rồi ghi. Không giữ transaction khi chờ người dùng. Xuất theo bộ lọc của báo cáo đã tải, đọc dữ liệu mới tại lúc xuất; có timestamp trong file và thông báo tải lại bảng khi cần.
- Busy chặn gửi lặp/filter/đóng. Hủy dialog không ghi. Mất quyền/stamp/phiên trong dialog bị chặn và xóa kết quả/bộ lọc cũ. Không gửi máy in hoặc mở hộp thoại lưu thật trong kiểm tra tự động.
- Ghi file tạm cùng thư mục, sau đó move/replace; lỗi ghi/đích đang mở không làm hỏng file cũ và dọn file tạm. Không tự tạo thư mục hoặc ghi CSV giả tên .xlsx.

## Định dạng và độ chính xác

Dùng thư viện chuẩn .NET Framework System.IO.Compression/System.Xml để tạo gói Open XML .xlsx, không cần cài Excel hoặc thêm dependency NuGet. Cấu trúc theo [SpreadsheetML của Microsoft](https://learn.microsoft.com/en-us/office/open-xml/spreadsheet/structure-of-a-spreadsheetml-document).

Tiền thông thường là ô số, format đơn vị đồng. Excel chỉ giữ khoảng 15 chữ số có nghĩa, vì vậy tiền có hơn 15 chữ số được ghi inline text, format Text, đủ chữ số và hậu tố “đ”; có ghi chú ngay đầu file. Các ô này không dùng SUM số trực tiếp. Tổng là kết quả decimal của query nghiệp vụ, không tính lại bằng double/công thức Excel dễ mất chính xác. Không macro/formula/external link/PII hoặc mã hóa đơn riêng tư trong báo cáo tổng hợp. Chặn vượt số dòng worksheet, giữ giới hạn ngày/overflow hiện tại.

## Kiểm tra đã chạy

- Build Debug/Release VerifyExcelDebug/VerifyExcelRelease đạt.
- Verify-RevenueExcel đạt cả hai build: View+Export/role hiện hành, cùng query/filter/ba cách nhóm/tổng/empty, ZIP/XML/namespace, metadata, numeric/text chính xác đến decimal.MaxValue, không PII/formula/macro, ghi đè file đang khóa không mất bản cũ và dọn file tạm.
- Verify-RevenueReportUi đạt cả hai build, 9 ảnh/build: đã xem nút Excel ở 960×640; save/cancel bằng callback giả lập; khóa busy/gửi lặp/filter/đóng và revoke Export trong dialog giữ nguyên file đích/xóa report. SaveFileDialog thật được nối trong Window nhưng không mở tự động.
- Hồi quy Debug: RevenueReport/PermissionMatrix/Authentication/Foundation/Invoices/Checkout/Billing/Dashboard đều đạt trên DB tạm.
- Đọc lại bốn file mẫu bằng openpyxl (chỉ đọc, không tạo/ghi) xác nhận sheet/panes/numeric/Text/29 chữ số. Import/render read-only bằng artifact-tool, đã xem ngày/tháng/loại phòng và tiền lớn, không cắt số. Không chạy Microsoft Excel thật; chưa kiểm tra giao diện Excel theo từng phiên bản Office.

Không thay MySQL/provider/schema/worker hoặc dữ liệu/ảnh thật trong lượt này. App vẫn SQLite v8/MySQL v1. Excel phòng/booking/món, lịch sử khách, migration MySQL, ERD/từ điển dữ liệu/demo và nhập/đối chiếu database + Content vẫn thuộc scope.
