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
    public sealed class BookingStatusReportViewModel : INotifyPropertyChanged
    {
        private readonly BookingStatusReportService service;
        private readonly LoginSession actor;
        private DateTime? from, to;
        private bool busy;
        private BookingStatusReport report;
        private string status = "Đang tải báo cáo…";
        public BookingStatusReportViewModel(BookingStatusReportService service, LoginSession actor)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service)); this.actor = actor;
            var today = DateTimeOffset.UtcNow.ToOffset(BookingHours.VietnamOffset).Date;
            from = new DateTime(today.Year, today.Month, 1); to = today;
        }
        public ObservableCollection<BookingStatusRow> Rows { get; } = new ObservableCollection<BookingStatusRow>();
        public DateTime? FromDate { get => from; set { if (busy || from == value) return; from = value; FilterChanged(); } }
        public DateTime? ToDate { get => to; set { if (busy || to == value) return; to = value; FilterChanged(); } }
        public bool IsBusy => busy;
        public bool CanSearch => !busy;
        public BookingStatusReport Report => report;
        public string Status => status;
        public string Summary => report == null ? "" : "Tổng " + report.TotalCount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) + " lượt đặt phòng có giờ bắt đầu trong kỳ.";
        public event PropertyChangedEventHandler PropertyChanged;
        public async Task SearchAsync()
        {
            if (busy) return;
            var start = from; var end = to;
            busy = true; Clear(); Changed();
            try
            {
                if (!start.HasValue || !end.HasValue) throw new ArgumentException("Cần chọn đủ ngày bắt đầu và kết thúc.");
                var result = await Task.Run(() => service.Read(actor, start.Value, end.Value));
                report = result;
                foreach (var row in result.Rows) Rows.Add(row);
                status = "Đã tải báo cáo. Trạng thái được đọc tại "
                    + result.CheckedAt.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy", CultureInfo.InvariantCulture) + " (giờ Việt Nam).";
            }
            catch (Exception error)
            {
                Clear();
                if (error is UnauthorizedAccessException)
                {
                    from = to = null;
                    status = "Không còn quyền xem báo cáo hoặc phiên đã hết hiệu lực. Hãy đóng/đăng nhập lại.";
                }
                else status = error is ArgumentException || error is InvalidOperationException ? error.Message + " Hãy tải lại."
                    : "Chưa tải được báo cáo. Dữ liệu cũ đã được xóa; hãy thử lại.";
            }
            finally { busy = false; Changed(); }
        }
        private void FilterChanged() { Clear(); status = "Bộ lọc đã đổi. Bấm Xem báo cáo để tải lại."; Changed(); }
        private void Clear() { report = null; Rows.Clear(); }
        private void Changed()
        {
            foreach (var property in new[] { nameof(FromDate), nameof(ToDate), nameof(IsBusy), nameof(CanSearch), nameof(Report), nameof(Summary), nameof(Status) }) Notify(property);
        }
        private void Notify([CallerMemberName] string property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
