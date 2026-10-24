using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class DashboardLine
    {
        public string Title { get; internal set; }
        public string Detail { get; internal set; }
        public string Code { get; internal set; }
        public string StatusCode { get; internal set; }
        public string StatusLabel { get; internal set; }
    }
    public sealed class DashboardViewModel : INotifyPropertyChanged
    {
        private readonly Func<LoginSession, DashboardService> create;
        private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
        private LoginSession actor;
        private int version;
        private bool busy;
        private DashboardSnapshot snapshot;
        private string status = "Đăng nhập để xem hoạt động quán.";
        public DashboardViewModel(Func<LoginSession, DashboardService> create) { this.create = create; }
        public ObservableCollection<DashboardLine> Rooms { get; } = new ObservableCollection<DashboardLine>();
        public ObservableCollection<DashboardLine> Bookings { get; } = new ObservableCollection<DashboardLine>();
        public ObservableCollection<DashboardLine> Sessions { get; } = new ObservableCollection<DashboardLine>();
        public ObservableCollection<DashboardLine> Orders { get; } = new ObservableCollection<DashboardLine>();
        public ObservableCollection<DashboardLine> Alerts { get; } = new ObservableCollection<DashboardLine>();
        public DashboardSnapshot Snapshot => snapshot;
        public bool IsBusy => busy;
        public bool CanRefresh => actor != null && !actor.IsSignedOut && !busy;
        public bool HasRevenue => snapshot?.TodayRevenue.HasValue == true;
        public string Revenue => HasRevenue ? "Doanh thu hôm nay: " + snapshot.TodayRevenue.Value.ToString("N0", Vietnamese) + " đ" : "";
        public string Summary => snapshot == null ? "" : "Phòng: " + snapshot.Rooms.Count + "    Booking hôm nay: " + snapshot.BookingsToday.Count
            + "    Đang sử dụng: " + snapshot.ActiveSessions.Count + "    Đơn chờ: " + snapshot.PendingOrders.Count;
        public int AvailableRoomCount => snapshot?.Rooms.Count(r => r.Status == "Available") ?? 0;
        public int OccupiedRoomCount => snapshot?.Rooms.Count(r => r.Status == "Occupied") ?? 0;
        public int BookingCount => snapshot?.BookingsToday.Count ?? 0;
        public int PendingOrderCount => snapshot?.PendingOrders.Count ?? 0;
        public string Updated => snapshot == null ? "" : "Cập nhật lúc " + Local(snapshot.CheckedAt) + " · Giờ Việt Nam";
        public string AlertHeading => snapshot == null ? "Cảnh báo" : "Cảnh báo cần lưu ý: " + snapshot.Alerts.Count;
        public string Status => status;
        public event PropertyChangedEventHandler PropertyChanged;
        public void Reset(LoginSession session)
        {
            version++; actor = session; busy = false; Clear();
            status = session == null ? "Chọn chức năng trong menu bên trái; dashboard cần quyền xem bảng điều hành." : "Bấm Làm mới để đọc hoạt động quán.";
            Changed();
        }
        public async Task RefreshAsync()
        {
            if (!CanRefresh) return;
            var current = actor; var request = ++version;
            busy = true; Clear(); status = "Đang đọc hoạt động quán…"; Changed();
            try
            {
                var result = await Task.Run(() => create(current).Read(current));
                if (request != version || actor != current || current.IsSignedOut) return;
                snapshot = result;
                foreach (var room in result.Rooms) Rooms.Add(new DashboardLine { Title = room.RoomCode + " — " + State(room.Status), Detail = room.RoomName,
                    Code = room.RoomCode, StatusCode = room.Status, StatusLabel = State(room.Status) });
                foreach (var booking in result.BookingsToday) Bookings.Add(Line("Booking #" + booking.ReservationId + " · " + booking.RoomCode + " · " + State(booking.Status),
                    Local(booking.Start) + " → " + Local(booking.End)));
                foreach (var session in result.ActiveSessions) Sessions.Add(Line("Phiên #" + session.SessionId + " · " + session.RoomCode + (session.IsWalkIn ? " · Khách trực tiếp" : " · Từ booking"),
                    "Nhận: " + Local(session.ActualStart) + " · Cần trả: " + Local((session.ExpectedEnd ?? session.ReturnBy).Value) + (session.IsOverdue ? " · Quá giờ" : "")));
                foreach (var order in result.PendingOrders) Orders.Add(Line("Đơn #" + order.OrderId + " · " + order.RoomCode, "Phiên #" + order.SessionId + " · Gửi lúc " + Local(order.CreatedAt)));
                foreach (var alert in result.Alerts) Alerts.Add(Line(AlertName(alert.Kind) + " · " + alert.RoomCode,
                    (alert.SessionId.HasValue ? "Phiên #" + alert.SessionId + " · " : "") + (alert.ReservationId.HasValue ? "Booking #" + alert.ReservationId + " · " : "") + Local(alert.RelevantAt)));
                status = "Dữ liệu tại thời điểm cập nhật. Bấm Làm mới khi cần; mở nghiệp vụ bằng menu bên trái.";
            }
            catch (Exception error)
            {
                if (request != version || actor != current) return;
                Clear(); status = error is UnauthorizedAccessException ? "Không còn quyền xem dashboard hoặc phiên đăng nhập đã hết hiệu lực. Hãy tải lại quyền hoặc đăng nhập lại."
                    : error is InvalidOperationException ? error.Message + " Hãy thử tải lại." : "Chưa đọc được dashboard. Dữ liệu cũ đã được xóa; hãy thử Làm mới.";
            }
            finally { if (request == version) { busy = false; Changed(); } }
        }
        private void Clear() { snapshot = null; Rooms.Clear(); Bookings.Clear(); Sessions.Clear(); Orders.Clear(); Alerts.Clear(); }
        private static DashboardLine Line(string title, string detail) => new DashboardLine { Title = title, Detail = detail };
        private static string Local(DateTimeOffset value) => value.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy", Vietnamese);
        private static string State(string value)
        {
            switch (value) { case "Available": return "Trống"; case "Occupied": return "Đang sử dụng"; case "Reserved": return "Chờ nhận phòng"; case "Inactive": return "Đã khóa";
                case "Confirmed": return "Đã xác nhận"; case "CheckedIn": return "Đã nhận phòng"; case "Completed": return "Hoàn tất"; case "Cancelled": return "Đã hủy"; case "NoShow": return "Không đến"; default: return value; }
        }
        private static string AlertName(string kind)
        { switch (kind) { case "NearNoShow": return "Sắp hết hạn nhận phòng"; case "Overdue": return "Khách quá giờ"; case "NextBooking": return "Cần trả trước lượt kế tiếp"; case "OutsideHours": return "Còn khách ngoài giờ mở cửa"; default: return kind; } }
        private void Changed()
        { foreach (var name in new[] { nameof(Snapshot), nameof(IsBusy), nameof(CanRefresh), nameof(HasRevenue), nameof(Revenue), nameof(Summary), nameof(AvailableRoomCount), nameof(OccupiedRoomCount), nameof(BookingCount), nameof(PendingOrderCount), nameof(Updated), nameof(AlertHeading), nameof(Status) }) Notify(name); }
        private void Notify([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
