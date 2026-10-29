using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class CheckoutLine
    {
        public int OrderId { get; set; }
        public string Name { get; set; }
        public int Quantity { get; set; }
        public long UnitPrice { get; set; }
        public decimal Amount => (decimal)Quantity * UnitPrice;
    }
    public sealed class CheckoutViewModel : INotifyPropertyChanged
    {
        private readonly ICheckoutService service;
        private readonly LoginSession actor;
        private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
        private bool busy, access, confirming, acknowledged;
        private CheckoutSessionChoice selected;
        private CheckoutPreview preview;
        private Invoice receipt;
        private string payment = "Cash", status = "Đang tải phiên đang sử dụng…";
        public CheckoutViewModel(ICheckoutService service, LoginSession actor) { this.service = service; this.actor = actor; }
        public ObservableCollection<CheckoutSessionChoice> Sessions { get; } = new ObservableCollection<CheckoutSessionChoice>();
        public ObservableCollection<CheckoutLine> Lines { get; } = new ObservableCollection<CheckoutLine>();
        public CheckoutSessionChoice Selected
        {
            get => selected;
            set { if (!CanChoose) return; selected = value; ClearPreview(); receipt = null; payment = "Cash"; Changed(); }
        }
        public string PaymentMethod { get => payment; set { if (busy) return; payment = value; acknowledged = false; Changed(); } }
        public bool Acknowledged { get => acknowledged; set { if (busy) return; acknowledged = value; Changed(); } }
        public bool IsBusy => busy;
        public bool CanReload => !busy && !confirming;
        public bool CanChoose => CanReload && access;
        public bool CanPreview => !busy && access && selected != null && receipt == null;
        public bool CanConfirm => !busy && access && confirming && preview != null && acknowledged && (payment == "Cash" || payment == "BankTransfer");
        public bool CanClose => !busy;
        public bool IsConfirming => confirming;
        public bool HasReceipt => receipt != null;
        public Invoice Receipt => receipt;
        public string Status => status;
        public string Details => preview == null ? "Chọn phiên và bấm Tính tiền để xem trước. Chỉ xác nhận sau khi đã kiểm tra nhận tiền." :
            "Phiên #" + preview.Bill.SessionId + " — Phòng " + preview.Bill.RoomCode + " — " + preview.Bill.RoomTypeName
            + "\nNhận thực tế: " + Local(preview.Bill.ActualStartTime) + "\nTính đến: " + Local(preview.Bill.BillingEndTime)
            + "\nThời lượng: " + preview.Bill.UsedMinutes.ToString("N2", Vietnamese) + " phút; giá đã chốt " + Money(preview.Bill.HourlyRate) + "/giờ.";
        public string Amounts => preview == null ? "" : "Tiền phòng: " + Money(preview.Bill.RoomCharge)
            + "\nMón đã phục vụ: " + Money(preview.Bill.ServiceCharge) + "\nTổng tạm tính: " + Money(preview.Bill.TotalAmount);
        public string PendingWarning => preview == null ? "" : preview.Bill.PendingOrderCount + " đơn chờ sẽ bị hủy khi trả phòng; chưa tính tiền.";
        public string ReceiptText => receipt == null ? "" : "Đã hoàn tất trả phòng\nHóa đơn: " + receipt.InvoiceNumber + "\nPhiên #" + receipt.RoomSessionId
            + "\nTiền phòng: " + Money(receipt.RoomCharge) + "\nMón đã phục vụ: " + Money(receipt.ServiceCharge)
            + "\nTổng đã chốt: " + Money(receipt.TotalAmount) + "\nHình thức: " + (receipt.PaymentMethod == "Cash" ? "Tiền mặt" : "Chuyển khoản")
            + "\nThu ngân: " + receipt.ProcessedByNameSnapshot + "\nThanh toán lúc: " + Local(receipt.PaidAt);
        public event PropertyChangedEventHandler PropertyChanged;
        public async Task ReloadAsync()
        {
            if (!CanReload) return; busy = true; Changed();
            try { await LoadAsync(); status = Sessions.Count == 0 ? "Không có phiên Active cần trả phòng." : "Chọn một phiên đang sử dụng để trả phòng."; }
            catch (Exception error) { Fail(error); }
            finally { busy = false; Changed(); }
        }
        private async Task LoadAsync()
        {
            ClearSensitive();
            var choices = await Task.Run(() => service.ListActiveStaff(actor));
            foreach (var choice in choices) Sessions.Add(choice);
            access = true;
        }
        public async Task PreviewAsync()
        {
            if (!CanPreview) return; var id = selected.SessionId; busy = true; ClearPreview(); Changed();
            try
            {
                preview = await Task.Run(() => service.PreviewStaff(actor, id));
                foreach (var order in preview.CompletedOrders) foreach (var item in order.Items)
                    Lines.Add(new CheckoutLine { OrderId = order.OrderId, Name = item.ServiceNameSnapshot, Quantity = item.Quantity, UnitPrice = item.UnitPrice });
                confirming = true; status = "Kiểm tra số tiền và hình thức nhận tiền. Tổng cuối được tính lại khi xác nhận.";
            }
            catch (Exception error) { Fail(error); }
            finally { busy = false; Changed(); }
        }
        public void Keep()
        { if (busy) return; confirming = acknowledged = false; status = "Chưa trả phòng; phiên tiếp tục tính tiền. Có thể chọn lại hoặc tính tiền lại."; Changed(); }
        public async Task<bool> ConfirmAsync()
        {
            if (!CanConfirm) return false;
            var id = preview.Bill.SessionId; var method = payment; busy = true; Changed(); Invoice saved = null;
            try { saved = await Task.Run(() => service.ConfirmStaff(actor, id, method)); }
            catch (Exception error) { Fail(error); }
            if (saved != null)
            {
                receipt = saved; ClearSensitive();
                try { await LoadAsync(); status = "Đã trả phòng thành công. Hóa đơn bên phải là kết quả đã chốt; chọn phiên khác để tiếp tục."; }
                catch (Exception) { ClearSensitive(); status = "Đã trả phòng thành công, nhưng chưa tải lại được danh sách hoặc quyền đã thay đổi. Giữ kết quả hóa đơn; không xác nhận lại lượt này."; }
            }
            busy = false; Changed(); return saved != null;
        }
        private void ClearPreview() { preview = null; confirming = acknowledged = false; Lines.Clear(); }
        private void ClearSensitive() { ClearPreview(); selected = null; Sessions.Clear(); access = false; }
        private void Fail(Exception error)
        {
            ClearSensitive();
            var message = error is UnauthorizedAccessException ? "Không còn quyền trả phòng hoặc phiên đăng nhập hết hiệu lực. Hãy đóng hoặc tải lại để kiểm tra quyền."
                : error is ArgumentException || error is InvalidOperationException ? error.Message + " Hãy tải lại trước khi thử tiếp."
                : "Chưa xử lý được yêu cầu. Hãy tải lại để kiểm tra dữ liệu; không tự gửi lại thanh toán.";
            status = (receipt == null ? "" : "Lượt trước đã trả phòng thành công; giữ kết quả hóa đơn. ") + message;
        }
        private static string Local(DateTimeOffset value) => value.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy", Vietnamese);
        private static string Money(decimal value) => value.ToString("N0", Vietnamese) + " đ";
        private void Changed()
        { foreach (var name in new[] { nameof(Selected), nameof(PaymentMethod), nameof(Acknowledged), nameof(IsBusy), nameof(CanReload), nameof(CanChoose), nameof(CanPreview), nameof(CanConfirm), nameof(CanClose), nameof(IsConfirming), nameof(HasReceipt), nameof(Receipt), nameof(Status), nameof(Details), nameof(Amounts), nameof(PendingWarning), nameof(ReceiptText) }) Notify(name); }
        private void Notify([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
