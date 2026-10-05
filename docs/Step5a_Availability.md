# Bước 5a — giờ đặt phòng và availability

Ngày 05/10/2026. Đối chiếu plan WPF và bản tham chiếu mục 6–9, 12–13, 38. Giữ schema v6, WPF .NET Framework 4.7.2 + SQLite. Bước này chỉ làm helper nghiệp vụ và kiểm tra, chưa thêm UI hoặc service tạo Reservation.

## Quy tắc đã triển khai

`BookingHours.ValidateReservation` đổi thời điểm về giờ Việt Nam UTC+7, độc lập timezone của máy chạy. Ngày từ hôm nay đến +30 ngày gồm ngày cuối; StartTime không trước now. Chỉ bắt đầu phút 00/30, giây/phần lẻ bằng 0; thời lượng ban đầu 60/90/120/180 phút. Khoảng phải trong một ca 09:00–12:00 hoặc 13:00–23:00 cùng ngày, có thể kết thúc đúng 12:00/23:00. Trả EndTime UTC; không ép giờ thực tế check-in/walk-in/checkout vào slot đặt trước.

`AvailabilityService.CheckReservation` kiểm tra phòng tồn tại/đang mở, khách tồn tại nếu truyền CustomerId, rồi các khoảng giữ lịch. CustomerId null chỉ để xem trước cho khách chưa có; service ghi tương lai phải resolve/tạo Customer trước khi chạy lại kiểm tra.

| Dữ liệu | Cách giữ lịch |
| --- | --- |
| Confirmed còn hiệu lực | [StartTime, EndTime) nếu now < StartTime+15 phút |
| Active từ Reservation | [ActualStartTime, ExpectedEndTime) |
| CheckedIn | Không dùng khoảng Reservation cũ để chặn thêm |
| Active walk-in | Không có khoảng chặn booking tương lai |
| Active quá ExpectedEndTime | Không tự kéo dài khoảng dự kiến; vẫn chặn đặt bắt đầu đúng now |
| Cancelled/NoShow/Completed/Confirmed hết grace | Không giữ lịch tương lai |

Overlap là newStart < existingEnd và newEnd > existingStart, kiểm tra cả Room và Customer. Ví dụ booking 20:00–22:00 cho phép đặt trước kết thúc đúng 20:00 hoặc bắt đầu đúng 22:00. Khách có lịch trùng không được đặt phòng khác trong cùng khoảng. Nếu StartTime đúng now, bất kỳ Active Session ở phòng hoặc khách đều chặn, kể cả walk-in hoặc phiên quá giờ. Đây chỉ là quy tắc tạo Reservation, chưa kiểm tra check-in/walk-in/gia hạn riêng ở mục 38.4.

Kết quả có CanBook, Reason, EndTime và CheckedAt. Không trả danh sách khách hoặc chi tiết lịch sử người khác. Lý do phân biệt phòng khóa/không tồn tại, khách không tồn tại, giờ không hợp lệ, phòng/khách còn phiên Active hoặc trùng lịch.

## Transaction và giới hạn

CheckReservation thông thường dùng một transaction đọc để xem trước. Overload nhận connection/transaction cho service ghi tương lai; lấy now từ IClock bên trong transaction rồi đọc lại Room/Customer/khoảng giữ lịch. Caller phải lấy writer bằng BeginWriteTransaction, authorize Guest/Staff đúng quy tắc, resolve Customer, chạy kiểm tra và INSERT/audit cùng transaction. Kết quả xem trước không giữ chỗ và không thay kiểm tra lúc submit.

AvailabilityService là helper quy tắc, không phải ranh giới phân quyền hoặc endpoint Guest Lookup. Nó chưa được nối vào UI public. Customer lookup Guest về sau phải dùng SĐT đã chuẩn hóa và chỉ trả phạm vi mục 22.

Chưa có worker ghi NoShow hoặc audit NoShow: truy vấn chỉ loại Confirmed hết grace ngay cả khi trạng thái stored còn Confirmed. Chưa tạo booking/customer, chưa có form chọn giờ, lịch ngày/tuần, lookup/hủy, cảnh báo quá giờ, hoặc luồng nhận phòng. Không coi bước 5 hoàn tất.

## Kiểm tra

- Debug/Release MSBuild đạt, không lỗi/cảnh báo. Verify-Foundation và Verify-Rooms đạt để giữ các kiểm tra dữ liệu/transaction/guard phòng cũ.
- Verify-Availability đạt trên SQLite tạm: UTC/giờ Việt Nam và ngày Việt Nam khác UTC, hôm nay/+30/+31, quá khứ/giây/phần lẻ/slot, bốn thời lượng và thời lượng không hợp lệ, đầu/cuối ca/nghỉ trưa/vượt ca.
- Phòng/khách không tồn tại, khóa; overlap và sát biên ở Room/Customer, khách khác/phòng khác, trạng thái lịch sử/CheckedIn; trước grace một tick vẫn chặn, đúng +15 phút không giữ lịch dù stored Confirmed.
- Phiên nguồn booking dùng ExpectedEndTime và không double-count Reservation; walk-in tương lai được đặt nhưng đúng now bị chặn cả Room/Customer; phiên quá giờ không nới lịch tương lai.
- Hai writer fixture dùng overload trong BeginWriteTransaction trước INSERT cùng phòng/giờ: đúng một writer thành công. Đây kiểm tra hợp đồng transaction trên SQLite thật, chưa phải test service booking/khách/quyền/audit hoàn chỉnh. Từ chối transaction null và giữ schema v6.

Database/fixture chỉ trong file tạm, không mở/xóa dữ liệu đang dùng. UI không thay đổi nên không chạy lại bài render UI ở bước này. Tiếp theo 5b: service đặt phòng với customer reuse, quyền và audit/NoShow; rồi form đặt và tra cứu/hủy, calendar theo plan.
