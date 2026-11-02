# Giảm độ trễ khởi tạo MySQL — Implementation Plan

> For agentic workers: use superpowers:executing-plans; inline execution per user request to fix automatically.

**Goal:** Bỏ khởi tạo DDL/khóa schema lặp trên database đã sẵn sàng, giữ kiểm phiên bản mỗi lần và toàn bộ luật/quyền.
**Architecture:** MySqlDatabase.Initialize thử mở đúng database, SELECT version. Current thì return; newer thì reject; missing database/table hoặc older thì quay về đường InitializeLocked hiện có. Không cache process, không nới SSL/pool/session SET, không sửa UI/provider/data.
**Tech Stack:** WPF net472, MySqlConnector2.6.2, Windows PowerShell STA.
**Spec:** Yêu cầu tự động sửa độ chậm và xét phần cần/không cần bằng Superpowers/Ponytail; docs/MySql_Migration_Status.md, Reference_Web_ProjectPlan_v1.3.md. User giao quyền quyết định nên không dừng chờ lựa chọn thiết kế.

## Approaches / quyết định

- Cache cờ initialized: ít request hơn nhưng giấu schema đổi/mới hơn trong cùng process → loại.
- Bỏ kiểm quyền/cache account: ảnh hưởng revoke/stamp và transaction → loại.
- Fast path kiểm version hiện hành mỗi lần, fallback initialization có khóa → chọn; sửa nhỏ tại shared data layer, không đổi public API.
- Chưa đổi main refresh/dashboard hay rewrite query: chưa có phép đo từng route chứng minh bước đó thừa. Không thêm thư viện, global cache hoặc retry.

## Global constraints

Không sửa production data/schema/SSL/timeout; chỉ GUID fixture được tạo/dọn. Giữ current version1 và SQLite8, Admin/RBAC/stamp/logout, transaction và Content local. Diff định dạng csproj171/49 giữ ngoài commit. Dùng branch codex/mysql-latency trong checkout hiện tại để giữ đúng môi trường người dùng, không đổi workspace/dependency.

## Review focus

1. Missing database hoặc version table phải khởi tạo được, không nuốt lỗi khác.
2. Version mới hơn trên cùng instance phải bị từ chối; không cache success.
3. Version row thiếu/lower phải theo đường recovery có khóa.
4. Ready database không chờ schema advisory lock của writer khác; read fast path không DDL/seed.
5. Login/account current rights và schema constraints còn đạt sau sửa.

## Task 1 — Shared initialize fast path

Files: Data/MySqlDatabase.cs; Tests/MySqlInitializeChecks.cs; Tests/Verify-MySqlInitialize.ps1.
Interfaces: public void MySqlDatabase.Initialize() không đổi.

- [x] Viết fixture GUID: initialize; giữ named schema lock trên writer; Initialize reader phải xong trong5s dù lock còn giữ. Bản cũ fail. Đo baseline3lần ở schema current ngoài test timing assertion.
- [x] Thêm fast SELECT version; catch chỉ1049/1146 để fallback, không catch permission/network/version exceptions.
- [x] Build Debug/Release và chạy test mới hai build. Foundation + Authentication + Accounts + OperationsUi và SQLite AuthenticationUi liên quan; kiểm read current/newer/lower/missing row/missing table/unmanaged theo fixture.
- [x] Đo lại Initialize cùng build candidate và baseline ở database thật current; không gọi login/write nghiệp vụ trên production.
- [x] Review diff, báo phạm vi/giới hạn, cập nhật AGENTS/README/plan/status/docs; commit tiếng Việt/push. Không nhận đã tối ưu mọi màn hình từ một thay đổi.

## Ledger

- Baseline: bin/Release hiện có, existing musicbox Initialize1885/1091/1057ms; log TEMP/MusicBoxInitBaseline_20261102.log. Root: startup + auth NeedsSetup/Setup/Login đều Initialize, chạy CREATE DATABASE/version table/GET_LOCK dù version1.
- Trước task: HEAD2e217b9, chỉ csproj diff có sẵn; branch codex/mysql-latency.

- RED: MusicBoxInitializeRed_20261102.log fail vì schema ready chờ advisory lock; trước đó runner Add-Type references lỗi được sửa, không tính compile error là RED hành vi.
- GREEN: Initialize Debug/Release và Foundation Debug/Release đạt, Authentication Debug/Release đạt; SQLite AuthenticationUi Debug142ảnh đạt. Accounts Debug đầu timeout, retry bị ngắt điện không có kết quả, resume tuần tự đạt.
- Ruling: OperationsUi hardcode29/10 khiến so TodayRevenue sai02/11; sửa fixture today/yesterday theo UTC+7, giữ assertion và production clocks. Đây là sửa kiểm thử cần thiết để hồi quy có nghĩa, không mở rộng product behavior.
- Measurement: candidate Release Initialize1747/683/612/613/616ms; không benchmark kiểm soát/toàn app. BinDebug/Release dùng thật đã build đạt.
- Review: fresh reviewer không finding product patch; có giới hạn test version0/network/permission injection chưa độc lập. Không tuyên bố mọi suite.

- Task1 complete: final9checks PASS, docs/verification/20261102-latency.json; OperationsUi fixture fixed after RED date mismatch and GREEN final. No production behavior/assertion weakened. Build actualDebug/Release passed. Fixture re-review no findings. Partial scope explicit in performance review.
