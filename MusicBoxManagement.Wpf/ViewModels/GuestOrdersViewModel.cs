using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class GuestOrdersViewModel : INotifyPropertyChanged
    {
        private readonly OrderService service;
        private readonly string phone;
        private readonly int sessionId;
        private bool busy, valid, canCreate;
        private string status, confirmation;
        private OrderLineRequest[] draft;
        private int cancelId, quantity=1;
        private OrderMenuItem selectedMenu;
        private ServiceOrderItem selectedCart;
        private ServiceOrder selectedOrder;
        public ObservableCollection<OrderMenuItem> Menu { get; } = new ObservableCollection<OrderMenuItem>();
        public ObservableCollection<ServiceOrderItem> Cart { get; } = new ObservableCollection<ServiceOrderItem>();
        public ObservableCollection<ServiceOrder> Orders { get; } = new ObservableCollection<ServiceOrder>();
        public string Title { get; }
        public int[] Quantities => Enumerable.Range(1,10).ToArray();
        public int Quantity { get=>quantity; set { if(!CanEdit)return;quantity=value;Notify(); } }
        public OrderMenuItem SelectedMenu { get=>selectedMenu; set { if(!CanEdit)return;selectedMenu=value;Notify();AccessChanged(); } }
        public ServiceOrderItem SelectedCart { get=>selectedCart; set { if(!CanEdit)return;selectedCart=value;Notify();AccessChanged(); } }
        public ServiceOrder SelectedOrder { get=>selectedOrder; set { if(!CanEdit)return;selectedOrder=value;Notify();Notify(nameof(OrderDetails));AccessChanged(); } }
        public bool IsBusy=>busy;
        public bool CanClose=>!busy;
        public bool CanEdit=>!busy && confirmation==null;
        public bool CanAdd=>CanEdit && valid && canCreate && selectedMenu!=null;
        public bool CanRemove=>CanEdit && selectedCart!=null;
        public bool CanPreview=>CanEdit && valid && canCreate && Cart.Count>0;
        public bool CanCancel=>CanEdit && valid && selectedOrder!=null && selectedOrder.Status=="Pending";
        public bool IsConfirming=>confirmation!=null;
        public bool CanConfirm=>!busy && confirmation!=null;
        public string Status=>status;
        public string ConfirmationText { get; private set; }
        public decimal CartAmount=>Cart.Sum(i=>(decimal)i.UnitPrice*i.Quantity);
        public string OrderDetails=>selectedOrder==null?"Chọn đơn để xem món và trạng thái.":
            "Đơn #"+selectedOrder.OrderId+" — "+StateLabel(selectedOrder.Status)+"\n"+
            string.Join("\n",selectedOrder.Items.Select(i=>i.ServiceNameSnapshot+" × "+i.Quantity+" · "+i.UnitPrice.ToString("N0")+" đ/món"));
        public event PropertyChangedEventHandler PropertyChanged;
        public GuestOrdersViewModel(OrderService service,string phone,int sessionId,string roomCode)
        { this.service=service;this.phone=PhoneNumberNormalizer.Normalize(phone);this.sessionId=sessionId;Title="Gọi món — phòng "+roomCode;status="Đang tải menu và đơn của phiên…"; }
        public async Task ReloadAsync()
        {
            if(!CanEdit)return;SetBusy(true);Clear();
            try{await LoadAsync();SetStatus("Đã tải menu và đơn của phiên. "+(canCreate?"Chọn món, số lượng rồi thêm vào giỏ.":"Hiện ngoài ca nhận món mới."));}
            catch(Exception error){Clear();ShowError(error);}
            finally{SetBusy(false);}
        }
        private async Task LoadAsync(int? selectId=null)
        {
            var result=await Task.Run(()=>Tuple.Create(service.ReadMenuGuest(phone,sessionId),service.ListGuest(phone,sessionId)));
            selectedMenu=null;selectedCart=null;Notify(nameof(SelectedMenu));Notify(nameof(SelectedCart));
            Menu.Clear();foreach(var item in result.Item1.Items)Menu.Add(item);
            Orders.Clear();foreach(var item in result.Item2)Orders.Add(item);
            valid=true;canCreate=result.Item1.CanCreate;selectedOrder=selectId.HasValue?Orders.FirstOrDefault(o=>o.OrderId==selectId):null;
            Notify(nameof(SelectedOrder));Notify(nameof(OrderDetails));AccessChanged();
        }
        public void Add()
        {
            if(!CanAdd)return;
            var old=Cart.FirstOrDefault(i=>i.ServiceId==selectedMenu.ServiceId);var count=(old?.Quantity??0)+quantity;
            if(quantity<1 || quantity>10 || count>10){SetStatus("Tổng số lượng mỗi món từ 1 đến 10.");return;}
            if(old!=null)Cart.Remove(old);
            Cart.Add(new ServiceOrderItem{ServiceId=selectedMenu.ServiceId,ServiceNameSnapshot=selectedMenu.Name,Quantity=count,UnitPrice=selectedMenu.Price});CartChanged();SetStatus("Đã thêm món vào giỏ, chưa gửi đơn.");
        }
        public void Remove(){if(!CanRemove)return;Cart.Remove(selectedCart);selectedCart=null;Notify(nameof(SelectedCart));CartChanged();}
        public async Task PreviewAsync()
        {
            if(!CanPreview)return;var input=Cart.Select(i=>new OrderLineRequest{ServiceId=i.ServiceId,Quantity=i.Quantity}).ToArray();SetBusy(true);
            try
            {
                var result=await Task.Run(()=>service.PreviewGuest(phone,sessionId,input));selectedCart=null;Notify(nameof(SelectedCart));Cart.Clear();foreach(var item in result.Items)Cart.Add(item);CartChanged();
                draft=result.Items.Select(i=>new OrderLineRequest{ServiceId=i.ServiceId,Quantity=i.Quantity}).ToArray();confirmation="Send";
                ConfirmationText="Gửi đơn chờ phục vụ?\nGiá trị giỏ hiện tại: "+result.Amount.ToString("N0")+" đ.\nGiá và tình trạng bán sẽ được kiểm tra lại khi gửi. Món chờ phục vụ chưa được tính tiền.";Notify(nameof(ConfirmationText));
                SetStatus("Kiểm tra giỏ rồi xác nhận gửi hoặc quay lại.");
            }
            catch(Exception error){Clear();ShowError(error);}
            finally{SetBusy(false);}
        }
        public void RequestCancel()
        {
            if(!CanCancel)return;cancelId=selectedOrder.OrderId;confirmation="Cancel";ConfirmationText="Hủy đơn #"+cancelId+" đang chờ phục vụ?";
            Notify(nameof(ConfirmationText));AccessChanged();
        }
        public void Keep(){if(busy)return;confirmation=null;draft=null;AccessChanged();SetStatus("Chưa thay đổi đơn món.");}
        public async Task<bool> ConfirmAsync()
        {
            if(!CanConfirm)return false;var kind=confirmation;var lines=draft;var id=cancelId;SetBusy(true);var saved=false;
            try
            {
                var result=await Task.Run(()=>kind=="Send"?service.CreateGuest(phone,sessionId,lines):service.CancelGuest(phone,id));saved=true;
                confirmation=null;draft=null;Cart.Clear();CartChanged();
                var message=kind=="Send"?"Đã gửi đơn #"+result.OrderId+" chờ phục vụ.":"Đã hủy đơn #"+result.OrderId+".";
                try{await LoadAsync(result.OrderId);SetStatus(message);}
                catch(Exception){Clear();SetStatus(message+" Chưa tải lại được kết quả; hãy tải lại hoặc quay về tra cứu.");}
            }
            catch(Exception error){Clear();ShowError(error);}
            finally{SetBusy(false);}
            return saved;
        }
        private void Clear()
        {
            valid=false;canCreate=false;confirmation=null;draft=null;selectedMenu=null;selectedCart=null;selectedOrder=null;
            Menu.Clear();Cart.Clear();Orders.Clear();Notify(nameof(SelectedMenu));Notify(nameof(SelectedCart));Notify(nameof(SelectedOrder));Notify(nameof(OrderDetails));CartChanged();
        }
        private void CartChanged(){Notify(nameof(CartAmount));AccessChanged();}
        private void SetBusy(bool value){busy=value;Notify(nameof(IsBusy));AccessChanged();}
        private void SetStatus(string value){status=value;Notify(nameof(Status));}
        private void ShowError(Exception error){SetStatus(error is ArgumentException || error is InvalidOperationException?error.Message:"Không xử lý được đơn món. Hãy tải lại hoặc quay về tra cứu.");}
        private void AccessChanged(){foreach(var p in new[]{nameof(CanClose),nameof(CanEdit),nameof(CanAdd),nameof(CanRemove),nameof(CanPreview),nameof(CanCancel),nameof(IsConfirming),nameof(CanConfirm)})Notify(p);}
        private static string StateLabel(string value)=>value=="Pending"?"Chờ phục vụ":value=="Completed"?"Đã phục vụ":"Đã hủy";
        private void Notify([CallerMemberName]string property=null)=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(property));
    }
}
