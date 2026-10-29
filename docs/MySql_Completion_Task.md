# Task hoàn tất MySQL/Aiven — 29/10/2026

Người dùng yêu cầu làm hết phần còn lại trong cùng task, không dừng sau mỗi mốc để chờ “tiếp tục”. Vẫn commit nhỏ/bàn giao/báo cáo tiếng Việt; không tuyên bố hoàn tất từ build/test GUID.

- [x] Đến 06a đã nghiệm thu và push (0eeb25d).
- [x] 06b–d báo cáo phòng/booking/món + Excel và 07 history: đã port 4 service, fixture RemainingReads dùng service checkout thật.
- [x] Hợp đồng service chung cho hai provider; nối VM/Views/App/NoShow worker, giữ UI/quyền/Guest và SQLite lựa chọn cấu hình rõ ràng, không fallback tự động.
- [x] Kiểm tra đầu-cuối/UI cả hai provider và toàn bộ suites phù hợp, ghi chính xác log/giới hạn/race.
- [x] Backup SQLite+Content nhất quán, công cụ import/verify ID/FK/decimal/ticks/password/stamp/snapshot/ảnh và rollback, rehearsal trên GUID.
- [x] Import dữ liệu thật đã sao lưu/đối chiếu rồi cutover có cấu hình và khôi phục; không ghi đè DB thật chưa kiểm tra.
- [x] ERD/từ điển/bản xuất/demo và bàn giao cuối.

App/worker mặc định MySQL; nguồn thực đã nhập và đối chiếu. Xem MySql_Migration_09_Import_Cutover.md cho log, giới hạn và khôi phục. Máy in thật/nghiệm thu tại quầy là công việc thực địa còn lại. Định dạng csproj có sẵn 171 thêm/49 xóa phải giữ ngoài commit; chỉ stage Compile entries của task. Không xóa schema test nếu chưa biết thuộc lượt mình. Không đổi SSL/pooling/timeout để che lỗi test.
