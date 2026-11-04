# Kế hoạch thông báo giờ nghỉ ở hai chiều

> Inline executing-plans/TDD, một review cuối. Spec: ../specs/2026-11-04-booking-shift-feedback.md.

- [x] RED popup cụ thể và preflight ở Guest/Staff booking/CheckIn; clock thay đổi trước Confirm.
- [x] Shared Try-hours + UI guards/warning + MySQL availability trả Reason, giữ Validate defense.
- [x] UI MySQL Debug/Release kiểm lunch/closing/exact endpoints/no-write/guard sau chờ; hồi quy đúng nhóm Reservations/CheckIn và Feedback.
- [x] Review, build thật, bàn giao docs/README/plan/AGENTS/status, commit Việt/push.

Base0995c21; csproj171/49dirty giữ riêng. Chỉ MySQL/UI, không mixed runner hoặc backend cũ. Không bỏ luật đủ duration/check-in grace để né lỗi. User đã yêu cầu sửa cụ thể, không hỏi lại lựa chọn thiết kế.

Kết quả: build thật Debug/Release exit0; Hours UI18 popup/bản; Reservations/CheckIn Release exit0; Feedback Debug34 popup exit0. Review cuối không có Critical/Important; bổ sung coverage đặt hộ biên12/23. RED với input hợp lệ tái hiện trên Release0995c21. Giữ hai lỗi fixture ban đầu và capture Debug đen trong [report](../../Booking_Shift_Feedback_20261104.md). Không chạy suite ngoài scope. User đã có chỉ đạo commit Việt/push; tích hợp main theo chỉ đạo có sẵn.
