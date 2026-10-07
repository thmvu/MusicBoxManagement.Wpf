# Bước 6b.1 — service nhận khách trực tiếp

Ngày: 07/10/2026. WPF .NET Framework 4.7.2 + SQLite, schema v6. Đối chiếu mục 12/13/17/35/38/42/55.4 của Reference_Web_ProjectPlan_v1.3.md. Bước này thêm xử lý dữ liệu; chưa có form walk-in.

## Đã làm

RoomSessionService.CreateWalkIn(actor, WalkInRequest) nhận RoomId, FullName và PhoneNumber; không có trường giờ đặt hoặc duration. Quyền **Session.WalkIn** được đọc trong transaction ghi. Không cần Reservation.Create/View, Room.Manage hoặc quyền Customer CRUD để nhận khách trực tiếp.

Trong cùng BeginWriteTransaction, service lấy một mốc IClock sau khi có quyền ghi, chuẩn hóa SĐT bằng PhoneNumberNormalizer, tìm khách cũ hoặc tạo khách mới. SĐT cũ giữ tên đã lưu; tên nhập vẫn phải hợp lệ 1–100 ký tự. Khách mới ghi audit Staff cùng nghiệp vụ, không tạo tài khoản đăng nhập Guest.

BookingHours.GetOpenShiftEnd nhận biết đúng ca Việt Nam 09–12 hoặc 13–23: 09/13 được nhận; đúng 12/23 và giờ nghỉ bị từ chối. Không cần slot 30 phút và không làm tròn actual, giữ giây/ticks. Không yêu cầu thời lượng tối thiểu trước booking tương lai theo plan.

AvailabilityService.CheckWalkInAt kiểm tra Room đang mở, không có Active Session của Room hoặc Customer, không có Confirmed còn grace đã tới giờ của một trong hai. Booking tương lai không cấm walk-in; booking đã tới giờ còn hạn phải dùng check-in hoặc xử lý booking trước. Đúng StartTime+15 phút không còn chặn; không dựa vào worker đã chạy hay chưa.

Thành công tạo RoomSession Active với ActualStartTime=now, **ReservationId/ExpectedEndTime/ActualEndTime=null**, snapshot giá/mã phòng/mã và tên loại theo danh mục lúc nhận. Không tạo Reservation giả, không đổi giờ booking khác. Ghi Session.WalkIn và commit cùng dữ liệu; giữ unique Active Room/Customer hiện có.

## Giờ cần trả phòng và lịch

CreateWalkIn trả WalkInResult gồm Session, ReturnBy, CheckedAt và IsOverdue. ReturnBy là mốc sớm nhất trong kết thúc ca gốc của session, Confirmed kế tiếp còn hiệu lực của Room và của Customer. Chỉ là cảnh báo; không lưu ReturnBy thành ExpectedEndTime, không tạo hold tương lai hoặc tự checkout.

ReadWalkIn(actor, sessionId) theo **Session.View** đọc lại phiên walk-in Active và tính ReturnBy trong cùng read transaction/một mốc clock. Thêm booking/hủy/NoShow làm deadline thay đổi; đọc không ghi NoShow/audit. Nó không phải API public Guest hay danh sách tất cả session, không đọc session nguồn booking hoặc Completed qua đường này.

ScheduleRules.WalkInReturnBy là helper dùng chung giữa service và CalendarService, giữ nguyên quy tắc lịch cũ. Lịch hiện Occupied và sự kiện WalkIn theo thời gian đã dùng; chỉ booking còn hiệu lực giữ khoảng tương lai. Availability vẫn cho đặt tương lai khi hợp lệ, nhưng thao tác bắt đầu đúng now kiểm tra Active thực tế. Quá ReturnBy vẫn Active đến khi có checkout; cảnh báo IsOverdue sau mốc, không tự chuyển trạng thái.

## Transaction và đồng thời

CreateWalkIn xử lý NoShow hết hạn trong cùng transaction giống writer booking hiện tại. Nếu tạo walk-in hoặc audit lỗi, rollback cả khách mới/session/audit/NoShow vừa xử lý. Mất quyền/khóa user/đăng xuất bị chặn trước ghi.

Hai walk-in cùng Room hoặc Customer tối đa một thành công. Nhận lặp không tự trả phiên cũ vì walk-in không có Reservation nguồn định danh yêu cầu; bị chặn bởi Active, không tạo phiên thứ hai. UI bước sau phải khóa gửi lặp và xử lý lỗi rõ.

Walk-in cạnh tranh check-in, booking bắt đầu ngay, khóa Room và worker NoShow đều dùng writer transaction; không thể khóa phòng rồi để lại Active trong phòng khóa, hoặc cho hai phiên dùng cùng Room/Customer. Booking tương lai vẫn được phép; đọc lại deadline sẽ cập nhật theo lịch mới. Không retry vô hạn.

## Kiểm tra

Build Debug/Release vào bin/VerifyDebug và bin/VerifyRelease đạt. **11 bộ kiểm tra** WalkIn, CheckIn, Availability, Calendar, Reservations, Rooms, Customers, Authentication, Foundation, GuestCalendar, AuthenticationUi đạt trên dữ liệu tạm riêng. UI cũ render 55 ảnh; chưa thêm ảnh/form walk-in. Database thật và ảnh phòng không thay đổi, schema giữ v6.

Verify-WalkIn kiểm tra quyền nhận độc lập các quyền khác/đọc theo Session.View/null/mất quyền/khóa/đăng xuất; SĐT chuẩn hóa/tên cũ/tạo khách mới; actual UTC giữ ticks; các biên ca/trước và đúng grace; Active Room/Customer; booking đã tới giờ so với tương lai/ngày mai; snapshot giữ nguyên khi danh mục đổi; ReadWalkIn không ghi audit; deadline Room/Customer/hủy/cuối ca/quá giờ và lịch thống nhất; booking tương lai không bị giữ vô hạn; audit lỗi rollback cả khách/session/NoShow; cạnh tranh Room/Customer/check-in/booking ngay/khóa/NoShow và lấy clock sau writer qua giờ đóng ca.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-WalkIn.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-CheckIn.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Calendar.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
```

## Làm tiếp

6b.2 UI nhận khách trực tiếp theo Session.WalkIn: chọn phòng mở qua route bảo vệ theo đúng quyền, tên/SĐT, xác nhận và kết quả snapshot/giờ cần trả; không yêu cầu duration, không fallback Guest, không đòi Room.Manage/Customer CRUD/Reservation.Create. Danh sách phòng/preview nếu thêm chỉ xem trước; ghi vẫn kiểm tra lại mọi điều kiện. Gia hạn/gọi món/Guest Active/tiền tạm tính/checkout/hóa đơn/dashboard/RBAC UI/báo cáo tiếp tục giữ scope và quyết định quản trị 07/10.
