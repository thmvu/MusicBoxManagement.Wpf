# Bước 6d.1 — nền tảng gọi món và trạng thái đơn

Ngày 08/10/2026. Đối chiếu mục 16/22–24/35/44–45/47/55.4 của Reference_Web_ProjectPlan_v1.3.md. Đây là bước dữ liệu/service; chưa có giao diện menu, giỏ món hoặc nút phục vụ/hủy món.

## Đã sửa và cách hoạt động

- Schema **v7** thêm Orders và OrderItems; migration trong transaction giữ dữ liệu v6, không seed đơn/món thật. Có FK đến phiên/người tạo/dịch vụ, không cascade delete, trạng thái Pending/Completed/Cancelled, unique (OrderId, ServiceId), CHECK số lượng nguyên 1–10/giá nguyên dương/tên snapshot/UTC CreatedAt. Ít nhất một item được service kiểm tra trước khi tạo; không giữ transaction qua bước chọn/xác nhận giao diện.
- Thêm OrderService và DTO ServiceOrder/ServiceOrderItem. Giỏ đầu vào chỉ có ServiceId/Quantity, không nhận tên/giá từ client. Mỗi lần gửi tạo một Order; dòng trùng ServiceId được gộp, tổng số lượng một món không vượt 10. Giỏ rỗng/món không tồn tại/ngừng bán bị từ chối.
- CreateGuest chuẩn hóa SĐT, đọc lại phiên Active thuộc số hiện hành trong writer transaction, kiểm tra giờ mở cửa rồi tạo Pending. CreatedByUserId null, audit Guest. Không gọi đường Staff, không cần account/OTP/mã truy cập.
- CreateStaff theo Order.Create hiện hành, độc lập Order.View/Order.Confirm/Session.View/Service.Manage; tạo Completed trực tiếp, lưu CreatedByUserId và audit Staff. API này dành cho **món đã phục vụ**, giao diện tương lai phải nói rõ điều đó.
- Mỗi item lưu ServiceNameSnapshot/UnitPrice lấy từ Services trong cùng transaction. Đổi tên/giá hoặc ngừng bán không sửa/hủy đơn cũ, kể cả Pending; đơn mới dùng thông tin hiện hành.
- ConfirmStaff kiểm tra Order.Confirm, chỉ Pending trong Active Session → Completed khi món đã phục vụ. CancelStaff kiểm tra Order.Cancel, CancelGuest kiểm tra số hiện hành và Active Session; chỉ Pending → Cancelled. Không sửa item/số lượng, không đổi Completed/Cancelled tiếp; muốn sửa thì hủy Pending rồi tạo đơn mới.
- Tạo món mới chỉ trong ca 09–12 hoặc 13–23 UTC+7, giờ thật không cần slot. Đơn Pending cũ có thể xác nhận/hủy khi phiên còn Active, kể cả giờ nghỉ/sau đóng cửa. Quá ExpectedEndTime không tự đóng phiên hoặc cấm xử lý món đã gọi.
- Quyền/quan hệ phiên–khách/SĐT/Active/trạng thái/cập nhật/audit cùng writer transaction, IClock sau lock. Xác nhận và hủy cạnh tranh chỉ một thao tác lưu; lần còn lại báo trạng thái đã thay đổi. Lỗi item hoặc audit rollback toàn bộ.
- ListGuest chỉ trả các đơn trong phiên Active đúng số hiện hành. ListStaff theo Order.View độc lập các quyền ghi, có thể đọc lịch sử phiên Completed. DTO không có CustomerId, SĐT/tên khách, RoomSessionId hoặc CreatedByUserId. Đọc snapshot, không đổi trạng thái/audit.

Service không có sửa/xóa đơn hoặc item. Không thêm ghi chú/tồn kho. Chưa tính tiền tạm tính hoặc hóa đơn; bước Billing chỉ cộng Order Completed, giữ Pending/Cancelled riêng đúng plan.

## Kiểm tra

Build Debug/Release vào bin/VerifyDebug và bin/VerifyRelease với intermediate riêng. Kiểm tra chỉ dùng database tạm, không ghi đè/xóa/nâng cấp database đang dùng.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Orders.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
```

OrderChecks kiểm tra:

- Database v6 có tài khoản/phòng/dịch vụ/khách/phiên/audit → v7 giữ dữ liệu, khởi tạo lại không mất đơn/item/audit. Inject bảng OrderItems gây migration lỗi: rollback cả bảng Orders và version.
- SĐT chuẩn hóa/số hiện hành/phiên của người khác, DTO public không PII, phiên Active/walk-in và guard Completed (fixture; chưa có checkout thật).
- Quyền Create/View/Confirm/Cancel độc lập và kiểm tra hiện hành/đăng xuất. Guest Pending/Staff Completed, creator và audit đúng.
- Giỏ rỗng, món không tồn tại/ngừng bán, số lượng sai, gộp dòng và tổng tối đa 10; giá/tên từ server, giữ snapshot cũ và dùng giá/tên mới cho đơn mới.
- Mốc 09/12/13/23 và giây/ticks, xử lý Pending cũ ngoài ca; chờ writer qua 23:00 phải từ chối đơn mới.
- FK/CHECK/unique và ngăn xóa dịch vụ đã có item; lỗi item thứ hai hoặc audit rollback đơn/trạng thái.
- Năm vòng ConfirmStaff/CancelGuest đồng thời: chỉ một audit chuyển trạng thái. Tạo đơn/ServiceCatalogService.Save đồng thời giữ snapshot của đơn trước, chặn tạo sau khi ngừng bán.

Đã chạy đạt **21 bộ kiểm tra**: Orders, Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, Calendar, GuestCalendar, CheckIn, WalkIn, Extension, Sessions, GuestSessions, AuthenticationUi. UI cũ render 76 ảnh; không thêm màn hình bước này.

Các kiểm tra cũ đối chiếu SqliteDatabase.CurrentSchemaVersion thay literal v6; fixture tái dựng phiên bản cũ bỏ hai bảng mới trước khi dựng lại và vẫn giữ kiểm tra rollback. Foundation từ chối CurrentSchemaVersion+1. Chưa kiểm tra cạnh tranh với checkout thật vì service checkout chưa có; bổ sung cùng bước thanh toán.

## Tiếp theo

6d.2: đường đọc menu đang bán theo quyền Order.Create hoặc Guest đúng phiên/SĐT, rồi giao diện Guest chọn món/số lượng/gửi Pending/xem-hủy Pending và nhân viên đọc/xác nhận đã phục vụ/tạo hộ Completed. Chặn gửi lặp ở UI; không tự coi cùng một giỏ là cùng đơn vì khách được đặt thêm lần khác.

Sau đó Billing dùng chung tiền tạm tính/hóa đơn, checkout tự hủy Pending trong cùng transaction. Giữ scope dashboard/RBAC/báo cáo/lịch sử khách và chưa coi vận hành/Guest mục 22 đã đầy đủ. Khi chạy bản mới, ứng dụng sẽ tự nâng database v6 lên v7; nên thoát bản ứng dụng cũ rồi mở bản build mới. Khi sao lưu giữ cả database và Content ảnh phòng.
