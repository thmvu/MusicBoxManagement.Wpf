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
        private readonly IClock clock;
        private StaffReservation selected;
        private bool busy, canView, canCancel, canCreate, canCheckIn, confirming, confirmingCheckIn;
        private RoomSession lastCheckedIn;
        private string checkInNotice;
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
            set { selected = value; confirming = confirmingCheckIn = false; lastCheckedIn = null; checkInNotice = null; Reason = null; Notify(); Notify(nameof(Details)); Notify(nameof(CheckInResult)); Notify(nameof(CheckInNotice)); AccessChanged(); }
        }
        public string Reason { get => reason; set { reason = value; Notify(); } }
        public string Details => Selected == null ? "Chọn booking để xem chi tiết." : Selected.Details;
        public string Status => message;
        public bool IsBusy => busy;
        public bool CanSearch => !busy && !confirming && !confirmingCheckIn;
        public bool CanSelect => !busy && !confirming && !confirmingCheckIn && canView;
        public bool CanClose => !busy;
        public bool CanCancel => !busy && !confirming && !confirmingCheckIn && canView && canCancel && Selected != null && Selected.IsCancellable;
        public bool CanCreate => !busy && !confirming && !confirmingCheckIn && canView && canCreate;
        public bool CanCheckIn => !busy && !confirming && !confirmingCheckIn && canView && canCheckIn && Selected != null && Selected.IsCancellable;
        public bool CanConfirmCheckIn => !busy && confirmingCheckIn && canView && canCheckIn && Selected != null;
        public bool IsConfirmingCheckIn => confirmingCheckIn;
        public string CheckInNotice => checkInNotice;
        public string CheckInResult => lastCheckedIn == null ? "" : "Phiên #" + lastCheckedIn.RoomSessionId + " — " + lastCheckedIn.Status + "\nPhòng: " + lastCheckedIn.RoomCodeSnapshot + " — " + lastCheckedIn.RoomTypeNameSnapshot
            + "\nNhận thực tế: " + Local(lastCheckedIn.ActualStartTime) + "\nDự kiến trả: " + Local(lastCheckedIn.ExpectedEndTime.Value)
            + "\nGiá đã chốt: " + lastCheckedIn.HourlyRate.ToString("N0") + " đ/giờ";
        public bool CanConfirm => !busy && confirming && canView && canCancel && Selected != null;
        public bool IsConfirming => confirming;
        public event PropertyChangedEventHandler PropertyChanged;
        public ReservationsViewModel(StaffReservationService service, LoginSession session) : this(service, session, new SystemClock()) { }
        public ReservationsViewModel(StaffReservationService service, LoginSession session, IClock clock)
        {
            this.service = service; this.session = session; this.clock = clock;
            FromDate = clock.UtcNow.ToOffset(BookingHours.VietnamOffset).Date;
            ToDate = FromDate.Value.AddDays(30); message = "Đang tải danh sách booking…";
        }
        private async Task ReloadAsync()
        {
            var from = FromDate; var to = ToDate; var phone = PhoneQuery; var state = StatusQuery;
            var result = await Task.Run(() => service.Search(session, from, to, phone, state == "Tất cả" ? null : state));
            Items.Clear(); foreach (var item in result.Items) Items.Add(item);
            Selected = null; canView = true; canCancel = result.CanCancel; canCreate = result.CanCreate; canCheckIn = result.CanCheckIn;
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
        public void KeepBooking() { if (busy) return; confirming = confirmingCheckIn = false; Reason = null; checkInNotice = null; Notify(nameof(CheckInNotice)); AccessChanged(); SetStatus("Đã giữ booking, chưa lưu thay đổi."); }
        public void RequestCheckIn()
        {
            if (!CanCheckIn) return;
            var now = clock.UtcNow;
            confirmingCheckIn = true;
            checkInNotice = "Nhận booking #" + Selected.ReservationId + " cho phòng " + Selected.RoomCode
                + "?\nNếu nhận tại " + Local(now) + ", dự kiến trả " + Local(now.Add(Selected.EndTime - Selected.StartTime))
                + ".\nGiá tham khảo: " + Selected.CurrentHourlyRate.ToString("N0") + " đ/giờ. Giờ thực tế và giá được chốt khi xác nhận; hệ thống kiểm tra lại lịch và hạn nhận.";
            Notify(nameof(CheckInNotice)); AccessChanged(); SetStatus("Kiểm tra thông tin rồi bấm Xác nhận nhận phòng.");
        }
        public async Task<bool> ConfirmCheckInAsync()
        {
            if (!CanConfirmCheckIn) return false;
            var id = Selected.ReservationId; Busy(true);
            RoomSession saved = null;
            try { saved = await Task.Run(() => service.CheckIn(session, id)); }
            catch (UnauthorizedAccessException) { Revoke(); }
            catch (InvalidOperationException error) { confirmingCheckIn = false; SetStatus(error.Message + " Hãy tải lại danh sách."); }
            catch (SQLiteException error) { SetStatus(error.ResultCode == SQLiteErrorCode.Busy || error.ResultCode == SQLiteErrorCode.Locked
                ? "Dữ liệu đang bận. Hãy thử lại sau." : "Không nhận được phòng. Thay đổi đã hoàn tác."); }
            catch (Exception) { SetStatus("Không nhận được phòng. Hãy tải lại trước khi thử lại."); }
            if (saved != null)
            {
                confirmingCheckIn = false;
                try
                {
                    await ReloadAsync();
                    foreach (var item in Items) if (item.ReservationId == id) { Selected = item; break; }
                    lastCheckedIn = saved; Notify(nameof(CheckInResult));
                    SetStatus("Đã nhận phòng booking #" + id + ", phiên #" + saved.RoomSessionId + ". Danh sách đã tải lại.");
                }
                catch (UnauthorizedAccessException) { Revoke(); SetStatus("Đã nhận phòng booking #" + id + ", nhưng không còn quyền xem. Hãy đóng cửa sổ."); }
                catch (Exception) { Clear(); SetStatus("Đã nhận phòng booking #" + id + ", nhưng chưa tải lại được danh sách. Hãy tải lại."); }
            }
            Busy(false); return saved != null;
        }
        public async Task<GuestBookingViewModel> PrepareBookingAsync()
        {
            if (!CanCreate) return null; Busy(true);
            try { return await Task.Run(() => new GuestBookingViewModel(service.ForBooking(session), clock, true)); }
            catch (UnauthorizedAccessException) { Revoke(); }
            catch (Exception) { SetStatus("Không mở được form đặt hộ. Hãy tải lại danh sách."); }
            finally { Busy(false); }
            return null;
        }
        public async Task AfterBookingAsync(int? createdId)
        {
            if (!createdId.HasValue) { await SearchAsync(); return; }
            // Make the saved row visible even when the old filter was Cancelled or another phone/date.
            FromDate = ToDate = null; PhoneQuery = null; StatusQuery = "Tất cả";
            foreach (var property in new[] { nameof(FromDate), nameof(ToDate), nameof(PhoneQuery), nameof(StatusQuery) }) Notify(property);
            Busy(true);
            try { await ReloadAsync(); foreach (var item in Items) if (item.ReservationId == createdId.Value) { Selected = item; break; }
                SetStatus("Đã đặt hộ booking #" + createdId + ". Danh sách đã được tải lại."); }
            catch (UnauthorizedAccessException) { Revoke(); SetStatus("Đã đặt hộ booking #" + createdId + ", nhưng không còn quyền xem. Hãy đóng cửa sổ."); }
            catch (Exception) { Clear(); SetStatus("Đã đặt hộ booking #" + createdId + ", nhưng chưa tải lại được danh sách. Hãy tải lại."); }
            finally { Busy(false); }
        }
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
        private void Clear() { canView = canCancel = canCreate = canCheckIn = confirming = confirmingCheckIn = false; Items.Clear(); Selected = null; }
        private void Revoke() { Clear(); SetStatus("Bạn không còn quyền thực hiện thao tác hoặc phiên đã hết hiệu lực. Hãy đóng cửa sổ hoặc tải lại để kiểm tra quyền."); }
        private void Busy(bool value) { busy = value; Notify(nameof(IsBusy)); AccessChanged(); }
        private void AccessChanged() { foreach (var property in new[] { nameof(CanSearch), nameof(CanSelect), nameof(CanClose), nameof(CanCancel), nameof(CanCreate), nameof(CanConfirm), nameof(IsConfirming), nameof(CanCheckIn), nameof(CanConfirmCheckIn), nameof(IsConfirmingCheckIn) }) Notify(property); }
        private static string Local(DateTimeOffset value) => value.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy");
        private void SetStatus(string value) { message = value; Notify(nameof(Status)); }
        private void Notify([CallerMemberName] string property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
