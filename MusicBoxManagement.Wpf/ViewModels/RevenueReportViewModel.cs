using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class ReportChoice
    {
        public string Code { get; }
        public string Label { get; }
        internal ReportChoice(string code, string label) { Code = code; Label = label; }
    }

    public sealed class RevenueReportViewModel : INotifyPropertyChanged
    {
        private readonly ReportService service;
        private readonly LoginSession actor;
        private DateTime? from, to;
        private string roomType, mode = "Day", status = "Đang tải báo cáo…";
        private bool busy;
        private RevenueReport report;

        public RevenueReportViewModel(ReportService service, LoginSession actor)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            this.actor = actor;
            var today = DateTimeOffset.UtcNow.ToOffset(BookingHours.VietnamOffset).Date;
            from = new DateTime(today.Year, today.Month, 1); to = today;
        }
        public IReadOnlyList<ReportChoice> RoomTypes { get; } = Array.AsReadOnly(new[] {
            new ReportChoice(null, "Tất cả"), new ReportChoice("STANDARD", "Standard"), new ReportChoice("VIP", "VIP") });
        public IReadOnlyList<ReportChoice> Modes { get; } = Array.AsReadOnly(new[] {
            new ReportChoice("Day", "Theo ngày"), new ReportChoice("Month", "Theo tháng"), new ReportChoice("RoomType", "Theo loại phòng") });
        public ObservableCollection<RevenueReportRow> Rows { get; } = new ObservableCollection<RevenueReportRow>();
        public DateTime? FromDate { get => from; set { if (busy || from == value) return; from = value; FilterChanged(); } }
        public DateTime? ToDate { get => to; set { if (busy || to == value) return; to = value; FilterChanged(); } }
        public string RoomTypeCode { get => roomType; set { if (busy || roomType == value) return; roomType = value; FilterChanged(); } }
        public string Mode { get => mode; set { if (busy || mode == value) return; mode = value; FilterChanged(); } }
        public bool IsBusy => busy;
        public bool CanSearch => !busy;
        public RevenueReport Report => report;
        public string Status => status;
        public string Summary => report == null ? "" : "Tổng " + report.InvoiceCount + " hóa đơn  ·  Tiền phòng: "
            + Money(report.RoomCharge) + "  ·  Tiền dịch vụ: " + Money(report.ServiceCharge) + "  ·  Doanh thu: " + Money(report.TotalAmount);
        public event PropertyChangedEventHandler PropertyChanged;

        public async Task SearchAsync()
        {
            if (busy) return;
            var start = from; var end = to; var type = roomType; var grouping = mode;
            busy = true; Clear(); Changed();
            try
            {
                if (!start.HasValue || !end.HasValue)
                    throw new ArgumentException("Cần chọn đủ ngày bắt đầu và kết thúc.");
                var result = await Task.Run(() => service.ReadRevenue(actor, start.Value, end.Value, type));
                if (grouping != "Day" && grouping != "Month" && grouping != "RoomType")
                    throw new ArgumentException("Cách nhóm báo cáo không hợp lệ.");
                report = result;
                foreach (var row in grouping == "Day" ? result.Days : grouping == "Month" ? result.Months : result.RoomTypes) Rows.Add(row);
                status = "Đã tải " + Rows.Count + " nhóm; lọc theo ngày thanh toán PaidAt, giờ Việt Nam. Cập nhật lúc "
                    + result.CheckedAt.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy", CultureInfo.InvariantCulture) + ".";
            }
            catch (Exception error)
            {
                Clear();
                if (error is UnauthorizedAccessException)
                {
                    from = to = null; roomType = null; mode = "Day";
                    status = "Không còn quyền xem báo cáo hoặc phiên đã hết hiệu lực. Hãy đóng/đăng nhập lại.";
                }
                else if (!start.HasValue || !end.HasValue) status = "Cần chọn đủ ngày bắt đầu và kết thúc.";
                else status = error is ArgumentException || error is InvalidOperationException ? error.Message + " Hãy tải lại."
                    : "Chưa tải được báo cáo. Dữ liệu cũ đã được xóa; hãy thử lại.";
            }
            finally { busy = false; Changed(); }
        }
        private void FilterChanged() { Clear(); status = "Bộ lọc đã đổi. Bấm Xem báo cáo để tải lại."; Changed(); }
        private void Clear() { report = null; Rows.Clear(); }
        private static string Money(decimal value) => value.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) + " đ";
        private void Changed()
        {
            foreach (var property in new[] { nameof(FromDate), nameof(ToDate), nameof(RoomTypeCode), nameof(Mode), nameof(IsBusy),
                nameof(CanSearch), nameof(Report), nameof(Summary), nameof(Status) }) Notify(property);
        }
        private void Notify([CallerMemberName] string property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
