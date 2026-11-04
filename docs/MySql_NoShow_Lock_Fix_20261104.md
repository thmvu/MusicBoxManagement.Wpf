# Sửa timeout và thứ tự khóa NoShow — 04/11/2026

## Nguyên nhân đã quan sát

Hai ảnh người dùng gửi nằm tại LockCandidates/ExecuteReader và catch chuyển deadlock/lock timeout thành InvalidOperationException. Lượt sửa popup trước không xử lý nguyên nhân SQL này.

Probe production chỉ đọc trên Aiven MySQL 8.4.11 thấy một transaction giữ hai record X của Reservations PRIMARY trong **569 giây**, RowsModified=0, trong khi Reservations chỉ có một NoShow và không có Confirmed. User đã chủ động Shift+F5; probe sau đó không còn transaction/lock/wait và thống kê trạng thái Reservations không đổi. Không KILL kết nối, không cập nhật/rollback thủ công dữ liệu quán.

Query cũ `WHERE Status='Confirmed' ORDER BY ReservationId FOR UPDATE` chạy trong Serializable quét PK, có thể khóa cả dòng lịch sử không khớp và gap. ProcessExpiredAt còn quét range lần nữa. Khi debugger pause giữa transaction, các khóa đó tồn tại và cản lượt xử lý khác. Đây là lỗi phạm vi khóa; debugger làm thời gian giữ khóa dài thêm, không phải bằng chứng app đã đóng.

## Thay đổi

- Khám phá ID Confirmed bằng transaction **RR read-only riêng, không khóa row**. Re-read trạng thái/StartTime hiện hành dưới khóa PK chính xác trong writer Serializable.
- Ghép ID booking nguồn của check-in/replay/hủy/gia hạn vào cùng tập ID; dedupe và sort **trước mọi khóa Reservation**. Giữ Room → Customer → Reservation theo ID → Session như cũ.
- Mỗi batch tối đa 128 statement point SELECT, gửi trong cùng command để giảm round-trip. Không dùng IN/range scan mà optimizer có thể mở rộng phạm vi khóa. Đã kiểm metadata `X,REC_NOT_GAP` ở phần tử qua ranh giới batch.
- ProcessExpiredAt chỉ duyệt dữ liệu đã khóa, không quét toàn bảng lần hai. Clock lấy sau toàn bộ khóa; giữ đúng Start+15 phút, kiểm source session, UPDATE guard và audit System cùng transaction/rollback.
- Giữ schema, isolation writer, SSL, timeout, quyền, giá, trạng thái và worker định kỳ. Không tắt maintenance, không SKIP LOCKED trong nghiệp vụ, không retry vô hạn. Lỗi cạnh tranh thật vẫn báo lịch phòng đang cập nhật.

Tập discovery là snapshot: candidate mới commit sau discovery có thể được xử lý ở lượt maintenance kế tiếp. Reader/availability vẫn dùng effective NoShow từ grace. Writer cùng Room/Customer giữ resource locks để bảo vệ quyết định liên quan. Discovery cần thêm một pooled connection trong lúc writer giữ resource locks; không cache kết quả/quyền.

Chỉ sửa năm service MySQL và thêm runner/test. Không sửa backend/provider/test cũ bị người dùng cấm. Các suite Reservations/CheckIn cũ được chạy bằng C# **thuần MySQL** với runner mới không nạp dependency của provider cũ; giữ nguyên source/assertion của chúng.

## Tái hiện và kiểm tra

`Tests/Verify-MySqlNoShowLocks.ps1`, tùy chọn Suite Locks/Reservations/CheckIn; mặc định Locks. Mỗi bài tạo schema GUID riêng và dọn đúng schema đó; không dùng `musicbox` để dựng fixture.

1. Tạo booking và chuyển NoShow qua service thật. Khóa row lịch sử trong transaction riêng rồi gọi worker: RED cũ không tới clock, Green mới tới clock và trả 0, không audit thêm.
2. Khóa một Confirmed: worker vẫn phải chờ, clock chỉ đọc sau khi thả khóa; đúng grace chuyển một NoShow.
3. Tạo A/B Confirmed, check-in A qua service thật. SQL-only lock gate mô phỏng stale discovery đang giữ A trước B. ExtendStaff phải chờ A mà chưa khóa B; sau thả gate gia hạn thành công. RED review chứng minh code trung gian đã khóa B trước A; fix gộp source vào tập sort làm ca này đạt. Không giả trạng thái phiên/hóa đơn bằng SQL.
4. 130 fixture Confirmed trên Room/Customer riêng cộng B còn hiệu lực, vượt 128. Giữ gate tại clock, kiểm last ID có exact record lock, không chuyển các booking tương lai hoặc ghi audit NoShow thừa. SQL chỉ dựng catalog/Confirmed fixtures để kiểm batching.
5. Suites Reservations/CheckIn kiểm quyền hiện hành, canonical customer, clocks sau chờ, grace/ticks/ca, rollback/audit/idempotence, NoShow–cancel/check-in hai thứ tự, replay và cạnh tranh thực qua service.
6. UI Feedback kiểm lại đặt/tra cứu/gia hạn/món/checkout/Invoice/hủy/revoke và 9 số, không chỉ chạy maintenance độc lập.

## Review và lịch sử lượt chạy

- RED đầu thất bại đúng việc worker chờ lịch sử NoShow; Green nhỏ đạt.
- Review duy nhất chỉ ra một P1 mới do discovery cũ chứa A còn extension chỉ thấy B rồi khóa source A sau B. Đã tái hiện RED riêng và sửa gộp source vào ID set trước sort. Không coi review ban đầu sạch.
- Lượt source-order đầu chưa tới gate trong 3s; đổi thời gian chờ mở gate thành 15s như các race test cũ rồi tái hiện đúng P1. Không đổi SQL timeout hoặc điều kiện B không được khóa trước A.
- Hai lượt build copy lỗi MSB302x vì thư mục test đang được PowerShell dùng và phiên Debug user mở lại. Không giết process; user Stop Debugging, sau đó build dùng thật đạt. Lỗi copy file khác lỗi SQL và được lưu riêng.
- Locks trên bản dùng thật Debug/Release đã đạt: terminal history, Confirmed wait/fresh clock, source order, batch boundary/REC_NOT_GAP và audit. Hai suite MySQL Reservations/CheckIn Debug ở bản trước source-order fix cũng đạt; kết quả hồi quy bản cuối ghi dưới đây, không thay thế bằng kết quả cũ.

## Giới hạn

Đây là sửa contention của NoShow và thứ tự khóa liên quan, không cam kết mạng cloud không bao giờ timeout hoặc mọi tổ hợp concurrency đã được thử. Những thao tác thực sự đụng cùng Confirmed vẫn serialize và có thể báo bận theo timeout hiện hành. Không thay cấu hình debugger; pause trong transaction đang giữ booking còn hiệu lực vẫn có thể cản writer khác cho tới Continue/Stop.

Ảnh popup/UI ở lượt trước không phải bằng chứng maintenance không tranh khóa. Bằng chứng lượt này gồm RED/Green SQL lock gate, metadata row locks, hồi quy service và UI trên MySQL riêng.

## Tài liệu MySQL chính thức

- [Locks set by SQL statements](https://dev.mysql.com/doc/refman/8.0/en/innodb-locks-set.html): range/index scan có thể khóa cả bản ghi không khớp; unique equality lookup giới hạn record lock.
- [Locking reads](https://dev.mysql.com/doc/refman/8.4/en/innodb-locking-reads.html): SKIP LOCKED không cung cấp view phù hợp cho quyết định giao dịch tổng quát; lượt này không dùng nó để bỏ qua xung đột đặt phòng.

## Kết quả cuối và log

| Kiểm tra cuối | Kết quả |
|---|---|
| Build dùng thật Debug/Release | Exit 0, [Debug](verification/20261104-noshow-locks/build-debug-final.log), [Release](verification/20261104-noshow-locks/build-release-final.log) |
| Locks trên hai build dùng thật | Exit 0, [Debug](verification/20261104-noshow-locks/locks-debug.log), [Release](verification/20261104-noshow-locks/locks-release.log) |
| Reservations bản cuối Release | Exit 0, [log](verification/20261104-noshow-locks/reservations-release-final.log) |
| CheckIn bản cuối Release | Exit 0, [log](verification/20261104-noshow-locks/checkin-release-final.log) |
| Feedback UI bản cuối Debug | Exit 0, 34 popup +3 ảnh, [log](verification/20261104-noshow-locks/feedback-debug-final.log) |

Đã xem ảnh popup/form/hóa đơn trong lượt sửa UI trước; lượt này cùng UI và assertion được chạy lại trên service NoShow mới. Không nhận là mọi suite MySQL đều được chạy, hoặc mọi cấu hình đều chạy tất cả suite.

[Metadata trước](verification/20261104-noshow-locks/live-before.log), [sau user Stop](verification/20261104-noshow-locks/live-after-stop.log), [RED lịch sử](verification/20261104-noshow-locks/red-history-lock.log), [RED source order](verification/20261104-noshow-locks/red-source-order.log). Fixture cuối Locks Debug `musicbox_test_515743a61e4846d4aeac133910904f33`, Release `musicbox_test_71085978b39b409cb3b6006eeadfe375` và Feedback `musicbox_test_e911c81ecb254f6dbc4d2f0fa9dfed1c` đã dọn trong finally trước exit 0. Không sửa dữ liệu quán.

Log nguồn ở TEMP máy thực thi; bản lưu Git bỏ khoảng trắng cuối dòng, không ghi mật khẩu/kết nối/PII của khách. Định dạng csproj có sẵn 171/49 vẫn ngoài commit; không thêm Compile entry hoặc package ở lượt này.
