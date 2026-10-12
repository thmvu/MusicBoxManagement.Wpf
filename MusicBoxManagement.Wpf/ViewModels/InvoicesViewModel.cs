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
    public sealed class InvoicesViewModel : INotifyPropertyChanged
    {
        private readonly InvoiceService service;
        private readonly LoginSession actor;
        private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
        private bool busy, access;
        private string number, phone, status = "Đang tải hóa đơn…";
        private DateTime? from, to;
        private InvoiceSummary selected;
        private InvoiceDetails details;
        public InvoicesViewModel(InvoiceService service, LoginSession actor) { this.service = service; this.actor = actor; }
        public ObservableCollection<InvoiceSummary> Items { get; } = new ObservableCollection<InvoiceSummary>();
        public ObservableCollection<InvoiceLine> Lines { get; } = new ObservableCollection<InvoiceLine>();
        public string NumberQuery { get => number; set { if (busy) return; number = value; FilterChanged(); Notify(); } }
        public string PhoneQuery { get => phone; set { if (busy) return; phone = value; FilterChanged(); Notify(); } }
        public DateTime? FromDate { get => from; set { if (busy) return; from = value; FilterChanged(); Notify(); } }
        public DateTime? ToDate { get => to; set { if (busy) return; to = value; FilterChanged(); Notify(); } }
        public InvoiceSummary Selected => selected;
        public InvoiceDetails Detail => details;
        public bool IsBusy => busy;
        public bool CanSearch => !busy;
        public bool CanSelect => !busy && access;
        public bool HasDetails => details != null;
        public bool CanPrint => !busy && details != null && details.CanPrint;
        public bool ShowHint => details == null;
        public string Status => status;
        public string DetailText => details == null ? "" : details.StoreName + "\nHóa đơn " + details.Invoice.InvoiceNumber
            + "\nPhòng: " + details.RoomCodeSnapshot + " — " + details.RoomTypeNameSnapshot
            + "\nNhận: " + Local(details.ActualStartTime) + "\nTrả: " + Local(details.ActualEndTime)
            + "\nThời lượng: " + details.UsedMinutes.ToString("N2", Vietnamese) + " phút"
            + "\nĐơn giá: " + Money(details.HourlyRate) + "/giờ"
            + "\nHình thức: " + (details.Invoice.PaymentMethod == "Cash" ? "Tiền mặt" : "Chuyển khoản")
            + "\nThu ngân: " + details.Invoice.ProcessedByNameSnapshot + "\nThanh toán: " + Local(details.Invoice.PaidAt);
        public string Amounts => details == null ? "" : "Tiền phòng: " + Money(details.Invoice.RoomCharge)
            + "    Món: " + Money(details.Invoice.ServiceCharge) + "\nTổng đã thanh toán: " + Money(details.Invoice.TotalAmount);
        public event PropertyChangedEventHandler PropertyChanged;
        public async Task SearchAsync()
        {
            if (busy) return; var code = number; var start = from; var end = to; var query = phone;
            busy = true; Clear(); Changed();
            try
            {
                var result = await Task.Run(() => service.Search(actor, code, start, end, query));
                foreach (var item in result.Items) Items.Add(item);
                access = true; status = "Tìm thấy " + Items.Count + " hóa đơn. Ngày lọc theo thời điểm thanh toán (giờ Việt Nam); chọn một dòng để xem chi tiết.";
            }
            catch (Exception error) { Fail(error); }
            finally { busy = false; Changed(); }
        }
        public async Task SelectAsync(InvoiceSummary value)
        {
            if (!CanSelect || selected == value) return;
            selected = value; ClearDetail(); busy = value != null; Changed();
            if (value == null) return;
            try
            {
                details = await Task.Run(() => service.Read(actor, value.InvoiceId));
                foreach (var item in details.Items) Lines.Add(item);
                status = "Đã đọc hóa đơn đã thanh toán. Giá phòng, món và thu ngân giữ theo dữ liệu lúc chốt.";
            }
            catch (Exception error) { Fail(error); }
            finally { busy = false; Changed(); }
        }
        public void ClearFilters()
        {
            if (busy) return; number = phone = null; from = to = null; FilterChanged();
            foreach (var name in new[] { nameof(NumberQuery), nameof(PhoneQuery), nameof(FromDate), nameof(ToDate) }) Notify(name);
        }
        public async Task<InvoicePrintViewModel> PreparePrintAsync()
        {
            if (!CanPrint) return null;
            var id = details.Invoice.InvoiceId; busy = true; Changed();
            try
            {
                var printable = await Task.Run(() => service.ReadForPrint(actor, id));
                return new InvoicePrintViewModel(service, actor, printable);
            }
            catch (Exception error) { Fail(error); status = "Chưa mở được bản in. " + status; return null; }
            finally { busy = false; Changed(); }
        }
        private void FilterChanged() { Clear(); status = "Bộ lọc đã đổi. Bấm Tìm / Cập nhật để đọc kết quả mới."; Changed(); }
        private void ClearDetail() { details = null; Lines.Clear(); }
        private void Clear() { selected = null; Items.Clear(); ClearDetail(); access = false; }
        private void Fail(Exception error)
        {
            Clear();
            if (error is UnauthorizedAccessException)
            {
                number = phone = null; from = to = null;
                foreach (var name in new[] { nameof(NumberQuery), nameof(PhoneQuery), nameof(FromDate), nameof(ToDate) }) Notify(name);
                status = "Không còn quyền xem hóa đơn hoặc phiên đăng nhập đã hết hiệu lực. Hãy đóng hoặc tải lại để kiểm tra quyền.";
            }
            else status = error is ArgumentException || error is InvalidOperationException ? error.Message + " Hãy kiểm tra bộ lọc và tải lại."
                : "Chưa đọc được hóa đơn. Hãy tải lại trước khi thử tiếp.";
        }
        private static string Local(DateTimeOffset value) => value.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy", Vietnamese);
        private static string Money(decimal value) => value.ToString("N0", Vietnamese) + " đ";
        private void Changed()
        { foreach (var name in new[] { nameof(Selected), nameof(Detail), nameof(IsBusy), nameof(CanSearch), nameof(CanSelect), nameof(HasDetails), nameof(CanPrint), nameof(ShowHint), nameof(Status), nameof(DetailText), nameof(Amounts) }) Notify(name); }
        private void Notify([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
