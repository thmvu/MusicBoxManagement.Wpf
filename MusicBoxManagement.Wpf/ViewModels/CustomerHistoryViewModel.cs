using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class CustomerHistoryViewModel : INotifyPropertyChanged
    {
        private readonly ICustomerHistoryService service;
        private readonly LoginSession actor;
        private readonly int customerId;
        private bool busy;
        private CustomerHistory history;
        private string status = "Đang tải lịch sử…";
        public CustomerHistoryViewModel(ICustomerHistoryService service, LoginSession actor, int customerId)
        { this.service = service ?? throw new ArgumentNullException(nameof(service)); this.actor = actor; this.customerId = customerId; }
        public CustomerHistory History => history;
        public bool IsBusy => busy;
        public bool CanReload => !busy;
        public string Status => status;
        public string CustomerLabel => history == null ? "" : history.Customer.FullName + " — SĐT " + history.Customer.PhoneNumber + " — #" + history.Customer.CustomerId;
        public string Summary => history == null ? "" : "Đã hoàn tất: " + history.CompletedSessionCount + " phiên • Sử dụng gần nhất: "
            + (history.LastUsedAt.HasValue ? Local(history.LastUsedAt.Value) : "Chưa sử dụng") + " (giờ nhận thực tế).";
        public string BookingNotice => history == null ? "" : history.Reservations.Count + " booking. Trạng thái đã lưu; không tự cập nhật NoShow khi xem lịch sử.";
        public string SessionNotice => history == null ? "" : history.Sessions.Count + " phiên. Walk-in không có giờ trả dự kiến; mã phòng/loại/giá là dữ liệu đã chốt khi nhận.";
        public string InvoiceNotice => history == null ? "" : !history.CanViewInvoices ? "Bạn không có quyền xem hóa đơn."
            : history.Invoices.Count == 0 ? "Khách hàng chưa có hóa đơn." : history.Invoices.Count + " hóa đơn đã thanh toán. Đây là danh sách tóm tắt.";
        public event PropertyChangedEventHandler PropertyChanged;
        public async Task ReloadAsync()
        {
            if (busy) return;
            busy = true; history = null; status = "Đang tải lịch sử…"; Changed();
            try
            {
                history = await Task.Run(() => service.Read(actor, customerId));
                status = "Dữ liệu tại " + Local(history.CheckedAt) + " (giờ Việt Nam). Bấm Tải lại để cập nhật.";
            }
            catch (Exception error)
            {
                history = null;
                status = error is UnauthorizedAccessException ? "Không còn quyền xem khách hàng hoặc phiên đã hết hiệu lực. Hãy đóng/đăng nhập lại."
                    : error is ArgumentException || error is InvalidOperationException ? error.Message
                    : "Không đọc được lịch sử. Dữ liệu cũ đã được xóa; hãy thử tải lại.";
            }
            finally { busy = false; Changed(); }
        }
        private static string Local(DateTimeOffset value) => value.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy", CultureInfo.GetCultureInfo("vi-VN"));
        private void Changed()
        {
            foreach (var property in new[] { nameof(History), nameof(IsBusy), nameof(CanReload), nameof(Status), nameof(CustomerLabel), nameof(Summary), nameof(BookingNotice), nameof(SessionNotice), nameof(InvoiceNotice) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }
}
