# Giao diện vận hành Music Box — 24/10/2026

## Mục tiêu và hướng thiết kế

Người dùng yêu cầu làm giao diện hợp với phần mềm quản lý quán trước khi tiếp tục MySQL. Ưu tiên thao tác tại quầy, nhận biết phòng đang dùng, tìm đúng thao tác và giữ form dễ đọc ở kích thước nhỏ. Đã đối chiếu plan WPF, ma trận nghiệp vụ hiện hành và reference mục 29/48; không coi giao diện mới là đã hoàn thành migration.

Hướng thiết kế dùng Segoe UI sẵn trên Windows: nội dung 14, tiêu đề 28–30, mã phòng 21 và số liệu 27. Bảng màu nền #F2F4F5, bề mặt trắng, chữ/slate #26313B, viền #CBD5DB, hành động chính xanh trầm #2B6B67 và cảnh báo lịch #A57533. Nút góc 2px, không gradient/neon, không thêm biểu đồ hoặc ảnh trang trí. Chỗ nổi bật tập trung vào trạng thái phòng và thao tác tại quầy; bảng/form giữ đơn giản.

```text
Thanh trái                 Nội dung
Music Box                  Tổng quan quán / tài khoản hiện hành
Tổng quan                  Nhận trực tiếp | Đặt hộ | Món | Trả phòng
Vận hành                   Trống | Đang dùng | Booking | Đơn chờ
Danh mục                   Doanh thu theo quyền / thời điểm cập nhật
Báo cáo                    Cảnh báo
Quản trị                   Phòng | Booking | Phiên | Món chờ
Tài khoản / đăng xuất      Bảng phòng có mã, tên và nhãn trạng thái
```

## Thay đổi đã làm

- App.xaml có style chung cho nút, nút chính, ô nhập, mật khẩu, ComboBox/popup, tab và header bảng. Có trạng thái disabled và focus bàn phím; giữ PART_ContentHost, PART_EditableTextBox, PART_Popup và cơ chế selection/keyboard của control WPF.
- Các form dùng lại style chung; style cục bộ của header bảng và form Nhân viên kế thừa đúng resource chung, tránh trở về giao diện cũ. Thao tác lưu/xác nhận được nhấn bằng nút chính.
- Menu nhóm dùng chevron và nền phẳng, giữ mở/thu gọn và cuộn độc lập. Phần cuối hiển thị giờ mở cửa; đăng nhập/đăng xuất vẫn cố định ở cuối thanh.
- Tổng quan có bốn số liệu lấy từ cùng DashboardSnapshot hiện hành. DashboardLine thêm Code/StatusCode/StatusLabel để vẽ bảng phòng. Không thêm truy vấn hoặc tính lại availability trong UI. Màu xanh trầm = trống, xanh slate = đang dùng, nâu vàng = chờ nhận, xám = khóa; mỗi ô luôn có chữ, không chỉ dựa màu.
- Giữ các tab booking/phiên/món, doanh thu theo quyền và cảnh báo sẵn có. Có thông báo khi chưa có phòng/cảnh báo; nội dung tổng quan cuộn khi cửa sổ thấp. Không có timer tự checkout hoặc tự làm mới mới.
- Bốn nút nhanh trên Tổng quan gọi lại ShowWalkIn/ShowStaffBooking/ShowOrders/ShowCheckout. Visibility và IsEnabled lấy từ nút menu tương ứng, nên dùng cùng quyền, busy guard và kiểm tra hiện hành khi mở service; không fallback Guest.
- Chế độ Khách có nút Xem phòng và đặt chỗ / Tra cứu booking–phiên ngay trong nội dung. Vẫn dùng màn hình phòng/lịch/đặt và tra cứu hiện có, không tạo booking khi chỉ xem.
- Nút Kiểm tra giờ/Xác nhận đặt được đặt ngoài vùng cuộn thông tin form Đặt phòng. Nút Lưu/Bỏ thay đổi của Dịch vụ được đặt ngoài vùng cuộn form. Khi thu nhỏ, thông tin có thể cuộn nhưng nút chính vẫn ở vị trí thấy được.

Các ô phòng chỉ là thông tin đọc; nhận/trả qua thao tác rõ ràng. Không biến việc bấm trạng thái thành thao tác ghi ngầm.

## Ranh giới nghiệp vụ và MySQL

Không sửa service nghiệp vụ, quyền, giá, ca, thời lượng, snapshot, trạng thái booking/session/order, công thức tiền hoặc transaction. Tất cả nút vẫn theo route cũ. Staff không thấy doanh thu tổng hợp; tài khoản chỉ có CheckOut vẫn mở được Trả phòng từ nút nhanh mà không cần View/Order/WalkIn. Guest không có nút nội bộ.

App/worker vẫn SQLite v8, đích MySQL v1. Không đổi provider, không import dữ liệu thật hoặc ảnh thật; ảnh phòng AI vẫn để người dùng chọn sau. Không thêm thư viện/NuGet hoặc font. Thay đổi định dạng csproj từ trước giữ riêng ngoài commit. Sau giao diện, migration tiếp 04e.4 Guest, không làm lại 01–04e.3 đã nghiệm thu.

## Kiểm tra

- Build Debug/Release bằng MSBuild Visual Studio, thư mục VerifyCounterFinalDebug/VerifyCounterFinalRelease.
- Verify-All chạy toàn bộ 44 bộ SQLite/WPF Debug đạt, gồm Workflow đầu-cuối, quyền/tài khoản, các nghiệp vụ vận hành, báo cáo/Excel và toàn bộ UI chuyên biệt. Log: `TEMP\MusicBoxRegression_4f2e34a7c0824c56b9a1e65f5bc64598\results.json`.
- Sau chỉnh cố định footer ở hai form, chạy lại AuthenticationUi trên cả hai build: 141 ảnh mỗi bản. Test thêm Guest không thấy quick action, CheckOut-only có đúng nút nhanh, mở checkout/walk-in qua nút nhanh, số liệu/bảng phòng khớp fixture, popup giờ mở đúng và đủ rộng, Save/Submit nằm trong cửa sổ tối thiểu.
- Khi resize, test chờ DataGrid tính lại độ rộng cột trước chụp/đối chiếu; không coi frame trung gian còn giữ độ rộng cũ là bố cục cuối. Đã xem ảnh main thường/tối thiểu, dashboard có dữ liệu, form đặt thường/tối thiểu, dịch vụ thu nhỏ và form thiếu thông tin. Ảnh minh họa chỉ dùng fixture, không dữ liệu khách thật.
- Các assertion quyền hiện hành, stale/duplicate/revoke, rollback và xóa dữ liệu khi đăng xuất vẫn giữ. Không nới quyền hoặc bỏ assertion để đổi theme. MySQL không đổi code nên không chạy lại online ở lượt giao diện.
- git diff --check; không gửi máy in hoặc thay dữ liệu thật trong kiểm tra.

Ảnh cục bộ: `TEMP\MusicBoxCounterFinalDebug`, `TEMP\MusicBoxCounterFinalRelease`; ảnh chờ layout ổn định tại `TEMP\MusicBoxCounterFinalLayoutDebug`. Có thể tái tạo bằng Verify-AuthenticationUi.ps1 sau khi build đúng cấu hình. Trong Visual Studio, dừng bản đang chạy, rebuild rồi chạy lại để nhận XAML/theme mới.
