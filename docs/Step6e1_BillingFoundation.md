# Bước 6e.1 — nền tảng tính tiền phiên

Ngày hoàn thành: 10/10/2026. WPF .NET Framework 4.7.2 + SQLite, giữ schema v7. Đối chiếu mục 17/25/26/55.2 và yêu cầu transaction/quyền hiện hành của plan tham chiếu trước triển khai.

## Đã làm và cách tính

Thêm BillingService và DTO SessionBill cho phiên Active:

- Tiền phòng tính từ ActualStartTime đến BillingEndTime = thời điểm đọc IClock sau khi đọc phiên/đơn trong cùng transaction; giữ giây/ticks và UTC. Không lấy giờ đặt, ExpectedEndTime, hạn trả walk-in hoặc giờ đóng cửa để cắt tiền.
- Dùng HourlyRate snapshot tại nhận phòng. Đổi giá/tên RoomType hay danh mục sau đó không đổi phiên cũ.
- RoomCharge = HourlyRate × thời lượng thực tế theo giờ, làm tròn đến đồng bằng MidpointRounding.AwayFromZero một lần. Không làm tròn block/phút nguyên, không tiền tối thiểu/VAT/phụ thu.
- ServiceCharge chỉ cộng Quantity × UnitPrice snapshot của Order Completed. Pending/Cancelled không cộng. Trả riêng số đơn của từng trạng thái để UI giải thích món chờ chưa tính tiền.
- TotalAmount = RoomCharge + ServiceCharge, dùng decimal cho tiền. Dùng phần nguyên và phần dư của ticks/giá trước khi làm tròn để tránh tràn phép nhân rate × ticks và giữ đúng mốc nửa đồng ở giá lớn.

Ví dụ 120.000 đ/giờ: 80 phút là 160.000 đ tiền phòng; hai món đã phục vụ giá snapshot 25.000 đ cộng 50.000 đ, tổng 210.000 đ. Món đang chờ hoặc đã hủy không cộng. 1 phút 30 giây là 3.000 đ tiền phòng; tại đúng giờ nhận là 0 đ.

Đây là nền tảng service, **chưa có nút/giao diện tiền tạm tính hoặc checkout/hóa đơn**.

## Truy cập và transaction

- ReadGuest(phoneNumber, sessionId): chuẩn hóa chung, chỉ phiên Active đúng SĐT hiện hành; số sai/số cũ/ID phiên khác/Completed bị chặn. Không OTP/account/mã truy cập mới.
- ReadStaff(actor, sessionId): Session.View hiện hành trong cùng read transaction, không đòi Order.View/Service.Manage/Invoice.View. DTO chỉ tổng tiền/giờ/snapshot phòng/số đơn, không có tên/SĐT/ID khách/RoomId/ReservationId hoặc danh sách chi tiết đơn; không mở đường đọc danh sách món khi thiếu Order.View.
- SessionBill trả ID phiên sở hữu/mã phòng/loại/giá đã chốt, giờ nhận/giờ tính, ticks/phút thực tế, tiền phòng/món/tổng và số đơn Completed/Pending/Cancelled. Không chứa PII, người tạo hay hóa đơn lịch sử.
- CalculateActive(connection, transaction, sessionId) là đường **internal**, dùng đúng connection/transaction của caller và đọc cùng công thức. Checkout tương lai phải Demand Session.CheckOut trong writer transaction trước gọi; không gọi ReadStaff để bắt buộc thêm Session.View. Thời điểm BillingEndTime trong kết quả dùng làm checkoutNow khi triển khai checkout.
- OrderService tách ReadOrders nội bộ để Billing dùng cùng cách đọc snapshot trong transaction, không gọi ListStaff/ListGuest hoặc tạo connection mới. API Order cũ giữ kiểm tra quyền/membership.
- Preview chỉ đọc: không tạo Invoice/audit, không ghi ActualEndTime/ExpectedEndTime, không hủy Pending/chuyển trạng thái phiên/Reservation/NoShow, không giữ giá/cache/PreviewId. Mỗi lần đọc tính lại đến giờ mới và món đã phục vụ mới nhất theo snapshot.
- Khi đồng hồ trước actual start, từ chối thay vì trả tiền âm hoặc âm thầm cắt về 0. Khi phiên quá giờ hoặc ngoài ca, tiền phòng vẫn tiếp tục tính đến now.

## Kiểm tra

Debug/Release build bin/VerifyDebug và bin/VerifyRelease. 22 bộ kiểm tra đạt trên dữ liệu tạm: Billing, Orders, Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, Calendar, GuestCalendar, CheckIn, WalkIn, Extension, Sessions, GuestSessions, AuthenticationUi. UI không đổi, bộ UI cũ vẫn render 94 ảnh.

Verify-Billing kiểm tra:

- 0/60/80/147 phút, 90 giây, một tick, UTC khác offset, actual giữ ticks, một clock.
- Dưới/đúng/trên nửa đồng, rate 1/120000/long.MaxValue, thời lượng đến DateTimeOffset.MaxValue; đối chiếu oracle BigInteger độc lập dùng phép chia số nguyên chính xác.
- Snapshot giá/phòng/món giữ khi danh mục thay đổi/ngừng bán; chỉ Completed cộng, tổng món vượt long vẫn đúng bằng decimal; đơn phiên khác không cộng.
- Guest số chuẩn hóa/số hiện hành/phiên khác; DTO tối thiểu; Staff chỉ Session.View, thiếu quyền/null/khóa tài khoản/đổi SecurityStamp/đăng xuất bị chặn.
- Read-only giữ audit/Order Pending/Active/ExpectedEnd/ActualEnd; walk-in qua ca/ngày vẫn tính; booking tính actual và vượt ExpectedEnd, không sửa Reservation.
- Read snapshot đồng thời ConfirmStaff: lần đọc đang mở giữ tổng/trạng thái cũ; lần đọc mới thấy Completed mới, không trộn hai snapshot hoặc cache.
- Helper đọc dữ liệu chưa commit trong cùng writer, rollback vẫn do caller quyết định; sai connection/null transaction bị chặn. Fixture writer sau chờ lock đọc giờ và món mới, Demand CheckOut độc lập Session.View. Đây là kiểm tra hợp đồng nền tảng, **chưa phải checkout thật**, chưa tạo Invoice/đóng phiên.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Billing.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
```

Không sửa database/ảnh thật hoặc đóng ứng dụng người dùng đang chạy.

## Tiếp theo

6e.2 nối tiền tạm tính vào Tra cứu SĐT/phiên Active của Guest và phiên nội bộ theo Session.View: hiển thị giờ tính/actual/giá đã chốt/thời lượng/tiền phòng/món/tổng, số đơn chờ chưa cộng và nút cập nhật. Đổi số/đổi phiên/mất quyền/phiên Completed xóa kết quả cũ; ghi rõ số tiền tiếp tục tăng, không phải tổng đã chốt. Không gọi đường Staff cho Guest.

Sau đó bước 7 checkout/hóa đơn: quyền Session.CheckOut trong writer, tính lại giờ và món mới, hủy Pending/ghi Invoice/Session/Reservation/audit cùng commit, Cash/BankTransfer/idempotence. Kiểm tra cạnh tranh Checkout–Order/gia hạn và rollback bằng service thật khi có. Giữ dashboard/doanh thu, quản trị tài khoản/RBAC/audit, báo cáo/Excel và lịch sử khách hàng.
