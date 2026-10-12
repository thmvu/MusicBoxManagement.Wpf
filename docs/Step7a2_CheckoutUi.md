# Bước 7a.2 — Giao diện trả phòng và xác nhận nhận tiền

Hoàn thành ngày **12/10/2026**. Giữ WPF .NET Framework 4.7.2 + SQLite schema **v8**. Đối chiếu bước 7/use case P01 và tham chiếu mục 5/17/24–28/46/55.4–55.6 trước khi nối UI. Không đổi công thức tiền hoặc transaction checkout 7a.1.

## Cách sử dụng

Đăng nhập → **Trả phòng** trên menu dọc → chọn phiên đang dùng → **Tính tiền / Cập nhật**.

1. Xem phòng/loại/giờ nhận/thời điểm tính/thời lượng và đơn giá snapshot bên trái; bảng chỉ các món đã phục vụ được cộng tiền.
2. Bên phải có tiền phòng/món/tổng tạm tính và số đơn chờ sẽ hủy khi trả phòng thành công.
3. Chọn **Tiền mặt** hoặc **Chuyển khoản**, kiểm tra thực tế đã nhận tiền rồi tích ô xác nhận.
4. Bấm **Xác nhận đã thu tiền**. Service tính lại tổng tại thời điểm Confirm, chốt phiên/booking/hóa đơn/audit và hủy Pending cùng transaction.
5. Xem kết quả đã chốt: mã hóa đơn, phiên, tiền phòng/món/tổng, hình thức, thu ngân và giờ thanh toán. Danh sách tải lại để loại phiên vừa hoàn tất; chọn phiên khác sẽ xóa kết quả trước.

**Quay lại** không ghi dữ liệu hoặc kết thúc phiên. Có thể đổi lựa chọn/tính lại. Mỗi lần tính lại hoặc đổi hình thức nhận tiền bỏ dấu xác nhận nhận tiền cũ. Trước Confirm là tiền tạm tính, không giữ giá; tổng cuối có thể tăng vì thời gian chờ hoặc món mới phục vụ.

## Đã sửa và phân quyền

- MainWindow thêm menu Trả phòng theo **Session.CheckOut**; Guest không thấy. Handler có guard mở lặp, dùng `StaffReservationService.ForCheckout`, kiểm tra quyền trước mở và cập nhật quyền khi đóng.
- `CheckoutService.ListActiveStaff` đọc selector bằng Session.CheckOut trong một read transaction. Chỉ Active; DTO `CheckoutSessionChoice` có ID phiên/mã phòng snapshot/giờ nhận/nhãn, không CustomerId/tên/SĐT/RoomId/ReservationId hoặc danh sách khách. Selector không phụ thuộc mốc giờ nên không đọc clock.
- Người chỉ có CheckOut vẫn mở/chọn/xem preview/xác nhận được, không đòi Session.View/Reservation.View/Order.View/Invoice.View/Customer CRUD. Các quyền khác không tự cho checkout. Không fallback Guest khi mất quyền.
- `CheckoutWindow`/`CheckoutViewModel` dùng PreviewStaff/ConfirmStaff hiện có, không viết SQL hoặc số tiền từ UI. Giỏ món Completed chỉ để xem, không sửa. Payment UI dùng nhãn Việt nhưng gửi đúng Cash/BankTransfer.
- Bố cục giữ nền xám nhạt/chữ đậm trung tính/bảng không bo góc; bên trái nội dung rộng, bên phải xác nhận. Nút Confirm/Quay lại ở cuối cột, phần dài cuộn. Ở kích thước tối thiểu 1000×640, fixture chuẩn thấy đủ cột thành tiền, hình thức, checkbox nhận tiền và nút Confirm.

## Trạng thái UI và xử lý lỗi

- Chọn phiên mới hoặc tải lại xóa lựa chọn/preview/món/xác nhận cũ. Khi đang xác nhận, không đổi phiên hoặc tải lại selector; vẫn có thể tính tiền lại, khi đó phải tích xác nhận lại.
- Busy khóa input/nút/đóng và chặn bấm lặp trong VM. ID phiên và payment lấy từ snapshot preview khi gửi, không theo dữ liệu form có thể đổi giữa chừng.
- Quyền bị thu hồi, phiên Completed trước preview, validation hoặc lỗi writer: xóa preview/danh sách/xác nhận, vẫn đóng/tải lại được. Lỗi audit rollback toàn bộ ở service, không hiển thị đã thanh toán.
- Người khác checkout sau preview trước Confirm: API replay trả Invoice đã có. UI dùng đúng tiền/method/PaidAt/thu ngân của Invoice cũ, không đổi theo lựa chọn người dùng mới và không tạo hóa đơn thứ hai.
- Confirm đã trả Invoice thành công thì giữ receipt đã chốt trước khi tải lại selector. Nếu refresh lỗi/mất quyền sau commit, xóa dữ liệu lựa chọn nhưng vẫn báo đã trả phòng và giữ kết quả; không bật Confirm hoặc tự gửi lại lượt đó.
- Tải lại thủ công sau khi đã có receipt cũng không được biến thông báo thành “chưa thanh toán”. Receipt của thao tác đã hoàn tất giữ đến khi người dùng chọn một lượt mới hoặc đóng cửa sổ.

Kết quả vừa xác nhận là receipt từ checkout, **chưa phải màn hình tìm/xem/in hóa đơn lịch sử**. Nó không mở thêm quyền Invoice.View/Print cho người chỉ có CheckOut.

## Kiểm tra

- Build Debug/Release vào bin/VerifyDebug và bin/VerifyRelease; không đóng ứng dụng người dùng đang chạy.
- **23 bộ kiểm tra đạt** trên database/ảnh/thư mục tạm: Checkout, Billing, Orders, Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup, StaffReservations, StaffBooking, Calendar, GuestCalendar, CheckIn, WalkIn, Extension, Sessions, GuestSessions, AuthenticationUi. Checkout kiểm tra thêm trên Release.
- Verify-Checkout thêm selector Active-only/DTO không PII/read-only/không clock/CheckOut-only, factory route và thu hồi quyền. Giữ kiểm tra writer/clock sau lock, migration, money precision, rollback, idempotence và cạnh tranh checkout/món/gia hạn thật ở 7a.1.
- AuthenticationUi thêm nút sidebar thật theo tài khoản CheckOut-only; Guest ẩn; danh sách rỗng không gửi; loại Completed fixture khỏi selector.
- Fixture UI tạo booking/check-in/walk-in/orders thật: 90 giây, món đã phục vụ 25.000 đ và món Pending → preview 28.000 đ. Món Pending được phục vụ và thời gian thêm 30 giây trước Confirm → chốt **79.000 đ**, BankTransfer. Xác nhận lặp chỉ một Invoice.
- Kiểm tra chưa tích checkbox không gửi, Quay lại không lưu, tính lại xóa checkbox, đổi phiên xóa dữ liệu; Cash/BankTransfer; mất quyền; audit lỗi giữ Active/Pending và không Invoice; replay sau writer khác giữ Cash cũ; refresh quyền bị thu hồi sau commit giữ receipt/status thành công.
- Gate clock giữ preview và writer đang chạy: bấm lặp/đóng bị chặn, chỉ một lượt đọc/ghi; preview không ghi Invoice. Completed sau khi tải selector nhưng trước preview xóa stale form.
- AuthenticationUi render **109 ảnh** tại `%TEMP%/MusicBoxUi_7a2`; đã xem ảnh preview/compact và kiểm tra hình học checkbox/nút/cột bảng ở cửa sổ tối thiểu. Không đưa ảnh hoặc database kiểm tra vào Git.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Checkout.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyDebug
```

## Tiếp theo

Đã có luồng WPF từ nhận khách đến trả phòng/xác nhận thu tiền. Bước kế tiếp: **nền tảng đọc/tìm hóa đơn theo Invoice.View**, tìm mã/ngày PaidAt/SĐT hiện hành, chi tiết chỉ lấy snapshot Session/Order/Invoice; tiếp đó UI xem/in theo Invoice.View + Invoice.Print. Không lấy giá/tên danh mục hoặc tên thu ngân hiện tại để thay phiếu cũ, không sửa/xóa hóa đơn, không cho Guest xem lịch sử.

Giữ dashboard hoạt động/doanh thu quán, quản trị tài khoản/role/ma trận RBAC/audit, báo cáo/Excel và lịch sử khách. Doanh thu phải lấy Invoice/PaidAt, không cộng tiền tạm tính Active; tổng tiền lưu TEXT phải đọc/tính bằng decimal, không SUM SQLite làm mất độ chính xác.
