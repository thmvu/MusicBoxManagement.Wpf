# Gom truy vấn màn hình MySQL — Implementation Plan

> Inline execution with superpowers:executing-plans, systematic-debugging/TDD/Ponytail; user yêu cầu tiếp tục tự động làm đủ.

**Goal:** Rà luồng toàn app và loại round-trip N+1 ở lịch giờ Khách, trạng thái phòng dashboard/lịch; giữ đủ quyền/tiền/snapshot/writer checks.
**Architecture:** Guest calendar read room+Active một lần, HoldsSql một lần trong RR; dựng28slots bằng BookingHours và overlap nửa mở, giữ exact now. Shared CASE SQL trạng thái theo Inactive→Occupied→Reserved→Available dùng trong SELECT Rooms ở dashboard/calendar, không vòng per-room. Không cache ngoài request.
**Spec:** Reference_Web_ProjectPlan_v1.3, MySql_Migration_Status; user giao tối ưu tự quyết. Không đổi schema/provider/UI/SSL/timeout/quyền/DTO hoặc production data. SQLite giữ nguyên. Existing csproj diff171/49 không stage.

## Approaches

- Cache ngày/quyền: loại vì stale/revoke/booking mới.
- Bỏ validation/writer check: loại vì thay nghiệp vụ.
- Batch cùng snapshot, giữ helper luật và clock: chọn, không thêm dependency hoặc abstraction framework.

## Checklist / review focus

- [x] RED test Com_select trên cùng session: đọc Guest28slots tối đa3SELECT (room+hold, dự phòng1), bản cũ phải fail. Fixtures GUID, không ghi production.
- [x] GREEN batching Guest; oracle từng slot so AvailabilityService với cùng now, đủ60/90/120/180, exactnow/1tick/grace/break/closed/walkin/bookedActive/future.
- [x] Batch room status shared SQL, Dashboard/Calendar tests giữ quyền/snapshot/current metadata/status và current grace.
- [x] Rà danh mục/lookup/session/order/billing/checkout/invoice/report/account: ghi đường có N+1, kiểm tra giữ cần thiết, không tự cắt scope; chạy đầy đủ runner để thấy phạm vi hồi quy, giữ timeout/retry gốc.
- [x] Build Debug/Release, test mới hai build, phù hợp online từng vùng và local; update docs/README/plan/status/AGENTS; review; commit/push.

Không dùng timing SLA làm test vì mạng biến động; Com_select đo số lệnh thật. Không giả kết quả các luồng chưa chạy. Trong bất kỳ tối ưu tiếp theo, writer ordering và permission demand trong transaction không được bỏ.

## Ledger

- Base846bbfa, branchcodex/mysql-read-batching, chỉ csproj dirty có sẵn.
- Root evidence: GuestCalendar loop gọi CheckReservationAt (IsActive+holds SELECT mỗi slot); Dashboard/Calendar loop gọi CurrentRoomStatus (Active+reservation SELECT mỗi phòng). Workers/auth initialized optimization trước giữ nguyên.


## Mở rộng cùng root-cause audit, không đổi nghiệp vụ

- RED thêm: Calendar Populate63SELECT/21phòng; WalkInReturnBy5SELECT/5phiên. Đã sửa Calendar bulk3queries và WalkInReturnByMany RR-only1query/request cho Dashboard/Calendar/StaffSession. Writer single-helper và locks giữ nguyên.
- Agent batch_orders owns ReadOrders + new MySqlOrderBatchChecks/runner: RED1/3/9/11SELECT0/1/4/5orders, GREENall1SELECT, oracle/order/snapshot/Serializable own-write passed. Debug isolated VerifyOrderBatchDebug. Existing Orders exit0, Billing failed8s writer.Wait original log preserved, Checkout stillrunning81364; isolate Billing retry after root budget pipeline clears.
- Agent audit_batch owns Accounts.List/ListAt + new MySqlAccountBatchChecks/runner: RED7SELECT/5accounts, GREEN2SELECT/5, exact old revision/RR writer/privateDTO/sort/missingrole passes; existing MySqlAccounts Debugexit0. No write/reset changes.
- Root builds VerifyBatchCompleteDebug/Release include all current production source and pass. Root final budget pipeline exec72692: ReadBatchDebug→ReadBatchRelease→OrderBatchRelease→AccountBatchRelease, saves TEMP/MusicBoxBatchFinalBudget_20261102.json. Root newReadBatch oracle can take minutes (24scenarios*28slots), no productiondata.
- Full local Debug44/44exit0, TEMP/MusicBoxRegression_1ae21c8ecc574533bef731343847aff6/results.json.
- Agent audit_batch queued finalCalendar/Dashboard/Sessions/BookingReads/Availability/OperationsUi Debug on VerifyBatchCompleteDebug, waiting Orders/Billing isolated retry slot. See planned TEMP/MusicBoxBatchFinal_{suite}_Debug_20261102.log. Do not claim passed until exit0.
- Static reviewer batch_orders found no product correctness issues in Guest, status CASE, calendarbulk, ReturnByMany; current testlimits recorded in report. Final region regressions pending. Branchbase846bbfa, no commits yet, csproj stillpreexisting171/49.

- New ReadBatch finalDebugexit0. Root budget Release records1/3/1/2 counts then oracle stillrunning. Local44/44durable summarysaved. Finalcode8services and3newtest suites, runner76total; no commits yet.

- Final4budget pipelineexit0, new3suites Debug+Release đạt (Order/AccountDebug agent). Existing Orders/Checkout14racespass; Billing original8s fail then isolatedretryexit0 on finalbuild, noassertchanges. Reviewer nofindings. ReadDay benchmarkmusicbox read-only8219→718ms,28slots, notSLA. RegionalCalendarfinalDebug0; othersqueued/running.

- RegionCalendar0/Dashboard0/Sessions0 on finalDebug. BookingReads next. Invoices/InvoicePrinting added afteroriginal6 becauseReadOrderssharedinvoicepath. Finaltestsallpending statuses at TEMP/MusicBoxBatchFinal_Debug_20261102_audit.json. Root benchmark8219→718ms; shippedDebug/Releasebuilds0.


## Final ledger

- Complete: allproductionchanges compiled finalDebug/Release; shippedDebug/Release0. NewReadBatch/OrderBatch/AccountBatch Debug+Release0. Local44/440. TargetedOrders/Accounts/Checkout0; Billingfirst8sassertfail then isolatedretry0, noassert/SSL/settings change. Finalregion8/80 includingInvoices/Printing. Sources/details/sanitized summaries in MySql_Read_Performance_20261102.md.
- Ruling: comprehensive audit covers all featuregroups, tests scope follows changedreadpaths:44local+15relatedMySQLDebug,3newRelease; notall76available suites. Corruption/history scans, writerlocksortedcart, freshpermissions andneededrefresh remain. No deliberatefeaturecut or cache to hide stale state.
- Wholebranch staticreview no Critical/Important, stale reportledger corrected to historical +finalstatus. No runningtests/workersleft from this task. Production onlyreadonlytiming, no data/schema/UI/provider changes.
