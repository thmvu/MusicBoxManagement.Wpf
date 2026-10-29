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
    public sealed class RoomUsageFilterChoice
    {
        public int? RoomId { get; }
        public string Label { get; }
        internal RoomUsageFilterChoice(int? roomId, string label) { RoomId = roomId; Label = label; }
    }

    public sealed class RoomUsageReportViewModel : INotifyPropertyChanged
    {
        private readonly IRoomUsageReportService service;
        private readonly LoginSession actor;
        private DateTime? from, to;
        private int? roomId;
        private bool busy;
        private RoomUsageReport report;
        private string status = "Đang tải báo cáo…";
        public RoomUsageReportViewModel(IRoomUsageReportService service, LoginSession actor)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service)); this.actor = actor;
            var today = DateTimeOffset.UtcNow.ToOffset(BookingHours.VietnamOffset).Date;
            from = new DateTime(today.Year, today.Month, 1); to = today;
            Rooms.Add(new RoomUsageFilterChoice(null, "Tất cả phòng"));
        }
        public ObservableCollection<RoomUsageFilterChoice> Rooms { get; } = new ObservableCollection<RoomUsageFilterChoice>();
        public ObservableCollection<RoomUsageRow> Rows { get; } = new ObservableCollection<RoomUsageRow>();
        public DateTime? FromDate { get => from; set { if (busy || from == value) return; from = value; FilterChanged(); } }
        public DateTime? ToDate { get => to; set { if (busy || to == value) return; to = value; FilterChanged(); } }
        public int? RoomId { get => roomId; set { if (busy || roomId == value) return; roomId = value; FilterChanged(); } }
        public bool IsBusy => busy;
        public bool CanSearch => !busy;
        public bool CanExport => !busy && report != null && report.CanExport;
        public RoomUsageReport Report => report;
        public string Status => status;
        public string Summary => report == null ? "" : "Tổng " + report.CompletedCount + " lượt hoàn tất  ·  Đã dùng: "
            + Number(report.UsedMinutes) + " phút  ·  Lịch tiêu chuẩn: " + Number(report.AvailableMinutes)
            + " phút  ·  Tỷ lệ chung: " + (report.UtilizationPercent.HasValue ? Number(report.UtilizationPercent.Value) + "%" : "Không có dữ liệu");
        public event PropertyChangedEventHandler PropertyChanged;
        public async Task SearchAsync()
        {
            if (busy) return;
            var start = from; var end = to; var selectedRoom = roomId;
            busy = true; ClearResult(); Changed();
            try
            {
                if (!start.HasValue || !end.HasValue) throw new ArgumentException("Cần chọn đủ ngày bắt đầu và kết thúc.");
                var result = await Task.Run(() => service.Read(actor, start.Value, end.Value, selectedRoom));
                Rooms.Clear(); Rooms.Add(new RoomUsageFilterChoice(null, "Tất cả phòng"));
                foreach (var choice in result.RoomChoices) Rooms.Add(new RoomUsageFilterChoice(choice.RoomId, choice.Label));
                report = result;
                foreach (var row in result.Rooms) Rows.Add(row);
                status = "Đã tải " + Rows.Count + " phòng. Tỷ lệ tính đến "
                    + result.CheckedAt.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy", CultureInfo.InvariantCulture) + " (giờ Việt Nam).";
            }
            catch (Exception error)
            {
                ClearResult(); Rooms.Clear(); Rooms.Add(new RoomUsageFilterChoice(null, "Tất cả phòng"));
                if (error is UnauthorizedAccessException)
                {
                    from = to = null; roomId = null;
                    status = "Không còn quyền xem báo cáo hoặc phiên đã hết hiệu lực. Hãy đóng/đăng nhập lại.";
                }
                else status = error is ArgumentException || error is InvalidOperationException ? error.Message + " Hãy tải lại."
                    : "Chưa tải được báo cáo. Dữ liệu cũ đã được xóa; hãy thử lại.";
            }
            finally { busy = false; Changed(); }
        }
        public async Task ExportAsync(Func<string, string> choosePath)
        {
            if (!CanExport) return;
            if (choosePath == null) throw new ArgumentNullException(nameof(choosePath));
            var loaded = report; busy = true; Changed();
            try
            {
                var saved = await ReportExportWorkflow.RunAsync(() => service.Export(actor, loaded.FromDate, loaded.ToDate, loaded.RoomId), choosePath);
                status = saved ? "Đã xuất Excel theo bộ lọc đang xem. Bấm Xem báo cáo để cập nhật lại bảng." : "Đã hủy xuất Excel; chưa ghi file.";
            }
            catch (UnauthorizedAccessException)
            {
                ClearResult(); Rooms.Clear(); Rooms.Add(new RoomUsageFilterChoice(null, "Tất cả phòng")); from = to = null; roomId = null;
                status = "Không còn quyền xuất báo cáo hoặc phiên đã hết hiệu lực. Chưa ghi file; hãy tải lại quyền/báo cáo.";
            }
            catch (Exception error) { status = error is ArgumentException || error is InvalidOperationException ? error.Message : "Chưa xuất được Excel. Kiểm tra thư mục lưu hoặc đóng file đang mở rồi thử lại."; }
            finally { busy = false; Changed(); }
        }
        private static string Number(decimal value) => value.ToString("N2", CultureInfo.GetCultureInfo("vi-VN"));
        private void FilterChanged() { ClearResult(); status = "Bộ lọc đã đổi. Bấm Xem báo cáo để tải lại."; Changed(); }
        private void ClearResult() { report = null; Rows.Clear(); }
        private void Changed()
        {
            foreach (var property in new[] { nameof(FromDate), nameof(ToDate), nameof(RoomId), nameof(IsBusy), nameof(CanSearch), nameof(CanExport), nameof(Report), nameof(Summary), nameof(Status) }) Notify(property);
        }
        private void Notify([CallerMemberName] string property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
