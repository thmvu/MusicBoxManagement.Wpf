# Kế hoạch thông báo và kiểm tra nhập liệu

> Thực hiện inline bằng executing-plans/TDD; chỉ một review cuối, không fan-out implementer.

Goal: lỗi và kết quả thao tác có popup tiếng Việt; SĐT sai bị chặn trước service, form giữ mở. Spec: ../specs/2026-11-04-ui-feedback.md. WPF net472 + MySQL hiện hành.

- [x] RED: bấm Tra cứu với 9 số phải có thông báo, giữ form và không ném ArgumentException từ normalizer.
- [x] Thêm TryNormalize; preflight phone/date UI; hộp thông báo dùng chung và các command Guest/nhân viên.
- [x] MySQL UI fixture: phone invalid/valid +84, thiếu trường, service từ chối, thành công/cancel/extend/order/checkout; form còn mở và dữ liệu/audit không đổi khi validation sai.
- [x] Build và kiểm UI Debug/Release, ảnh popup, rà luồng/code; giữ giới hạn chứng cứ.
- [x] Review cuối và tài liệu bàn giao. Tích hợp tiếp theo: commit tiếng Việt và push theo ủy quyền; kết quả Git báo trên chat.

Ledger: base b7fdc1a. Chỉ csproj dirty khi bắt đầu. Ruling: yêu cầu user đã xác định cụ thể cách hiển thị lỗi, dùng popup WPF thuộc form; không hỏi lại thiết kế hoặc đổi nghiệp vụ. Chỉ rà source UI và MySQL; các nhánh backend cũ có sẵn không chỉnh/chạy.

## Kết quả và rulings

- RED popup9 số thất bại đúng trước mở database; Green nhỏ đạt. Full workflow đầu đạt 25 popup.
- Review duy nhất phát hiện hai P2 về Status cũ/row-selection. RED cart và RED customer-save thất bại đúng; đã sửa một lượt, workflow sau đạt 33 popup.
- Bản cuối dùng thật Debug và Release build exit0; UI full trên cả hai exit0, 34 popup/3 ảnh mỗi bản. Thêm kiểm reentry, unexpected-error không lộ thông tin, từ chối tiền chưa nhận và một Invoice service thật.
- Ruling: initial load/chọn dòng/navigation không popup; lệnh dữ liệu người dùng bấm mới popup. Không sửa business service để né debugger. Các helper/guard UI không cấp quyền thay service.
- Ruling: phạm vi kiểm động là Guest workflow + phone/CRUD/checkout/current rights; auth/room editor/quản trị/Excel được rà source/build, không coi 34 popup là mọi tính năng đã test. Review declined backend/production csproj cũ nằm ngoài thay đổi, được giữ nguyên.
- Source và bằng chứng: docs/Ui_Feedback_Validation_20261104.md. Hồi quy CounterUi Debug lượt đầu timeout, retry riêng đạt exit0; giữ cả hai log và assertion/SSL/timeout. Đã kiểm GUID của lượt fail không còn. Chỉ MySQL UI, không mixed runners.
