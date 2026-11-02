# Counter calendar/dashboard implementation plan

> Execute inline with superpowers:executing-plans/TDD; one whole-branch review at end, no implementer fan-out.

Goal: triển khai spec ../specs/2026-11-02-counter-calendar-dashboard.md đã được user chốt, dùng WPFnet472/MySQL. Frontend-design: sơ đồ phòng/lịch là trọng tâm, chữ dễ đọc, không charts giả/dependency mới.

Files: CalendarDayWindow.xaml/.cs (sticky axes/month/sizing/blocks); CalendarDayViewModel.cs (week default choice if needed); DashboardView.xaml/.cs and DashboardViewModel.cs (presentation only); MainWindow.xaml/.cs +ReservationsWindow.xaml (one booking entry/create-only route); Tests/MySqlCounterUiChecks.cs + Verify-MySqlCounterUi.ps1; docs/README/plan/status/AGENTS.

- [x] RED targeted UI fixture: MonthCalendar exists/selection reload/defaultDayAll; sticky axes fields; dashboard named2D board and compact render; unified booking label and rights. Real MySQL fixture, no mixed runners/providers. Capture baseline screenshot.
- [x] Calendar redesign: adapt columns, sticky hour/header, month nav, faithful overlap/expected/actual, keyboard event details. No new queries from layout/resize.
- [x] Dashboard2D native: room board and clear metrics/alerts/agenda, HasRevenue preserved, empty/loading/revoke/compact; no fabricated series.
- [x] Main navigation: singlebooking entry, Create-only fallback, View route existing, quickcalendar via sameguard, createcaption consistent.
- [x] GREEN newUI Debug/Release, screenshot inspect usual/compact/day/week/selected/empty/revoked/Staff noRevenue. MySQL-only relevant checks, no broad suites.
- [x] Final review, actual buildDebug/Release, updatehandoff. Preserve existingcsproj formatting171/49.
- Tích hợp sau xác minh: commit tiếng Việt và push theo ủy quyền hiện hành; kết quả Git ghi tại bàn giao trên chat.

Review focus: no doubleload from Calendar selection/navigation/busy, no header driftscroll, no clipped timelineevents, noPII afterrevoke, no new rolegrant/fake metrics. Test scripts must not explicitly load/call old provider; do not change tests to bypass rights.

Ledger: base c457526, only csproj dirty at entry. Existing Calendar is Canvas with scrolling headers; Dashboard already has snapshot/4counts/status board; ReservationsWindow already has Create, duplicate main menu entry can be hidden without a new business flow.

## Nhật ký triển khai

- Base c457526; nhánh codex/counter-ui-calendar. Không thay csproj: diff có sẵn 171/49 giữ ngoài staging.
- Ruling: dùng native WPF 2D và ledger ngay trong plan trên PowerShell hiện hành; không dựng framework UI hoặc thêm truy vấn để có biểu đồ trang trí. Không chạy công cụ/test pha provider. Quyền push đã được người dùng cho từ trước.
- RED: MonthCalendar thiếu, exit 1 trước khi tạo database. Sau đó triển khai lịch, dashboard và menu.
- Lượt GreenDebug đầu lỗi binding ProgressBar TwoWay với property chỉ đọc sau khi các kiểm tra lịch đạt; sửa OneWay và chạy lại đạt. Không bỏ assertion hay nới quyền/kết nối.
- Đã xem ảnh lịch ngày/tuần, dashboard Admin/Staff và compact. Sửa template nút khối lịch, chữ chế độ chọn và lịch tháng cho dễ đọc.
- Review cuối: một reviewer chỉ đọc; không Critical/Important xác nhận. Minor busy của quick-create đã sửa; thêm assertion. Concern về SelectedDatesChanged được quyết bằng test +10 ngày sang tuần khác, exact Range.Start và phòng thật; thêm Count để không All trên danh sách rỗng.
- FinalDebug2 và FinalRelease candidate đạt trên database MySQL GUID riêng. Không chạm database quán. Xem Ui_Calendar_Dashboard_20261102.md cho log thất bại/đạt và giới hạn.

- Complete: build dùng thật Debug/Release exit 0; Verify-MySqlCounterUi trên hai build cuối exit 0, 8 ảnh/build. Đã xem ảnh, lưu 8 ảnh Release và 6 log tại docs. Count Read initial/Today/mode/month/room = 1/2/3/4/5; scroll/resize không tăng. View-only/Create-only/Staff/revoke/busy đạt. Không mở rộng thành bằng chứng mọi nghiệp vụ hoặc mọi DPI.
- Ruling cho review declined: runtime/render do implementer kiểm và lưu ảnh/log; backend không thay nên không mở rộng test; production data, driver và csproj ngoài scope. Concern binding đã có assertion runtime, không hạ điều kiện.
