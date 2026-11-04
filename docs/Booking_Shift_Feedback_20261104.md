# Thông báo giờ nghỉ ở đặt và nhận phòng — 04/11/2026

## Lỗi và cách sửa

Ảnh người dùng gửi dừng tại `MySqlRoomSessionService.CheckInCore`: booking hợp lệ nhưng giờ nhận thực tế cộng đủ thời lượng đã vượt ca. Ví dụ đặt 11:00–12:00, nhận lúc 11:10 thì kết thúc 12:10. Trước đây UI chưa chặn trường hợp này; service ném lỗi nghiệp vụ và Visual Studio có thể dừng trước khi UI xử lý. Nội dung lỗi cũ cũng chỉ nêu hai ca, không giải thích nghỉ trưa.

Đã sửa hai phía:

- **Khách tự đặt và nhân viên đặt hộ:** kiểm tra giờ trước cả Kiểm tra giờ và Xác nhận đặt. Khoảng qua 12:00 báo nghỉ trưa 12:00–13:00; quá 23:00 báo đóng cửa. Popup tiếng Việt dùng cơ chế WPF hiện có, giữ nội dung form. Không gọi Preview/Create khi giờ sai thông thường.
- **Nhân viên nhận phòng:** kiểm tra giờ thực tế trước mở xác nhận và kiểm tra lại khi bấm Xác nhận. Thông báo ghi giờ nhận, đủ số phút đã đặt, giờ trả dự kiến và lý do nghỉ trưa/đóng cửa. Khi từ chối, đóng phần xác nhận nhưng giữ danh sách và booking đang chọn.

`BookingHours` cung cấp các hàm Try trả reason để UI và MySQL availability xử lý đầu vào thường gặp mà không dùng exception. Các hàm Validate cũ vẫn bảo vệ caller trực tiếp. MySQL availability dùng Try, không sửa SQL/schema hoặc thứ tự khóa. Service writer tiếp tục lấy giờ/kiểm tra lại sau khóa và rollback khi không hợp lệ.

## Nghiệp vụ được giữ

Đối chiếu `Reference_Web_ProjectPlan_v1.3.md` mục 6/11/16/38/43 và checklist kiểm thử: mở 09:00–12:00, 13:00–23:00; booking slot 30 phút, thời lượng 60/90/120/180; từ hôm nay tới ngày +30; nhận thực tế giữ toàn bộ duration. Không cắt thời lượng, làm tròn giờ, tự chuyển walk-in hoặc cho qua giờ nghỉ.

Kết thúc **đúng 12:00 hoặc 23:00 vẫn hợp lệ**. Popup preview và đặt thành công nhắc khách đến sớm trước giờ đã đặt vì nhận muộn không đủ thời lượng trước giờ nghỉ/đóng cửa. Với các lượt này, thông tin sau đặt không còn hứa chung rằng đến trước Start+15 luôn nhận được; hạn NoShow 15 phút vẫn giữ nguyên và không bảo đảm các điều kiện nhận khác.

Giữ quyền Guest/nhân viên, transaction, audit, snapshot giá, NoShow và SSL/timeout. Không sửa dữ liệu quán hoặc backend cũ bị cấm.

## Kiểm tra

Runner mới `Tests/Verify-MySqlBookingHoursUi.ps1` chỉ nạp dependency MySQL/WPF, tạo database tên GUID riêng rồi dọn trong finally. Dùng service MySQL thật tạo phòng/booking/nhận phòng; SQL phụ chỉ kiểm đếm, đọc người tạo và cleanup database sở hữu.

Phạm vi:

- Guest và route nhân viên đặt hộ: 11:00/90, 22:00/90 bị chặn trước service, popup đúng lý do và không đổi Reservation/Session/Customer/Audit; 11:00/60 và 22:00/60 đặt được kèm cảnh báo. Route nhân viên dùng actor Admin, có kiểm CreatedByUserId; không coi đây là lượt đăng nhập UI riêng bằng role Staff.
- Nhận muộn 11:10 và 22:10 bị chặn trước writer, giữ đủ duration; giờ thay đổi giữa xem trước và xác nhận cũng bị chặn. FirstChance kiểm không phát sinh ArgumentException từ BookingHours hoặc InvalidOperationException từ CheckIn ở các ca UI này.
- Gọi writer trực tiếp với giờ không hợp lệ vẫn bị từ chối và rollback; exception có chủ ý ở ca bảo vệ backend này được tách khỏi ca UI.
- Nhận đúng giờ để kết thúc 12:00/23:00 tạo đúng một phiên. Pure contract kiểm biên +1 tick, 12:00 bắt đầu bị chặn, 13:00 hợp lệ, trước 09:00, qua ngày, ngày +30/+31 và slot có tick lẻ.

| Kiểm tra trên build dùng thật | Kết quả |
|---|---|
| Build Debug/Release | Exit 0: [Debug](verification/20261104-booking-shifts/build-debug.log), [Release](verification/20261104-booking-shifts/build-release.log) |
| Hours UI Debug/Release | Exit 0, 18 popup mỗi bản: [Debug](verification/20261104-booking-shifts/hours-debug.log), [Release](verification/20261104-booking-shifts/hours-release.log) |
| Reservations Release | Exit 0: [log](verification/20261104-booking-shifts/reservations-release.log) |
| CheckIn Release | Exit 0: [log](verification/20261104-booking-shifts/checkin-release.log) |
| Feedback workflow Debug | Exit 0, 34 popup: [log](verification/20261104-booking-shifts/feedback-debug.log) |

Hai suite Reservations/CheckIn dùng loader MySQL-only `Verify-MySqlNoShowLocks.ps1 -Suite ...`, không chạy runner pha provider. Không nhận mọi suite MySQL đã chạy hoặc mọi suite đều được chạy ở hai cấu hình.

Đã xem [popup nghỉ trưa Release](images/20261104-booking-shifts/lunch-notice.png) và [nhận hợp lệ kết thúc đúng 23:00 Release](images/20261104-booking-shifts/checkin-exact-closing.png). Ảnh popup Debug bị đen do capture/render, không dùng làm bằng chứng hình ảnh; assertion đọc nội dung và số popup Debug vẫn đạt. Hai ảnh Release lưu trong Git là ảnh đã xem được nội dung.

## Lịch sử RED và chỉnh fixture

Giữ log gốc trong `verification/20261104-booking-shifts/`:

1. [RED đầu](verification/20261104-booking-shifts/red-initial-fixture.log) và [green đầu](verification/20261104-booking-shifts/green-initial-fixture-failure.log) dùng 11:30/22:30, vốn không nằm trong Slots hiện có vì không đủ thời lượng tối thiểu 60 phút. Assertion reason thất bại trước khi tới tình huống cần thử; đây là lỗi fixture. Không đổi Slots để làm test qua.
2. [RED đã chỉnh](verification/20261104-booking-shifts/red-supported-input.log) dùng giá trị chọn được 11:00/90 và 22:00/90 chạy trên bản Release cũ 0995c21: thất bại đúng assertion popup phải giải thích nghỉ trưa trước submit.
3. [Green Debug lượt hai](verification/20261104-booking-shifts/green-clock-fixture-failure.log) qua phần booking nhưng fixture nhảy clock tới buổi tối trước khi nhận booking buổi trưa. Search chạy maintenance NoShow hợp lệ làm thay đổi audit; không thể quay clock ngược để hồi sinh booking. Đã nhận buổi trưa trước rồi mới thử buổi tối và lấy mốc count mới; không sửa production hoặc nới assertion rollback.
4. [Candidate Debug](verification/20261104-booking-shifts/candidate-debug.log) sau sửa fixture đạt 18 popup. Kết quả bản dùng thật ghi riêng trong bảng trên, không lấy candidate thay cho Debug/Release cuối.

Review cuối một lượt không có Critical/Important trong bốn file product. Góp ý thiếu coverage đặt hộ sát giờ đóng cửa/biên ca đã được bổ sung trong fixture trước các lượt cuối.

## Giới hạn và cách thử tại máy

Đây là kiểm tra phía UI cho giờ thông thường và phòng vệ backend. Nếu thời gian/trạng thái/quyền thay đổi trong lúc chờ khóa, service vẫn phải từ chối theo giờ mới; Visual Studio bật break on thrown/user-unhandled có thể dừng tại exception trước khi caller nhận lỗi. Không đổi cấu hình debugger hoặc thêm global handler nuốt exception để che tình huống đó. Không khẳng định mọi race hoặc mọi màn đều đã chạy.

Chạy bản build mới, chọn 11:00/90 phút rồi Kiểm tra giờ hoặc Xác nhận đặt: phải thấy popup nghỉ trưa, form còn nguyên. Chọn 22:00/90: popup đóng cửa 23:00. Chọn 11:00/60: đặt được và nhắc đến sớm. Khi nhận booking sát biên ca, thông báo từ chối phải nêu giờ trả thực tế vượt ca, không tạo phiên/audit nhận phòng.
