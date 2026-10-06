using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class GuestCalendarViewModel : INotifyPropertyChanged
    {
        private readonly IBookingService service;
        private readonly int roomId;
        private readonly DateTime date;
        private readonly int duration;
        private bool busy;
        private string status, title;
        private PublicBookingSlot selected;
        public ObservableCollection<PublicBookingSlot> Slots { get; } = new ObservableCollection<PublicBookingSlot>();
        public PublicBookingSlot Selected { get => selected; set { selected = value; Notify(); Notify(nameof(CanChoose)); } }
        public string Title => title;
        public string Status => status;
        public bool IsBusy => busy;
        public bool AccessDenied { get; private set; }
        public bool CanClose => !busy;
        public bool CanChoose => !busy && Selected != null && Selected.CanChoose && Slots.Contains(Selected);
        public event PropertyChangedEventHandler PropertyChanged;
        public GuestCalendarViewModel(IBookingService service, int roomId, PublicRoomDay initial)
        { this.service = service; this.roomId = roomId; date = initial.Date; duration = initial.DurationMinutes; Apply(initial); }
        private void Apply(PublicRoomDay result)
        {
            title = result.RoomCode + " — " + result.RoomName + "\n" + result.Date.ToString("dd/MM/yyyy") + " · " + result.DurationMinutes + " phút"; Notify(nameof(Title));
            foreach (var slot in result.Slots) Slots.Add(slot);
            status = "Kiểm tra lúc " + result.CheckedAt.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy") + ". Chưa giữ chỗ; xác nhận đặt sẽ kiểm tra lại."; Notify(nameof(Status));
        }
        public async Task RefreshAsync()
        {
            if (busy) return; busy = true; AccessDenied = false; Slots.Clear(); Selected = null; Changed();
            try { Apply(await Task.Run(() => service.ReadDay(roomId, date, duration))); }
            catch (UnauthorizedAccessException) { AccessDenied = true; status = "Không còn quyền đặt hộ hoặc phiên đã hết hiệu lực. Hãy đóng cửa sổ."; }
            catch (ArgumentException error) { status = error.Message; }
            catch (InvalidOperationException error) { status = error.Message; }
            catch (Exception) { status = "Không tải được lịch. Hãy thử lại hoặc làm mới danh sách phòng."; }
            finally { busy = false; Changed(); }
        }
        private void Changed() { foreach (var property in new[] { nameof(IsBusy), nameof(CanClose), nameof(CanChoose), nameof(Status) }) Notify(property); }
        private void Notify([CallerMemberName] string property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
