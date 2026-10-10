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
    public sealed class StaffOrdersViewModel : INotifyPropertyChanged
    {
        private readonly OrderService service;
        private readonly LoginSession actor;
        private bool busy, canView, canCreate, canConfirm, canCancel;
        private OrderSessionChoice selectedSession;
        private ServiceOrder selectedOrder;
        private string confirmation, notice, status = "Đang tải phiên và quyền đơn món…";
        private int confirmedId;
        public ObservableCollection<OrderSessionChoice> Sessions { get; } = new ObservableCollection<OrderSessionChoice>();
        public ObservableCollection<ServiceOrder> Orders { get; } = new ObservableCollection<ServiceOrder>();
        public OrderSessionChoice SelectedSession { get => selectedSession; set { if (!CanEdit) return; selectedSession = value; ClearOrders(); Notify(); AccessChanged(); } }
        public ServiceOrder SelectedOrder { get => selectedOrder; set { if (!CanEdit) return; selectedOrder = value; confirmation = null; notice = null; Notify(); Notify(nameof(Details)); Notify(nameof(Notice)); AccessChanged(); } }
        public bool IsBusy => busy;
        public bool CanClose => !busy;
        public bool CanEdit => !busy && confirmation == null;
        public bool HasView => canView;
        public bool CanSelectOrder => CanEdit && canView;
        public bool CanCreate => CanEdit && canCreate && selectedSession != null && selectedSession.IsActive;
        private bool Pending => CanSelectOrder && selectedSession != null && selectedSession.IsActive && selectedOrder != null && selectedOrder.Status == "Pending";
        public bool CanServe => Pending && canConfirm;
        public bool CanCancel => Pending && canCancel;
        public bool IsConfirming => confirmation != null;
        public bool CanConfirm => !busy && confirmation != null;
        public string Notice => notice;
        public string Status => status;
        public string Details => selectedOrder == null ? (canView ? "Chọn đơn để xem món đã chốt." : "Không có quyền xem đơn. Có thể tạo đơn hộ nếu được cấp Order.Create.") :
            "Đơn #" + selectedOrder.OrderId + " — " + (selectedOrder.Status == "Pending" ? "Chờ phục vụ" : selectedOrder.Status == "Completed" ? "Đã phục vụ" : "Đã hủy") + "\n" +
            "Gửi lúc: " + selectedOrder.CreatedAt.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy") + "\n" +
            string.Join("\n", selectedOrder.Items.Select(i => i.ServiceNameSnapshot + " × " + i.Quantity + " · " + i.UnitPrice.ToString("N0") + " đ/món"));
        public event PropertyChangedEventHandler PropertyChanged;
        public StaffOrdersViewModel(OrderService service, LoginSession actor) { this.service = service; this.actor = actor; }
        public async Task ReloadAsync()
        {
            if (!CanEdit) return; var id = selectedSession?.SessionId; Busy(true); Clear();
            try { await LoadAsync(id); SetStatus(Sessions.Count == 0 ? "Không có phiên phù hợp." : "Đã tải phiên và quyền hiện hành. Chọn phiên để xem đơn hoặc tạo hộ món đã phục vụ."); }
            catch (Exception error) { Fail(error); }
            finally { Busy(false); }
        }
        public async Task LoadSelectedAsync()
        {
            if (!CanEdit || selectedSession == null) return; var id = selectedSession.SessionId; Busy(true); Clear();
            try { await LoadAsync(id); SetStatus("Đã tải đơn của phiên đã chọn theo quyền hiện hành."); }
            catch (Exception error) { Fail(error); }
            finally { Busy(false); }
        }
        private async Task LoadAsync(int? sessionId, int? orderId = null)
        {
            var result = await Task.Run(() =>
            {
                var workspace = service.ReadWorkspaceStaff(actor);
                var choice = sessionId.HasValue ? workspace.Sessions.FirstOrDefault(s => s.SessionId == sessionId) : workspace.Sessions.FirstOrDefault();
                if (sessionId.HasValue && choice == null) throw new InvalidOperationException("Phiên đã thay đổi hoặc không còn quyền truy cập. Hãy tải lại.");
                var items = workspace.CanView && choice != null ? service.ListStaff(actor, choice.SessionId) : new System.Collections.Generic.List<ServiceOrder>();
                return Tuple.Create(workspace, choice, items);
            });
            Sessions.Clear(); foreach (var item in result.Item1.Sessions) Sessions.Add(item);
            selectedSession = result.Item2; canView = result.Item1.CanView; canCreate = result.Item1.CanCreate;
            canConfirm = result.Item1.CanConfirm; canCancel = result.Item1.CanCancel;
            Orders.Clear(); foreach (var item in result.Item3) Orders.Add(item);
            selectedOrder = orderId.HasValue ? Orders.FirstOrDefault(o => o.OrderId == orderId) : null;
            foreach (var p in new[] { nameof(SelectedSession), nameof(SelectedOrder), nameof(HasView), nameof(Details) }) Notify(p); AccessChanged();
        }
        public void RequestServe() { if (!CanServe) return; Prepare("Serve", "Xác nhận đơn #" + selectedOrder.OrderId + " đã được phục vụ? Chỉ xác nhận sau khi giao món."); }
        public void RequestCancel() { if (!CanCancel) return; Prepare("Cancel", "Hủy đơn #" + selectedOrder.OrderId + " đang chờ phục vụ?"); }
        private void Prepare(string kind, string text) { confirmedId = selectedOrder.OrderId; confirmation = kind; notice = text; Notify(nameof(Notice)); AccessChanged(); }
        public void Keep() { if (busy) return; confirmation = notice = null; Notify(nameof(Notice)); AccessChanged(); SetStatus("Chưa thay đổi đơn món."); }
        public async Task<bool> ConfirmAsync()
        {
            if (!CanConfirm) return false; var kind = confirmation; var id = confirmedId; var sessionId = selectedSession.SessionId; Busy(true); var saved = false;
            try
            {
                var result = await Task.Run(() => kind == "Serve" ? service.ConfirmStaff(actor, id) : service.CancelStaff(actor, id)); saved = true;
                confirmation = notice = null; Notify(nameof(Notice));
                var message = kind == "Serve" ? "Đã xác nhận phục vụ đơn #" + result.OrderId + "." : "Đã hủy đơn #" + result.OrderId + ".";
                try { await LoadAsync(sessionId, id); SetStatus(message); }
                catch (Exception) { Clear(); SetStatus(message + " Chưa tải lại được dữ liệu/quyền; hãy tải lại trước khi thao tác tiếp."); }
            }
            catch (Exception error) { Fail(error); }
            finally { Busy(false); }
            return saved;
        }
        private void ClearOrders() { selectedOrder = null; confirmation = notice = null; Orders.Clear(); foreach (var p in new[] { nameof(SelectedOrder), nameof(Details), nameof(Notice) }) Notify(p); AccessChanged(); }
        private void Clear() { selectedSession = null; canView = canCreate = canConfirm = canCancel = false; Sessions.Clear(); ClearOrders(); Notify(nameof(SelectedSession)); Notify(nameof(HasView)); }
        private void Fail(Exception error) { Clear(); SetStatus(error is UnauthorizedAccessException || error is InvalidOperationException || error is ArgumentException ? error.Message : "Không đọc/xử lý được đơn món. Hãy tải lại."); }
        private void Busy(bool value) { busy = value; Notify(nameof(IsBusy)); AccessChanged(); }
        private void SetStatus(string value) { status = value; Notify(nameof(Status)); }
        private void AccessChanged() { foreach (var p in new[] { nameof(CanClose), nameof(CanEdit), nameof(CanSelectOrder), nameof(CanCreate), nameof(CanServe), nameof(CanCancel), nameof(IsConfirming), nameof(CanConfirm) }) Notify(p); }
        private void Notify([CallerMemberName] string property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
