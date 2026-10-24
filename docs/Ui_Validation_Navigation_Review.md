# Rà soát nhập liệu, menu và tổng quan — 24/10/2026

## Vì sao Visual Studio dừng ở Exception?

Người dùng đã xác nhận Visual Studio dừng tại Exception và có Continue/F5. Đây là debugger dừng lúc exception được ném ra (first chance), chưa chứng minh ứng dụng thoát hoặc exception không được xử lý. Bấm Continue/F5 để chương trình chạy tới `catch` và hiển thị thông báo.

Trong Visual Studio: **Debug → Windows → Exception Settings**. Bỏ chọn dừng khi **Thrown** ở nhóm Common Language Runtime Exceptions nếu không muốn debugger dừng ở cả các lỗi đã được bắt. Vẫn giữ việc phát hiện lỗi không được xử lý; không thêm handler toàn ứng dụng để nuốt mọi exception.

Ví dụ luồng cũ: `ServicesWindow.Save_Click` → `ServicesViewModel.SaveAsync` → `Task.Run(ServiceCatalogService.Save)` → kiểm tra nghiệp vụ ném `ArgumentException` → `await` trả lỗi về `catch (ArgumentException)` → cập nhật `Status` → `StatusText` hiện lý do. `finally` mở lại nút. Lỗi dữ liệu được hoàn tác bởi transaction của service. Đây vẫn là lớp bảo vệ khi gọi service trực tiếp hoặc quyền/dữ liệu thay đổi sau khi mở form.

## Thay đổi nhập liệu

Thêm kiểm tra thông tin bắt buộc ở đầu thao tác UI, dùng thông báo cụ thể và trả về trước khi gọi service:

- Dịch vụ: tên, nhóm, giá nguyên đồng dương.
- Khách hàng: họ tên và SĐT khi lưu.
- Đặt phòng Khách/đặt hộ: họ tên và SĐT khi xác nhận. Kiểm tra lịch chỉ cần SĐT; vẫn xem lịch trống/bận khi chưa nhập tên.
- Nhận khách trực tiếp: họ tên và SĐT trước khi mở xác nhận.
- Tra cứu Khách: SĐT trước khi tra cứu; xóa kết quả cũ.
- Thêm phòng: mã, tên, loại, ảnh; sửa phòng: tên, loại và lý do nếu khóa.
- Sửa loại phòng: tên; sức chứa/giá tiếp tục dùng TryParse.
- Đăng nhập/thiết lập Admin: tên đăng nhập, họ tên khi thiết lập và mật khẩu.

Giữ nguyên kiểm tra định dạng/SĐT/giới hạn/overlap/quyền hiện hành tại service, `try/catch/finally`, chống gửi lặp và thông báo thành công khi commit đã xong nhưng reload lỗi. Không đổi rule, schema hoặc provider. Thông báo trên form giúp người dùng sửa tiếp; trường trống thông thường không cần ném exception từ service.

## Menu và dashboard

**Tổng quan** nằm trên cùng thanh trái và mở sau đăng nhập nếu có Dashboard.View. Dashboard hiện có doanh thu hôm nay theo quyền, tình trạng từng phòng, booking hôm nay, phiên đang sử dụng, đơn món chờ và cảnh báo.

Các chức năng giữ nguyên nhưng nằm trong bốn nhóm mở/thu gọn:

| Nhóm | Chức năng |
|---|---|
| Vận hành | Booking, Đặt hộ, Khách trực tiếp, Phiên sử dụng, Đơn món, Trả phòng, Hóa đơn, Lịch phòng |
| Danh mục | Phòng, Loại phòng, Dịch vụ, Khách hàng |
| Báo cáo | Doanh thu, sử dụng phòng, đặt phòng, doanh thu và top món |
| Quản trị | Nhân viên, Ma trận quyền, Nhật ký |

Cả bốn nhóm thu gọn khi tạo cửa sổ để thấy ngay Tổng quan và các nhóm chức năng. Bấm tiêu đề để mở các mục bên trong. Nhóm không có chức năng được phép sẽ ẩn; từng nút vẫn theo quyền hiện hành. Menu cuộn độc lập, đăng nhập/đăng xuất ở cuối; giữ kích thước tối thiểu và vùng nội dung rộng.

Rà lại bố cục: tiêu đề nhóm có màu chữ sáng rõ để không bị style TextBlock toàn ứng dụng đổi thành màu tối trên nền sidebar. Nhãn nút dài cho phép xuống dòng thay vì bị cắt. Kiểm tra trạng thái thu gọn ngay sau đăng nhập; sau khi đóng modal, WPF có thể mở nhóm để đưa nút được trả focus vào vùng nhìn thấy, đây không phải lỗi mất quyền. Giữ các binding và handler nghiệp vụ sẵn có. Người dùng xác nhận nếu nhánh hiện tại không có diff UI khác thì chưa có phần sửa riêng cần ghép; diff csproj có sẵn chỉ đổi định dạng và vẫn giữ ngoài commit.

Đã đối chiếu plan WPF và bản tham chiếu mục 4.2, 5: Staff xem tổng quan vận hành nhưng **không xem doanh thu tổng hợp**; Admin/Manager có quyền báo cáo mới thấy phần doanh thu tương ứng. Không tự cấp Report.View cho Staff hoặc xóa chức năng khỏi scope.

## Kiểm tra

`Verify-AuthenticationUi` bổ sung bấm nút thật trên bảy form: dịch vụ, khách hàng, đặt phòng, walk-in, tra cứu, thêm phòng và đăng nhập. Kiểm tra thông báo cụ thể, cửa sổ còn mở, hết busy, không mở xác nhận sai, không ghi nghiệp vụ/audit; nhập đúng sau khi bị từ chối vẫn lưu được. Các ca lỗi sửa phòng/loại phòng đã có trong bộ UI tiếp tục chạy. Có ảnh trường trống và menu nhóm, kiểm tra menu thường/tối thiểu, thứ tự Tổng quan, quyền Guest/Staff/Manager/Admin và thu hồi quyền.

Các bài UI báo cáo/ma trận/nhật ký mở nhóm trước khi kiểm tra nút con, giống thao tác người dùng. Không coi việc nút nằm trong nhóm thu gọn là mất quyền.

Thêm runner để nhóm có thể chạy lại toàn bộ kiểm tra và giữ log/exit code từng bộ:

Build solution trước và trỏ BuildDirectory tới đúng bản vừa build; runner không tự build.

```powershell
# SQLite/WPF; tự tạo thư mục log trong TEMP
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-All.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\Debug

# Bao gồm các service MySQL đã port; cần biến môi trường và CA Aiven đã cấu hình
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-All.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\Debug -IncludeMySql
```

Mỗi bộ chạy process STA riêng, tối đa ba bộ cùng lúc (đổi bằng Parallelism 1–4), lưu stdout/stderr và `results.json`, exit 1 khi có bộ thất bại. Chỉ dùng database GUID/thư mục tạm theo hợp đồng các bài kiểm tra; không nhập hoặc thay dữ liệu thật. Các phần MySQL chưa port không nằm trong bằng chứng nghiệm thu này.

## Kết quả ngày 24/10/2026

| Phạm vi | Kết quả |
|---|---|
| Build UI cuối | Debug/Release đạt, thư mục VerifyUiReviewDebug/VerifyUiReviewRelease |
| Toàn bộ SQLite/WPF | 43/43 bộ Debug đạt, gồm UI báo cáo, Excel, tài khoản, RBAC, audit, lịch sử khách và vận hành |
| Toàn bộ MySQL đã port | 15/15 bộ online Debug đạt: Foundation, Authentication, Accounts, RoomTypes, Rooms, Services, Customers, Availability, Reservations, BookingReads, StaffReservations, Calendar, CheckIn, WalkIn, Sessions |
| UI sau thay đổi bố cục cuối | AuthenticationUi Debug/Release đều đạt, 140 ảnh mỗi bản |
| Rà ảnh | Đã xem trực tiếp menu thu gọn, nhóm Báo cáo mở, cửa sổ tối thiểu và form thiếu thông tin; không chồng vùng nội dung, đăng xuất còn nhìn thấy |
| Git | diff --check đạt; định dạng csproj có sẵn giữ riêng |

Ở lượt đầu, hai bài AuditLogUi/BookingStatusReportUi chưa mở nhóm nên kiểm tra IsVisible thất bại. Sau khi cập nhật test để mở nhóm như người dùng, cả hai đạt trong lượt 43/43. Khi kiểm tra thu gọn, đặt assertion ngay sau đăng nhập, trước các thao tác modal có thể khôi phục focus. Sau chỉnh xuống dòng nhãn menu, chạy lại toàn bộ AuthenticationUi ở cả hai build và xem ảnh cuối; không có thay đổi nghiệp vụ sau lượt hồi quy.

Bằng chứng cục bộ: `TEMP\MusicBoxRegression_5a6c051d26454b088e7754e641db42e0\results.json` chứa 43 bộ SQLite/WPF đạt; nhóm MySQL trong `TEMP\MusicBoxRegression_595e4294ed594fc09e253b37617bbd7d\results.json` có 15 bộ đạt. Ảnh cuối tại `TEMP\MusicBoxUiReviewCompletedDebug` và `TEMP\MusicBoxUiReviewCompletedRelease`. Các thư mục TEMP không đưa vào Git; runner cho phép tái tạo bằng chứng. Không đọc/chỉnh dữ liệu nghiệp vụ thật, ảnh thật hoặc gửi máy in.
