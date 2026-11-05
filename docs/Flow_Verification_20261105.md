# Kiểm thử luồng MySQL/Aiven và ảnh WPF — 05/11/2026

## Phạm vi và môi trường

Người dùng yêu cầu chạy các trường hợp luồng nghiêm túc và gửi ảnh trực tiếp trong chat. Lượt này build lại Debug/Release dùng thật, chạy lại các suite MySQL độc lập và WPF kết nối MySQL. Không lấy kết quả 04/11 làm kết quả mới.

Product được kiểm là commit `20788df`; các thay đổi mới chỉ ở runner, assertion/capture của test và tài liệu. Giữ riêng diff csproj có sẵn 171/49. Hash executable lưu ở [build-hashes.json](verification/20261105-flows/build-hashes.json).

Database fixture có tên GUID riêng, dùng tài khoản/khách/phòng giả; các ca giờ nghỉ dùng IClock kiểm thử để không phải chờ đúng giờ trưa/đóng cửa. Các cửa sổ là WPF thật và service MySQL thật; ảnh render từ visual tree của cửa sổ trong lúc chạy, không phải mockup hoặc ảnh AI. Đây không phải một lượt click tay trên dữ liệu quán. Mỗi suite dùng fixture riêng; ví dụ chuỗi Feedback24.000đ, Operations110.000đ và hóa đơn lịch sử53.000đ không phải cùng một giao dịch để so tổng giữa các nhóm ảnh. Suite cạnh tranh tạo nhiều writer/read thật và có gate/clock có kiểm soát.

Allowlist gồm 29 nhóm service/nền tảng và 5 nhóm UI. Hai nhóm cũ bị loại khỏi phạm vi là `MySqlAuthenticationChecks` có gọi trực tiếp backend cũ và Import; không chạy hoặc sửa chúng để lách chỉ đạo không dùng SQLite. Đăng nhập MySQL hiện hành vẫn được kiểm qua Account và đăng nhập cửa sổ thật trong OperationsUi. Không coi việc bỏ bài cross-provider là đã kiểm cross-provider.

## Ma trận nghiệp vụ đối chiếu reference mục 53

| Luồng | Nhóm kiểm tra | Nội dung trọng tâm |
|---|---|---|
| Nền tảng | Foundation, Initialize | Schema/version, ràng buộc FK/CHECK, seed lặp, UTC tick, decimal, invoice bất biến, khởi tạo đồng thời |
| Tài khoản, vai trò | Account, AccountBatch, PermissionMatrix | Identity/password reset, đổi vai trò/khóa làm mất phiên cũ, Admin cuối, Staff không Report, quyền quản trị Admin-only, stale revision, rollback audit, quyền sau chờ |
| Nhật ký | AuditLog | Đọc không ghi, ngày Việt Nam/tick/cursor/filter, snapshot, quyền hiện hành |
| Danh mục | RoomType, Room, Service, Customer | Nhập sai, stale/concurrent edit, ảnh local, khóa phòng khi có lịch/Active, ngừng/bán lại món, SĐT canonical/unique, quyền và rollback |
| Đặt phòng | Availability, Reservation, BookingRead, StaffReservation | Guest/đặt hộ, cùng phòng/cùng khách chồng giờ, giờ nghỉ/slot/duration, NoShow/grace, hủy và lý do, catalog/lịch public, quyền riêng View/Create/Cancel |
| Nhận phòng | CheckIn | Sớm không giới hạn 30 phút khi đủ điều kiện, actual duration/tick/ca, Active/locked/overlap, replay, Room/Customer cạnh tranh, NoShow hai thứ tự, clock sau khóa |
| Khách trực tiếp | WalkIn | Không tạo booking/ExpectedEndTime, ReturnBy theo ca/lịch phòng/khách, không giữ lịch tương lai vô hạn, tranh chấp Room/Customer/booking/check-in/khóa phòng, rollback |
| Phiên/gia hạn/tra cứu | Sessions, GuestSessions | +30/+60, observed end, quá giờ/ca/trùng lịch, SĐT hiện hành, hủy Guest đúng mốc 2 giờ, không lộ PII, quyền/snapshot/rollback, writer thật đồng thời |
| Món | Order, OrderBatch | Menu/giỏ, gộp quantity/max10, snapshot giá/tên, Pending/Completed/Cancelled, confirm-cancel, catalog wait, phiên đã hoàn tất, quyền hiện hành, snapshot/read budget |
| Tiền và trả phòng | Billing, Checkout | Actual ticks/decimal/làm tròn, Completed-only, confirm lấy clock mới, Pending hủy, một Invoice/replay, lỗi rollback đủ, checkout với order/booking/cancel/extension thật |
| Hóa đơn | Invoice, InvoicePrinting | Filter/mã/SĐT/ngày, snapshot không tính lại lịch sử, quyền xem/in, revoke giữa dialog, preview/4 trang/70 dòng, cancel/reprint/busy/queue failure, máy in giả |
| Lịch/dashboard | Calendar, Dashboard, ReadBatch, NoShowLock | Snapshot, Room/Customer ReturnBy, cảnh báo, Staff không tổng doanh thu, không N+1, lịch sử không bị khóa range, source ordering và batch128 |
| Báo cáo/Excel/lịch sử khách | RevenueReport, RemainingReads | PaidAt/CreatedAt/StartTime đúng từng báo cáo, ca mở cửa, tiền decimal lớn, snapshot lịch sử, ngày/tick, bốn loại Excel, quyền View/Export và revoke |
| UI đầu-cuối | FeedbackUi | Nhập sai giữ form/dữ liệu/audit, sửa +84, trùng lịch, tra cứu tab Active/gia hạn, giỏ/đơn, tiền tạm tính khớp checkout/Invoice, walk-in qua hai nút, hủy Guest, revoke |
| UI giờ nghỉ | BookingHoursUi | Cả Guest và route đặt hộ: 11:00/90,22:00/90 bị chặn trước service; kết thúc đúng12/23 có cảnh báo; check-in muộn và đổi giờ trước Confirm; backend vẫn rollback |
| UI quầy/báo cáo | CounterUi, OperationsUi | Lịch Ngày/Tuần/tháng/cuộn/resize/chỉ đọc một lần; dashboard Admin/Staff; View-only/Create-only; wiring MySQL/worker, đăng nhập thật, doanh thu từ checkout, báo cáo và lịch sử khách |

Đây là ma trận các assertion hiện có và assertion bổ sung, không phải tuyên bố đã thử mọi tổ hợp đầu vào có thể tồn tại. Các nhóm service không tự chứng minh mọi nút trên từng form CRUD đã được click ở UI.

## Kết quả cuối

**34/34 nhóm Release đạt kết quả cuối**: 29 nhóm service/nền tảng và 5 nhóm UI. Thêm hai nhóm UI Debug đạt: Hours18 popup và Feedback36 popup. Build dùng thật Debug/Release exit0. Đây là số nhóm suite, không phải số test case; mỗi nhóm có nhiều assertion/đầu vào/race.

Release Hours lượt cuối18 popup; Feedback lượt cuối36 popup; Counter8 ảnh; Operations6 ảnh; InvoicePrinting7 ảnh với hóa đơn70 dòng/4 trang. Gallery chọn117 ảnh; tổng raw304 ảnh ở mọi lượt, gồm ảnh đầu/capture chưa hoàn chỉnh. Không dùng số ảnh thay số ca đã kiểm.

[Manifest kết quả và lịch sử lượt lỗi](verification/20261105-flows/summary.json), [hash ảnh](verification/20261105-flows/image-manifest.json). Hai lượt OperationsUi không đạt (-1/1) là lỗi driver test được giữ và thay bằng lượt corrected đạt; các lỗi compile loader chưa tới nghiệp vụ cũng giữ log riêng. Không cần sửa product hoặc thay nghiệp vụ để test qua.

| Suite | Build | Exit | Giây | Bằng chứng |
|---|---|---|---|---|
| Foundation | Release | 0 | 28.8 | [log](verification/20261105-flows/foundation-release.log) |
| Initialize | Release | 0 | 37.3 | [log](verification/20261105-flows/initialize-release.log) |
| Account | Release | 0 | 105.2 | [log](verification/20261105-flows/account-release.log) |
| AccountBatch | Release | 0 | 26.2 | [log](verification/20261105-flows/accountbatch-release.log) |
| PermissionMatrix | Release | 0 | 105.2 | [log](verification/20261105-flows/permissionmatrix-release.log) |
| AuditLog | Release | 0 | 69.3 | [log](verification/20261105-flows/auditlog-release.log) |
| RoomType | Release | 0 | 50 | [log](verification/20261105-flows/roomtype-release.log) |
| Room | Release | 0 | 133.5 | [log](verification/20261105-flows/room-release.log) |
| Service | Release | 0 | 55.3 | [log](verification/20261105-flows/service-release.log) |
| Customer | Release | 0 | 56.8 | [log](verification/20261105-flows/customer-release.log) |
| RevenueReport | Release | 0 | 75.2 | [log](verification/20261105-flows/revenuereport-release.log) |
| RemainingReads | Release | 0 | 108.6 | [log](verification/20261105-flows/remainingreads-release.log) |
| ReadBatch | Release | 0 | 155.2 | [log](verification/20261105-flows/readbatch-release.log) |
| OrderBatch | Release | 0 | 51.6 | [log](verification/20261105-flows/orderbatch-release.log) |
| Availability | Release | 0 | 64.5 | [log](verification/20261105-flows/availability-release.log) |
| Reservation | Release | 0 | 126.9 | [log](verification/20261105-flows/reservation-release.log) |
| BookingRead | Release | 0 | 70.6 | [log](verification/20261105-flows/bookingread-release.log) |
| StaffReservation | Release | 0 | 74 | [log](verification/20261105-flows/staffreservation-release.log) |
| CheckIn | Release | 0 | 154.9 | [log](verification/20261105-flows/checkin-release.log) |
| WalkIn | Release | 0 | 164.5 | [log](verification/20261105-flows/walkin-release.log) |
| Sessions | Release | 0 | 119.5 | [log](verification/20261105-flows/sessions-release.log) |
| GuestSessions | Release | 0 | 247.5 | [log](verification/20261105-flows/guestsessions-release.log) |
| Order | Release | 0 | 250.5 | [log](verification/20261105-flows/order-release.log) |
| Billing | Release | 0 | 138.7 | [log](verification/20261105-flows/billing-release.log) |
| Checkout | Release | 0 | 338.5 | [log](verification/20261105-flows/checkout-release.log) |
| Invoice | Release | 0 | 101 | [log](verification/20261105-flows/invoice-release.log) |
| Calendar | Release | 0 | 44.3 | [log](verification/20261105-flows/calendar-release.log) |
| Dashboard | Release | 0 | 151.1 | [log](verification/20261105-flows/dashboard-release.log) |
| NoShowLock | Release | 0 | 41.2 | [log](verification/20261105-flows/noshowlock-release.log) |
| BookingHoursUi | Release | 0 | 68.3 | [log](verification/20261105-flows/bookinghoursui-release-corrected.log) |
| FeedbackUi | Release | 0 | 91.7 | [log](verification/20261105-flows/feedbackui-release-final.log) |
| CounterUi | Release | 0 | 51 | [log](verification/20261105-flows/counterui-release.log) |
| OperationsUi | Release | 0 | 43.6 | [log](verification/20261105-flows/operationsui-release-corrected.log) |
| InvoicePrinting | Release | 0 | 100.5 | [log](verification/20261105-flows/invoiceprinting-release.log) |
| BookingHoursUi | Debug | 0 | 65.6 | [log](verification/20261105-flows/bookinghoursui-debug.log) |
| FeedbackUi | Debug | 0 | 76.7 | [log](verification/20261105-flows/feedbackui-debug.log) |

[Xem gallery 117 ảnh ở các lượt cuối](Flow_Test_Gallery_20261105.html): popup, form đặt/tra cứu/gia hạn/giỏ món/walk-in/tiền/checkout, lịch/dashboard, đăng nhập/báo cáo/lịch sử và preview hóa đơn. Các ảnh/raw run đầu vẫn được giữ riêng, không lấy ảnh đẹp của lượt cũ thay ảnh lượt mới. Một số ảnh ngoài client có vùng đen từ render cửa sổ; nội dung cần đối chiếu được xem trực tiếp trước khi đưa vào chat. Fixture in hóa đơn dùng ngày lịch sử 13/10 để kiểm snapshot, không phải hóa đơn quán ngày05/11.

Một review độc lập đọc runner/diff test/claims, không chạy test thay lượt thực thi. Không có Critical/Important được tìm thấy; góp ý nhỏ cập nhật số36 popup sau khi chạy đã được xử lý. Phần suite còn pending vẫn do người thực thi đối chiếu manifest cuối, không nhận review là bằng chứng mọi test đã chạy.

## Lỗi bộ chạy gặp trong lượt này

Giữ cả lượt đầu, không chỉ lưu log xanh:

1. Loader CodeDom mới nhận tên assembly thay vì đường dẫn vật lý: BookingHoursUi và Availability chưa chạy tới nghiệp vụ. Sửa reference thành executable path. Log `*-loader-failure.log`.
2. Suite Room chưa compile do thiếu reference `System.Memory` chứa ReadOnlySpan trong API SkiaSharp. Runtime đã load dependency nhưng CodeDom chưa được cấp reference; thêm các DLL đi kèm build. Parent catalog đầu dừng trước khi ghi result Room; lưu ghi chú output ở `room-loader-parent-output.txt`. Không nới compiler assertion, chỉ giữ cảnh báo hợp nhất1701 như runner phòng cũ.
3. OperationsUi cũ bị chờ popup đăng nhập thành công sau thay đổi thông báo UI. Cửa sổ test không xuất hiện trong inventory desktop, không dùng thao tác mù. Dừng đúng child test PID26184 thuộc lượt này, không dừng Visual Studio/app người dùng; dọn đúng GUID `musicbox_test_004bbcd29f62450c82d495a5af823cc6`, có [log cleanup](verification/20261105-flows/operationsui-interrupted-cleanup.log). Manifest giữ exit -1.
4. Driver đăng nhập mới lượt đầu kiểm số popup trước khi async Login_Click hoàn tất nên báo missing/repeated. Đã chuyển assertion ra sau Pump đợi StaffPanel/dashboard; không bỏ assertion một popup. Có log `operationsui-release-final.log` và manifest exit1. Lượt corrected kiểm popup thực, đăng nhập, dashboard, báo cáo, lịch sử và quyền sau revoke.
5. Capture popup lúc mới hiện có ảnh thiếu nền/nút. Giữ ảnh/lượt đầu trong nhóm riêng; thêm chờ một nhịp render trước capture và tạm dừng timer đóng popup để không re-enter. Không sửa nội dung ảnh sau chụp hoặc sửa product. Ảnh dùng trong chat/gallery ưu tiên lượt cuối đã xem được nội dung.

FeedbackUi Release đầu có34 popup. Bổ sung assertion tab Active hiển thị S01, tiền tạm tính Guest7phút+1món=24.000đ và walk-in qua hai nút không sinh Reservation/ExpectedEndTime; Debug/Release có36 popup ở lượt bổ sung. Không lấy34 của lượt đầu để khẳng định đã chạy hai ca UI mới.

## Cách chạy lại

Chạy Windows PowerShell Desktop với STA, chọn rõ suite:

```powershell
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File Tests/Verify-MySqlFlows.ps1 -Suite Checkout -BuildDirectory MusicBoxManagement.Wpf/bin/Release
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File Tests/Verify-MySqlFlows.ps1 -Suite FeedbackUi -BuildDirectory MusicBoxManagement.Wpf/bin/Debug -OutputDirectory "$env:TEMP/MusicBoxFeedbackReview"
```

Runner allowlist không sửa các runner pha provider cũ; source có dependency SQLite/Import bị chặn trước khi compile. Không dùng Verify-All/IncludeMySql. Các suite tạo dữ liệu ở database GUID và tự dọn trong finally; chỉ dùng credential môi trường hiện có, không ghi password vào report/log.

## Giới hạn

- Không thử dữ liệu quán, mạng tại quầy của máy khác hoặc tài khoản thật của người dùng. Mạng Aiven có thể timeout ở thời điểm khác; kết quả không phải SLA.
- Không in giấy/driver thật; toàn bộ gửi lệnh in dùng printer giả. Preview và phân trang không thay nghiệm thu in giấy.
- Không chạy import/seed/cutover, suite backend bị cấm hoặc bài authentication cross-provider. Không suy rộng đây là kiểm thử an ninh toàn diện.
- GUI tự động có clock/fixture/gate xác định, không đại diện mọi lịch sử dữ liệu hoặc mọi thứ tự scheduler có thể xảy ra. Backend vẫn phải kiểm quyền/clock/lịch sau khóa và rollback khi điều kiện thay đổi.
- Không tuyên bố mọi lỗi đã hết hoặc bồi thường tài khoản. Các kết quả có ý nghĩa trong phạm vi assertion và build được ghi ở report.
