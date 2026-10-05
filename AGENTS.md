# Music Box WPF

## Yêu cầu đã chốt với người dùng

- Đây là project riêng: WPF trên .NET Framework 4.7.2 và SQLite.
- Giữ chế độ Khách tại máy demo/quầy. Khu vực nhân viên sẽ cần đăng nhập.
- Đồ án nhỏ, làm từng bước dễ hiểu; báo cáo bằng tiếng Việt về phần đã làm, cách hoạt động và kiểm tra.
- Giao diện màu trung tính có tương phản, hạn chế card bo góc và màu neon.
- Trước khi thay nghiệp vụ, đối chiếu `MusicBoxManagement_Wpf_ProjectPlan_v0.1.md` và `docs/Reference_Web_ProjectPlan_v1.3.md`; không tự cắt chức năng hoặc đổi quy tắc.
- Bản tham chiếu web chỉ là nguồn nghiệp vụ. Không dùng ASP.NET MVC, SQL Server hay OWIN cookie cho nền tảng project này.

## Hiện trạng

- Bước 1 đã có: solution WPF truyền thống, SQLite schema v1/RoomTypes, Standard/VIP, màn hình đọc danh mục và nút làm mới.
- Đã build Debug/Release, kiểm tra database bằng `Tests/Verify-Foundation.ps1` và thử giao diện.
- Bước 2 đã chốt kỹ thuật tại `docs/Wpf_Authentication_Transactions.md`: Identity Core + kho SQLite, quyền hiện hành và phiên trong bộ nhớ. Có BeginWriteTransaction; đã build Debug/Release và kiểm tra commit/rollback/hai kết nối tranh quyền ghi trên database tạm. Schema vẫn v1.
- Bước 3 đã có schema v2 (nâng cấp giữ dữ liệu v1), Identity/UserManager + kho SQLite, thiết lập Admin đầu tiên, đăng nhập/đăng xuất, PermissionService đọc quyền hiện hành và UI Khách/nhân viên. Đã build Debug/Release và kiểm tra nền tảng, xác thực/quyền, rollback/bootstrap đồng thời, UI WPF trên database tạm. Chi tiết `docs/Step3_Authentication.md`.
- UI quản lý user/reset mật khẩu/đổi role/ma trận quyền/audit đầy đủ vẫn ở bước 8; kho Identity hiện chỉ lưu tài khoản mới cho bootstrap, không cho cập nhật tài khoản bỏ qua service quản trị.
- Bước 4a đã có sửa Standard/VIP: Name/Capacity/PricePerHour/Amenities/Description, Code cố định; service kiểm tra RoomType.Edit trong transaction cùng cập nhật/AuditLog, chặn form cũ ghi đè. UI nhân viên có danh mục/nút sửa; Guest chỉ xem. Schema hiện v3, nhật ký nhân viên đã chuyển User → Staff đúng plan, giữ dữ liệu cũ. Debug/Release và kiểm tra nền tảng/xác thực/RoomType/UI đạt trên database tạm. Chi tiết `docs/Step4a_RoomTypes.md`.
- Bước 4b.1 đã có thêm phòng với mã/tên/loại/một ảnh bắt buộc JPEG/PNG/WebP <=5 MB, danh sách/ảnh nội bộ; schema v4 giữ dữ liệu cũ, mã unique/cố định. Room.Manage kiểm tra trong transaction cùng dữ liệu/nhật ký; ảnh chuẩn hóa PNG lưu cạnh database trong Content/uploads/rooms. Debug/Release và kiểm tra nền tảng/xác thực/RoomType/Rooms/UI đạt trên dữ liệu tạm. Chi tiết `docs/Step4b1_RoomCreation.md` (có giới hạn rollback filesystem khi process dừng đột ngột).
- Bước 4b.2a đã có sửa tên/mô tả/thay ảnh phòng, giữ mã/loại/trạng thái/CreatedAt; GetForEdit/Update kiểm tra Room.Manage hiện hành, chặn form cũ và ghi Room.Update cùng transaction. Schema giữ v4. Ảnh cũ sau thay giữ trên đĩa, chưa có bộ dọn; xem `docs/Step4b2a_RoomEditing.md`. Debug/Release và kiểm tra nền tảng/phòng/UI đạt trên dữ liệu tạm.
- Bước 4b.2b đã có đổi loại/khóa mở có lý do và log cùng transaction; chặn Active Session, khóa chặn Confirmed còn hạn (bao gồm tương lai), đúng +15 phút không còn chặn. Schema hiện v5 thêm Customers/Reservations/RoomSessions nền tảng, FK/CHECK/unique Active Room/Customer và Reservation nguồn; chưa có service/UI booking/session. Có IClock cho mốc thời gian trong transaction. Debug/Release và năm bộ kiểm tra đạt trên dữ liệu tạm. Chi tiết `docs/Step4b2b_RoomState.md`.
- Bước 4c đã có danh mục dịch vụ: thêm/sửa tên/nhóm/giá/mô tả/đang bán, ba nhóm cố định, giá nguyên đồng dương, ngừng bán thay xóa. Schema hiện v6 thêm Services giữ dữ liệu cũ; Service.Manage/quy tắc/form cũ/nhật ký cùng transaction. Debug/Release và sáu bộ kiểm tra đạt trên dữ liệu tạm. Chi tiết `docs/Step4c_Services.md`.
- Bước 4d.1 đã có CustomerService/PhoneNumberNormalizer và UI tìm tên/SĐT đầy đủ, thêm/sửa khách; quyền View/Create/Edit riêng, số chuẩn hóa unique, chặn form cũ, dữ liệu/log cùng transaction, không xóa. Schema giữ v6/dùng Customers từ v5. Debug/Release và bảy bộ kiểm tra đạt trên dữ liệu tạm, UI render 17 ảnh. Chi tiết `docs/Step4d1_Customers.md`. Chưa có trang lịch sử/đếm lần hoàn tất/lần dùng gần nhất (giữ scope khi có luồng Reservation/Session/Invoice).
- Bước 5a đã có BookingHours/AvailabilityService: ngày Việt Nam UTC+7 hôm nay..+30, ca/slot/thời lượng ban đầu, overlap Room/Customer, hiệu lực Confirmed +15 phút, khoảng session nguồn booking, walk-in và phiên quá giờ; overload cùng connection/transaction cho writer tương lai, IClock. Schema giữ v6, chưa UI hoặc service tạo booking/worker NoShow. Debug/Release và Verify-Availability/Foundation/Rooms đạt trên dữ liệu tạm. Chi tiết `docs/Step5a_Availability.md`.
- Chưa có luồng đặt phòng, phiên sử dụng, gọi món, checkout/hóa đơn và báo cáo. Guest hiện chỉ có danh mục loại phòng, chưa đủ các luồng nghiệp vụ Guest.
- Database nằm ở `%LOCALAPPDATA%\MusicBoxManagement.Wpf\musicbox.db`; không ghi đè/xóa database đang dùng khi kiểm tra.

## Làm tiếp

Đọc README, plan và các báo cáo bước 2/3/4a/4b.1/4b.2a/4b.2b/4c/4d.1/5a trước. Tiếp theo 5b theo phần nhỏ: service tạo Reservation cho Guest/Staff, customer reuse và audit/NoShow trong transaction, rồi form đặt/chọn ngày giờ/phòng và Guest lookup/hủy theo mục 6–10/21–22/38. Dùng PhoneNumberNormalizer chung; có SĐT thì dùng Customer cũ, không ghi đè FullName từ tên Guest nhập. Khách hàng hiện mới danh mục 4d.1; giữ trang lịch sử Reservation/Session/Invoice và thống kê khách ở phạm vi 4d/bước 8 khi có dữ liệu tương ứng. Booking/session mới có schema, chưa có luồng nghiệp vụ; writer phải authorize/resolve Customer rồi chạy lại AvailabilityService trong cùng BeginWriteTransaction trước ghi, tái sử dụng IClock. Availability là helper quy tắc, không phải ranh giới quyền/Guest Lookup; không trả chi tiết khách qua UI public. NoShow worker/migration trạng thái chưa có, hiện chỉ tính Confirmed hết grace là không giữ lịch. OrderItem tương lai phải snapshot tên/giá, chặn món mới từ dịch vụ ngừng bán, giữ món cũ theo mục 23. Tái sử dụng PermissionService/AuditService; không giả định đồ án đã hoàn thành.

## Kiểm tra và Git

- Build solution `MusicBoxManagement.Wpf.sln` bằng MSBuild của Visual Studio có workload .NET desktop development.
- Sau thay đổi nền tảng dữ liệu, chạy Windows PowerShell: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-Foundation.ps1`.
- Khi sửa xác thực/quyền, chạy thêm `Tests/Verify-Authentication.ps1`; khi sửa UI đăng nhập, chạy `powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-AuthenticationUi.ps1`.
- Khi sửa loại phòng, chạy thêm `Tests/Verify-RoomTypes.ps1`; bài kiểm tra UI trên cũng kiểm tra mở/sửa/lưu/hủy loại phòng.
- Khi sửa phòng/ảnh, chạy thêm `Tests/Verify-Rooms.ps1` và bài kiểm tra UI trên (thêm phòng/ảnh/hủy/refresh). Khi sao lưu dữ liệu thật, giữ cả database và thư mục Content chứa ảnh; kiểm tra chỉ dùng thư mục tạm riêng.
- Khi sửa dịch vụ, chạy thêm `Tests/Verify-Services.ps1` và bài kiểm tra UI trên (giá/thêm/sửa/nhóm/ngừng bán/bỏ thay đổi).
- Khi sửa khách hàng/SĐT, chạy thêm `Tests/Verify-Customers.ps1` và bài kiểm tra UI trên (chuẩn hóa/thêm/tìm/sửa/lỗi SĐT/bỏ thay đổi). Không coi tìm khách nội bộ là hoàn tất Guest Lookup.
- Khi sửa giờ/availability/booking, chạy thêm `Tests/Verify-Availability.ps1` (giờ Việt Nam/mốc ca/slot/ngày/overlap/grace/walk-in/overdue/transaction). Fixture writer chỉ chứng minh hợp đồng kiểm tra trong transaction, chưa phải service booking đầy đủ.
- Commit tiếng Việt. Đây là Git repository độc lập, origin là `https://github.com/thmvu/MusicBoxManagement.Wpf.git` (private); không dùng remote của project web.
