# Tối ưu các luồng đọc MySQL — 02/11/2026

Tiếp [tối ưu khởi tạo](MySql_Performance_Review_20261102.md) theo yêu cầu người dùng tự động làm đủ và áp dụng Superpowers/Ponytail. Đây là rà đường dữ liệu và tối ưu những round-trip thừa được chứng minh, không cắt tính năng hay cache quyền/dữ liệu quán. [Plan và ledger](superpowers/plans/2026-11-02-mysql-read-batching.md).

## Vấn đề và thay đổi

| Đường đọc | Trước sửa đo bằng Com_select cùng session | Sau sửa / hợp đồng |
|---|---:|---|
| Guest/Staff booking ReadDay, 28 ô | 50 SELECT ở ngày trống duration60 | Room+Active1, Holds1; dựng slot trong bộ nhớ từ cùng RR/clock, không cache ngoài request |
| Dashboard trạng thái21 phòng | 43 SELECT |1 SELECT với CASE/EXISTS, không round-trip từng phòng |
| Staff calendar21 phòng trống |63 SELECT phần Populate, ngoài SELECT quyền/phòng |3 SELECT cho holds/booking/session, grouping RoomId; walk-in deadlines thêm1 SELECT khi có |
| ReturnBy5 walk-in |5 SELECT |1 SELECT với MIN booking theo Room **hoặc** Customer, chỉ requested IDs cùng RR |
| Order list4/5 đơn |9/11 SELECT |1 LEFT JOIN giữ header cả khi chưa có item, OrderId/OrderItemId order, dữ liệu snapshot/ticks/giá Int64 |
| List5 tài khoản |7 SELECT gồm Admin quyền |2 SELECT, giữ projection/revision cũ gồm passwordhash/stamp/role nội bộ, không lộ trong DTO |

Số lệnh SQL là phép đo cấu trúc; không nhân thành cam kết thời gian toàn app. Các truy vấn correlated vẫn làm lookup ở server và có thể tốn CPU khi dữ liệu lớn; mục tiêu này bỏ network N+1. Lần mở lạnh/mạng vẫn tốn thời gian; không đổi SSL, pooling/reset, timeout hoặc session sql_mode/timezone/lock_wait.

Guest calendar vẫn dùng BookingHours, khoảng nửa mở, grace strict Start>now−15phút và exact-now Active block. Walk-in chỉ chặn bắt đầu ngay, không thành future hold. Future booking và session từ booking giữ holds theo ExpectedEnd. Public DTO không thêm ID/PII; invalid/missing/inactive room vẫn báo như cũ. Staff cùng ReadAt được hưởng tối ưu, không bypass Reservation.Create.

RoomStatusSql dùng đúng ưu tiên Inactive→Occupied→Reserved→Available. Calendar vẫn đọc lịch sử phòng khóa, actual khác expected, không ghi NoShow. Ba bulk query dùng cùng ranges và IDs được parameter hóa; giữ sort HoldsStart/eventsStart. ReturnByMany chỉ dùng cho RR reader, không thay helper/writer walk-in/check-in/checkout; dùng lại cùng pure shift-end UTC+7, không sửa ExpectedEnd/Reservation hoặc lưu cảnh báo thành giờ trả thật.

ReadOrders được dùng bởi danh sách đơn, tiền tạm tính, checkout preview/confirm và đọc hóa đơn. Đổi shared helper giữ cùng caller connection/transaction, không thay Room→Customer→source→session→order locks, clock chốt tiền sau dữ liệu và replay Invoice. Pending/Cancelled vẫn xuất hiện trong danh sách/counts; chỉ Completed được billing cộng. Không dedup hai giỏ giống nhau, không tái định giá món lịch sử.

Accounts.List dùng ListAt chung với wrapper RR hiện có, LEFT JOIN user-role và Project cũ. Giữ normalized-name sort, missing-role failure, current Admin-only, exact revision và mọi write/reset/lock/self/lastAdmin guard. Không giả LoginSession để benchmark; test bootstrap account trong GUID riêng.

## Rà đủ nhóm và quyết định phần giữ

| Nhóm | Đối chiếu luồng / quyết định |
|---|---|
| Startup/login | Fast schema-version path từ commit846bbfa giữ; không cache initialized/quyền |
| Room/type/service/customer CRUD | Catalog một data query; Customer.View và Create/Edit hiện hành giữ; duplicate check và writer transaction cần thiết |
| Đặt phòng/lookup/hủy/gia hạn | ReadDay N+1 sửa; normalize SĐT/membership snapshot/NoShow maintenance route riêng và writer recheck giữ |
| Lịch ngày/tuần, dashboard | Bulk state/calendar/ReturnBy như trên; cùng snapshot quyền/metadata/data/one clock; Staff không tổng doanh thu |
| Check-in/walk-in/session | Search ReturnBy batch; discovery và khóa Room/Customer/candidates/source/session/recheck sau chờ giữ |
| Menu/cart/đơn | Menu một query; list batch. ReadCart đọc Services tăng dần từng ID giữ vì Create dùng nó để lấy catalog lock **trước clock**; không gộp writer SELECT bằng phỏng đoán execution plan. Preview dùng helper hiện có, chưa tách một bộ pricing/validation riêng chỉ để giảm vài request |
| Billing/checkout/invoice | Shared ReadOrders batch. Checkout materialize hai lần trong cùng transaction nay đều1query; giữ lần này vì reuse giữa calculator/clock/pending cancellation cần thiết kế và race-proof riêng, không đơn giản xóa một lần đọc sau writer |
| Reports/Excel/history/audit/matrix | Phần lớn đã set-based, filter kỳ/quyền/snapshot giữ. RoomUsage đọc lịch sử để validate corruption/count Completed, không thêm filter ngày làm mất kiểm tra. Audit keyset và detail/ma trận current role giữ |
| Accounts | List batch; mutation/reset/revision/security giữ nguyên |
| UI/worker/Content/SQLite | Giữ Task.Run, guards busy/stale/revoke, refresh thực sự cần dữ liệu mới; không thay UI/palette. Worker MySQL không Initialize mỗi tick. Không xóa provider phục hồi/ảnh/seed/tests/docs vì chúng không phải công việc lặp trong UI |

Rà soát trên không có nghĩa mọi truy vấn đều tối ưu hoặc không còn cơ hội cải thiện. Không xóa trust-boundary validation, quyền bên trong transaction, trạng thái/cạnh tranh/rollback để đổi tốc độ. Dữ liệu thật, schema, ảnh và các seed người dùng giữ nguyên; toàn bộ writer fixtures ở GUID của test.

## Kiểm chứng

TDD RED giữ log riêng: Guest50SELECT, Dashboard43/21, Calendar63/21, ReturnBy5/5, Order0/1/4/5 đơn lần lượt1/3/9/11SELECT, Account7/5. Các lượt Add-Type/fixture category/reference lỗi trước RED hành vi được phân biệt, không gọi build error là regression đúng. GREEN budgets không dựa timing SLA. Budget SHOW SESSION STATUS không tính vào Com_select.

Guest test24 tổ hợp duration60/90/120/180 với empty/Confirmed/walk-in/bookedActive/grace/tick, so từng28slots với availability helper gốc. Test mới không tự chứng minh concurrency snapshot hoặc mọi ngày; oracle ở transaction kế tiếp, không writer đồng thời. Các suite BookingReads/Calendar/Dashboard/Sessions hiện có kiểm phần snapshot/current rights/clock/grace/locked history bổ sung. Account/Order tests mới có RR writer thật và oracle revision/snapshot.

Các kiểm tra đang chạy/hoàn tất và summary cuối ghi ở phần ledger bên dưới khi nhận exit code; không coi build/budget hoặc output giữa suite là toàn suite đạt. Runner hiện44 local+32 MySQL=76 bộ có sẵn, không phải phạm vi mặc nhiên đã chạy. Các test dùng 5ảnh OperationsUi, máy in giả, không in giấy thật.


## Nhật ký kiểm tra theo thời điểm (trạng thái cuối ở mục bên dưới)

- Build VerifyBatchCompleteDebug/Release đạt; source gồm cả Accounts/Order/Calendar/Guest/Dashboard/StaffSession batching, không csproj compile entry mới vì chỉ sửa service có sẵn.
- Full SQLite/WPF Debug44/44exit0: TEMP/MusicBoxRegression_1ae21c8ecc574533bef731343847aff6/results.json; summary trong docs/verification/20261102-batch-local-debug.json.
- NewReadBatch finalDebug exit0, Release đang chạy, log TEMP/MusicBoxBatchFinal_MySqlReadBatch_{Debug,Release}_20261102.log. Release đã ghi actual counts Dashboard1/21, Calendar3/21, ReturnBy1/5, Guest2/28. Cả suite chưa xong thì không nhận exit0.
- NewOrderBatch Debugexit0, TEMP/MusicBox_OrderBatch_GREEN_20261102.log; newAccountBatch Debugexit0 +existingAccountsDebugexit0, TEMP/musicbox-accountbatch-{green,regression}.log. Root Release budgets xếp hàng sau ReadBatch Release.
- Orders existingDebugexit0; Billing đầu fail tại ConcurrentReads writer.Wait8000 sau core phases đạt, log gốc TEMP/MusicBox_OrderBatch_MySqlBilling_20261102.log giữ. Checkout đang chạy writer races trong exec81364. Retry Billing sẽ chạy riêng khi root budget pipeline xong; không nới8s/SSL/reset/timeout hoặc sửaassertion. Không tự suy lỗi chờ8s là lock regression hay mạng.
- Region regression mới trên finalbuild (Calendar,Dashboard,Sessions,BookingReads,Availability,OperationsUi) do audit_batch chạy tuần tự sau slot retryBilling; chưa tuyên bố đạt.
- Static review độc lập không finding Guest/status/calendar/ReturnBy; root reviewed Order+Account diff, no broad write changes. NewReadBatch originalAdd-Type internalsetter failure corrected via reflection fixture, không tính compiler error là RED; prior trueRED counts logs giữ riêng.


### Phép đo màn hình thật (chỉ đọc dữ liệu)

Cùng API MySqlGuestBookingService.ReadDay cho một phòng active đầu danh sách trên musicbox, ngày mai/duration60,28slots: binary trước batching (VerifyLatencyRelease)8219ms; binary mới (VerifyBatchCompleteRelease)718ms. Cả hai làm nóng pool bằng ListRooms rồi mới đo ReadDay bằng Stopwatch, các process tách assembly. Không khởi tạo/đặt phòng/NoShow/login/ghi dữ liệu quán. Đây là mẫu1lần mỗi binary, không benchmark p95 hoặc SLA, mạng/tải vẫn thay đổi; kết quả cấu trúc2SELECT vs49/50 là bằng chứng ổn định hơn. Logs TEMP/MusicBoxReadDayBefore_20261102.log và MusicBoxReadDayAfter_20261102.log. Lịch nhân viên finalCalendarDebug đãexit0 sau bulkqueries.


## Kết quả cuối — đã hoàn tất lượt tối ưu

- Build VerifyBatchCompleteDebug/Release và bin/Debug/Release dùng thật: exit0. Không thêm package hoặc Compile entry, csproj171thêm/49xóa có sẵn giữ ngoài commit.
- **44/44 SQLite/WPF Debug** đạt trên finalbuild, có AuthenticationUi142ảnh và workflow đầu-cuối. [Summary local](verification/20261102-batch-local-debug.json).
- **3 bộ tối ưu mới** (ReadBatch/OrderBatch/AccountBatch) đều đạt Debug và Release. ReadBatch đo đủ1/3/1/2SELECT cho status/calendar/ReturnBy/day. OrderBatch giữ old ReadOrder oracle/RR writer/Serializable ownwrite; AccountBatch giữ old Identity-store oracle/exact revisions/current rights/RR writer. [Budget Release+ReadBatch Debug](verification/20261102-batch-budgets.json), [các lượt targeted](verification/20261102-batch-mysql-targeted.json).
- Existing Orders, Accounts, Checkout và Billing đạt kết quả cuối. Checkout gồm14ca writer thật cả hai thứ tự và MVCC/freshclock/actor sau Room wait. **Billing đầu fail assertion writer.Wait8000**, sau đó chạy lại **riêng trên finalbuild** với cùng assertion/SSL/timeout đạt toàn bộ. Không khẳng định nguyên nhân cụ thể của lượt đầu; có thể khác điều kiện mạng/tải, không lấy retry làm bằng chứng lượt đầu đã đạt. Giữ log gốc và isolatedretry; không sửa test để bỏ mốc8giây.
- **8/8 bộ tích hợp MySQL finalDebug** đạt ngay pipeline cuối, không retry: Calendar, Dashboard, Sessions, BookingReads, Availability, OperationsUi, Invoices, InvoicePrinting. [Summary integration](verification/20261102-batch-mysql-integration.json). Invoices giữ PaidAt/ngày Việt Nam/currentphone/combinedfilter/snapshots/decimal vượtInt64/readonly/RR vs checkout thật. Printing máy giả kiểm pagination/hủy/in lại/currentrights/busy/failure/UIentry;7ảnh,70items/4trang. Đã xem ảnh Dashboard fixture cuối, không phải doanh thu quán; chưa in giấy/driver thật và không nhận đã xem toàn bộ ảnh print trong lượt này.
- Tổng phạm vi:44bộ local và15nhóm MySQL Debug liên quan, ba bộ mới thêm Release; **không** tuyên bố chạy toàn76bộ hoặc mọi tổ hợp/tải lớn. Các suite targeted giữ build tương ứng trong summary, integration8+ReadBatchDebug và Release3trên build tổng hợp; không đổi tên log để nhận tất cả là cùng một binary.
- Review độc lập toàn diff7service +3test/runner không phát hiện Critical/Important; đã xét mapping/status/ticks/grace/privacy/schema/transaction/current scope. Các hạn chế đo lường/coverage trong mục trên vẫn áp dụng.

Log final integration: TEMP/MusicBoxBatchFinal_{Calendar,Dashboard,Sessions,BookingReads,Availability,OperationsUi,Invoices,InvoicePrinting}_Debug_20261102.log; summary TEMP/MusicBoxBatchFinal_Debug_20261102_audit.json. Source build/timing logs và RED/firstfailure/agent builds đều giữ ngoài Git, các JSON đã lọc được commit để người pull nắm tiến độ và chạy lại. Không có công việc kiểm tra còn chạy khi chốt commit.
