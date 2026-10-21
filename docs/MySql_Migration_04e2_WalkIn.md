# MySQL 04e.2 — Nhận khách trực tiếp (walk-in)

Ngày 21/10/2026. Port các đường walk-in của RoomSessionService sang MySqlRoomSessionService. Đối chiếu mục 12/38/42/55.4 của Reference_Web_ProjectPlan_v1.3 và Step6b1_WalkIn/Step6b2_WalkInUi. Trạng thái bàn giao chung: [MySql_Migration_Status.md](MySql_Migration_Status.md).

## Service và cách hoạt động

- `ListWalkInRooms`: Session.WalkIn hiện hành, chỉ phòng mở/DTO PublicRoom. Không đòi Room.Manage, Reservation.Create/View, Customer CRUD hoặc Session.View.
- `PreviewWalkIn`: cùng quyền WalkIn, kiểm tra tên/SĐT và Room/Customer/ca; đọc giá tham khảo/ReturnBy động cùng snapshot/một clock. Không tạo khách/session/NoShow/audit/hold. Preview không khóa giá/lịch; writer đọc lại khi nhận.
- `CreateWalkIn`: nhận RoomId/FullName/PhoneNumber, không duration/giờ đặt. Chuẩn hóa SĐT chung, giữ tên khách cũ hoặc tạo khách mới; tạo Active có actual giữ UTC ticks, ReservationId/ExpectedEndTime/ActualEndTime null; snapshot giá/mã phòng/mã và tên loại từ server lúc nhận.
- `ReadWalkIn`: Session.View hiện hành, chỉ phiên walk-in Active; không đọc phiên từ booking/Completed qua đường này.
- `ReadWalkInReceipt`: Session.WalkIn và audit xác nhận chính người nhận phiên; người khác có cùng role/quyền không được đọc receipt này. Không thay quyền Session.View cho đọc nội bộ chung.

Read dùng RepeatableRead read-only, kiểm tra quyền bằng ReadAccess trong cùng snapshot, không dùng Demand chỉ dành cho Serializable writer. Writer Serializable: quyền → khóa Room → khách theo SĐT chuẩn hóa/tạo mới và khóa Customer → khóa Confirmed candidates theo ID → một IClock → kiểm tra ca/NoShow/availability → snapshot/session/audit → commit. Khóa candidate cùng quy ước 04b/04e.1 tránh đảo khóa với NoShow, có thể chờ booking phòng khác. Không advisory lock hoặc retry vô hạn; lock timeout/deadlock/unique race báo kiểm tra lại.

Walk-in bị chặn nếu phòng khóa, Room/Customer còn Active, hoặc Confirmed đã tới giờ và còn grace của một trong hai. Booking tương lai không chặn; đúng StartTime+15 phút không giữ lịch. NoShow vừa xử lý, khách mới, session và nhật ký cùng transaction: bất kỳ lỗi nào rollback toàn bộ, kể cả bảo trì NoShow. Nhận lặp bị Active guard, không trả session cũ vì walk-in không có ID nguồn để replay.

ReturnBy là cảnh báo min(kết thúc ca gốc, booking còn hiệu lực kế tiếp của Room/Customer), dùng MySqlScheduleRules chung với Calendar. Đọc lại thay đổi theo booking/hủy/NoShow; không lưu ExpectedEnd, không tạo future hold, không tự checkout hoặc gia hạn walk-in. Quá ReturnBy vẫn Active đến luồng checkout thật.

## Kiểm tra

Build Debug/Release tại bin/VerifyWalkInMySqlDebug/Release đạt. Tests/Verify-MySqlWalkIn.ps1 dùng database musicbox_test_GUID và guard tên trước DROP; không ghi musicbox nghiệp vụ hoặc dữ liệu/ảnh SQLite thật.

Bài online gồm quyền WalkIn-only/View-only/receipt người tạo/Guest/revoke/stamp, preview không ghi, SĐT tương đương/new/old/giữ tên, actual ticks/một clock/null nguồn và ExpectedEnd, snapshot, biên ca/grace/Room-Customer Active/booking tới giờ/phòng khóa, ReturnBy Room/Customer/hủy/ngày mai/quá giờ/không giữ lịch tương lai, rollback khách/session/audit/NoShow, writer thật tranh Room/Customer/check-in/booking ngay/khóa phòng/worker NoShow, clock sau chờ Room qua giờ đóng ca. Phiên Completed trong test chỉ fixture kiểm tra guard, chưa checkout MySQL.

Riêng worker NoShow và walk-in có thể gặp deadlock/timeout InnoDB: API báo kiểm tra/chạy lại, không tự retry vô hạn. Bài test chỉ chấp nhận conflict có inner MySqlException 1213/1205, chờ cả hai writer kết thúc rồi chạy lại phần chưa xong tối đa một lần; cuối cùng bắt buộc đúng một phiên và một log NoShow. Không đòi cả hai transaction phải thành công ngay lần đầu hoặc bỏ qua lỗi nghiệp vụ khác.

**Verify-MySqlWalkIn bản test cuối online Debug/Release đều đạt (exit 0)**, gồm preview grace không ghi NoShow và một session/log sau retry. Hồi quy MySqlCheckIn/Calendar/Availability/Reservations/Foundation online Debug đạt. SQLite WalkIn/CheckIn/Availability/Calendar/Reservations/Rooms/Customers/Authentication/Foundation/GuestCalendar Debug đạt; AuthenticationUi Debug đạt, render 131 ảnh. Không tuyên bố đã chạy lại mọi bộ MySQL hoặc đổi UI/provider.

Chạy lại sau khi build (đọc cấu hình Aiven từ môi trường như các bước trước, không ghi mật khẩu vào lệnh):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlWalkIn.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyWalkInMySqlDebug
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlWalkIn.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyWalkInMySqlRelease
```

## Làm tiếp

04e.3 gia hạn/đọc phiên nội bộ, Guest session/lookup/hủy đầy đủ, sau đó Order/Billing/checkout/invoice/dashboard/quản trị/ma trận quyền/audit/báo cáo/Excel/lịch sử. Mỗi phần cập nhật bảng bàn giao cùng code/test/README/plan. App/worker vẫn SQLite v8, MySQL v1; chưa nhập dữ liệu thật, chưa đổi giao diện hoặc provider. Giữ sao lưu/import/đối chiếu database + Content/UI và ERD/từ điển dữ liệu/demo trước cutover.
