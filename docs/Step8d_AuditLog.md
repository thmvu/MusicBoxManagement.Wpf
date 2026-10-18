# Bước 8d — Xem nhật ký thao tác

## Nền tảng nghiệp vụ

Đối chiếu mục 5, 32, 54 và 55 của bản tham chiếu cùng plan WPF. Admin xem danh sách, lọc cơ bản và mở nội dung đầy đủ của nhật ký Staff/Guest/System. Không sửa, xóa hoặc xuất nhật ký ở bước này. Các service nghiệp vụ tiếp tục ghi AuditLog cùng transaction như trước; đọc nhật ký không phát sinh nhật ký mới.

`AuditLogService.Search` và `Read` kiểm tra `Audit.View` hiện hành bằng PermissionService trong cùng read transaction với truy vấn. Admin luôn toàn quyền; grant sai Audit.View cho Staff/Manager vẫn bị chặn. Tài khoản bị khóa, đổi role, đổi SecurityStamp, đăng xuất hoặc phiên khác provider đều bị từ chối ở lần đọc tiếp theo. Không dùng quyền đã lưu trong UI thay cho kiểm tra service.

DTO `AuditLogEntry` có các trường gốc và nhãn tiếng Việt. Thời gian hiển thị UTC+7, giữ ticks trong CreatedAt. UserName/FullName từ LEFT JOIN là **thông tin tài khoản hiện tại**, không phải snapshot tên tại thời điểm hành động. UI phải ghi rõ ý nghĩa này. Nếu thiếu metadata thì dùng UserId; Guest/System hiển thị dấu gạch. DTO dùng internal setter, danh sách bọc ReadOnlyCollection để người gọi không thay đổi tập kết quả.

## Bộ lọc và phân trang

Ngày bắt đầu/kết thúc là ngày Việt Nam UTC+7, bao gồm cả hai ngày; quy đổi thành khoảng UTC nửa mở `[đầu ngày, đầu ngày sau ngày kết thúc)`. Có thể để trống từng đầu. Ngày đảo, ngày kết thúc quá lớn hoặc ngày đầu không biểu diễn được UTC bị từ chối. Tác nhân chỉ Staff/Guest/System; action và entity là mã chính xác sau trim, tối đa 100 ký tự. Các điều kiện kết hợp AND; trống nghĩa là tất cả. Tham số truy vấn được bind, không ghép bộ lọc vào SQL.

AuditLog cũ có CreatedAt với số chữ số phần lẻ không đồng nhất. Không so sánh trực tiếp chuỗi ISO, cũng không dùng riêng julianday vì nó làm tròn millisecond. Truy vấn dùng julianday với khoảng biên rộng hơn một giây, sau đó kiểm tra chính xác DateTimeOffset/ticks trước khi đếm kết quả. Vì vậy dòng `16:59:59.9999999Z` vẫn thuộc đúng ngày Việt Nam trước mốc `17:00:00Z`, bao gồm chuỗi legacy không phần lẻ hoặc phần lẻ ngắn và múi giờ có offset.

Sắp theo AuditLogId giảm dần; trang sau chỉ lấy ID nhỏ hơn NextBeforeId. Đọc dừng khi có `pageSize+1` dòng thực sự khớp sau kiểm tra ticks, trả tối đa pageSize (1–200), HasMore và cursor. Dòng mới được thêm trong lúc phân trang không lặp lại các dòng cũ. Mỗi lần tải là một snapshot riêng; thông tin tên tài khoản sẽ cập nhật ở lần tải tiếp theo. Search lấy đúng một mốc IClock sau khi kiểm tra quyền, trả trong CheckedAt.

## Kiểm tra nền tảng

`Verify-AuditLog.ps1` dùng database GUID trong thư mục tạm. Kiểm tra đọc audit do CustomerService thật tạo; quyền Admin hiện hành, grant sai cho Staff/Manager, khóa/đổi stamp/đổi role/đăng xuất; filter AND/trim/chính xác; UTC+7 ở ranh giới và ticks/legacy; thông tin user hiện tại; pagination khi có log mới; danh sách chỉ đọc; ID không tồn tại và đầu vào sai; không thay schema.

Trigger tạm chặn insert/update/delete AuditLog chứng minh đọc không ghi nhật ký. Fixture WAL cho phép writer đổi tên/khóa Admin ngay sau bước đọc quyền: kết quả đang đọc giữ cùng snapshot tên cũ, lần đọc kế tiếp bị từ chối. Không đụng database hoặc dữ liệu người dùng đang dùng. Kết quả build/test/UI được ghi tiếp khi nghiệm thu.

Ngày 18/10/2026: build tích hợp Debug/Release đạt tại `bin/VerifyAuditCustomersDebug` và `bin/VerifyAuditCustomersRelease`; **Verify-AuditLog đạt cả hai build**, gồm tick sát biên ngày, chuỗi legacy Z/+00:00/+07:00, một mốc clock, snapshot WAL, cursor và quyền hiện hành. Kết quả UI/hồi quy chung được ghi riêng khi chốt hai nhánh.

App vẫn dùng SQLite (schema v8), nhánh Aiven tiếp tục riêng theo plan. Chưa nối màn hình nhật ký này sang MySQL và chưa chuyển provider của app.

## Giao diện và cách dùng

Đăng nhập Admin → menu **Nhật ký**. Để trống bộ lọc để xem mới nhất; có thể chọn từ/đến ngày Việt Nam, Nhân viên/Khách/Hệ thống và nhập mã hành động/đối tượng đầy đủ, ví dụ `Customer.Update` / `Customer`. Bấm **Tìm / Cập nhật**, chọn dòng để đọc mô tả và ID gốc. **Trang cũ hơn** tải tối đa 100 dòng tiếp theo; bấm Tìm để quay về trang mới nhất. **Xóa lọc** xóa cả kết quả, sau đó tìm lại.

MainWindow chỉ hiện menu theo quyền và kiểm tra lại qua ForAudit trước mở; Search/Read tiếp tục kiểm tra độc lập. ViewModel xóa danh sách/chi tiết/cursor khi đổi filter hoặc lỗi; mất quyền xóa thêm giá trị filter. Trong lúc tải, khóa chọn/filter/gửi lặp/đóng để không ghi đè kết quả bằng yêu cầu cũ. Không có nút sửa/xóa hoặc endpoint ghi nhật ký từ màn hình này.

Cột tài khoản và phần chi tiết ghi rõ dữ liệu tài khoản **hiện hành**. Window tối thiểu 960×640 vẫn có đủ bộ lọc/nút, bảng có cuộn ngang và chi tiết cuộn dọc. Không tự refresh hoặc giữ transaction trong lúc người dùng đọc màn hình.

## Nghiệm thu hai nhánh ngày 18/10/2026

- Build Debug/Release kiểm tra và `bin/Debug`, `bin/Release` mặc định đều đạt; không mở app với database thật.
- Verify-AuditLog và Verify-AuditLogUi đạt cả Debug/Release; UI mới render 6 ảnh/build, đã xem ảnh cửa sổ tối thiểu và bộ lọc. Kiểm tra menu thật Guest/Staff/Manager/Admin, binding bộ lọc/ngày, phân trang/chi tiết/empty/stale, busy/duplicate và xóa dữ liệu khi mất quyền.
- 29/29 bộ hồi quy SQLite hiện có đạt trên Debug (tổng cộng 31 bộ khi tính hai bộ nhật ký mới). AuthenticationUi render 131 ảnh; PermissionMatrixUi vẫn đạt, InvoicePrinting chỉ xem trước, không gửi máy in.
- MySqlCustomers online Debug/Release và MySqlFoundation online Debug đạt trên GUID database riêng; xem báo cáo migration 3d.

Tiếp theo WPF làm báo cáo theo kỳ/Excel theo mục 31, giữ lịch sử khách/ERD/demo trong scope; Aiven port availability/booking/NoShow và các nghiệp vụ còn lại. Ma trận quyền và nhật ký cũng cần service MySQL trước đổi App. Không thay schema hoặc dữ liệu vận hành ở lượt này.
