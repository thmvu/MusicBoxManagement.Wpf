# MySQL 4d — Service lịch nội bộ

## Nghiệm thu bổ sung 21/10/2026

Kết nối online đã hoạt động. Lượt chạy thực phát hiện Read gọi MySqlPermissionService.Demand dành cho writer Serializable trong transaction đọc RR; đã đổi sang ReadAccess + kiểm tra Calendar.View trong cùng read snapshot, giữ nguyên ranh giới quyền/PII và chế độ read-only. Verify-MySqlCalendar **online Debug/Release đạt** trên bin/VerifyMigrationDebug/Release. Hồi quy MySqlAvailability/MySqlReservations/MySqlFoundation online Debug đạt. Các DB GUID đã dọn, không đổi dữ liệu thật/provider. Nhật ký 20/10 dưới đây là lịch sử; trạng thái hiện tại đã nghiệm thu. Bảng chung: [MySql_Migration_Status.md](MySql_Migration_Status.md).

Ngày 20/10/2026. Port đường đọc CalendarService sang MySqlCalendarService, đối chiếu mục 38 và lịch ngày/tuần của plan. Chưa nối UI/provider, giữ schema MySQL v1 và SQLite v8.

## Cách hoạt động

`Read(actor, range, roomId)` yêu cầu Calendar.View hiện hành trong cùng transaction RepeatableRead read-only với dữ liệu. Dùng CalendarRange ngày/tuần UTC+7 (thứ Hai), một IClock cho toàn bộ kết quả; không Initialize/DDL hoặc gọi NoShow maintenance. Không đòi Reservation.View/Session.View/Room.Manage.

Tái dùng RoomSchedule/RoomScheduleEvent và MySqlScheduleRules: CurrentStatus là trạng thái hiện tại, Holds là khoảng giữ lịch, Events là lịch thực tế/dự kiến. Confirmed hết đúng +15 phút không giữ lịch dù worker chậm. Phiên booking giữ ActualStart..ExpectedEnd, không cộng thêm nguồn CheckedIn hoặc kéo dài hold theo thời gian quá giờ. Active thực tế đến now; Completed đến ActualEnd; giữ ticks và giờ gốc cho UI clip theo range.

Walk-in không tạo future hold/ExpectedEnd; ReturnBy động lấy mốc ca/lịch Room và Customer qua quy tắc chung. Phòng khóa vẫn có lịch sử. Kết quả có tên/SĐT khách nội bộ, không dùng cho Guest; public tiếp tục qua MySqlGuestCalendarService. SQL derived table có alias theo cú pháp MySQL.

## Kiểm tra ngày 20/10

- Build Debug/Release thành công tại bin/VerifyCalendarMySqlDebug và bin/VerifyCalendarMySqlRelease.
- Verify-Calendar và Verify-Availability SQLite đạt trên build Debug với database tạm.
- Verify-Authentication và Verify-Foundation SQLite cũng đạt trên build Debug; không thay dữ liệu đang sử dụng.
- Đã thêm Verify-MySqlCalendar và fixture: Calendar-only/current permission, snapshot quyền+dữ liệu, một clock, ngày/tuần/filter, grace ±1 tick/không ghi NoShow/audit, actual/expected/overdue, phòng khóa, walk-in/ReturnBy theo booking và hết grace, stamp/schema.
- Test MySQL Debug/Release đã biên dịch fixture nhưng không chạy được kiểm tra online: hostname `musicbox-mysql-baitapnet.j.aivencloud.com` trả **DNS name does not exist**; TCP cũng báo không phân giải được host. Chưa xác nhận các assertion trên MySQL thật, không coi bước này đã nghiệm thu online.

Fixture tạo booking bằng MySqlReservationService thật; phiên Active/Completed là SQL fixture chỉ để kiểm tra projection. Chưa có check-in/checkout MySQL thật trong bước này. Test chỉ dùng tên musicbox_test_GUID, guard trước DROP; không thay database musicbox hoặc defaultdb. Cần chạy lại MySqlCalendar Debug/Release cùng MySqlAvailability/MySqlReservations/MySqlFoundation khi kết nối phục hồi.

## Làm tiếp

Hoàn tất kiểm tra online rồi port check-in/walk-in/gia hạn và Guest lookup đầy đủ; tiếp order/billing/checkout/invoice/quản trị/báo cáo. App vẫn chạy SQLite, chưa chuyển worker/provider hoặc nhập dữ liệu thật. Trước cutover vẫn sao lưu/đối chiếu database + Content và kiểm tra UI riêng.
