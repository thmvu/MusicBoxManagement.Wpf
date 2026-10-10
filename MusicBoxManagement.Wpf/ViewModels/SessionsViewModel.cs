using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class SessionsViewModel : INotifyPropertyChanged
    {
        private readonly StaffSessionService service;
        private readonly LoginSession actor;
        private bool busy, canView, canExtend, confirming;
        private StaffSession selected;
        private SessionExtensionCheck preview;
        private string phone, state = "Active", status = "Đang tải phiên…", notice;
        private int minutes = 30, confirmedMinutes, confirmedId;
        public ObservableCollection<StaffSession> Items { get; } = new ObservableCollection<StaffSession>();
        public string[] StatusOptions { get; } = { "Active", "Completed", "Tất cả" };
        public int[] MinuteOptions { get; } = { 30, 60 };
        public string PhoneQuery { get => phone; set { if (!CanSearch) return; phone = value; Clear(); Notify(); } }
        public string StatusQuery { get => state; set { if (!CanSearch) return; state = value; Clear(); Notify(); } }
        public int Minutes { get => minutes; set { if (!CanSelect) return; minutes = value; Notify(); } }
        public StaffSession Selected { get => selected; set { if (!CanSelect) return; Select(value); } }
        public string Details => selected == null ? "Chọn phiên để xem chi tiết." : selected.Details;
        public string Status => status;
        public string Notice => notice;
        public bool IsBusy => busy;
        public bool CanSearch => !busy && !confirming;
        public bool CanSelect => CanSearch && canView;
        public bool CanExtend => CanSelect && canExtend && selected != null && selected.IsExtendable;
        public bool CanOpenBill => CanSelect && selected != null && selected.Session.Status == "Active";
        internal SessionBillViewModel CreateBillViewModel()
        {
            var id = selected.Session.RoomSessionId;
            var billing = service.ForBilling();
            return new SessionBillViewModel(() => billing.ReadStaff(actor, id));
        }
        public bool CanConfirm => !busy && confirming && canView && canExtend && preview != null;
        public bool IsConfirming => confirming;
        public bool CanClose => !busy;
        public event PropertyChangedEventHandler PropertyChanged;
        public SessionsViewModel(StaffSessionService service, LoginSession actor) { this.service = service; this.actor = actor; }
        private void Select(StaffSession value)
        { selected = value; confirming = false; preview = null; notice = null; Notify(nameof(Selected)); Notify(nameof(Details)); Notify(nameof(Notice)); AccessChanged(); }
        private void Clear() { Select(null); Items.Clear(); canView = canExtend = false; AccessChanged(); }
        private async Task ReloadAsync(int? selectedId = null)
        {
            var query = phone; var filter = state;
            var result = await Task.Run(() => service.Search(actor, query, filter == "Tất cả" ? null : filter));
            Select(null); Items.Clear(); foreach (var item in result.Items) Items.Add(item);
            canView = true; canExtend = result.CanExtend;
            foreach (var item in Items) if (item.Session.RoomSessionId == selectedId) { Select(item); break; }
        }
        public async Task SearchAsync()
        {
            if (!CanSearch) return; Busy(true); Clear();
            try { await ReloadAsync(); SetStatus("Tìm thấy " + Items.Count + " phiên. Thời gian theo giờ Việt Nam; Cập nhật để đọc lịch và quyền hiện hành."); }
            catch (Exception error) { Fail(error); }
            finally { Busy(false); }
        }
        public async Task PreviewAsync()
        {
            if (!CanExtend) return;
            var id = selected.Session.RoomSessionId; var observed = selected.Session.ExpectedEndTime.Value; var count = minutes; Busy(true);
            try
            {
                var result = await Task.Run(() => service.Preview(actor, id, count));
                if (result.ExpectedEndTime != observed) { Clear(); SetStatus("Giờ trả đã thay đổi. Hãy tải lại phiên trước khi gia hạn."); return; }
                notice = result.Reason + "\nGiới hạn: " + StaffSession.Local(result.MaximumEndTime); Notify(nameof(Notice));
                if (!result.CanExtend) { SetStatus("Chưa thể gia hạn khoảng đã chọn. Không có thay đổi được lưu."); return; }
                preview = result; confirmedId = id; confirmedMinutes = count; confirming = true;
                notice = "Gia hạn phiên #" + id + " thêm " + count + " phút?\nTừ " + StaffSession.Local(result.ExpectedEndTime)
                    + " đến " + StaffSession.Local(result.NewEndTime) + ".\nGiá đã chốt giữ nguyên. Lịch và giờ được kiểm tra lại lúc lưu.";
                Notify(nameof(Notice)); SetStatus("Kiểm tra thông tin rồi xác nhận gia hạn.");
            }
            catch (Exception error) { Fail(error); }
            finally { Busy(false); }
        }
        public void Keep()
        { if (busy) return; confirming = false; preview = null; notice = null; Notify(nameof(Notice)); AccessChanged(); SetStatus("Chưa gia hạn; có thể chọn lại thời lượng."); }
        public async Task<bool> ConfirmAsync()
        {
            if (!CanConfirm) return false;
            var id = confirmedId; var end = preview.ExpectedEndTime; var count = confirmedMinutes; Busy(true);
            RoomSession saved = null;
            try { saved = await Task.Run(() => service.Extend(actor, id, end, count)); }
            catch (SessionExtensionException error) { Clear(); SetStatus(error.Message + " Hãy tải lại phiên."); }
            catch (Exception error) { Fail(error); }
            if (saved != null)
            {
                confirming = false; preview = null;
                try
                {
                    await ReloadAsync(id);
                    notice = "Đã gia hạn phiên #" + id + " đến " + StaffSession.Local(saved.ExpectedEndTime.Value)
                        + ". Giá giữ nguyên: " + saved.HourlyRate.ToString("N0") + " đ/giờ."; Notify(nameof(Notice));
                    SetStatus("Đã gia hạn thành công và tải lại danh sách.");
                }
                catch (UnauthorizedAccessException) { Clear(); SetStatus("Đã gia hạn thành công, nhưng không còn quyền xem. Hãy đóng cửa sổ."); }
                catch (Exception) { Clear(); SetStatus("Đã gia hạn thành công, nhưng chưa tải lại được danh sách. Hãy tải lại trước khi gia hạn thêm."); }
            }
            Busy(false); return saved != null;
        }
        private void Fail(Exception error)
        {
            Clear();
            if (error is UnauthorizedAccessException) SetStatus("Không còn quyền xem/gia hạn phiên hoặc phiên đăng nhập đã hết hiệu lực. Hãy tải lại để kiểm tra quyền.");
            else if (error is ArgumentException || error is InvalidOperationException) SetStatus(error.Message + " Hãy tải lại phiên.");
            else SetStatus("Chưa xử lý được yêu cầu. Hãy tải lại trước khi thử tiếp.");
        }
        private void SetStatus(string value) { status = value; Notify(nameof(Status)); }
        private void Busy(bool value) { busy = value; AccessChanged(); }
        private void AccessChanged()
        { foreach (var p in new[] { nameof(IsBusy), nameof(CanSearch), nameof(CanSelect), nameof(CanExtend), nameof(CanOpenBill), nameof(CanConfirm), nameof(IsConfirming), nameof(CanClose) }) Notify(p); }
        private void Notify([CallerMemberName] string property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
