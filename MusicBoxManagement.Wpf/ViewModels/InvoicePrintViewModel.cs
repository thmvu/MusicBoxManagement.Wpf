using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Documents;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Printing;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class InvoicePrintViewModel : INotifyPropertyChanged
    {
        private readonly InvoiceService service;
        private readonly LoginSession actor;
        private readonly int invoiceId;
        private bool busy;
        private FlowDocument document;
        private string status = "Xem trước khổ A4. Bản gửi in sẽ dàn trang theo vùng in của máy đã chọn.";
        public InvoicePrintViewModel(InvoiceService service, LoginSession actor, InvoiceDetails details)
        {
            this.service = service; this.actor = actor; invoiceId = details.Invoice.InvoiceId;
            if (!details.CanPrint) throw new UnauthorizedAccessException("Không có quyền in hóa đơn.");
            SetDocument(details, InvoiceDocument.A4);
        }
        public FlowDocument Document => document;
        public DocumentPaginator Paginator => document == null ? null : ((IDocumentPaginatorSource)document).DocumentPaginator;
        public bool IsBusy => busy;
        public bool CanPrint => !busy && document != null;
        public string Status => status;
        public event PropertyChangedEventHandler PropertyChanged;
        public async Task PrintAsync(IInvoicePrinter printer)
        {
            if (!CanPrint) return;
            busy = true; Changed();
            try
            {
                // No read transaction is held while the user chooses a printer.
                var details = await Task.Run(() => service.ReadForPrint(actor, invoiceId));
                SetDocument(details, InvoiceDocument.A4);
                if (!printer.Select()) { status = "Đã hủy chọn máy in. Hóa đơn giữ nguyên; chưa gửi lệnh in."; return; }
                // Permissions may have changed while the native dialog was open.
                details = await Task.Run(() => service.ReadForPrint(actor, invoiceId));
                var output = InvoiceDocument.Create(details, printer.PrintableArea);
                var paginator = ((IDocumentPaginatorSource)output).DocumentPaginator;
                paginator.ComputePageCount();
                printer.Send(paginator, "Music Box — " + details.Invoice.InvoiceNumber);
                status = "Đã gửi lệnh in hóa đơn " + details.Invoice.InvoiceNumber + ". Có thể in lại khi cần.";
            }
            catch (Exception error)
            {
                document = null;
                status = error is UnauthorizedAccessException ? "Không còn quyền xem/in hoặc phiên đăng nhập đã hết hiệu lực. Hãy đóng và tải lại hóa đơn."
                    : error is InvalidOperationException ? error.Message + " Hãy đóng và mở lại bản xem trước."
                    : "Chưa xác nhận được việc gửi lệnh in. Hãy kiểm tra hàng đợi máy in trước khi thử lại.";
            }
            finally { busy = false; Changed(); }
        }
        private void SetDocument(InvoiceDetails details, System.Windows.Size size)
        { document = InvoiceDocument.Create(details, size); Paginator.ComputePageCount(); Notify(nameof(Document)); }
        private void Changed()
        { foreach (var name in new[] { nameof(Document), nameof(IsBusy), nameof(CanPrint), nameof(Status) }) Notify(name); }
        private void Notify([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
