# Bước 8b.2 — Giao diện Admin quản lý nhân viên

Ngày **14/10/2026**. Đối chiếu plan WPF bước 8 và tham chiếu **5/32/33**, tái dùng service [8b.1](Step8b1_AccountFoundation.md). Schema giữ **v8**.

## Cách dùng

Đăng nhập Admin → **Nhân viên** trong menu trái.

- **Thêm nhân viên**: username/họ tên/Staff–Manager–Admin/đang hoạt động/mật khẩu nhập hai lần → **Kiểm tra / Lưu** → xem nội dung → **Xác nhận lưu**.
- Chọn dòng để sửa tên đăng nhập/họ tên, gán role hoặc bỏ chọn hoạt động để khóa. Chọn lại hoạt động để mở. Ghi qua cùng bước kiểm tra/xác nhận; **Quay lại** chưa lưu.
- Chọn dòng → **Reset mật khẩu** → nhập mật khẩu mới hai lần → kiểm tra/xác nhận. Không email/OTP.
- Tài khoản chính mình không sửa được role/active trên form; service vẫn kiểm tra self/last Admin. Có thể sửa metadata hoặc reset mật khẩu chính mình.
- Tự reset: báo đã lưu, xóa dữ liệu quản trị và khóa tải lại. Đóng cửa sổ đưa màn chính về Khách, xóa dashboard/menu/phiên cũ; đăng nhập lại bằng mật khẩu mới.
- Không có Delete user: khóa thay cho xóa cứng để giữ Invoice/AuditLog theo plan.

## Thay đổi và luồng

- `MainWindow` thêm AccountsButton chỉ theo User.Manage hiện hành; Guest/Staff/Manager không thấy. `ShowAccounts_Click` guard mở trùng, `ForAccounts` kiểm tra quyền rồi mở AccountsWindow; đóng tải lại access/dashboard.
- `AccountsViewModel.ReloadAsync` đọc danh sách qua AccountManagementService.List. Selected DTO giữ Revision cho form; không sửa trực tiếp DTO, SQL hoặc Identity store từ UI.
- `Select/StartNew/StartReset` mở form; setters dùng trạng thái CanInput. PrepareAsync đọc quyền/bản hiện hành để chặn form stale trước xác nhận, giữ bản sao AccountEdit; service writer vẫn kiểm tra lại.
- ConfirmAsync gọi CreateAsync/UpdateAsync/ResetPasswordAsync trong Task.Run, kể cả phần hash mật khẩu. Busy chặn chọn/form/tải lại/đóng/gửi lặp. Lưu thành công rồi đọc lại lỗi vẫn báo đã lưu, xóa dữ liệu cũ và không gửi lại.
- PasswordBox đọc/xóa trong Confirm_Click; không có property mật khẩu trong ViewModel và không log. Đổi form/dòng, Quay lại, tải lại, lỗi, lưu/đóng xóa control. FormIdentity chỉ phát hiệu ứng xóa khi thực sự đổi ngữ cảnh, không xóa mật khẩu khi Prepare đang tải cùng form.
- Hai mật khẩu không khớp không gửi service; mật khẩu/username/họ tên/role hợp lệ và self/last Admin kiểm tra bởi service 8b.1.
- Mất quyền/stamp hoặc lỗi/stale xóa danh sách, form, confirmation và mật khẩu. Reload vẫn thử lại được; không fallback sang Guest route cho thao tác quản trị.
- Giao diện hai cột, nền trung tính, không bo góc/neon. Bảng chỉ đọc, cột active binding OneWay; danh sách/form có cuộn, các cột vừa cửa sổ tối thiểu 1020×680.
- Bổ sung luồng UI Admin vào [Code_Flow_Full_Guide.md](Code_Flow_Full_Guide.md).

## Kiểm tra

- Debug/Release build đạt tại bin/VerifyDebug và bin/VerifyRelease.
- **12 bộ kiểm tra liên quan đạt**: Accounts, Authentication, Foundation, Dashboard, Checkout, Invoices, Reservations, Sessions, Extension, Orders, Billing, AuthenticationUi. UI chạy Debug và lượt cuối Release.
- AuthenticationUi **131 ảnh**, thêm 9 ảnh account list/create-confirm/reset-confirm/compact/post-commit/revoked/self-reset và menu thật/self-reset màn chính. Tài nguyên TEMP/MusicBoxUi_8b2 và TEMP/MusicBoxUi_8b2_Final, không đưa DB/ảnh test vào Git. Đã xem ảnh để kiểm tra bố cục, cột bảng ở cửa sổ nhỏ và nút xác nhận.
- Tạo/nhập lại mismatch/giữ/duplicate, sửa fullname/gán role/khóa-mở/reset qua service thật; đăng nhập mới theo password/role mới và phiên role cũ bị chặn.
- Thay target giữa Prepare và Confirm: stale bị chặn/xóa form. Trigger lỗi audit: rollback, dữ liệu target giữ nguyên. Trigger đổi stamp actor khi ghi audit: Commit target thành công, reload bị chặn nhưng UI vẫn báo đã lưu.
- Self role/state bị khóa trên form, ép ViewModel tự khóa vẫn bị service từ chối. Tự reset bằng PasswordBox/nút thật: private data cleared, RequiresRelogin; main handler đưa về Guest, dashboard null/menu nhân viên ẩn.
- Gate clock giữ writer: click Confirm lần hai/đóng bị chặn, chỉ một user được tạo. Thu hồi stamp trước Prepare xóa cả control mật khẩu và danh sách. Sidebar Staff/Guest ẩn entry, Admin mở đúng cửa sổ.
- Kiểm tra chỉ trên DB GUID tạm. Không thay luật, schema, mật khẩu hoặc tài khoản đang dùng thật.

## Tiếp theo

Quản trị tài khoản đã có service và UI. Tiếp theo **ma trận quyền Staff/Manager và audit**: Role → Permission đúng giới hạn bất biến (Staff không Report.*, Manager không User/Permission/Audit, Admin cố định toàn quyền; Print cần View, Export cần Report.View), quyền hiện hành/transaction/audit. Giữ báo cáo/Excel, lịch sử khách, dữ liệu demo và hoàn thiện đồ án.
