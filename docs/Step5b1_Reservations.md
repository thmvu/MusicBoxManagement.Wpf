# Bước 5b.1 — tạo Reservation và xử lý NoShow

Ngày 05/10/2026. Đối chiếu WPF plan và mục 5–9, 21–22, 32, 38, 40, 55 của bản tham chiếu. Giữ WPF .NET Framework 4.7.2 + SQLite/schema v6. Bước này là service và tác vụ nền; chưa thêm form đặt phòng.

## Đã làm

ReservationRequest chỉ nhận RoomId/StartTime/DurationMinutes/FullName/PhoneNumber. Không nhận CustomerId, UserId, Status, EndTime hay CreatedAt từ người dùng. Kết quả Reservation trả ID, phòng/khách, giờ UTC, Confirmed, người tạo và giờ tạo; không trả tên/SĐT cũ của Customer hoặc lịch sử người khác.

`CreateGuest` là đường Guest public riêng; `CreateStaff` bắt buộc phiên và quyền Reservation.Create hiện hành, không tự chuyển sang Guest nếu phiên null/hết hiệu lực. Staff/Manager mặc định có quyền; không kiểm tra cứng theo tên role. Tìm/tạo Customer trong booking được phép theo nghiệp vụ Reservation.Create; không mở quyền xem danh sách/sửa khách hàng riêng cho người chỉ có quyền đặt hộ.

Trong BeginWriteTransaction: kiểm tra quyền → validation tên/SĐT → lấy một mốc IClock → kiểm tra giờ → xử lý Confirmed hết hạn → tìm/tạo Customer → kiểm tra lại AvailabilityService với cùng connection/transaction/mốc giờ → INSERT Confirmed → audit → commit. Không thu cọc. Họ tên trim 1–100 ký tự, chuẩn hóa SĐT chung. SĐT cũ dùng Customer cũ, không ghi đè tên bằng tên vừa nhập. Khách mới và Customer.Create/Reservation.Create đều cùng transaction; failure rollback toàn bộ. Audit Guest không có UserId, Staff có UserId. Thời gian lưu chuẩn UTC O và được xác định từ hệ thống.

Availability helper có overload internal nhận mốc đã lấy để validation/grace/CreatedAt/audit của cùng booking thống nhất. Quy tắc ngày/ca/slot/thời lượng/Room.IsActive/overlap Room và Customer vẫn dùng nền tảng 5a. Writer SQLite tuần tự hóa kiểm tra và ghi; không dựa vào preview UI. Gửi tạo lặp cùng khoảng đang giữ lịch bị từ chối trùng, không tạo booking thứ hai; UI sau này phải khóa submit lúc bận.

## NoShow và worker

NoShowService.ProcessExpired lấy writer và mốc giờ hiện hành; chỉ chọn Confirmed với StartTime <= now−15 phút và không có RoomSession nguồn. UPDATE NoShow và Reservation.NoShow ActorType System/UserId null cùng transaction. Giữ nguyên StartTime/EndTime/CreatedAt. Không đánh dấu CheckedIn hoặc booking đã có session thành NoShow; không tự kết thúc phiên. Chạy lại hoặc hai worker cùng xử lý không ghi log trùng.

NoShowWorker dùng DispatcherTimer mỗi phút, thực hiện Initialize/ProcessExpired bằng Task.Run, có cờ không chồng lượt chạy trong một worker. App khởi động worker sau khi mở cửa sổ; StartAsync xử lý ngay một lượt để bắt booking tồn đọng. OnExit Dispose dừng timer. Nếu lỗi, ghi Trace và chờ lượt kế tiếp; SQLite rollback trạng thái/log. Worker chỉ chạy khi app mở; không cam kết đúng từng giây hoặc chạy khi máy/app tắt. Thao tác đang xử lý có thể hoàn tất khi timer được dừng; mỗi transaction vẫn nguyên tử.

Tạo booking gọi NoShow trong cùng transaction. Nếu booking sau đó thất bại, phần NoShow của transaction đó cũng rollback; worker độc lập xử lý lại. Availability vẫn loại Confirmed hết grace dù status chưa được cập nhật. Khi bổ sung preview/calendar/lookup ở UI cần xử lý tồn đọng trước các truy vấn quan trọng.

AuditService bổ sung đường Guest/System và mốc giờ tùy chọn; các caller Staff cũ giữ cách dùng. Không tạo tài khoản giả cho Guest/worker. Không thay schema hoặc seed booking/khách thử vào database thật.

## Kiểm tra

- Debug/Release MSBuild đạt, không warning/error.
- Verify-Reservations đạt trên SQLite tạm: Guest/Staff Confirmed/UTC/creator/audit, SĐT +84/84/0, tái sử dụng khách và giữ tên, quyền hiện hành/thu hồi/logout, tạo khách trong booking không mở Customer CRUD, validation, giờ/phòng/khách trùng, phòng khóa/không tồn tại và sát biên.
- Inject lỗi Reservation.Create audit: rollback booking/khách/log; inject lỗi NoShow audit: rollback status. Trước grace một tick chưa NoShow, đúng mốc thì NoShow; tạo thay thế xử lý NoShow cùng transaction; booking thất bại rollback cả maintenance, chạy maintenance riêng phục hồi. CheckedIn có session không bị đánh dấu.
- Hai CreateGuest cùng Room/giờ chỉ một booking và chỉ giữ khách của lượt thắng; cùng SĐT chuẩn hóa đặt hai Room cùng giờ cũng chỉ một booking/Customer. RoomService khóa thật và ReservationService tạo thật cạnh tranh: chỉ một thao tác thành công. Hai NoShowService writer chỉ một status/log.
- Verify-AuthenticationUi ở STA dùng database worker tạm riêng: StartAsync xử lý quá hạn, lỗi audit giữ Confirmed, gọi lại sau khi bỏ lỗi thành NoShow và không log trùng. Đây kiểm tra startup/retry qua cùng đường xử lý; không chờ timer đủ một phút. Các UI cũ vẫn đạt và render 17 ảnh; chưa có ảnh/form đặt phòng mới.
- Verify-Foundation/Authentication/RoomTypes/Rooms/Services/Customers/Availability đều đạt sau mở rộng AuditService/Availability. Tổng chín bộ kiểm tra đạt; không mở/xóa database đang dùng.

Chưa có service check-in nên chưa tuyên bố kiểm tra cuộc đua NoShow/check-in hoàn chỉnh; phải thêm ở bước vận hành. Chưa có UI booking, Guest lookup/hủy, calendar hoặc phiên/checkout. Tiếp theo **5b.2 — danh sách phòng public và form chọn ngày/giờ/phòng/thời lượng/tên/SĐT**, preview rồi submit qua service; sau đó lookup/hủy và các UI còn lại theo plan.
