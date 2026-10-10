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
    public sealed class StaffOrderCreateViewModel : INotifyPropertyChanged
    {
        private readonly OrderService service;
        private readonly LoginSession actor;
        private readonly int sessionId;
        private bool busy, allowed, confirming;
        private int quantity = 1;
        private OrderMenuItem selectedMenu;
        private ServiceOrderItem selectedCart;
        private OrderLineRequest[] draft;
        private string status = "Đang tải menu…", confirmationText, receipt;
        public ObservableCollection<OrderMenuItem> Menu { get; } = new ObservableCollection<OrderMenuItem>();
        public ObservableCollection<ServiceOrderItem> Cart { get; } = new ObservableCollection<ServiceOrderItem>();
        public string Title { get; }
        public int[] Quantities { get; } = Enumerable.Range(1, 10).ToArray();
        public int Quantity { get => quantity; set { if (!CanEdit) return; quantity = value; Notify(); } }
        public OrderMenuItem SelectedMenu { get => selectedMenu; set { if (!CanEdit) return; selectedMenu = value; Notify(); AccessChanged(); } }
        public ServiceOrderItem SelectedCart { get => selectedCart; set { if (!CanEdit) return; selectedCart = value; Notify(); AccessChanged(); } }
        public bool IsBusy => busy;
        public bool CanClose => !busy;
        public bool CanEdit => !busy && !confirming;
        public bool CanAdd => CanEdit && allowed && selectedMenu != null;
        public bool CanRemove => CanEdit && selectedCart != null;
        public bool CanPreview => CanEdit && allowed && Cart.Count > 0;
        public bool IsConfirming => confirming;
        public bool CanConfirm => !busy && confirming && draft != null;
        public string Status => status;
        public string ConfirmationText => confirmationText;
        public string Receipt => receipt;
        public decimal CartAmount => Cart.Sum(i => (decimal)i.UnitPrice * i.Quantity);
        public event PropertyChangedEventHandler PropertyChanged;
        public StaffOrderCreateViewModel(OrderService service, LoginSession actor, OrderSessionChoice session)
        { this.service = service; this.actor = actor; sessionId = session.SessionId; Title = "Ghi món đã phục vụ — " + session.RoomCode + " · phiên #" + sessionId; }
        public async Task ReloadAsync()
        {
            if (!CanEdit) return; Busy(true); Clear();
            try { await LoadAsync(); SetStatus(allowed ? "Chỉ ghi đơn hộ cho món đã được phục vụ." : "Ngoài ca nhận món mới; chưa thể tạo đơn hộ."); }
            catch (Exception error) { Fail(error); }
            finally { Busy(false); }
        }
        private async Task LoadAsync()
        {
            var menu = await Task.Run(() => service.ReadMenuStaff(actor, sessionId));
            selectedMenu = null; Notify(nameof(SelectedMenu)); Menu.Clear();
            foreach (var item in menu.Items) Menu.Add(item);
            allowed = menu.CanCreate; AccessChanged();
        }
        public void Add()
        {
            if (!CanAdd) return;
            var old = Cart.FirstOrDefault(i => i.ServiceId == selectedMenu.ServiceId);
            var total = (old?.Quantity ?? 0) + quantity;
            if (quantity < 1 || quantity > 10 || total > 10) { SetStatus("Tổng số lượng mỗi món từ 1 đến 10."); return; }
            if (old != null) Cart.Remove(old);
            Cart.Add(new ServiceOrderItem { ServiceId = selectedMenu.ServiceId, ServiceNameSnapshot = selectedMenu.Name, Quantity = total, UnitPrice = selectedMenu.Price });
            CartChanged(); SetStatus("Đã thêm vào giỏ, chưa ghi đơn.");
        }
        public void Remove()
        { if (!CanRemove) return; Cart.Remove(selectedCart); selectedCart = null; Notify(nameof(SelectedCart)); CartChanged(); }
        public async Task PreviewAsync()
        {
            if (!CanPreview) return;
            var lines = Cart.Select(i => new OrderLineRequest { ServiceId = i.ServiceId, Quantity = i.Quantity }).ToArray(); Busy(true);
            try
            {
                var preview = await Task.Run(() => service.PreviewStaff(actor, sessionId, lines));
                selectedCart = null; Notify(nameof(SelectedCart)); Cart.Clear(); foreach (var item in preview.Items) Cart.Add(item);
                draft = preview.Items.Select(i => new OrderLineRequest { ServiceId = i.ServiceId, Quantity = i.Quantity }).ToArray(); confirming = true;
                confirmationText = "Xác nhận những món này đã được phục vụ?\nGiá trị giỏ: " + preview.Amount.ToString("N0") + " đ.\nĐơn được ghi Đã phục vụ ngay. Giá, quyền và phiên được kiểm tra lại khi lưu.";
                Notify(nameof(ConfirmationText)); CartChanged(); SetStatus("Chỉ xác nhận sau khi đã phục vụ món.");
            }
            catch (Exception error) { Fail(error); }
            finally { Busy(false); }
        }
        public void Keep() { if (busy) return; confirming = false; draft = null; AccessChanged(); SetStatus("Chưa ghi đơn; giỏ được giữ lại."); }
        public async Task<bool> ConfirmAsync()
        {
            if (!CanConfirm) return false; var lines = draft; Busy(true); var saved = false;
            try
            {
                var result = await Task.Run(() => service.CreateStaff(actor, sessionId, lines)); saved = true;
                Clear(); receipt = "Đơn #" + result.OrderId + " — Đã phục vụ\n" + string.Join("\n", result.Items.Select(i => i.ServiceNameSnapshot + " × " + i.Quantity + " · " + i.UnitPrice.ToString("N0") + " đ/món")); Notify(nameof(Receipt));
                try { await LoadAsync(); SetStatus("Đã ghi đơn #" + result.OrderId + " cho món đã phục vụ."); }
                catch (Exception) { Clear(); SetStatus("Đã ghi đơn #" + result.OrderId + " cho món đã phục vụ, nhưng chưa tải lại được menu. Hãy tải lại trước khi tạo đơn khác."); }
            }
            catch (Exception error) { Fail(error); }
            finally { Busy(false); }
            return saved;
        }
        private void Clear()
        {
            allowed = confirming = false; draft = null; receipt = confirmationText = null; selectedMenu = null; selectedCart = null;
            Menu.Clear(); Cart.Clear(); foreach (var p in new[] { nameof(SelectedMenu), nameof(SelectedCart), nameof(Receipt), nameof(ConfirmationText) }) Notify(p); CartChanged();
        }
        private void Fail(Exception error) { Clear(); SetStatus(error is ArgumentException || error is InvalidOperationException || error is UnauthorizedAccessException ? error.Message : "Không xử lý được đơn hộ. Hãy tải lại."); }
        private void Busy(bool value) { busy = value; Notify(nameof(IsBusy)); AccessChanged(); }
        private void SetStatus(string value) { status = value; Notify(nameof(Status)); }
        private void CartChanged() { Notify(nameof(CartAmount)); AccessChanged(); }
        private void AccessChanged() { foreach (var p in new[] { nameof(CanClose), nameof(CanEdit), nameof(CanAdd), nameof(CanRemove), nameof(CanPreview), nameof(IsConfirming), nameof(CanConfirm) }) Notify(p); }
        private void Notify([CallerMemberName] string property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
