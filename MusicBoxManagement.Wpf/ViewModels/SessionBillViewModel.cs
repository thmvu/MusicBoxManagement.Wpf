using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class SessionBillViewModel : INotifyPropertyChanged
    {
        private readonly Func<SessionBill> read;
        private bool busy;
        private SessionBill bill;
        private string status = "Đang tải tiền tạm tính…";
        private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
        public SessionBillViewModel(Func<SessionBill> read) { this.read = read ?? throw new ArgumentNullException(nameof(read)); }
        public SessionBill Bill => bill;
        public bool IsBusy => busy;
        public bool CanRefresh => !busy;
        public bool HasBill => bill != null;
        public string Status => status;
        public string Details => bill == null ? "" : "Phiên #" + bill.SessionId + " · Phòng " + bill.RoomCode + " · " + bill.RoomTypeName
            + "\nNhận thực tế: " + Local(bill.ActualStartTime)
            + "\nTính đến: " + Local(bill.BillingEndTime) + " (giờ Việt Nam)"
            + "\nĐã sử dụng: " + bill.UsedMinutes.ToString("N2", Vietnamese) + " phút (tính cả giây)"
            + "\nGiá đã chốt: " + bill.HourlyRate.ToString("N0", Vietnamese) + " đ/giờ";
        public string RoomAmount => Money(bill?.RoomCharge);
        public string ServiceAmount => Money(bill?.ServiceCharge);
        public string TotalAmount => Money(bill?.TotalAmount);
        public string OrdersNote => bill == null ? "" : bill.CompletedOrderCount + " đơn đã phục vụ được cộng tiền. "
            + bill.PendingOrderCount + " đơn chờ phục vụ chưa cộng tiền; " + bill.CancelledOrderCount + " đơn đã hủy không tính tiền.";
        public event PropertyChangedEventHandler PropertyChanged;
        public async Task RefreshAsync()
        {
            if (busy) return;
            busy = true; bill = null; status = "Đang cập nhật tiền tạm tính…"; Changed();
            try { bill = await Task.Run(read); status = "Đã cập nhật. Tổng tiền tiếp tục thay đổi theo thời gian sử dụng và món được phục vụ."; }
            catch (UnauthorizedAccessException) { status = "Không còn quyền xem phiên hoặc phiên đăng nhập đã hết hiệu lực. Hãy đóng và kiểm tra lại."; }
            catch (Exception error)
            { status = error is ArgumentException || error is InvalidOperationException ? error.Message + " Hãy đóng và tra cứu lại phiên." : "Chưa tải được tiền tạm tính. Bấm Cập nhật để thử lại."; }
            finally { busy = false; Changed(); }
        }
        private static string Local(DateTimeOffset value) => value.ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm:ss dd/MM/yyyy", Vietnamese);
        private static string Money(decimal? value) => value.HasValue ? value.Value.ToString("N0", Vietnamese) + " đ" : "";
        private void Changed()
        { foreach (var property in new[] { nameof(Bill), nameof(IsBusy), nameof(CanRefresh), nameof(HasBill), nameof(Status), nameof(Details), nameof(RoomAmount), nameof(ServiceAmount), nameof(TotalAmount), nameof(OrdersNote) }) Notify(property); }
        private void Notify([CallerMemberName] string property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
