# Migration MySQL 3d — khách hàng và số điện thoại

Ngày 18/10/2026. Đối chiếu plan WPF, tham chiếu mục 21–22 và báo cáo Step4d1_Customers. Giữ nguyên nghiệp vụ; ứng dụng WPF/worker vẫn SQLite, schema SQLite v8/MySQL v1. Không nhập hoặc sửa dữ liệu thật.

## Đã làm và cách hoạt động

`MySqlCustomerService.Search/Save` là đường truy cập MySQL tương ứng `CustomerService`; chưa nối giao diện Khách hàng vào MySQL. Search cần Customer.View, trả danh sách và CanCreate/CanEdit theo cùng snapshot RepeatableRead read-only. Tên tìm một phần bằng .NET OrdinalIgnoreCase để giữ cách tìm tiếng Việt, có phân biệt dấu; SĐT chỉ tìm đầy đủ sau chuẩn hóa, hai bộ lọc kết hợp.

Save cần Customer.View và Customer.Create hoặc Customer.Edit tương ứng; một quyền không tự cấp quyền khác. Dùng lại PhoneNumberNormalizer: bỏ khoảng trắng/chấm/gạch ngang, +84/84 về 0, đúng 10 chữ số ASCII bắt đầu bằng 0; không thêm OTP hoặc quy tắc nhà mạng. Họ tên trim 1–100 ký tự, SĐT chuẩn hóa unique. Tạo trùng số báo dùng khách hiện có, không ghi đè tên cũ.

Trong writer Serializable, đọc lại phiên/quyền, khóa khách sửa bằng SELECT FOR UPDATE và so tên/số bản gốc để chặn form cũ. No-op không tạo nhật ký. INSERT lấy LAST_INSERT_ID trên chính connection; UPDATE giữ CustomerId. Customer.Create/Customer.Update dùng ActorType Staff/UserId/EntityId/thời gian UTC, ghi cùng transaction; lỗi audit rollback dữ liệu. UNIQUE SĐT bảo vệ thêm trước hai số tương đương gửi đồng thời. Deadlock/timeout báo tìm lại; không retry vô hạn. Lỗi duplicate khi tranh ghi báo số đã có.

Đổi tên/số không đổi CustomerId hoặc dữ liệu Reservation/RoomSession liên kết; tra cứu sau đó dùng số mới. Đây là service danh mục nội bộ, không công khai danh sách khách hoặc lịch sử. Không xóa khách. Giữ các phạm vi lịch sử/đếm lần dùng gần nhất/báo cáo và Guest membership cho bước tương ứng; chưa có booking/session MySQL thật.

## Kiểm tra

`Tests/Verify-MySqlCustomers.ps1 -BuildDirectory <folder>` biên dịch MySqlCustomerChecks rồi chạy Aiven online. Mỗi lượt tạo database `musicbox_test_<GUID>` riêng và chỉ dọn đúng tên đã kiểm tra; mật khẩu đọc cấu hình máy, không ghi secret vào repo/log. Fixture phòng chỉ lưu đường dẫn giả, không sao chép ảnh hoặc dữ liệu thật.

Bộ kiểm tra gồm chuẩn hóa số tương đương/số sai/Unicode/không kiểm tra nhà mạng, trim/required/giới hạn tên, tìm tên tiếng Việt và số đầy đủ, quyền View/Create/Edit riêng và thu hồi hiện hành, Guest/logout bị chặn, duplicate giữ tên, ID trả về khớp server và audit actor/ID, no-op/stale/form đồng thời, hai số tương đương tạo đồng thời tối đa một khách, rollback create/update khi audit lỗi, schema/check constraint và mở lại giữ dữ liệu. Reservation/RoomSession là SQL fixture để kiểm tra giữ CustomerId khi đổi số; không coi đó là nghiệm thu các service MySQL chưa port.

Kết quả ngày 18/10/2026 sau khi ghép hai nhánh:

- Build Debug/Release đạt tại `bin/VerifyAuditCustomersDebug` và `bin/VerifyAuditCustomersRelease`.
- Verify-MySqlCustomers chạy Aiven online đạt cả Debug và Release (exit 0), mỗi lượt trên GUID database riêng; gồm cạnh tranh form cũ và số chuẩn hóa tương đương cùng rollback audit.
- Verify-MySqlFoundation online Debug đạt (exit 0), giữ schema/seed/idempotence/UTF-8/RBAC/FK/CHECK/UTC ticks/Active uniqueness/snapshot/decimal/invoice immutable/version guard.
- Hồi quy SQLite/UI của nhánh WPF được ghi trong báo cáo nhật ký 8d và báo cáo tổng hợp; các kết quả MySQL trên không thay thế kiểm tra UI khi nối provider.

## Tiếp theo

Port availability/BookingHours/ScheduleRules và booking/NoShow trên MySQL với transaction/clock/overlap Room lẫn Customer, rồi session/Order/Billing/checkout/báo cáo. Các chức năng WPF mới như ma trận quyền và nhật ký cũng cần đường MySQL trước đổi App. Chỉ chuyển provider sau sao lưu/nhập/đối chiếu dữ liệu và Content cùng kiểm tra UI; không coi service này là app đã chạy Aiven.
