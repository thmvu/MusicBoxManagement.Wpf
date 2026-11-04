# Thông báo ca đặt/nhận phòng — 04/11/2026

User yêu cầu sửa cả phía người đặt và phía nhân viên nhận phòng; ảnh là CheckIn bị từ chối vì giờ thực tế + đủ duration vượt ca. Reference mục 6/11: ca09–12 và13–23, giữ đủ thời lượng từ actual check-in; không tự cắt hoặc kéo qua nghỉ. Kết thúc đúng12/23 vẫn hợp lệ.

Sửa: shared BookingHours Try-validation không ném lỗi cho đầu vào thường gặp, reason tiếng Việt phân biệt nghỉ12–13/chưa mở9/đóng23. Existing Validate wrappers vẫn bảo vệ service trực tiếp. Guest/Staff booking VM kiểm trước Preview/Create, nhận phòng VM kiểm trước Request và Confirm (giờ có thể đổi sau preview). MySQL availability dùng Try để trả Reason. Cảnh báo lượt sát biên12/23 khuyên đến sớm, không hứa grace15 luôn đủ ca. Không đổi luật/quyền/schema/NoShow locks/audit/tiền hoặc backend cũ bị cấm. UI vẫn popup dùng chung.
