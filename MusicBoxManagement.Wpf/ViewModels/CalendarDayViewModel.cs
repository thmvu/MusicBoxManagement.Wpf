using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class CalendarRoomChoice
    {
        public int? RoomId { get; internal set; }
        public string Label { get; internal set; }
    }
    public sealed class CalendarDayViewModel : INotifyPropertyChanged
    {
        private readonly CalendarService service;
        private readonly LoginSession session;
        private readonly IClock clock;
        private DateTime? date;
        private CalendarRoomChoice room;
        private bool busy;
        private bool week;
        public bool IsWeek => week;
        public bool IsDay => !week;
        public string Heading => week ? "Lịch phòng · Tuần" : "Lịch phòng · Ngày";
        public string PreviousLabel => week ? "Tuần trước" : "Ngày trước";
        public string NextLabel => week ? "Tuần sau" : "Ngày sau";
        private string status, details = "Chọn một lượt trên lịch để xem chi tiết.";
        public ObservableCollection<CalendarRoomChoice> RoomChoices { get; } = new ObservableCollection<CalendarRoomChoice>();
        public ObservableCollection<RoomSchedule> Rooms { get; } = new ObservableCollection<RoomSchedule>();
        public DateTime? Date { get => date; set { if (date == value) return; date = value; Notify(); Clear(); } }
        public CalendarRoomChoice SelectedRoom { get => room; set { if (room == value) return; room = value; Notify(); Clear(); } }
        public bool IsBusy => busy;
        public bool CanLoad => !busy;
        public string Status => status;
        public string Details => details;
        public string CurrentRoomsStatus => string.Join("\n", Rooms.Select(r => r.RoomCode + ": " +
            (r.CurrentStatus == "Inactive" ? "Đang khóa" : r.CurrentStatus == "Occupied" ? "Đang có khách" : r.CurrentStatus == "Reserved" ? "Tới lượt đặt" : "Đang trống")));
        public event PropertyChangedEventHandler PropertyChanged;
        public CalendarDayViewModel(CalendarService service, LoginSession session) : this(service, session, new SystemClock()) { }
        public CalendarDayViewModel(CalendarService service, LoginSession session, IClock clock)
        {
            this.service = service; this.session = session; this.clock = clock;
            date = clock.UtcNow.ToOffset(BookingHours.VietnamOffset).Date;
            status = "Đang tải lịch ngày…";
        }
        public async Task LoadAsync()
        {
            if (busy) return; busy = true; Notify(nameof(IsBusy)); Notify(nameof(CanLoad)); Clear();
            try
            {
                if (!Date.HasValue) throw new ArgumentException("Cần chọn ngày xem lịch.");
                var range = week ? CalendarRange.Week(Date.Value) : CalendarRange.Day(Date.Value); var chosen = SelectedRoom?.RoomId;
                var result = await Task.Run(() => service.Read(session, range));
                RoomChoices.Clear(); RoomChoices.Add(new CalendarRoomChoice { Label = "Tất cả phòng" });
                foreach (var item in result) RoomChoices.Add(new CalendarRoomChoice { RoomId = item.RoomId, Label = item.RoomCode + " — " + item.RoomName });
                room = RoomChoices.FirstOrDefault(r => r.RoomId == chosen) ?? RoomChoices[0]; Notify(nameof(SelectedRoom));
                foreach (var item in result.Where(r => !room.RoomId.HasValue || r.RoomId == room.RoomId.Value)) Rooms.Add(item);
                Notify(nameof(CurrentRoomsStatus));
                SetStatus(Rooms.Count == 0 ? "Chưa có phòng. Thêm phòng ở danh mục Phòng để xem lịch." :
                    (week ? "Tuần " + range.Start.ToOffset(BookingHours.VietnamOffset).ToString("dd/MM/yyyy") + " – " + range.End.AddDays(-1).ToOffset(BookingHours.VietnamOffset).ToString("dd/MM/yyyy") : "Lịch ngày " + Date.Value.ToString("dd/MM/yyyy")) + ". Trạng thái hiện tại lúc " + result[0].CheckedAt.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy") + ". Làm mới để kiểm tra thay đổi.");
            }
            catch (UnauthorizedAccessException) { RoomChoices.Clear(); room = null; Notify(nameof(SelectedRoom)); SetStatus("Không còn quyền xem lịch hoặc phiên đã hết hiệu lực. Hãy đóng cửa sổ hoặc đăng nhập lại."); }
            catch (ArgumentException error) { SetStatus(error.Message); }
            catch (Exception) { RoomChoices.Clear(); room = null; Notify(nameof(SelectedRoom)); SetStatus("Không tải được lịch. Hãy thử làm mới."); }
            finally { busy = false; Notify(nameof(IsBusy)); Notify(nameof(CanLoad)); }
        }
        public async Task MoveAsync(int days)
        {
            if (busy) return;
            var current = Date ?? clock.UtcNow.ToOffset(BookingHours.VietnamOffset).Date;
            if ((days < 0 && current.Year <= 2) || (days > 0 && current.Year >= 9998)) { Clear(); SetStatus("Ngày lịch không hợp lệ."); return; }
            Date = current.AddDays(week ? days * 7 : days); await LoadAsync();
        }
        public async Task ChangeModeAsync(bool isWeek)
        {
            if (busy || week == isWeek) return;
            week = isWeek;
            foreach (var property in new[] { nameof(IsWeek), nameof(IsDay), nameof(Heading), nameof(PreviousLabel), nameof(NextLabel) }) Notify(property);
            Clear(); await LoadAsync();
        }
        public async Task TodayAsync() { if (busy) return; Date = clock.UtcNow.ToOffset(BookingHours.VietnamOffset).Date; await LoadAsync(); }
        public void Select(RoomSchedule schedule, RoomScheduleEvent item)
        {
            if (busy || !Rooms.Contains(schedule) || !schedule.Events.Contains(item)) return;
            details = Describe(schedule, item); Notify(nameof(Details));
        }
        public static string Describe(RoomSchedule room, RoomScheduleEvent item)
        {
            var state = item.Kind == "Confirmed" ? "Đã đặt" : item.Kind == "Completed" ? "Đã hoàn tất" : item.Kind == "WalkIn" ? "Walk-in — chưa chốt kết thúc" : "Đang sử dụng";
            var text = room.RoomCode + " — " + room.RoomName + "\n" + state + (item.IsOverdue ? " — QUÁ GIỜ" : "") +
                "\n" + (item.SessionId.HasValue ? "Phiên #" + item.SessionId : "Booking #" + item.ReservationId) +
                "\nKhách: " + item.CustomerName + "\nSĐT: " + item.PhoneNumber + "\nBắt đầu: " + Time(item.Start) +
                "\n" + (item.Kind == "Confirmed" ? "Kết thúc đặt: " : item.Kind == "Completed" ? "Trả phòng: " : "Đã dùng đến: ") + Time(item.End);
            if (item.Kind == "Active" && item.ExpectedEnd.HasValue) text += "\nKết thúc dự kiến: " + Time(item.ExpectedEnd.Value);
            if (item.ReturnBy.HasValue) text += "\nCần trả phòng: " + Time(item.ReturnBy.Value);
            if (item.IsOverdue) text += "\nCần xử lý phiên trước khi nhận khách tiếp theo.";
            return text;
        }
        private static string Time(DateTimeOffset value) => value.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy");
        private void Clear() { Rooms.Clear(); Notify(nameof(CurrentRoomsStatus)); details = "Chọn một lượt trên lịch để xem chi tiết."; Notify(nameof(Details)); SetStatus("Bấm Tải lịch để xem ngày/phòng đã chọn."); }
        private void SetStatus(string value) { status = value; Notify(nameof(Status)); }
        private void Notify([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
