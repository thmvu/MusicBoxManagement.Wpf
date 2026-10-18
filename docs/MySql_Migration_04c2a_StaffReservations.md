# MySQL 4c.2a — Danh sách/chi tiết và hủy booking nội bộ

Ngày 18/10/2026. Sau khi kiểm tra lại booking reads vừa pull, bổ sung MySqlStaffReservationService.Search/Cancel. Chỉ là service nội bộ, chưa nối WPF/provider; schema MySQL v1 và SQLite v8 giữ nguyên.

## Cách hoạt động

- Search yêu cầu Reservation.View hiện hành. Lọc From/To ngày Việt Nam theo StartTime [đầu From, đầu ngày sau To), SĐT hiện hành chuẩn hóa chung và một trong năm trạng thái; các bộ lọc kết hợp AND. Có thể bỏ từng filter; từ chối khoảng ngày/SĐT/status sai.
- Kiểm tra quyền trước NoShow maintenance; maintenance riêng qua MySqlNoShowService. Sau đó mở RepeatableRead và kiểm tra lại quyền trong cùng snapshot với dữ liệu riêng tư. Mất quyền/stamp/role/logout/database scope đều bị chặn, không fallback Guest.
- Tái dùng DTO StaffReservation/StaffReservationSearch: tên/SĐT khách hiện tại, phòng, giờ, lý do hủy, tên người đặt, session nguồn và giá danh mục hiện hành. Đây là DTO nội bộ theo View, không dùng cho Guest.
- Quyền Cancel/Create/CheckIn trả như metadata hiện hành, độc lập View. IsCancellable chỉ mô tả Confirmed trước grace và chưa có session, không thay việc writer kiểm tra lại.
- Cancel gọi MySqlReservationService.CancelStaff thật, quyền Reservation.Cancel độc lập View, lý do/quyền/status/grace/source/Room-Customer và audit cùng transaction theo 4b. Không có giới hạn hai giờ của Guest; không có CheckIn giả khi chưa port service phiên.
- Không Initialize/DDL khi đọc. Ngoài NoShow maintenance trước snapshot, Search không ghi dữ liệu hoặc audit. Thông tin chi tiết trả cùng danh sách để không tải PII bằng đường không bảo vệ.

## Kiểm tra

Verify-MySqlStaffReservations chỉ tạo/xóa database musicbox_test_<GUID> có kiểm tra tên trước cleanup. Dữ liệu đặt phòng và hủy dùng service MySQL thật; kiểm tra View-only/Cancel-only, metadata quyền, creator Guest/Staff, current phone, các filter AND/ngày ±1 tick, Invalid/empty, lý do và audit/replay, NoShow đúng grace.

Kiểm tra thu hồi View trong maintenance bị chặn ở protected read; snapshot quyền và khách nhất quán khi dữ liệu/quyền thay sau lần SELECT quyền; lần đọc sau bị chặn. Có guard stamp và session nguồn. Session nguồn trong test là fixture chỉ chứng minh guard đọc/hủy, chưa nghiệm thu check-in MySQL thật.

Đã chạy online Debug/Release. Hồi quy MySqlBookingReads và các bộ reservation/availability/foundation được ghi tại AGENTS.md/plan. Không sửa database musicbox/defaultdb hay ảnh/dữ liệu thật.

## Làm tiếp

Guest lookup đầy đủ phải nối ActiveSession/tiền/món/gia hạn khi các service phiên tương ứng đã port; không trả Lookup giả với ActiveSession null. Đường Guest hủy riêng vẫn giữ scope quy tắc SĐT hiện hành/hạn hai giờ/audit Guest. Tiếp tục calendar nội bộ, check-in/walk-in/gia hạn/order/billing/checkout/invoice và ma trận quyền/nhật ký/báo cáo MySQL; trước cutover vẫn phải sao lưu/nhập/đối chiếu dữ liệu + Content và kiểm tra UI.
