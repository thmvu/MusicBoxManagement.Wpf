# Chuyển Aiven MySQL — bước 3c: danh mục dịch vụ

Ngày 18/10/2026. Đối chiếu plan WPF, báo cáo Step4c và mục 5/23 của bản nghiệp vụ tham chiếu. Giữ schema MySQL v1/SQLite v8.

## Đã làm và cách hoạt động

Đã thêm `MySqlServiceCatalogService.ListForManagement/Save` cho danh mục dịch vụ trên MySQL. API dùng lại ServiceItem/ServiceEdit, giữ ba nhóm Đồ uống/Đồ ăn/Khác; thêm/sửa tên, nhóm, giá nguyên đồng dương, mô tả và trạng thái đang bán. Ngừng bán rồi bán lại bằng IsActive; không xóa vật lý hoặc seed món mẫu.

Hiểu đơn giản: khi nhân viên lưu, service kiểm tra lại quyền Service.Manage trong giao dịch, đọc bản dịch vụ hiện tại và khóa đúng dòng đang sửa. Nếu form đã cũ thì yêu cầu tải lại. Nếu hợp lệ, thông tin dịch vụ và nhật ký Staff được lưu cùng nhau; lỗi ghi nhật ký sẽ hủy thay đổi dịch vụ. Không đổi gì thì không thêm nhật ký.

**Ứng dụng và giao diện hiện vẫn dùng SQLite.** Đây là phần C# truy cập MySQL chuẩn bị cho việc chuyển toàn bộ ứng dụng sang Aiven; chưa đổi provider, nhập dữ liệu thật hoặc nối màn hình danh mục sang MySQL. Nhánh hoàn thiện chức năng WPF được làm song song, không bỏ các mục plan còn lại.

## Các quy tắc giữ nguyên

- Đọc danh mục nội bộ chỉ khi có Service.Manage hiện hành, gồm cả món ngừng bán. Guest và Staff mặc định bị chặn; Staff được cấp quyền này có thể dùng độc lập các quyền khác. Không fallback Guest.
- Tên trim 1–100 ký tự; nhóm phải thuộc ba giá trị cố định; Price kiểu long/BIGINT dương, không chuyển qua floating point; mô tả tùy chọn trim/NULL, tối đa 2000 ký tự.
- Read dùng RepeatableRead read-only, quyền và dữ liệu cùng snapshot. Writer Serializable, kiểm tra quyền/active/stamp cùng transaction rồi SELECT FOR UPDATE dòng sửa; so toàn bộ dữ liệu gốc để chặn form cũ.
- LAST_INSERT_ID lấy trên cùng kết nối vừa INSERT. Service.Create/Service.Update và dữ liệu cùng commit, ActorType Staff. Không sửa ID và không cập nhật ngược OrderItems.
- Không dùng khóa schema làm khóa nghiệp vụ; không tự retry vô hạn. Deadlock/timeout yêu cầu tải lại. Lỗi mạng đúng lúc COMMIT có thể chưa biết kết quả; caller cần tải lại đối chiếu trước gửi lại, đặc biệt khi thêm mới. Chưa kiểm thử fault cắt mạng đúng COMMIT.
- MySQL OrderService chưa chuyển trong bước này: chặn món mới ngừng bán, tạo/confirm/cancel và concurrency với checkout sẽ được nghiệm thu bằng service thật khi port nhóm Order. Fixture snapshot chỉ chứng minh sửa danh mục không viết đè món đã lưu.

## Kiểm tra

`Tests/Verify-MySqlServices.ps1` biên dịch `MySqlServiceChecks.cs` và chạy trên database `musicbox_test_<GUID>` riêng, dọn bằng tên đã kiểm tra biểu thức an toàn. Không chạm database musicbox/defaultdb ngoài kết nối quản trị để tạo/xóa đúng database GUID; không đọc/in mật khẩu.

Kiểm tra bao gồm Admin/Manager/Staff/Guest, cấp-thu hồi quyền/logout, thêm/sửa/bán tiếp/ngừng bán, ba nhóm, trim/NULL/độ dài/giá BIGINT chính xác, CHECK database, no-op, thiếu ID, form cũ/hai form đồng thời, rollback Create/Update khi audit lỗi, dữ liệu khi mở lại, và snapshot món lịch sử fixture giữ nguyên tên/giá.

Build tích hợp Debug/Release đạt tại bin/VerifyParallelDebug và bin/VerifyParallelRelease. **Verify-MySqlServices đạt online cả Debug và Release**, exit code 0; gồm kiểm tra snapshot OrderItems fixture, quyền hiện hành, hai form đồng thời và rollback Create/Update khi audit lỗi. **Verify-MySqlFoundation đạt online trên Debug**, exit code 0. Tất cả dùng database GUID riêng và đã cleanup; không thay schema/App/provider hoặc dữ liệu thật. Hồi quy SQLite/UI được báo cáo trong bước WPF song song.

## Làm tiếp

3d chuyển khách hàng/SĐT, rồi availability/booking/calendar/session/order/billing/checkout/invoice/dashboard/worker. Trước khi đổi App sang Aiven phải đủ service, sao lưu/nhập/đối chiếu dữ liệu + Content và kiểm tra UI. Ma trận quyền/audit UI/báo cáo-Excel/lịch sử khách/ERD/từ điển dữ liệu/demo vẫn trong phạm vi.


Chạy lại từ thư mục repo:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlServices.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyParallelDebug
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlServices.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyParallelRelease
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tests\Verify-MySqlFoundation.ps1 -BuildDirectory .\MusicBoxManagement.Wpf\bin\VerifyParallelDebug
```
