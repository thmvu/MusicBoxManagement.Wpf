using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class GuestBookingViewModel : INotifyPropertyChanged
    {
        private readonly GuestBookingService service;
        private PublicRoom selectedRoom;
        private DateTime? date;
        private string time, fullName, phone, status, preview;
        private int duration = 60;
        private bool busy, saved;
        public ObservableCollection<PublicRoom> Rooms { get; } = new ObservableCollection<PublicRoom>();
        public IReadOnlyList<int> Durations => BookingHours.InitialDurations;
        public IReadOnlyList<string> Slots { get; } = Enumerable.Range(18,27).Where(i => i<=22 || i>=26).Select(i=>TimeSpan.FromMinutes(i*30).ToString(@"hh\:mm")).ToArray();
        public DateTime FirstDate { get; }
        public DateTime LastDate => FirstDate.AddDays(30);
        public PublicRoom SelectedRoom { get => selectedRoom; set { selectedRoom=value; Changed(); Notify(nameof(CanBook)); } }
        public DateTime? SelectedDate { get => date; set { date=value; Changed(); } }
        public string StartTimeText { get => time; set { time=value; Changed(); } }
        public int Duration { get => duration; set { duration=value; Changed(); } }
        public string FullName { get => fullName; set { fullName=value; Changed(); } }
        public string PhoneNumber { get => phone; set { phone=value; Changed(); } }
        public bool IsBusy => busy;
        public bool CanReset => !busy;
        public bool CanInput => !busy && !saved;
        public bool CanBook => CanInput && SelectedRoom!=null;
        public string Status => status;
        public string PreviewText => preview;
        public event PropertyChangedEventHandler PropertyChanged;
        public GuestBookingViewModel(GuestBookingService service) : this(service,new SystemClock()) { }
        public GuestBookingViewModel(GuestBookingService service,IClock clock)
        {
            this.service=service; var now=clock.UtcNow.ToOffset(BookingHours.VietnamOffset); FirstDate=now.Date; date=FirstDate;
            time=Slots.FirstOrDefault(slot=> FirstDate.Add(TimeSpan.Parse(slot,CultureInfo.InvariantCulture))>=now.DateTime);
            if(time==null){date=FirstDate.AddDays(1);time="09:00";}
            preview="Nhập SĐT để kiểm tra cả lịch phòng và lịch của bạn.";
        }
        private void Changed([CallerMemberName] string property=null)
        { Notify(property); preview="Kiểm tra giờ đã chọn trước khi đặt. Lịch sẽ được kiểm tra lại lúc lưu."; Notify(nameof(PreviewText)); }
        public async Task RefreshAsync()
        {
            if(!CanInput)return; SetBusy(true);
            try { var id=SelectedRoom?.RoomId; var rooms=await Task.Run(()=>service.ListRooms()); Rooms.Clear();foreach(var room in rooms)Rooms.Add(room);
                SelectedRoom=Rooms.FirstOrDefault(r=>r.RoomId==id)??Rooms.FirstOrDefault(); SetStatus(Rooms.Count==0?"Chưa có phòng đang mở để đặt. Vui lòng liên hệ nhân viên.":"Chọn phòng, ngày/giờ và nhập thông tin đặt phòng."); }
            catch(Exception){Rooms.Clear();SelectedRoom=null;SetStatus("Không tải được phòng. Hãy thử làm mới.");}
            finally{SetBusy(false);}
        }
        private ReservationRequest Request()
        {
            if(SelectedRoom==null||!SelectedDate.HasValue)throw new ArgumentException("Cần chọn phòng và ngày đặt.");
            if(!Slots.Contains(StartTimeText))throw new ArgumentException("Cần chọn giờ bắt đầu trong danh sách.");
            return new ReservationRequest{RoomId=SelectedRoom.RoomId,StartTime=new DateTimeOffset(DateTime.SpecifyKind(SelectedDate.Value.Date.Add(TimeSpan.Parse(StartTimeText,CultureInfo.InvariantCulture)),DateTimeKind.Unspecified),BookingHours.VietnamOffset),
                DurationMinutes=Duration,FullName=FullName,PhoneNumber=PhoneNumber};
        }
        public async Task PreviewAsync()
        {
            if(!CanBook)return; SetBusy(true);
            try{var input=Request();var result=await Task.Run(()=>service.Preview(input));preview=result.Reason+" (Kiểm tra lúc "+result.CheckedAt.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss")+").";Notify(nameof(PreviewText));SetStatus(result.CanBook?"Có thể đặt khoảng này tại thời điểm kiểm tra.":"Khoảng đã chọn chưa đặt được.");}
            catch(Exception error){ShowError(error);}
            finally{SetBusy(false);}
        }
        public async Task<bool> SubmitAsync()
        {
            if(!CanBook)return false; SetBusy(true);
            try{var input=Request();var roomCode=SelectedRoom.RoomCode;var result=await Task.Run(()=>service.Create(input));saved=true;
                preview="Đã xác nhận, không thu cọc. Bạn cần đến nhận phòng trước "+result.StartTime.AddMinutes(15).ToOffset(BookingHours.VietnamOffset).ToString("HH:mm dd/MM/yyyy")+".";Notify(nameof(PreviewText));
                SetStatus("Đặt phòng thành công — mã #"+result.ReservationId+", "+roomCode+", "+result.StartTime.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm dd/MM/yyyy")+" đến "+result.EndTime.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm")+". Trạng thái: Confirmed (đã xác nhận).");return true;}
            catch(Exception error){ShowError(error);return false;}
            finally{SetBusy(false);}
        }
        public void StartNew(){if(busy)return;saved=false;FullName=PhoneNumber=null;SetStatus("Nhập thông tin cho lượt đặt mới.");SetBusy(false);}
        private void ShowError(Exception error)
        {
            preview="Hãy kiểm tra lại thông tin hoặc chọn khoảng giờ khác.";Notify(nameof(PreviewText));
            if(error is ArgumentException || error is InvalidOperationException)SetStatus(error.Message);
            else if(error is SQLiteException sqlite && (sqlite.ResultCode==SQLiteErrorCode.Busy || sqlite.ResultCode==SQLiteErrorCode.Locked))SetStatus("Dữ liệu đang bận. Hãy thử lại sau.");
            else SetStatus("Không xử lý được đặt phòng. Hãy thử lại hoặc liên hệ nhân viên.");
        }
        private void SetBusy(bool value){busy=value;foreach(var p in new[]{nameof(IsBusy),nameof(CanReset),nameof(CanInput),nameof(CanBook)})Notify(p);}
        private void SetStatus(string value){status=value;Notify(nameof(Status));}
        private void Notify([CallerMemberName]string property=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(property));
    }
}
