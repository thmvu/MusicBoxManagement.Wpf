# Giải thích kiến trúc MVC/MVVM và các Service trong Music Box WPF

Tài liệu này giải thích cấu trúc mã nguồn hiện tại của Music Box Management WPF, vai trò của từng phần và cách các service phối hợp với nhau. Dự án dùng **WPF trên .NET Framework 4.7.2 và SQLite**.

> **Điểm cần phân biệt:** project này không dùng ASP.NET MVC. Giao diện desktop được tổ chức theo kiểu **MVVM đơn giản**, cộng với các lớp Service và Data. Tài liệu so sánh với MVC để người học dễ liên hệ.

## 1. MVC là gì?

MVC là viết tắt của **Model–View–Controller**. Đây là cách chia một ứng dụng thành ba vai trò:

- **Model** biểu diễn dữ liệu và một phần quy tắc nghiệp vụ. Ví dụ một booking có mã, khách, phòng, giờ bắt đầu, giờ kết thúc và trạng thái.
- **View** là phần người dùng nhìn thấy và tương tác. Trong ứng dụng web, đó có thể là trang HTML.
- **Controller** nhận yêu cầu, gọi nghiệp vụ cần thiết rồi chọn dữ liệu/trang để trả về cho người dùng.

Trong một ứng dụng web MVC, một luồng có thể là: người dùng gửi form đặt phòng → controller nhận dữ liệu → model/service kiểm tra và lưu → controller trả trang xác nhận.

MVC thường được dùng với web framework, nhưng bản Music Box này là ứng dụng Windows desktop WPF. Vì vậy không có HTTP request, route hay ASP.NET MVC controller.

## 2. Project này dùng MVVM như thế nào?

MVVM là **Model–View–ViewModel**, cách tổ chức thường dùng với WPF vì WPF có Data Binding.

| Phần | Vai trò | Trong Music Box |
|---|---|---|
| Model | Đối tượng dữ liệu hoặc DTO nghiệp vụ | `Models/Room.cs`, `Reservation.cs`, `RoomSession.cs`, `RoomSchedule.cs`… |
| View | Giao diện và tương tác trực quan | `Views/*.xaml` và một phần `Views/*.xaml.cs` |
| ViewModel | Trạng thái màn hình, dữ liệu bind lên View và thao tác người dùng | `ViewModels/*ViewModel.cs` |
| Service | Quy tắc nghiệp vụ, quyền và thao tác dữ liệu nghiệp vụ | `Services/*Service.cs` |
| Data | Kết nối, schema và lưu trữ SQLite/Identity | `Data/*` |

ViewModel là một phần riêng của MVVM, không có trong định nghĩa MVC cổ điển. Nó không đơn giản là một cái tên khác của Controller: ViewModel chủ yếu giữ trạng thái trình bày và điều phối thao tác của màn hình; service mới là nơi thực hiện nghiệp vụ dùng chung và kiểm tra lại quyền/dữ liệu.

Trong project hiện tại, một số ViewModel áp dụng khá rõ `INotifyPropertyChanged` và `ObservableCollection` để WPF tự cập nhật màn hình. Một số điều phối cửa sổ vẫn nằm trong code-behind. Vì vậy đây là **MVVM đơn giản**, chưa phải triển khai MVVM thuần tách toàn bộ sự kiện khỏi code-behind.

## 3. Đường đi của dữ liệu

```text
Người dùng
   │ bấm nút / nhập form
   ▼
View: cửa sổ WPF (.xaml)
   │ Binding hoặc sự kiện trong .xaml.cs
   ▼
ViewModel: dữ liệu đang xem, lựa chọn, trạng thái bận/lỗi
   │ gọi phương thức
   ▼
Service: quyền + quy tắc nghiệp vụ + transaction
   │ đọc/ghi
   ▼
Data: SQLite connection, schema, Identity store
   ▼
SQLite

Kết quả quay lại theo chiều ngược:
SQLite → Service → ViewModel → Binding → giao diện
```

Ví dụ trong màn hình chính: `App.xaml.cs` tạo các service và truyền vào `MainWindow`. `MainWindow` mở cửa sổ nghiệp vụ khi người dùng chọn nút. ViewModel gọi service để nạp dữ liệu; sau đó WPF hiển thị các đối tượng trong collection. Có thể xem phần khởi tạo ở [`App.xaml.cs`](../MusicBoxManagement.Wpf/App.xaml.cs), màn hình chính ở [`MainWindow.xaml.cs`](../MusicBoxManagement.Wpf/Views/MainWindow.xaml.cs) và ViewModel danh mục ban đầu ở [`MainViewModel.cs`](../MusicBoxManagement.Wpf/ViewModels/MainViewModel.cs).

### Binding hoạt động ra sao?

Ví dụ ViewModel có thuộc tính `Status` chứa dòng “Đang tải…”. XAML có thể bind một `TextBlock` vào thuộc tính đó. Khi ViewModel đổi `Status` và phát sự kiện `PropertyChanged`, WPF cập nhật dòng chữ mà không cần viết lệnh sửa `TextBlock.Text` ở mọi chỗ.

`ObservableCollection<T>` cũng thông báo khi thêm/xóa phần tử, nên bảng hoặc danh sách có thể tự làm mới. Những lớp này phục vụ giao diện; chúng không thay thế kiểm tra nghiệp vụ trong service.

## 4. Model và DTO của project

Các lớp trong `Models` chủ yếu là những kiểu dữ liệu đơn giản. Chúng không đồng nghĩa với bảng database trong mọi trường hợp.

- `Room`, `RoomType`, `Customer`: dữ liệu phòng, loại phòng và khách.
- `Reservation`: booking ở lớp nghiệp vụ.
- `GuestReservation` và `StaffReservation`: hình dạng dữ liệu được dùng ở các màn hình khách và nhân viên.
- `PublicRoom`, `PublicRoomDay`, `PublicBookingSlot`: dữ liệu công khai phục vụ khách xem phòng/lịch.
- `RoomSchedule`, `RoomScheduleEvent`, `ScheduleInterval`: các thành phần lịch nhân viên.
- `RoomSession`: dữ liệu một phiên sử dụng phòng sau khi nhận phòng.
- `ServiceItem`: một món/dịch vụ bán như đồ uống hoặc đồ ăn; đây không phải service class.

**DTO** là đối tượng chuyên chở dữ liệu giữa các lớp hoặc trả về cho một màn hình. Ví dụ, dữ liệu lịch dành cho khách chỉ cần giờ và trạng thái bận/trống. Nó không nên chứa tên/số điện thoại khách hoặc ID booking nội bộ rồi trông chờ giao diện che đi. Vì thế có DTO public riêng thay vì bind trực tiếp dữ liệu lịch nhân viên.

## 5. Service là gì trong ứng dụng này?

`Service` ở đây là một lớp C# chạy ngay trong ứng dụng. Nó không phải một máy chủ riêng, không tự tạo kết nối mạng và không phải microservice.

Service nhận dữ liệu từ ViewModel hoặc lớp điều phối, kiểm tra quy tắc nghiệp vụ, mở kết nối database khi cần, kiểm tra quyền và lưu dữ liệu. Mỗi service nên chịu trách nhiệm cho một nhóm nghiệp vụ dễ gọi tên.

Ví dụ tên gọi:

- `RoomService`: nghiệp vụ quản lý phòng.
- `RoomSessionService`: nghiệp vụ phiên sử dụng phòng, hiện có nhận phòng từ booking.
- `ServiceCatalogService`: quản lý danh mục món/dịch vụ bán.
- `ServiceItem`: một món trong danh mục.

Hai từ “service” trong hai ví dụ đầu/cuối có nghĩa khác nhau.

## 6. Nhóm nền tảng: database, thời gian, quyền và audit

### `SqliteDatabase`

[`Data/SqliteDatabase.cs`](../MusicBoxManagement.Wpf/Data/SqliteDatabase.cs) mở kết nối SQLite, bật foreign keys, khởi tạo database và nâng cấp schema. File dữ liệu mặc định nằm trong thư mục dữ liệu ứng dụng của Windows, không nằm chung với mã nguồn project. Schema hiện tại là **v6**.

Khi ghi một nghiệp vụ cạnh tranh — ví dụ tạo booking hay check-in — ứng dụng dùng transaction ghi. Với SQLite, transaction này lấy quyền ghi trước khi service đọc trạng thái cần kiểm tra. Nhờ vậy hai thao tác cùng lúc không thể đều đọc “phòng còn trống” rồi cùng ghi thành công.

### Xác thực tài khoản

- [`AuthenticationService`](../MusicBoxManagement.Wpf/Services/AuthenticationService.cs) thiết lập Admin đầu tiên, đăng nhập và đăng xuất.
- `Pbkdf2PasswordHasher` băm và xác minh mật khẩu; ứng dụng không cần lưu mật khẩu dạng đọc được.
- `SqliteIdentityStore` nối ASP.NET Identity Core với phần lưu SQLite của ứng dụng desktop.
- `AuthenticationSchema` khai báo/cập nhật các bảng tài khoản, role và permission.
- `LoginSession` biểu diễn phiên đăng nhập trong bộ nhớ của ứng dụng.

Project dùng một số thư viện Identity Core để xử lý danh tính, nhưng **không dùng ASP.NET MVC hay OWIN cookie**.

### Phân quyền

- [`PermissionCatalog`](../MusicBoxManagement.Wpf/Services/PermissionCatalog.cs) liệt kê các mã quyền đã biết và quyền mặc định cho role.
- [`PermissionService`](../MusicBoxManagement.Wpf/Services/PermissionService.cs) đọc tài khoản/role/quyền hiện hành và cung cấp thao tác như `HasPermission` hoặc `Demand`.

Giao diện có thể ẩn nút nếu tài khoản không có quyền để người dùng dễ hiểu. Tuy nhiên, ẩn nút không phải cơ chế bảo vệ dữ liệu: service vẫn phải đọc lại quyền khi thực hiện thao tác. Với thao tác ghi, kiểm tra quyền nên nằm trong chính transaction ghi để không dùng quyền cũ đã đọc trước đó.

### Thời gian

- `IClock` là giao diện cung cấp mốc thời gian.
- `SystemClock` dùng giờ thật của máy.
- Service có thể nhận một clock giả trong kiểm thử để thử đúng mốc như trước/đúng/sau hạn nhận phòng mà không cần đợi đồng hồ chạy.
- `BookingHours` thống nhất quy tắc giờ Việt Nam, ca hoạt động, thời lượng và slot booking.

Giờ của nghiệp vụ được chuẩn hóa/lưu UTC; khi hiển thị hoặc áp dụng lịch đặt phòng thì đổi về múi giờ Việt Nam UTC+7 theo quy tắc của project.

### Ghi nhật ký

`AuditService` là helper dùng nội bộ để ghi thao tác quan trọng như tạo/hủy booking, check-in, khóa phòng hoặc đổi danh mục. Nhật ký cần nằm trong cùng transaction với thay đổi nghiệp vụ: nếu ghi audit thất bại thì thay đổi chính cũng rollback, tránh database có nghiệp vụ đã đổi nhưng thiếu log.

## 7. Nhóm phòng và danh mục

### `RoomTypeService`

Đọc loại phòng Standard/VIP và lưu thay đổi loại phòng theo quyền. Mã loại được giữ cố định; người dùng chỉnh những thông tin được cho phép như tên, sức chứa, giá, tiện ích và mô tả.

### `RoomService`

Quản lý danh sách phòng nội bộ, tạo phòng, đọc thông tin sửa, cập nhật tên/mô tả/ảnh, đổi loại và khóa/mở phòng. Service kiểm tra quyền `Room.Manage`, dữ liệu hiện tại, các điều kiện liên quan phiên đang dùng/booking còn hiệu lực và ghi audit cùng transaction.

`RoomImages` đọc và xác thực file ảnh, giới hạn kiểu/kích thước, chuẩn hóa ảnh trước khi lưu. Service quản lý đường dẫn ảnh cùng thông tin phòng; phần đọc file ảnh không phải quy tắc đặt phòng.

### `ServiceCatalogService`

Quản lý danh mục đồ ăn/đồ uống và dịch vụ bán tại quầy: tên, nhóm, giá, mô tả và trạng thái đang bán. Có quyền riêng `Service.Manage`. Món đã ngừng bán không nên xóa nếu sau này hóa đơn/đơn hàng cần giữ lại tên và giá lịch sử.

### `CustomerService` và `PhoneNumberNormalizer`

`CustomerService` phục vụ danh mục khách nội bộ: tìm kiếm và thêm/sửa thông tin theo quyền `Customer.View`, `Customer.Create`, `Customer.Edit`. Số điện thoại được chuẩn hóa bởi `PhoneNumberNormalizer` để tìm/kiểm tra trùng nhất quán.

Đường này dành cho nhân viên quản lý danh mục. Nó không phải đường tra cứu booking công khai của khách; Guest có service riêng để không vô tình mở danh sách khách cho giao diện quầy.

## 8. Nhóm đặt phòng

### `AvailabilityService`, `BookingHours`, `ScheduleRules`

`AvailabilityService` trả lời câu hỏi “phòng/khách có thể đặt khoảng này không?”. Nó kiểm tra ngày và giờ Việt Nam, slot/thời lượng được phép, ca hoạt động, phòng đang mở, khách hợp lệ và khoảng bị giữ bởi booking hoặc phiên phòng.

`ScheduleRules` định nghĩa các khoảng được xem là đang giữ lịch và được chia sẻ giữa kiểm tra đặt phòng và lịch. Quy tắc overlap dùng khoảng nửa mở `[bắt đầu, kết thúc)`: một lượt kết thúc đúng lúc lượt kế tiếp bắt đầu thì không bị xem là chồng nhau.

Kết quả preview không giữ chỗ. Một nhân viên hoặc khách khác có thể tạo booking trước khi người xem bấm lưu. Vì thế thao tác lưu phải chạy kiểm tra lại bên trong transaction ghi.

### `ReservationService`

Đây là service ghi booking nền tảng:

- `CreateGuest` tạo booking từ luồng khách, không yêu cầu phiên đăng nhập nhân viên.
- `CreateStaff` tạo booking hộ và gắn người nhân viên tạo booking; yêu cầu quyền phù hợp.
- `CancelStaff` hủy booking nội bộ, kiểm tra quyền hủy, trạng thái, thời hạn, lý do và session liên quan.

Khi tạo booking, service chuẩn hóa số điện thoại, tìm hoặc tạo khách, kiểm tra phòng/khách/lịch, lưu trạng thái và ghi audit trong cùng transaction. Nếu kiểm tra thất bại thì khách mới vừa tạo cũng không bị để lại rời rạc.

### `IBookingService`, `GuestBookingService`, `StaffBookingService`

`IBookingService` mô tả những thao tác mà form đặt phòng cần: liệt kê phòng có thể hiện, xem trước giờ, đọc lịch ngày, lấy ảnh và tạo booking.

- `GuestBookingService` là route công khai cho khách. Nó chỉ trả thông tin phòng được phép hiện công khai và khi tạo thì dùng `CreateGuest`.
- `StaffBookingService` dùng lại form nhưng kiểm tra quyền `Reservation.Create` và gọi `CreateStaff`.

Tách hai route này giúp form có thể dùng chung mà không làm mất sự khác nhau về quyền, actor và audit. Nhân viên mất quyền cũng không được tự động chuyển sang đường Guest.

### `GuestReservationService`

Tra cứu/hủy booking theo số điện thoại do khách cung cấp. Nó chỉ trả booking của số điện thoại chuẩn hóa đó, áp dụng trạng thái/hạn còn hiệu lực và kiểm tra lại dữ liệu trong transaction khi xác nhận hủy. Đây không phải tài khoản Guest: người dùng không cần mật khẩu hay OTP theo quyết định hiện tại của project.

### `StaffReservationService`

Cung cấp đường nội bộ cho màn hình booking nhân viên: tìm theo khoảng ngày, số điện thoại, trạng thái; đọc chi tiết; cung cấp route đặt hộ hoặc lịch nội bộ; chuyển việc hủy sang `ReservationService.CancelStaff`. Quyền xem booking và quyền tạo/hủy booking là các quyền riêng.

## 9. Lịch và dữ liệu công khai

### `GuestCalendarService`

Tạo `PublicRoomDay` và các ô `PublicBookingSlot` chỉ thể hiện phòng trống/bận theo ngày/thời lượng. Không đưa tên khách, số điện thoại hoặc ID booking/session vào DTO công khai. Đọc lịch không tự ghi NoShow.

### `CalendarService`

Tạo lịch ngày/tuần nội bộ cho nhân viên có `Calendar.View`. Dữ liệu phong phú hơn gồm trạng thái phòng hiện tại, các hold, booking và phiên thực tế/dự kiến. Dữ liệu này chứa chi tiết nội bộ, vì thế không dùng thẳng cho màn hình Guest.

Lịch tách ba ý nghĩa khác nhau:

- **CurrentStatus**: trạng thái phòng tại thời điểm đọc, ví dụ đang trống hay đang có phiên.
- **Holds**: các khoảng được booking hoặc session giữ lịch.
- **Events**: các mục hiển thị trên lịch, bao gồm thời gian thực tế và dự kiến.

Một session có thể quá giờ dự kiến; lịch hiển thị thời gian thực tế và dự kiến riêng, không tự kéo dài hold booking vô hạn. Khi giao diện vẽ lịch, nó cần cắt phần hiển thị vào phạm vi ngày/tuần đang xem nhưng vẫn giữ giờ gốc trong chi tiết.

## 10. NoShow và worker nền

- `NoShowService` tìm booking Confirmed quá mốc grace 15 phút, chưa có session, chuyển sang NoShow và ghi audit System cùng transaction.
- `NoShowWorker` chạy tác vụ lúc ứng dụng khởi động và theo chu kỳ khi app còn mở; nó được dừng khi ứng dụng thoát.

Worker xử lý booking đến hạn, nhưng các thao tác khác không được dựa vào worker đã chạy đúng giây. Ví dụ, lúc check-in hoặc đọc availability, service vẫn cần tự kiểm tra giờ hiệu lực. Check-in và NoShow dùng transaction ghi cùng quy tắc để không tạo tình trạng một booking vừa có session lại vừa bị đánh dấu NoShow.

## 11. RoomSession và check-in hiện tại

[`RoomSessionService`](../MusicBoxManagement.Wpf/Services/RoomSessionService.cs) đã có phương thức nhận phòng từ booking Confirmed. Service kiểm tra quyền `Session.CheckIn`, hạn grace, phòng đang mở, phòng/khách không có phiên Active, ca hoạt động và overlap lịch. Khi hợp lệ, nó tạo `RoomSession`, đổi booking sang CheckedIn, chốt giá/loại phòng/tên loại và ghi audit trong một transaction.

Check-in sớm được phép nếu đủ chỗ cho toàn bộ thời lượng. Giờ nhận được giữ theo thời điểm thực tế, không ép vào slot 30 phút. Nếu bấm lại cùng booking sau khi phiên đã được tạo, service trả phiên cũ và không tạo phiên/log thứ hai.

**Hiện trạng giao diện (07/10/2026):** đã có nút Nhận phòng và panel xác nhận/kết quả ở chi tiết booking. ViewModel gọi service, hiển thị giờ/giá chốt và tải lại danh sách; xem Step6a2_CheckInUi.md. Đã có CreateWalkIn và ReadWalkIn ở RoomSessionService cho phần xử lý khách trực tiếp, gồm snapshot/giờ cần trả động; xem Step6b1_WalkIn.md. UI walk-in, gia hạn, gọi món, tính tiền, checkout/hóa đơn và báo cáo chưa được triển khai.

## 12. Transaction ghi nghiệp vụ: ví dụ check-in

Các bước khái quát của một thao tác ghi quan trọng:

1. Mở kết nối SQLite và lấy transaction ghi.
2. Kiểm tra phiên đăng nhập và quyền hiện hành trong transaction.
3. Đọc booking/phòng/khách mới nhất từ database.
4. Kiểm tra trạng thái, giờ, ca và trùng lịch.
5. Ghi session và đổi booking.
6. Ghi audit.
7. Commit.

Nếu một bước từ 2 đến 6 thất bại, transaction được rollback. Như vậy thao tác không để lại trạng thái nửa chừng, chẳng hạn có session nhưng booking vẫn Confirmed hoặc đã đổi trạng thái mà không có audit.

Đây cũng là lý do View hoặc ViewModel không nên cập nhật database trực tiếp. Giao diện thu thập ý định của người dùng; service chịu trách nhiệm thực hiện nghiệp vụ an toàn.

## 13. So sánh ngắn MVC với project này

| Nếu nghĩ theo MVC | Thành phần gần tương ứng trong Music Box | Ghi chú |
|---|---|---|
| Model | `Models` + `Services` + một phần `Data` | Model MVC thường bao trùm dữ liệu/nghiệp vụ; project tách ra nhiều lớp |
| View | `Views/*.xaml` | Đây là các cửa sổ desktop, không phải trang Razor |
| Controller | Điều phối sự kiện trong code-behind và ViewModel | Không có lớp Controller ASP.NET riêng |
| Xử lý nghiệp vụ | `Services/*Service.cs` | Kiểm tra nghiệp vụ/quyền và gọi Data |
| Lưu trữ | `Data/SqliteDatabase.cs`, schema, Identity store | SQLite cục bộ thay vì SQL Server web |

Nói ngắn gọn trong buổi bảo vệ: **“Ứng dụng không dùng ASP.NET MVC. Đây là WPF theo MVVM đơn giản; View là XAML, ViewModel giữ trạng thái giao diện, Service xử lý nghiệp vụ, Data truy cập SQLite.”**

## 14. Gợi ý đọc mã nguồn theo thứ tự

Để hiểu một luồng hoàn chỉnh, nên đọc từ giao diện xuống service rồi tới data:

1. `Views/GuestBookingWindow.xaml` — các trường và nút form.
2. `ViewModels/GuestBookingViewModel.cs` — trạng thái form, kiểm tra thao tác và gọi route.
3. `Services/IBookingService.cs`, `GuestBookingService.cs` — route public của form.
4. `Services/ReservationService.cs` — ghi booking và kiểm tra transaction.
5. `Services/AvailabilityService.cs`, `BookingHours.cs`, `ScheduleRules.cs` — điều kiện giờ và overlap.
6. `Data/SqliteDatabase.cs`, `Data/RoomUsageSchema.cs` — kết nối và cấu trúc dữ liệu liên quan.

Với check-in, đọc `Models/RoomSession.cs`, `Services/RoomSessionService.cs`, rồi đối chiếu `Services/NoShowService.cs` và `Services/CalendarService.cs` để thấy session mới ảnh hưởng lịch và trạng thái phòng ra sao.
