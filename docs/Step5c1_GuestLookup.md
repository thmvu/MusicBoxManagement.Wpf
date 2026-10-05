# Bước 5c.1 — Guest tra cứu và hủy booking

Ngày: 05/10/2026. WPF .NET Framework 4.7.2 + SQLite, schema giữ **v6**. Đối chiếu mục 9–10/21–22/38–39/55 của `Reference_Web_ProjectPlan_v1.3.md`; không đổi nghiệp vụ hoặc thêm OTP/tài khoản/mã bí mật.

## Đã làm và cách dùng

Chế độ Khách có nút **Tra cứu SĐT**, mở cửa sổ riêng. Nhập SĐT đầy đủ rồi Tra cứu; số 0/+84/84, khoảng trắng/chấm/gạch ngang dùng `PhoneNumberNormalizer` chung với đặt phòng và khách hàng. Số rỗng/sai báo lỗi; số chưa tồn tại trả danh sách rỗng, không tạo Customer.

Danh sách chỉ có Confirmed còn hiệu lực: `StartTime > now−15 phút`. Hiện mã booking, mã/tên phòng, giờ bắt đầu/kết thúc theo Việt Nam UTC+7. Không trả tên khách/CustomerId/SĐT/lịch sử hóa đơn hoặc danh sách toàn bộ khách. JOIN theo CustomerId và SĐT hiện hành nên nhân viên đổi số khách thì các lần tra cứu/hủy dùng số mới, vẫn giữ liên kết lịch sử. Không lọc Room.IsActive khi tra cứu để không làm mất booking đã lưu do trạng thái phòng.

Chọn booking để xem điều kiện hủy. Khi `now <= StartTime−2 giờ`, Hủy booking đã chọn → Đồng ý hủy. Giữ booking/đóng trước xác nhận không lưu; dưới 2 giờ hiện liên hệ cửa hàng. Khi chờ xác nhận, khóa đổi SĐT/chọn dòng; khi bận khóa thao tác/đóng. Sửa ô SĐT xóa ngay danh sách và selection cũ. Xác nhận xong tải lại; booking đã hủy không còn trong danh sách. Nếu hủy đã commit nhưng tải lại lỗi, hiện rõ đã hủy và yêu cầu tra cứu lại, không gợi ý gửi lại lệnh hủy.

## Dữ liệu và transaction

- `GuestReservationService.Lookup`: chuẩn hóa số, khởi tạo schema nếu cần, chạy `NoShowService.ProcessExpired` trước truy vấn quan trọng, rồi đọc danh sách trong read transaction với một mốc IClock. Booking hết đúng +15 phút không xuất hiện dù clock tiến trong khoảng xử lý worker/đọc.
- `Cancel`: BeginWriteTransaction, lấy IClock trong transaction, đọc lại booking theo ID + SĐT hiện hành. Chặn không thuộc SĐT/không tồn tại, status khác Confirmed, hết grace hoặc đã có session nguồn; sau đó kiểm tra lại mốc <=2 giờ. Không tin CanCancel từ DTO/UI.
- Ghi Status=Cancelled, CancellationReason=`Customer cancelled online`, AuditLog Action=`Reservation.Cancel`, ActorType=Guest/UserId=NULL, cùng transaction/clock. Audit lỗi rollback; không xóa booking hoặc thay CustomerId/RoomId/StartTime/EndTime. Availability loại Cancelled nên có thể đặt lại khoảng đó theo validation hiện hành.
- Hai lần hủy cùng booking được serialize: chỉ lần đầu ghi trạng thái/log; lần sau bị chặn. Phần NoShow trong lookup là transaction bảo trì riêng; truy vấn không tạo Customer/Reservation.

## Kiểm tra

Build Debug và Release đạt. Chạy 11 bộ kiểm tra trên database/thư mục tạm: Foundation, Authentication, RoomTypes, Rooms, Services, Customers, Availability, Reservations, GuestBooking, GuestLookup và AuthenticationUi. Không đọc/ghi/xóa database người dùng.

GuestLookup kiểm tra số tương đương/không tồn tại/rỗng/khác khách, DTO public, số mới sau đổi khách, chỉ Confirmed còn hiệu lực, đúng/trước +15 phút và NoShow; dưới/đúng 2 giờ (lệch một tick); lý do/log Guest, rollback audit, hủy lặp/đồng thời, trạng thái đổi sau lookup, giải phóng slot và giữ lịch cũ. ViewModel kiểm tra giữ booking, đổi số xóa dữ liệu và hủy thành công nhưng refresh lỗi do NoShow audit.

UI chạy binding/event handler thật: mở từ MainWindow, SĐT sai/không có, danh sách 3 booking, dòng dưới 2 giờ hướng dẫn liên hệ, xác nhận/giữ, hủy đúng mốc 2 giờ, xác nhận cũ bị chặn khi clock qua mốc, đổi số xóa kết quả. Toàn bộ suite render 25 ảnh; đã xem ảnh màn hình chính, xác nhận và hủy thành công.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-GuestLookup.ps1
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1
```

## Còn lại theo plan

Tiếp theo 5c.2 là màn hình nhân viên danh sách/chi tiết/đặt hộ và hủy Confirmed theo quyền Reservation.View/Create/Cancel, lý do hủy bắt buộc; quy tắc Staff không dùng giới hạn 2 giờ của Guest. Calendar ngày/tuần làm tiếp sau đó. Chưa có luồng check-in/walk-in/gia hạn nên phần lookup phiên Active, tiền tạm tính/gọi món/hủy Pending sẽ bổ sung ở bước vận hành. Bước này hoàn tất tra cứu/hủy Confirmed, chưa hoàn tất toàn bộ mục 22 hoặc toàn đồ án.
