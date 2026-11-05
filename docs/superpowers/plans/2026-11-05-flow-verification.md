# Kiểm chứng luồng MySQL và ảnh thực tế — 05/11/2026

Yêu cầu: chạy các trường hợp luồng hiện có một cách nghiêm túc, chụp UI và gửi ảnh ngay trong chat. Không coi build thành công hoặc kết quả ngày trước là bằng chứng lượt này.

- [x] Kiểm trạng thái Git/base20788df, chỉ csproj định dạng171/49 có sẵn; build dùng thật Debug/Release.
- [x] Lập allowlist runner MySQL-only cho source MySQL độc lập; không dùng Verify-All/runner pha provider. Authentication test cũ thực sự gọi backend bị cấm và Import bị loại; không sửa chúng để lách chỉ đạo. Đăng nhập thực được kiểm qua OperationsUi và account services.
- [x] Chạy các suite service/race/security/catalog/reports và UI theo allowlist, lưu exit code/log từng lượt; lần lỗi đầu phải được giữ.
- [x] Chụp và xem ảnh popup/đặt/tra cứu/gọi món/checkout/hóa đơn/lịch/dashboard/báo cáo. Ảnh có dữ liệu fixture, không phải dữ liệu quán.
- [x] Đối chiếu phạm vi còn thiếu, báo cáo kết quả thực và ảnh trong chat, cập nhật bàn giao/commit Việt/push theo chỉ đạo có sẵn.

Không đổi luật/SSL/timeout/assertion để test qua; không sửa dữ liệu quán hoặc chạy seed/import/cutover. Chỉ bổ sung hạ tầng kiểm chứng/ảnh. Các suite nhiều ca cạnh tranh được chạy giới hạn song song ở database GUID riêng; UI chụp lần lượt. Không hứa mọi tổ hợp đầu vào/race ngoài các assertion hiện có đã được bao phủ. Máy in giả chỉ chứng minh preview/queue, không thay in giấy thật.

Bổ sung kiểm chứng UI có ảnh: chọn tab Đang sử dụng thật trước gia hạn; tiền tạm tính Guest đối chiếu7 phút + món đã phục vụ =24.000đ; nhận walk-in qua cả hai nút và xác nhận không sinh Reservation/ExpectedEndTime. Chỉ bổ sung assertion/capture, không sửa product. Release Feedback lượt đầu34 popup được giữ; lượt cuối bổ sung walk-in đã đạt36 popup cả Debug/Release.

Kết quả cuối:34/34 Release +2 nhóm UI Debug đạt; product hash đầu/cuối khớp. Review một lượt không có Critical/Important, đã sửa chữ dự kiến36 thành số thực. Gallery117 ảnh, raw304 ảnh; giữ các attempt loader/driver đầu và cleanup interrupted fixture. [Report](../../Flow_Verification_20261105.md). Tích hợp main/commit Việt/push theo chỉ đạo có sẵn; không đưa csproj định dạng cũ vào commit.
