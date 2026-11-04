# Sửa khóa NoShow — 04/11/2026

User báo Command Timeout/User-Unhandled tại MySqlNoShowService.LockCandidates và lỗi chuyển sang InvalidOperationException. Metadata chỉ đọc: một transaction musicbox giữ hai record X trên Reservations PRIMARY hơn 569 giây, RowsModified=0; dữ liệu chỉ có 1 NoShow, không có Confirmed. User đã dừng debug theo yêu cầu. Không KILL/rollback kết nối hoặc sửa dữ liệu quán.

Nguyên nhân: SELECT WHERE Status=Confirmed ORDER BY PK FOR UPDATE trong Serializable quét và khóa cả lịch sử không khớp và khoảng trống. Worker chạy mỗi phút và writer cùng gọi helper này. ProcessExpiredAt lại quét range lần nữa. Phần popup trước không giải quyết nguyên nhân SQL.

Sửa: khám phá ID Confirmed bằng snapshot RR read-only không khóa, sau đó khóa từng PK chính xác theo ID tăng dần trong transaction writer hiện hành; batch nhiều point SELECT để không N+1 round-trip. Đọc trạng thái/StartTime mới sau khóa và truyền đúng danh sách khóa vào ProcessExpiredAt; không quét range lần hai. Không đổi schema/isolation writer/SSL/timeout/quyền/clock sau khóa/grace15/audit/rollback. Không SKIP LOCKED trong business checks. Caller cùng resource locks Room->Customer trước candidate->source->session như cũ.

Các candidate mới commit sau discovery được maintenance lượt kế tiếp; availability/reader vẫn kiểm effectiveNoShow từ grace hiện hành. Writer cùng Room/Customer tiếp tục serialize bởi resource locks. Không tăng timeout, retry vô hạn hoặc tắt worker.

Review ruling: source ReservationId phải được gộp/dedupe/sort cùng candidate trước mọi khóa reservation; tránh stale discovery gồm A nhưng extension chỉ thấy B rồi khóa B trước source A. Có RED public ExtendStaff + SQL lock gate tái hiện đúng lỗi, không tạo trạng thái Session/Invoice giả. Discovery dùng một pooled connection bổ sung trong lúc writer giữ resource locks.
