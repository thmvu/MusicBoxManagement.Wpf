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
    public sealed class ServiceSalesReportViewModel : INotifyPropertyChanged
    {
        private readonly ServiceSalesReportService service;
        private readonly LoginSession actor;
        private DateTime? from, to;
        private int? serviceId;
        private int selectedTab;
        private bool busy;
        private ServiceSalesReport report;
        private string status = "Đang tải báo cáo…";
        public ServiceSalesReportViewModel(ServiceSalesReportService service, LoginSession actor)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service)); this.actor = actor;
            var today = DateTimeOffset.UtcNow.ToOffset(BookingHours.VietnamOffset).Date;
            from = new DateTime(today.Year, today.Month, 1); to = today;
        }
        public ObservableCollection<ServiceSalesRow> Rows { get; } = new ObservableCollection<ServiceSalesRow>();
        public ObservableCollection<ServiceSalesRow> TopItems { get; } = new ObservableCollection<ServiceSalesRow>();
        public ObservableCollection<ServiceReportChoice> Services { get; } = new ObservableCollection<ServiceReportChoice>();
        public DateTime? FromDate { get => from; set { if (busy || from == value) return; from = value; FilterChanged(); } }
        public DateTime? ToDate { get => to; set { if (busy || to == value) return; to = value; FilterChanged(); } }
        public int? SelectedServiceId { get => serviceId; set { if (busy || serviceId == value) return; serviceId = value; FilterChanged(); } }
        public bool IsBusy => busy;
        public bool CanSearch => !busy;
        public bool CanExport => !busy && report != null && report.CanExport;
        public int SelectedTab { get => selectedTab; set { if (busy || selectedTab == value || value < 0 || value > 1) return; selectedTab = value; Notify(); } }
        public ServiceSalesReport Report => report;
        public string Status => status;
        public string Summary => report == null ? "" : "Đã bán " + report.TotalQuantity.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))
            + " món • Doanh thu " + report.TotalRevenue.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) + " đ";
        public event PropertyChangedEventHandler PropertyChanged;
        public async Task SearchAsync()
        {
            if (busy) return;
            var start = from; var end = to; var filter = serviceId;
            busy = true; Clear(); Changed();
            try
            {
                if (!start.HasValue || !end.HasValue) throw new ArgumentException("Cần chọn đủ ngày bắt đầu và kết thúc.");
                var result = await Task.Run(() => service.Read(actor, start.Value, end.Value, filter));
                report = result;
                Services.Clear(); foreach (var choice in result.Services) Services.Add(choice);
                foreach (var row in result.Rows) Rows.Add(row);
                foreach (var row in result.TopItems) TopItems.Add(row);
                status = "Đã tải lúc " + result.CheckedAt.ToOffset(BookingHours.VietnamOffset)
                    .ToString("HH:mm:ss dd/MM/yyyy", CultureInfo.InvariantCulture) + " (giờ Việt Nam).";
            }
            catch (Exception error)
            {
                Clear(); Services.Clear(); serviceId = null;
                if (error is UnauthorizedAccessException)
                { from = to = null; status = "Không còn quyền xem báo cáo hoặc phiên đã hết hiệu lực. Hãy đóng/đăng nhập lại."; }
                else status = error is ArgumentException || error is InvalidOperationException ? error.Message + " Hãy tải lại."
                    : "Chưa tải được báo cáo. Dữ liệu cũ đã được xóa; hãy thử lại.";
            }
            finally { busy = false; Changed(); }
        }
        public async Task ExportAsync(Func<string, string> choosePath)
        {
            if (!CanExport) return;
            if (choosePath == null) throw new ArgumentNullException(nameof(choosePath));
            var loaded = report; var topItems = selectedTab == 1; busy = true; Changed();
            try
            {
                var saved = await ReportExportWorkflow.RunAsync(() => service.Export(actor, loaded.FromDate, loaded.ToDate, loaded.ServiceId, topItems), choosePath);
                status = saved ? "Đã xuất Excel theo tab và bộ lọc đang xem. Bấm Xem báo cáo để cập nhật lại bảng." : "Đã hủy xuất Excel; chưa ghi file.";
            }
            catch (UnauthorizedAccessException)
            {
                Clear(); Services.Clear(); from = to = null; serviceId = null; selectedTab = 0;
                status = "Không còn quyền xuất báo cáo hoặc phiên đã hết hiệu lực. Chưa ghi file; hãy tải lại quyền/báo cáo.";
            }
            catch (Exception error) { status = error is ArgumentException || error is InvalidOperationException ? error.Message : "Chưa xuất được Excel. Kiểm tra thư mục lưu hoặc đóng file đang mở rồi thử lại."; }
            finally { busy = false; Changed(); }
        }
        private void FilterChanged() { Clear(); status = "Bộ lọc đã đổi. Bấm Xem báo cáo để tải lại."; Changed(); }
        private void Clear() { report = null; Rows.Clear(); TopItems.Clear(); }
        private void Changed()
        {
            foreach (var property in new[] { nameof(FromDate), nameof(ToDate), nameof(SelectedServiceId), nameof(SelectedTab), nameof(IsBusy), nameof(CanSearch), nameof(CanExport), nameof(Report), nameof(Summary), nameof(Status) }) Notify(property);
        }
        private void Notify([CallerMemberName] string property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
