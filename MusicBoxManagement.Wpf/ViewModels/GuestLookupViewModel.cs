using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class GuestLookupViewModel:INotifyPropertyChanged
    {
        private readonly GuestReservationService service;
        private readonly GuestSessionService sessions;
        private GuestSession activeSession;
        private SessionExtensionCheck extension;
        private int extensionMinutes=30, confirmedMinutes;
        private string phone,lookedUpPhone,status;
        private GuestReservation selected;
        private bool busy,confirming;
        public ObservableCollection<GuestReservation> Items {get;}=new ObservableCollection<GuestReservation>();
        public string PhoneNumber
        {
            get=>phone;
            set{if(busy)return;phone=value;ClearResults();Notify();AccessChanged();SetStatus("Nhập SĐT và bấm Tra cứu.");}
        }
        public GuestReservation Selected {get=>selected;set{selected=value;confirming=false;Notify();Notify(nameof(Details));AccessChanged();}}
        public bool IsBusy=>busy;
        public bool CanSearch=>!busy && !confirming && extension==null;
        public bool CanClose=>!busy;
        public bool CanCancel=>CanSearch && lookedUpPhone!=null && Selected!=null && Selected.CanCancel;
        public bool CanConfirm=>!busy && confirming && lookedUpPhone!=null && Selected!=null;
        public bool IsConfirming=>confirming;
        public string Details=>Selected==null?"Chọn booking để xem điều kiện hủy.":"Booking #"+Selected.ReservationId+" — đã xác nhận. "+Selected.CancelNotice;
        public string Status=>status;
        public GuestSession ActiveSession=>activeSession;
        public bool ShowExtensionControls=>activeSession!=null && activeSession.FromBooking;
        public bool CanExtend=>CanSearch && lookedUpPhone!=null && activeSession!=null && activeSession.CanExtend;
        public bool IsExtensionConfirming=>extension!=null;
        public bool CanConfirmExtension=>!busy && extension!=null && lookedUpPhone!=null && activeSession!=null;
        public int[] ExtensionChoices=>new[]{30,60};
        public int ExtensionMinutes {get=>extensionMinutes;set{if(!CanSearch)return;extensionMinutes=value;Notify();}}
        public string SessionDetails=>activeSession==null?"Không có phiên đang sử dụng cho SĐT đã tra cứu.":
            "Phiên #"+activeSession.SessionId+" — phòng "+activeSession.RoomCode+" · "+activeSession.RoomTypeName+
            "\nGiá theo giờ đã chốt: "+activeSession.HourlyRate.ToString("N0")+" đ/giờ"+
            "\nNhận phòng: "+TimeLabel(activeSession.ActualStartTime)+
            "\n"+(activeSession.FromBooking?"Trả dự kiến: ":"Cần trả trước: ")+TimeLabel(activeSession.ReturnBy)+
            "\n"+(!activeSession.FromBooking?"Khách trực tiếp không có gia hạn.":activeSession.CanExtend?"Có thể chọn gia hạn 30 hoặc 60 phút.":"Phiên đã quá giờ dự kiến. Vui lòng liên hệ nhân viên.");
        public string ExtensionDetails=>extension==null?"":"Gia hạn "+confirmedMinutes+" phút.\nGiờ trả hiện tại: "+TimeLabel(extension.ExpectedEndTime)+
            "\nGiờ trả mới: "+TimeLabel(extension.NewEndTime)+"\nGiới hạn sử dụng: "+TimeLabel(extension.MaximumEndTime)+"\nLịch sẽ được kiểm tra lại khi xác nhận.";
        public event PropertyChangedEventHandler PropertyChanged;
        public GuestLookupViewModel(GuestReservationService service){this.service=service;this.sessions=service.ForSessions();status="Nhập SĐT đầy đủ để xem booking và phiên đang sử dụng của bạn.";}
        private async Task ReloadAsync(string number)
        {
            var result=await Task.Run(()=>service.Lookup(number));Items.Clear();foreach(var item in result.Items)Items.Add(item);
            lookedUpPhone=number;Selected=null;activeSession=result.ActiveSession;SessionChanged();
        }
        public async Task SearchAsync()
        {
            if(!CanSearch)return;var number=PhoneNumber;SetBusy(true);ClearResults();
            try{await ReloadAsync(number);SetStatus(Items.Count==0 && activeSession==null?"Không có booking còn hiệu lực hoặc phiên đang sử dụng cho SĐT này.":"Tìm thấy "+Items.Count+" booking còn hiệu lực"+(activeSession==null?" và không có phiên đang sử dụng.":" và 1 phiên đang sử dụng. Mở tab Đang sử dụng để xem/gia hạn."));}
            catch(Exception error){ClearResults();ShowError(error);}
            finally{SetBusy(false);}
        }
        public void RequestCancel(){if(!CanCancel)return;confirming=true;AccessChanged();SetStatus("Bạn muốn hủy booking #"+Selected.ReservationId+"? Bấm Đồng ý hủy để xác nhận.");}
        public void KeepBooking(){if(busy)return;confirming=false;AccessChanged();SetStatus("Đã giữ booking, chưa thay đổi dữ liệu.");}
        public async Task<bool> ConfirmCancelAsync()
        {
            if(!CanConfirm)return false;var id=Selected.ReservationId;var number=lookedUpPhone;SetBusy(true);
            var cancelled=false;
            try{await Task.Run(()=>service.Cancel(number,id));cancelled=true;SetStatus("Đã hủy booking #"+id+".");}
            catch(Exception error){ShowError(error);}
            confirming=false;
            // Never suggest cancelling again when the commit succeeded but refreshing failed.
            try{await ReloadAsync(number);}
            catch(Exception){ClearResults();SetStatus(cancelled?"Đã hủy booking #"+id+", nhưng chưa tải lại được danh sách. Hãy tra cứu lại.":"Chưa tải lại được danh sách. Hãy tra cứu lại.");}
            finally{SetBusy(false);}
            return cancelled;
        }
        public async Task PreviewExtensionAsync()
        {
            if(!CanExtend)return;var number=lookedUpPhone;var current=activeSession;var minutes=extensionMinutes;SetBusy(true);
            try
            {
                var check=await Task.Run(()=>sessions.PreviewExtension(number,current.SessionId,minutes));
                if(check.ExpectedEndTime!=current.ExpectedEndTime){ClearResults();SetStatus("Giờ trả dự kiến đã thay đổi. Hãy tra cứu lại.");return;}
                if(!check.CanExtend){ClearResults();SetStatus(check.Reason+" Hãy tra cứu lại.");return;}
                extension=check;confirmedMinutes=minutes;SessionChanged();SetStatus("Kiểm tra giờ trả mới rồi bấm Xác nhận gia hạn, hoặc Giữ giờ trả.");
            }
            catch(Exception error){ClearResults();ShowError(error);}
            finally{SetBusy(false);}
        }
        public void KeepSession(){if(busy)return;extension=null;SessionChanged();SetStatus("Đã giữ giờ trả, chưa gia hạn.");}
        public async Task<bool> ConfirmExtensionAsync()
        {
            if(!CanConfirmExtension)return false;var number=lookedUpPhone;var id=activeSession.SessionId;var observed=extension.ExpectedEndTime;var minutes=confirmedMinutes;
            SetBusy(true);var saved=false;
            try
            {
                await Task.Run(()=>sessions.Extend(number,id,observed,minutes));saved=true;extension=null;SessionChanged();
                try{await ReloadAsync(number);SetStatus("Đã gia hạn phiên #"+id+" thêm "+minutes+" phút.");}
                catch(Exception){ClearResults();SetStatus("Đã gia hạn phiên #"+id+", nhưng chưa tải lại được kết quả. Hãy tra cứu lại.");}
            }
            catch(Exception error){ClearResults();ShowError(error);}
            finally{SetBusy(false);}
            return saved;
        }
        private void ClearResults(){lookedUpPhone=null;Items.Clear();Selected=null;confirming=false;activeSession=null;extension=null;SessionChanged();}
        private void SessionChanged(){Notify(nameof(ActiveSession));Notify(nameof(ShowExtensionControls));Notify(nameof(SessionDetails));Notify(nameof(ExtensionDetails));AccessChanged();}
        private static string TimeLabel(DateTimeOffset value)=>value.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy");
        private void ShowError(Exception error)
        {
            if(error is ArgumentException || error is InvalidOperationException)SetStatus(error.Message);
            else if(error is SQLiteException sqlite && (sqlite.ResultCode==SQLiteErrorCode.Busy || sqlite.ResultCode==SQLiteErrorCode.Locked))SetStatus("Dữ liệu đang bận. Hãy thử lại sau.");
            else SetStatus("Không xử lý được tra cứu/hủy/gia hạn. Thay đổi chưa được lưu; hãy thử lại.");
        }
        private void SetBusy(bool value){busy=value;Notify(nameof(IsBusy));AccessChanged();}
        private void AccessChanged(){foreach(var p in new[]{nameof(CanSearch),nameof(CanClose),nameof(CanCancel),nameof(CanConfirm),nameof(IsConfirming),nameof(CanExtend),nameof(IsExtensionConfirming),nameof(CanConfirmExtension)})Notify(p);}
        private void SetStatus(string value){status=value;Notify(nameof(Status));}
        private void Notify([CallerMemberName]string property=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(property));
    }
}
