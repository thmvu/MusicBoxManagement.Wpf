# MySQL 04e.1 — Check-in từ booking

Ngày 21/10/2026. Port RoomSessionService.CheckIn sang MySqlRoomSessionService.CheckIn, giữ nghiệp vụ mục 11/38/41/55.4 và báo cáo Step6a1_CheckIn. Xem bảng bàn giao chung [MySql_Migration_Status.md](MySql_Migration_Status.md) trước khi tiếp tục phần khác.

## Hợp đồng và khóa

Session.CheckIn hiện hành độc lập Reservation.View/Create, Session.View và Customer CRUD. Serializable kiểm tra phiên/quyền cùng transaction; khám phá RoomId/CustomerId bằng kết nối đọc riêng không giữ lock Reservation trước Room. Writer khóa Room → Customer → các Confirmed candidate theo ID giống Reservation/NoShow → Reservation nguồn, rồi đọc lại ID/status/session và mới lấy IClock.

Candidate locks dùng chung MySqlNoShowService.LockCandidates để tránh đảo thứ tự khóa giữa source và danh sách giữ lịch/NoShow. Chúng có thể khiến writer chờ booking phòng khác; không dùng advisory lock toàn ứng dụng. Không gọi ProcessExpired hoặc chuyển NoShow trong check-in. Quyền/tài khoản đọc trong Serializable được giữ nhất quán đến commit; không dựa vào metadata UI cũ.

Replay có session nguồn trả phiên cũ kể cả Completed, không thêm audit hoặc đổi snapshot/giờ/giá; vẫn kiểm tra quyền và khóa nguồn. Nhận mới cần Confirmed và now < StartTime+15 phút. ActualStart là now UTC có giây/ticks sau lock, ExpectedEnd = now + duration booking. Giữ giờ đặt gốc, không ValidateReservation/ép slot/giới hạn nhận sớm; ValidateSessionInterval và MySqlAvailabilityService.CheckCheckInAt kiểm tra đủ ca và overlap Room/Customer, bỏ nguồn, chặn Active kể cả walk-in/quá giờ/phòng khóa.

INSERT snapshot RoomCode/RoomTypeCode/RoomTypeName/HourlyRate từ danh mục hiện hành, Reservation CheckedIn và Audit Staff Session.CheckIn cùng transaction/một now. Lỗi status/audit rollback toàn bộ. Unique Active Room/Customer và ReservationId tiếp tục bảo vệ dữ liệu. Deadlock/lock timeout/unique race báo kiểm tra lại, không retry vô hạn hoặc tự tạo walk-in.

## Phạm vi

Chỉ thêm writer CheckIn, chưa có walk-in/gia hạn/StaffSessionService/GuestSessionService MySQL hoặc provider UI. Không đổi schema v1/SQLite v8 hoặc nhập dữ liệu thật. Thử Completed trong bài là fixture phục vụ replay/guard, chưa checkout MySQL thật.

## Kiểm tra

Build Debug/Release VerifyMigrationDebug/Release đạt. Verify-MySqlCheckIn chạy database musicbox_test_GUID riêng, guard tên trước DROP, không ghi database musicbox nghiệp vụ. Bài gồm quyền chỉ CheckIn/Guest/revoke/stamp, actual ticks/một clock/nhận sớm/đủ duration, grace ±1 tick, ca/nghỉ/kết thúc đúng ca, Room/Customer Active/overlap/phòng khóa, snapshot sau đổi danh mục, replay Active/Completed và audit không lặp, trigger audit lỗi rollback, hai replay cạnh tranh, NoShow/check-in hai thứ tự writer thật, clock sau chờ Room qua grace và hai writer tranh Room/Customer, Cancelled source.

**Verify-MySqlCheckIn bản test cuối online Debug/Release đều đạt (exit 0)**, gồm các cạnh tranh writer thật và clock sau khi chờ qua grace. SQLite CheckIn/Availability/Reservations/Calendar/Foundation đã đạt trên build Debug; không thay UI. Calendar MySQL hai build online đạt sau sửa 04d; MySqlAvailability/Reservations/Foundation online Debug cũng đạt. AuthenticationUi Debug cũng đạt, render 131 ảnh. Không tuyên bố kiểm tra lại mọi bộ MySQL/WPF trong lượt này.

## Tiếp tục từ đây

04e.2 thêm walk-in vào MySqlRoomSessionService, sau đó đọc phiên/gia hạn/Guest lookup đầy đủ. Giữ ReturnBy động/null ExpectedEnd/snapshot/clock/quyền độc lập và test cạnh tranh. Tiếp Order/Billing/checkout/invoice/dashboard/quản trị/báo cáo/Excel/lịch sử; cập nhật bảng bàn giao cùng mỗi commit. Sao lưu/nhập/đối chiếu database + Content và kiểm tra UI trước đổi provider.
