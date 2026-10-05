using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class ReservationsViewModel : INotifyPropertyChanged
    {
        private readonly StaffReservationService service;
        private readonly LoginSession session;
        private StaffReservation selected;
        private bool busy, canView, canCancel, confirming;
        private string reason, message;
        public ObservableCollection<StaffReservation> Items { get; } = new ObservableCollection<StaffReservation>();
        public string[] StatusOptions { get; } = { "Tất cả", "Confirmed", "CheckedIn", "Completed", "Cancelled", "NoShow" };
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string PhoneQuery { get; set; }
        public string StatusQuery { get; set; } = "Tất cả";
        public StaffReservation Selected
        {
            get => selected;
            set { selected = value; confirming = false; Reason = null; Notify(); Notify(nameof(Details)); AccessChanged(); }
        }
        public string Reason { get => reason; set { reason = value; Notify(); } }
        public string Details => Selected == null ? "Chọn booking để xem chi tiết." : Selected.Details;
        public string Status => message;
        public bool IsBusy => busy;
        public bool CanSearch => !busy && !confirming;
        public bool CanSelect => !busy && !confirming && canView;
        public bool CanClose => !busy;
        public bool CanCancel => !busy && !confirming && canView && canCancel && Selected != null && Selected.IsCancellable;
        public bool CanConfirm => !busy && confirming && canView && canCancel && Selected != null;
        public bool IsConfirming => confirming;
        public event PropertyChangedEventHandler PropertyChanged;
        public ReservationsViewModel(StaffReservationService service, LoginSession session) : this(service, session, new SystemClock()) { }
        public ReservationsViewModel(StaffReservationService service, LoginSession session, IClock clock)
        {
            this.service = service; this.session = session;
            FromDate = clock.UtcNow.ToOffset(BookingHours.VietnamOffset).Date;
            ToDate = FromDate.Value.AddDays(30); message = "Đang tải danh sách booking…";
        }
        private async Task ReloadAsync()
        {
            var from = FromDate; var to = ToDate; var phone = PhoneQuery; var state = StatusQuery;
            var result = await Task.Run(() => service.Search(session, from, to, phone, state == "Tất cả" ? null : state));
            Items.Clear(); foreach (var item in result.Items) Items.Add(item);
            Selected = null; canView = true; canCancel = result.CanCancel;
        }
        public async Task SearchAsync()
        {
            if (!CanSearch) return; Busy(true);
            try { await ReloadAsync(); SetStatus("Tìm thấy " + Items.Count + " booking. Ngày lọc theo giờ Việt Nam; SĐT dùng số đầy đủ."); }
            catch (UnauthorizedAccessException) { Revoke(); }
            catch (ArgumentException error) { Clear(); SetStatus(error.Message); }
            catch (Exception) { Clear(); SetStatus("Không đọc được booking. Hãy tải lại danh sách."); }
            finally { Busy(false); }
        }
        public void RequestCancel()
        {
            if (!CanCancel) return; confirming = true; AccessChanged();
            SetStatus("Nhập lý do và xác nhận hủy booking #" + Selected.ReservationId + ".");
        }
        public void KeepBooking() { if (busy) return; confirming = false; Reason = null; AccessChanged(); SetStatus("Đã giữ booking, chưa lưu thay đổi."); }
        public async Task<bool> ConfirmCancelAsync()
        {
            if (!CanConfirm) return false;
            var id = Selected.ReservationId; var text = Reason; Busy(true);
            var cancelled = false; var refresh = false;
            try { await Task.Run(() => service.Cancel(session, id, text)); cancelled = refresh = true; confirming = false; }
            catch (ArgumentException error) { SetStatus(error.Message); }
            catch (InvalidOperationException error) { SetStatus(error.Message); confirming = false; refresh = true; }
            catch (UnauthorizedAccessException) { Revoke(); }
            catch (SQLiteException error) { SetStatus(error.ResultCode == SQLiteErrorCode.Busy || error.ResultCode == SQLiteErrorCode.Locked
                ? "Dữ liệu đang bận. Hãy thử lại sau." : "Không hủy được booking. Thay đổi đã hoàn tác."); }
            catch (Exception) { SetStatus("Không hủy được booking. Hãy thử lại."); }
            if (refresh)
            {
                try { await ReloadAsync(); if (cancelled) SetStatus("Đã hủy booking #" + id + ", lưu lý do và nhật ký."); }
                catch (UnauthorizedAccessException) { Revoke(); if (cancelled) SetStatus("Đã hủy booking #" + id + ", nhưng không còn quyền xem. Hãy đóng cửa sổ."); }
                catch (Exception) { Clear(); SetStatus(cancelled ? "Đã hủy booking #" + id + ", nhưng chưa tải lại được danh sách. Hãy tải lại." : "Chưa tải lại được danh sách. Hãy tải lại."); }
            }
            Busy(false); return cancelled;
        }
        private void Clear() { canView = canCancel = confirming = false; Items.Clear(); Selected = null; }
        private void Revoke() { Clear(); SetStatus("Bạn không còn quyền thực hiện thao tác hoặc phiên đã hết hiệu lực. Hãy đóng cửa sổ hoặc tải lại để kiểm tra quyền."); }
        private void Busy(bool value) { busy = value; Notify(nameof(IsBusy)); AccessChanged(); }
        private void AccessChanged() { foreach (var property in new[] { nameof(CanSearch), nameof(CanSelect), nameof(CanClose), nameof(CanCancel), nameof(CanConfirm), nameof(IsConfirming) }) Notify(property); }
        private void SetStatus(string value) { message = value; Notify(nameof(Status)); }
        private void Notify([CallerMemberName] string property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
