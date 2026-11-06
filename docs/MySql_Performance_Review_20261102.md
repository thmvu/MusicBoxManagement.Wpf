# Rà soát độ trễ MySQL — 02/11/2026

Người dùng yêu cầu tự động tối ưu và xét phần cần/không cần bằng Superpowers và [Ponytail](https://github.com/DietrichGebert/ponytail/blob/main/skills/ponytail/SKILL.md). Đã đọc using-superpowers, systematic-debugging, brainstorming, writing-plans, executing-plans, TDD, review và verification; áp dụng Ponytail trực tiếp từ nguồn, không cài plugin/dependency hoặc chạy script bên ngoài. User giao tự quyết nên ghi lựa chọn/phạm vi vào [plan](https://github.com/thmvu/MusicBoxManagement.Wpf/blob/72a0fa70cdec42769f00a2bd9d5826de4f2adb53/docs/superpowers/plans/2026-11-02-mysql-latency.md) và triển khai không chờ hỏi thiết kế.

## Phân loại từ luồng thực tế

| Phần đang có | Quyết định | Lý do |
|---|---|---|
| Startup/NeedsSetup/SetupAdmin/Login gọi MySqlDatabase.Initialize | Tối ưu shared Initialize | Database đã version1 vẫn CREATE DATABASE, ChangeDatabase, GET_LOCK, kiểm marker, CREATE/INSERT IGNORE marker, SELECT version, RELEASE_LOCK; nhiều vòng mạng không cần cho schema sẵn sàng |
| Kiểm version mỗi lần | Giữ | Không cache initialized khiến bỏ sót schema mới hơn trong cùng process |
| DDL/seed/advisory lock cho database mới/thiếu marker/lower version | Giữ | Tránh writer tạo schema đồng thời, phục hồi setup chưa hoàn tất và chặn database không được quản lý |
| Demand trước mở route và kiểm quyền lại trong transaction | Giữ | Quyền có thể bị thu hồi/stamp/logout giữa hai bước; chúng không tương đương về thời điểm và bảo vệ |
| RefreshAccessAsync khi đóng dialog và dashboard snapshot mới | Giữ lượt này | Menu/tài khoản/trạng thái phòng có thể thay đổi; muốn giảm cần bằng chứng theo route và hợp đồng freshness rõ ràng |
| SET session, SSL VerifyFull, connection reset/pool | Giữ | Không tắt an toàn/transaction isolation để giảm thời gian hoặc che timeout |
| Task.Run ở VM/handler | Giữ | Đưa truy vấn đồng bộ ra khỏi UI thread; không giải quyết round-trip bằng cách bỏ async UI |
| Worker NoShow | Giữ | MySQL worker hiện không initialize schema mỗi tick; có chức năng nghiệp vụ thực, không xóa vì chạy nền |
| Provider SQLite, tests, docs, seed tools | Giữ | Provider phục hồi có chủ đích; tests/docs không chạy trong app, seed chỉ chạy chủ động. Xóa chúng không tăng tốc thao tác WPF |

Không thay API/UI/VM/quyền/transaction/tính tiền hoặc dữ liệu quán. Không coi mọi hàm nhiều dòng là dư, không cắt chức năng theo số dòng. Root cause lần này là schema ready vẫn đi qua đường khởi tạo đầy đủ; không tuyên bố đây là nguyên nhân duy nhất của mọi màn hình chậm.

## Thay đổi nhỏ tại MySqlDatabase.Initialize

Thử OpenConnection đúng database và SELECT Version hiện hành. Version1 trả về ngay, không DDL/seed/schema lock. Version lớn hơn vẫn báo từ chối. Chỉ lỗi MySQL1049 (database thiếu),1146 (bảng thiếu), marker row thiếu hoặc version thấp mới đi tiếp quy trình locked cũ. Lỗi mạng/quyền/SSL khác không bị catch hoặc retry. Mỗi lần đọc lại version, không global cache. Các caller hiện có hưởng thay đổi mà không sửa luồng đăng nhập.

Schema current mới không chờ advisory lock khởi tạo; kiểm version bảo vệ binary hiện tại, không phải cơ chế hỗ trợ upgrade schema tương lai trong lúc người dùng vẫn thao tác. Khi nâng schema thật cần dừng app/worker theo quy trình vận hành như trước. Setup lần đầu có thêm lần thăm dò database/marker thiếu, chấp nhận đánh đổi vì đây không phải thao tác lặp thường ngày.

## Đo tại máy

Trước sửa trên musicbox ready: Initialize1885/1091/1057ms (lần đầu lạnh; hai lần ấm khoảng1,06–1,09s). Sau sửa Release:1747/683/612/613/616ms (lần đầu lạnh; bốn lần ấm khoảng0,61–0,68s). Đây là mẫu đo khác thời điểm/mạng và tải cloud, không phải benchmark kiểm soát; không nhân thành phần trăm cải thiện toàn ứng dụng hoặc cam kết mọi nút nhanh hơn. Đường schema ready bỏ các lệnh DDL/lock/seed cụ thể, nhưng kết nối và SELECT qua Internet vẫn có độ trễ.

Số đo đọc ban đầu trước sửa: SELECT1 trên connection mở96–137ms; OpenConnection+session SET dùng pool283–376ms; Guest rooms RR539–710ms. Vì vậy nhiều lượt dữ liệu nối tiếp cộng độ trễ dù DB ít dòng. Không đủ bằng chứng kết luận gói Aiven miễn phí thiếu CPU/RAM; chưa có server profiling/EXPLAIN mọi route hoặc đo từng cú bấm của người dùng.

Logs ngoài Git: TEMP/MusicBoxReadLatency_20261102.log, MusicBoxInitBaseline_20261102.log, MusicBoxInitAfter_20261102.log. Đo production không đăng nhập/ghi nghiệp vụ; baseline Initialize thực hiện IF NOT EXISTS/INSERT IGNORE trên schema sẵn sàng như app cũ, không import/seed/reset dữ liệu quán.

## Kiểm tra và review

Test mới MySqlInitializeChecks/Verify-MySqlInitialize dùng GUID sở hữu riêng: tạo schema thật, writer giữ advisory schema lock trong khi Initialize reader phải hoàn tất trước5s, schema mới99 bị từ chối trên cùng object, thiếu marker với bảng nghiệp vụ bị chặn, marker thiếu row phục hồi trên DB trống có marker. Bản cũ RED đúng thông báo chờ schema lock; bản mới GREEN Debug1229ms/Release778ms trong ca khóa. Test không dùng giới hạn5s làm SLA UI. Missing row đi qua version0 recovery; chưa có ca độc lập version0 đã lưu hoặc network/permission-denied injection. Các lỗi này không bị catch ngoài1049/1146 theo review.

Hai lần dựng runner Add-Type ban đầu lỗi reference trước fixture, sửa runner theo mẫu có sẵn rồi mới có RED đúng hành vi. Review độc lập tĩnh không có finding cần sửa; không coi static review là online test.

Kết quả hồi quy chi tiết và log cuối được ghi dưới đây sau khi process hoàn tất. Không nhận runner toàn73 bộ đã chạy trong lượt tối ưu nhỏ này; runner nay44 SQLite/WPF+29 MySQL, thêm Initialize. Mất điện ngắt một retry Accounts, log chưa có kết quả/không biết GUID nên không dọn schema đoán; chạy lượt mới có log riêng.


Lượt OperationsUi chạy lại sau mạng hết timeout đã lộ lỗi fixture lịch cũ: invoice cố định29/10 nhưng dashboard/Revenue/RoomReport VM dùng ngày/tháng hiện tại02/11. Assertion doanh thu không đúng khi sang ngày/tháng mới. Đã thay ngày fixture thành hôm nay Việt Nam và booking worker thành hôm qua, giữ toàn bộ assertions/production clocks. Không sửa dashboard/query hoặc giả Invoice bằng SQL. Log thất bại `MusicBoxLatencyOperationsRetryDebug_20261102.log` giữ nguyên; log sau sửa fixture có tên FinalDebug, không ghi đè lịch sử.


### Kết quả cuối / phạm vi

9 lượt kiểm tra cuối đạt: Initialize/Foundation/Authentication mỗi bộ Debug+Release (6), Accounts Debug (1), OperationsUi Debug (1), AuthenticationUi SQLite Debug (1). Summary đã lọc không dữ liệu riêng: [20261102-latency.json](verification/20261102-latency.json). AuthenticationUi142 ảnh, OperationsUi5 ảnh; đã xem dashboard fixture hiển thị110.000đ ngày02/11. Không in giấy thật hoặc tạo dữ liệu production. Build VerifyLatencyDebug/Release và bin/Debug, bin/Release dùng thật đều đạt exit0.

Giữ nguyên log đầu Accounts/OperationsUi Debug Connect Timeout, không coi lượt đầu toàn đạt. Accounts retry đầu bị ngắt điện không có kết quả; resume tuần tự đạt. OperationsUi retry thứ nhất lỗi ngày fixture29/10; sau sửa ngày today/yesterday, FinalDebug đạt. Đây là các nguyên nhân khác nhau, không giảm SSL/reset/timeout/assertion để qua test. Không rerun mọi suite writer/race vì không chạm code nghiệp vụ; vẫn chưa đo từng route/UI hành động thật hoặc in driver thật. Review fixture mới không finding; giới hạn còn lại là test đi qua nửa đêm Việt Nam có thể đổi ngày giữa fixture/runtime, đặc biệt giao tháng.

Các log: MusicBoxInitializeRed_20261102.log (RED), GreenDebug/GreenRelease như summary; MusicBoxLatencyAccountsDebug_20261102.log và MusicBoxLatencyOperationsDebug_20261102.log (timeout); MusicBoxLatencyAccountsRetryDebug_20261102.log (mất điện); MusicBoxLatencyOperationsRetryDebug_20261102.log (fixture date); MusicBoxLatencyShipDebug/ShipRelease_20261102.log (build thật). Toàn bộ ở TEMP ngoài Git, summary và mô tả được lưu trong Git để bàn giao.

Để thử bản sửa: đóng/mở lại app hoặc build/run project bình thường. Mẫu này chủ yếu giảm khởi tạo/đăng nhập, không biến truy vấn online thành tốc độ SQLite local. Nếu màn cụ thể còn chậm, đo đúng route tiếp theo; không coi report này là chứng nhận đã tối ưu toàn repo.
