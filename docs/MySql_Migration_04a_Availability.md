# Migration MySQL 4a — giờ và kiểm tra trống/bận

Ngày 18/10/2026. Đối chiếu plan WPF và tham chiếu nghiệp vụ mục 11–14, 38, 41–43, 55.4; tái sử dụng BookingHours hiện có. WPF/worker tiếp tục dùng SQLite; schema SQLite v8/MySQL v1 giữ nguyên. Không nhập hoặc sửa database thật.

## Đã làm và cách hoạt động

MySqlAvailabilityService tương ứng AvailabilityService: kiểm tra ngày Việt Nam hôm nay đến +30, slot 00/30, thời lượng ban đầu 60/90/120/180 và cùng ca 09–12 hoặc 13–23. Cả Room và Customer được kiểm tra overlap theo khoảng nửa mở, cho phép tiếp giáp. Confirmed chỉ giữ chỗ khi StartTime > now−15 phút; đúng hạn +15 phút không còn giữ, nhưng đọc không ghi NoShow.

MySqlScheduleRules dùng chung khoảng giữ lịch: Confirmed còn hạn và Active từ booking dùng ActualStart/ExpectedEnd, không đếm lại CheckedIn. Walk-in không giữ tương lai; phiên quá giờ không tự kéo dài khoảng giữ. Tuy nhiên mọi Active của Room/Customer đều chặn đặt bắt đầu đúng now. Trạng thái phòng hiện tại và cảnh báo ReturnBy walk-in là các phép đọc riêng: ưu tiên phòng khóa/Active/Confirmed đã tới giờ, hạn trả động theo ca và lịch Room lẫn Customer; không lưu ExpectedEnd cho walk-in, không tự checkout.

Các helper nội bộ cho check-in, walk-in và gia hạn cũng được chuyển: nhận sớm/actual giữ giây và ticks, bỏ nguồn booking khi kiểm tra overlap, walk-in chặn booking đã tới giờ còn grace, gia hạn chỉ Active từ booking +30/+60 trước/đúng ExpectedEnd và cùng ca/Room/Customer. Đây là helper nghiệp vụ, chưa phải service được phân quyền để tạo booking/nhận phòng/gia hạn MySQL.

MySQL lưu UTC chuỗi O chuẩn 7 số lẻ theo CHECK hiện có, nên so sánh lịch giữ nguyên tick và không phụ thuộc múi giờ máy. Các bảng con UNION được đặt alias theo cú pháp MySQL.

## Transaction và cạnh tranh

Preview độc lập dùng RepeatableRead read-only; kết quả không giữ chỗ. Overload nhận connection/transaction dùng cùng dữ liệu với caller. Nếu Serializable, nó khóa Rooms FOR UPDATE trước rồi Customers FOR UPDATE trước khi đọc IClock và kiểm tra overlap; nếu RepeatableRead thì chỉ đọc, không hứa chống cạnh tranh.

LockResources là API dùng chung cho writer tương lai, yêu cầu cùng connection/transaction Serializable. Các đường writer phải theo cùng thứ tự tài nguyên và lấy mốc giờ sau khóa, rồi kiểm tra/lưu/audit cùng transaction. Helper At nhận một mốc IClock đã có, không tự lấy giờ khác; caller ghi phải khóa tài nguyên trước khi lấy mốc ấy. Không dùng khóa toàn ứng dụng, không giữ khóa từ preview đến submit. Caller cần xử lý deadlock/timeout và thử lại từ đầu theo nghiệp vụ; chưa tự retry hoặc thêm worker ở bước này.

## Kiểm tra

Tests/Verify-MySqlAvailability.ps1 biên dịch MySqlAvailabilityChecks và chạy online trên database musicbox_test_GUID riêng, chỉ dọn tên GUID đã kiểm tra. Không dùng musicbox hoặc dữ liệu/ảnh thật. Các SQL writer trong test chỉ là fixture để chứng minh hợp đồng transaction, chưa phải ReservationService/RoomSessionService đã chuyển.

Kiểm tra gồm giờ Việt Nam/ca/slot/ngày +30/ticks; Room khóa/thiếu và Customer thiếu; overlap Room/Customer/tiếp giáp; Confirmed hết đúng grace không ghi NoShow; CheckedIn không giữ trùng; booking session/walk-in/overdue; hai writer cùng phòng hoặc cùng khách tối đa một booking; rollback writer không giữ chỗ; clock sau khóa khi giờ bắt đầu đã qua (barrier sau khi đã mở connection/transaction); overload read-only không FOR UPDATE; CurrentRoomStatus và ReturnBy động; check-in bỏ nguồn/actual ticks; walk-in grace; gia hạn ticks/giới hạn lịch khách và sau ExpectedEnd.

Kết quả ngày 18/10/2026 sau khi ghép nhánh WPF:

- Build Debug/Release đạt tại bin/VerifyRevenueAvailabilityDebug và bin/VerifyRevenueAvailabilityRelease.
- Verify-MySqlAvailability online đạt cả Debug và Release (exit 0), gồm fixture rollback và barrier clock sau khi mở connection/transaction, trên GUID database riêng từng lượt.
- Verify-MySqlFoundation online Debug đạt (exit 0): schema/seed/idempotence/UTF-8/RBAC/FK/CHECK/rollback/UTC ticks/Active uniqueness/walk-in/snapshot/decimal/invoice immutable/version guard.
- Hồi quy SQLite/UI được ghi ở báo cáo nhánh WPF; kết quả helper MySQL không thay thế kiểm tra UI khi nối provider.

## Tiếp theo

Port ReservationService/NoShow cùng quyền/chuẩn hóa khách/audit/clock và cạnh tranh trên MySQL, sau đó public/staff booking/calendar/session/order/billing/checkout/báo cáo. Chỉ đổi App provider sau khi đầy đủ các đường dữ liệu, sao lưu/nhập/đối chiếu SQLite và Content, cùng kiểm tra UI. Availability đạt không có nghĩa ứng dụng đã chạy Aiven.
