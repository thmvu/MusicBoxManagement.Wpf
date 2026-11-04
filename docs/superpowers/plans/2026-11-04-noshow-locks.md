# Kế hoạch sửa khóa NoShow

> Inline executing-plans/TDD; một review cuối.

Spec: ../specs/2026-11-04-noshow-locks.md. Base4405635; chỉ csproj định dạng171/49 dirty. Chỉ MySQL/Aiven, không backend/test/provider cũ.

- [x] RED: worker phải tới clock trong khi một row NoShow lịch sử bị khóa riêng; giữ log lock waits.
- [x] Thay range lock bằng RR ID discovery + batch PK point locks; ProcessExpiredAt nhận dữ liệu đã khóa; cập nhật các MySQL callers.
- [x] GREEN Debug/Release: terminal history không cản worker; Confirmed vẫn chờ và clock mới sau chờ; NoShow/audit/idempotence/rollback, không phá race check-in.
- [x] Hồi quy MySQL Reservations/CheckIn thật bằng runner MySQL-only; UI feedback luồng thật. Không runner pha provider, không nới assertion.
- [x] Review cuối, build dùng thật, docs/AGENTS/README/plan/status. Tích hợp: commit Việt và push theo ủy quyền, kết quả Git báo trên chat.

Ruling: đọc metadata production chỉ để chẩn đoán, không ghi dữ liệu hoặc giết kết nối; user đã chủ động Shift+F5. Không quy lỗi service thành lỗi popup/debugger đơn thuần.

## Nhật ký cuối

- RED worker lịch sử thất bại đúng rồi Green nhỏ đạt. Locks Release bản trung gian kiểm batch >128/fresh clock đạt.
- Review duy nhất tìm P1 stale candidate/source lock inversion. RED source-order đầu chưa tới gate; thời gian chờ gate15s giống bài cũ rồi RED2 thất bại đúng B-before-A. Gộp source ID trước sort cho CheckIn/StaffExtend/GuestExtend/GuestCancel, không thay isolation/SQL timeout.
- Build copy lỗi khi PowerShell giữ file test và user mở lại Debug; đã giữ log. User Stop lần hai; bản dùng thật Debug retry/Release build exit0. Không kill process của user.
- Final Locks Debug/Release0; Reservations/CheckIn finalRelease0; Feedback full UI finalDebug0 (34 popup/3 ảnh). Hồi quy Debug trước source-fix cũng đạt, không thay cho bản cuối.
- Live metadata sau Stop không có trx/lock/wait, Reservation status vẫn một NoShow. Production chỉ đọc, fixture GUID tự dọn.
- Ruling: một pooled connection RR bổ sung để discovery không giữ range locks; không SKIP LOCKED quyết định nghiệp vụ. Candidate mới commit sau discovery xử lý lượt sau, reader vẫn effective grace; tài nguyên Room/Customer bảo vệ quyết định writer.
- Scope review declined runtime/build đã được implementer kiểm bằng log; source legacy/schema/csproj cũ không sửa; mọi tổ hợp/máy in không nằm trong claim. Không fan-out implementer hoặc reviewer lặp.
