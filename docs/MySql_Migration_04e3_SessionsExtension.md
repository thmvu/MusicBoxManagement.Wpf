# MySQL 04e.3 — Đọc phiên và gia hạn Staff

Ngày 22/10/2026. Port StaffSessionService và các đường gia hạn Staff trong RoomSessionService, giữ mục 14/38/43/55.4 cùng Step6c1_Extension/Step6c2_SessionsExtensionUi. Đọc [bảng bàn giao](MySql_Migration_Status.md) trước khi nhận phần tiếp theo.

## Service đọc phiên

`MySqlStaffSessionService.Search(actor, phoneNumber, status)` dùng Session.View hiện hành, độc lập Reservation.View/Room.Manage/Customer CRUD. SĐT đầy đủ chuẩn hóa bằng helper chung; status Active (mặc định), Completed hoặc null để đọc tất cả; lọc AND. Trả StaffSession/StaffSessionSearch đã có, tên/SĐT Customer hiện hành, snapshot phòng/loại/giá, actual/expected/Completed. Sắp ActualStartTime rồi ID giảm dần, gồm lịch sử phòng khóa.

ReadAccess/quyền/tài khoản/stamp, một IClock, khách/phiên/lịch và metadata CanExtend trong cùng RR read-only snapshot. ReturnBy phiên booking là ExpectedEnd; walk-in tính động theo ca gốc và booking Room/Customer. Không audit, NoShow, cache, DDL hoặc giả lập dữ liệu. Thiếu View không trả PII. Không có factory Billing giả; nối Billing/provider/UI vẫn thuộc các bước sau.

## Gia hạn và phân quyền

`MySqlRoomSessionService.PreviewExtensionStaff/ExtendStaff` chỉ cần Session.Extend, độc lập Session.View. `MySqlStaffSessionService.Preview/Extend` đi đường viewed nội bộ yêu cầu cả View và Extend ngay tại service, không tin metadata cũ. Không có Guest fallback.

Preview cùng RR snapshot/một clock, trả khả năng/giờ mới/giới hạn không PII, không chốt lịch hoặc giá. Writer Serializable kiểm tra quyền, khám phá Room/Customer/Reservation ngoài writer locks, rồi khóa Room → Customer → Confirmed candidates → Reservation nguồn (nếu có) → RoomSession. Đọc lại ID/status/observedEnd trước một clock sau lock. Cùng quy ước khóa 04b/04e.1/04e.2; candidate locks có thể khiến phòng khác phải chờ, không dùng advisory lock toàn ứng dụng.

Chỉ Active từ booking có ExpectedEnd; +30 hoặc +60 tính từ giờ trong DB. observedEnd chỉ chặn form cũ/gửi lặp, chấp nhận cùng thời điểm ở offset khác. Đúng ExpectedEnd được gia hạn; muộn một tick bị chặn. MySqlAvailabilityService.CheckExtensionAt kiểm tra ca/Room/Customer, bỏ nguồn, không nới hold theo giờ quá hạn và bỏ Confirmed hết grace dù worker chậm. Walk-in/Completed/missing ID không được gia hạn.

Chỉ ExpectedEndTime thay đổi, UPDATE có guard status/giờ cũ và audit Staff Session.Extend trong cùng commit/một now; giữ Reservation, actual và toàn bộ snapshot. Lỗi audit rollback giờ trả; deadlock/timeout báo kiểm tra lại, không retry vô hạn. Không ghi NoShow hoặc tự checkout. Gia hạn thêm lần nữa phải dùng observedEnd mới.

## Kiểm tra

Build Debug/Release tại bin/VerifySessionsMySqlDebug/Release đạt. **Verify-MySqlSessions online cả hai build đạt** trên database GUID riêng. Bài gồm View-only/Extend-only/viewed route, grant/revoke/stamp/Guest, snapshot quyền và tên khách khi đồng thời sửa quyền/dữ liệu, lọc SĐT/status/Completed/phòng khóa, một clock/ReturnBy walk-in, đổi SĐT, preview/read không NoShow/audit, +30/+60/offset/ticks/giữ booking/snapshot, stale/duplicate, walk-in/Completed/missing/invalid minutes, overlap Room/Customer/ca/exact end/overdue, rollback audit, cạnh tranh cùng observedEnd và booking thật, clock sau chờ Room qua ExpectedEnd.

Check-in/walk-in/booking trong fixture đi qua service MySQL thật. Completed dùng fixture cập nhật trạng thái để kiểm tra đọc/guard, chưa phải nghiệm thu checkout MySQL; cạnh tranh checkout–gia hạn bổ sung khi port checkout. Hồi quy MySqlCheckIn/WalkIn/Availability/Reservations/Calendar/Foundation online Debug đạt. 16 bộ SQLite/WPF Debug Sessions/Extension/WalkIn/CheckIn/Availability/Calendar/Reservations/GuestCalendar/Authentication/Foundation/StaffReservations/Rooms/Customers/GuestSessions/GuestLookup/AuthenticationUi đạt, UI 131 ảnh. Không thay UI/provider hoặc nhập dữ liệu thật. Không tuyên bố chạy mọi bộ MySQL/WPF.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlSessions.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifySessionsMySqlDebug
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlSessions.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifySessionsMySqlRelease
```

Không thay UI/App/worker/provider/schema SQLite v8/MySQL v1, không nhập dữ liệu/ảnh thật. Tests chỉ tạo/xóa database musicbox_test_GUID đã kiểm tra tên; không ghi musicbox nghiệp vụ hoặc gửi máy in.

## Tiếp theo

04e.4 Guest session/gia hạn và lookup/hủy booking đầy đủ theo SĐT hiện hành, audit Guest, DTO public riêng; không gọi Staff service cho Guest hoặc trả ActiveSession=null giả. Tiền tạm tính/menu/order giữ trong bước port tương ứng, không cắt chức năng. Sau đó Order/Billing/checkout/invoice/dashboard/quản trị/ma trận quyền/audit/báo cáo/Excel/lịch sử, backup/import/đối chiếu database + Content/UI trước cutover và ERD/từ điển dữ liệu/demo. Giao diện vẫn để sau theo yêu cầu.
