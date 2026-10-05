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
        private string phone,lookedUpPhone,status;
        private GuestReservation selected;
        private bool busy,confirming;
        public ObservableCollection<GuestReservation> Items {get;}=new ObservableCollection<GuestReservation>();
        public string PhoneNumber
        {
            get=>phone;
            set{phone=value;lookedUpPhone=null;Items.Clear();Selected=null;confirming=false;Notify();AccessChanged();SetStatus("Nhập SĐT và bấm Tra cứu.");}
        }
        public GuestReservation Selected {get=>selected;set{selected=value;confirming=false;Notify();Notify(nameof(Details));AccessChanged();}}
        public bool IsBusy=>busy;
        public bool CanSearch=>!busy && !confirming;
        public bool CanClose=>!busy;
        public bool CanCancel=>!busy && !confirming && lookedUpPhone!=null && Selected!=null && Selected.CanCancel;
        public bool CanConfirm=>!busy && confirming && lookedUpPhone!=null && Selected!=null;
        public bool IsConfirming=>confirming;
        public string Details=>Selected==null?"Chọn booking để xem điều kiện hủy.":"Booking #"+Selected.ReservationId+" — đã xác nhận. "+Selected.CancelNotice;
        public string Status=>status;
        public event PropertyChangedEventHandler PropertyChanged;
        public GuestLookupViewModel(GuestReservationService service){this.service=service;status="Nhập SĐT đầy đủ để xem booking còn hiệu lực của bạn.";}
        private async Task ReloadAsync(string number)
        {
            var result=await Task.Run(()=>service.Lookup(number));Items.Clear();foreach(var item in result.Items)Items.Add(item);
            lookedUpPhone=number;Selected=null;
        }
        public async Task SearchAsync()
        {
            if(!CanSearch)return;var number=PhoneNumber;SetBusy(true);Items.Clear();Selected=null;lookedUpPhone=null;
            try{await ReloadAsync(number);SetStatus(Items.Count==0?"Không có booking còn hiệu lực cho SĐT này.":"Tìm thấy "+Items.Count+" booking còn hiệu lực. Chọn dòng để xem/hủy.");}
            catch(Exception error){ShowError(error);}
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
            catch(Exception){Items.Clear();Selected=null;lookedUpPhone=null;SetStatus(cancelled?"Đã hủy booking #"+id+", nhưng chưa tải lại được danh sách. Hãy tra cứu lại.":"Chưa tải lại được danh sách. Hãy tra cứu lại.");}
            finally{SetBusy(false);}
            return cancelled;
        }
        private void ShowError(Exception error)
        {
            if(error is ArgumentException || error is InvalidOperationException)SetStatus(error.Message);
            else if(error is SQLiteException sqlite && (sqlite.ResultCode==SQLiteErrorCode.Busy || sqlite.ResultCode==SQLiteErrorCode.Locked))SetStatus("Dữ liệu đang bận. Hãy thử lại sau.");
            else SetStatus("Không xử lý được tra cứu/hủy. Thay đổi chưa được lưu; hãy thử lại.");
        }
        private void SetBusy(bool value){busy=value;Notify(nameof(IsBusy));AccessChanged();}
        private void AccessChanged(){foreach(var p in new[]{nameof(CanSearch),nameof(CanClose),nameof(CanCancel),nameof(CanConfirm),nameof(IsConfirming)})Notify(p);}
        private void SetStatus(string value){status=value;Notify(nameof(Status));}
        private void Notify([CallerMemberName]string property=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(property));
    }
}
