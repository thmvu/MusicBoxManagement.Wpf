# MySQL 06a — Doanh thu và Excel doanh thu

Ngày 29/10/2026. Đối chiếu reference mục 5/31.1/31.2/31.4, ReportService và các báo cáo SQLite hiện có. Thêm MySqlReportService.ReadRevenue/ExportRevenue và ForReports theo Report.View độc lập quyền chi tiết khác.

Admin hoặc Manager có Report.View hiện hành được xem; Staff bị chặn kể cả gán nhầm Report.View/Export. Export cần thêm Report.Export hiện hành. Quyền và dữ liệu cùng RR read-only snapshot, một UTC clock; không audit/maintenance/ghi dữ liệu/cache. Ngày From–To theo ngày Việt Nam, bỏ phần giờ/Kind, [đầu From, đầu ngày sau To), lọc Standard/VIP trim/uppercase. MySQL UTC O cố định so sánh exact ticks, DECIMAL đọc GetDecimal/cộng bằng decimal, tràn trả lỗi rõ ràng, không double/Int64 hoặc SQL SUM gây đổi precision.

Nguồn là Invoice.PaidAt và tiền đã chốt, không ngày sử dụng/booking hoặc ước tính Active. Group ngày/tháng/RoomTypeCodeSnapshot, giữ tiền phòng/dịch vụ/tổng/số hóa đơn và DTO chỉ đọc hiện có. Đổi RoomType/khóa phòng/giá món không phân loại lại hoặc tính lại lịch sử. Không thêm bảng Report.

Export trả ReportExcelFile bằng ReportExcelWriter hiện có, dùng chính cùng core đọc/quyền/bộ lọc và ba chế độ Day/Month/RoomType. Giữ số tiền lớn chính xác trong XLSX; chưa nối VM/hộp thoại Save hoặc đổi nguồn UI. Không coi Excel doanh thu xong là các báo cáo phòng/booking/món và Excel của chúng đã chuyển.

## Bằng chứng kiểm tra

Build VerifyRevenueMySqlDebug/Release đạt. Verify-MySqlRevenueReport online cả Debug/Release đạt exit 0: %TEMP%/MusicBoxRevenueMySqlDebug_20261029.log và %TEMP%/MusicBoxRevenueMySqlRelease_20261029.log. Production source và bài mới không sửa sau hai lượt này. Hồi quy cũ chốt theo summary runner bên dưới. Verify-MySqlRevenueReport tạo GUID database/guard cleanup/log lỗi trước finally. Tất cả Completed/Invoice qua walk-in/Order/Checkout MySQL thật, không SQL giả Invoice/Completed. SQL chỉ seed catalog/user, thay giá snapshot cho max-rate fixture, metadata/quyền hiện hành.

Kiểm tra quyền độc lập/Staff misgrant/Manager View-only/Export grant–revoke; rỗng/date sai/Kind/type; checkout đúng đầu ngày, một tick trước cuối kỳ và đúng đầu kỳ sau; nhóm ngày/tháng/type; tiền lớn vượt Int64; loại Active dù có món Completed; đổi danh mục/phòng khóa; XLSX ba chế độ XML/tổng chính xác/không formula. Clock hook sau ReadAccess cho checkout thật và revoke Report.View commit trước reader đọc tiền: reader cũ giữ doanh thu/quyền cũ, lần sau thấy Invoice mới hoặc bị từ chối. Kiểm tra stamp/active/current role/logout/schema version. Tràn tổng decimal giữ guard production; bài MySQL này chưa tạo fixture tràn tổng hoặc thử mọi tổ hợp XLSX/driver Excel.

Chạy `powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File Tests/Verify-MySqlRevenueReport.ps1 -BuildDirectory MusicBoxManagement.Wpf/bin/VerifyRevenueMySqlDebug` (đổi Release để chạy bản Release).

App/worker/UI vẫn SQLite v8, MySQL v1, chưa nhập dữ liệu/Content/cutover. Chỉ Compile entry MySqlReportService; định dạng csproj có sẵn ngoài commit. Tiếp báo cáo sử dụng phòng, rồi booking/món/Excel tương ứng/history, worker/provider/UI/import/đối chiếu/ERD/từ điển/demo/máy in thật.

Nghiệm thu 29/10: build VerifyRevenueMySqlDebug/Release và bài MySqlRevenueReport online hai build đều đạt exit 0, log %TEMP%/MusicBoxRevenueMySqlDebug_20261029.log và %TEMP%/MusicBoxRevenueMySqlRelease_20261029.log. Hồi quy Debug 11/11 bộ liên quan đạt: 3 MySQL (bài mới + Authentication/Foundation) và 8 SQLite/WPF (RevenueReport/RevenueExcel/RevenueReportUi/Dashboard/Invoices/Authentication/Foundation/AuthenticationUi). Summary 10 bộ hồi quy %TEMP%/MusicBoxRevenueRegression_e1753feb19464ea0804e8267c9eeaab5/results.json; AuthenticationUi 142 ảnh tại %TEMP%/MusicBoxUi_469ed58f01e946de8a6d193e4ebe8a21. Không sửa assertion cũ/UI/provider/import; không tuyên bố chạy cả 69 bộ có sẵn hoặc đã chuyển các báo cáo/Excel khác.
