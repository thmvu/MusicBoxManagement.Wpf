using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class WalkInViewModel : INotifyPropertyChanged
    {
        private readonly RoomSessionService service;
        private readonly LoginSession actor;
        private bool busy, denied, saved, confirming;
        private PublicRoom room;
        private string name, phone, status, details;
        private WalkInRequest draft;
        private int? createdId;
        public ObservableCollection<PublicRoom> Rooms { get; } = new ObservableCollection<PublicRoom>();
        public PublicRoom SelectedRoom { get => room; set { room = value; Change(); } }
        public string FullName { get => name; set { name = value; Change(); } }
        public string PhoneNumber { get => phone; set { phone = value; Change(); } }
        public string Status => status;
        public string Details => details;
        public bool IsBusy => busy;
        public bool CanInput => !busy && !denied && !saved && !confirming;
        public bool CanReceive => CanInput && room != null;
        public bool CanConfirm => !busy && confirming && !denied && !saved;
        public bool CanRefresh => !busy && !denied && !confirming;
        public bool CanNew => !busy && saved && !denied;
        public bool CanClose => !busy;
        public bool IsConfirming => confirming;
        public int? CreatedSessionId => createdId;
        public event PropertyChangedEventHandler PropertyChanged;
        public WalkInViewModel(RoomSessionService service, LoginSession actor) { this.service = service; this.actor = actor; }
        private void Change([CallerMemberName] string property = null)
        { Notify(property); Notify(nameof(CanReceive)); }
        public async Task RefreshAsync()
        {
            if (!CanRefresh) return; SetBusy(true);
            try
            {
                if (saved) ShowReceipt(await Task.Run(() => service.ReadWalkInReceipt(actor, createdId.Value)));
                else
                {
                    var id = room?.RoomId;
                    var rows = await Task.Run(() => service.ListWalkInRooms(actor));
                    Rooms.Clear(); foreach (var row in rows) Rooms.Add(row);
                    SelectedRoom = Rooms.FirstOrDefault(r => r.RoomId == id) ?? Rooms.FirstOrDefault();
                    SetStatus(Rooms.Count == 0 ? "Chưa có phòng đang mở. Hãy thêm hoặc mở phòng trước." : "Chọn phòng và nhập tên, SĐT khách đến trực tiếp.");
                }
            }
            catch (Exception error) { Fail(error); if (!saved) { Rooms.Clear(); SelectedRoom = null; } }
            finally { SetBusy(false); }
        }
        public async Task PrepareAsync()
        {
            if (!CanReceive) return; SetBusy(true);
            try
            {
                var input = new WalkInRequest { RoomId = room.RoomId, FullName = name, PhoneNumber = phone };
                var preview = await Task.Run(() => service.PreviewWalkIn(actor, input));
                if (!preview.CanReceive) { SetStatus(preview.Reason); return; }
                draft = input; confirming = true;
                details = "Phòng " + room.RoomCode + " · Giá tham khảo " + preview.HourlyRate.ToString("N0") + " đ/giờ\nGiờ cần trả phòng: " + Time(preview.ReturnBy.Value)
                    + "\nKiểm tra lúc " + Time(preview.CheckedAt) + ". Giá, giờ nhận và lịch sẽ được kiểm tra lại lúc xác nhận.";
                Notify(nameof(Details)); SetStatus("Xác nhận nhận khách ngay? Không tạo booking hoặc chọn thời lượng.");
            }
            catch (Exception error) { Fail(error); }
            finally { SetBusy(false); }
        }
        public void KeepEditing()
        { if (!CanConfirm) return; confirming = false; draft = null; details = null; Notify(nameof(Details)); SetStatus("Tiếp tục chỉnh thông tin, chưa nhận khách."); SetBusy(false); }
        public async Task ConfirmAsync()
        {
            if (!CanConfirm) return; SetBusy(true);
            try
            {
                var result = await Task.Run(() => service.CreateWalkIn(actor, draft));
                saved = true; confirming = false; draft = null; createdId = result.Session.RoomSessionId;
                Notify(nameof(CreatedSessionId)); ShowReceipt(result);
                // Keep the committed receipt even if refreshing the room catalog fails.
                try { var rows = await Task.Run(() => service.ListWalkInRooms(actor)); Rooms.Clear(); foreach (var row in rows) Rooms.Add(row); }
                catch (UnauthorizedAccessException) { Deny(); SetStatus("Đã nhận khách thành công; quyền đã thay đổi. Đóng form và kiểm tra lại quyền."); }
                catch (Exception) { SetStatus("Đã nhận khách thành công. Chưa tải lại được phòng; không bấm nhận lần nữa."); }
            }
            catch (Exception error) { confirming = false; draft = null; details = null; Notify(nameof(Details)); Fail(error); }
            finally { SetBusy(false); }
        }
        public async Task StartNewAsync()
        {
            if (!CanNew) return;
            saved = false; createdId = null; FullName = PhoneNumber = null; details = null; Notify(nameof(Details)); SetBusy(false);
            await RefreshAsync();
        }
        private void ShowReceipt(WalkInResult result)
        {
            var s = result.Session;
            details = "Phiên #" + s.RoomSessionId + " · " + s.RoomCodeSnapshot + " · " + s.RoomTypeNameSnapshot
                + "\nNhận thực tế: " + Time(s.ActualStartTime) + "\nGiá đã chốt: " + s.HourlyRate.ToString("N0") + " đ/giờ"
                + "\nGIỜ CẦN TRẢ PHÒNG: " + Time(result.ReturnBy) + "\nCập nhật lúc " + Time(result.CheckedAt)
                + (result.IsOverdue ? " · ĐÃ QUÁ GIỜ, nhân viên cần xử lý trả phòng." : "")
                + "\nMốc trả thay đổi theo ca và booking kế tiếp của phòng/khách. Bấm Cập nhật để đọc lại; phiên vẫn Active đến khi trả phòng.";
            Notify(nameof(Details)); SetStatus("Đã nhận khách trực tiếp thành công. Trạng thái: Active (đang sử dụng).");
        }
        private static string Time(DateTimeOffset time) => time.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy");
        private void Deny()
        { denied = true; confirming = false; draft = null; Rooms.Clear(); SelectedRoom = null; FullName = PhoneNumber = null; details = null; Notify(nameof(Details)); }
        private void Fail(Exception error)
        {
            if (error is UnauthorizedAccessException) { Deny(); SetStatus(saved ? "Phiên đã được tạo; không còn quyền đọc kết quả. Hãy đóng form." : "Không còn quyền nhận khách trực tiếp hoặc phiên đăng nhập đã hết hiệu lực. Chưa nhận khách."); }
            else if (error is ArgumentException || error is InvalidOperationException) SetStatus(error.Message);
            else SetStatus(saved ? "Phiên đã được tạo. Chưa cập nhật được giờ trả; hãy thử lại, không nhận khách lần nữa." : "Chưa xử lý được nhận khách. Hãy thử lại sau.");
        }
        private void SetStatus(string value) { status = value; Notify(nameof(Status)); }
        private void SetBusy(bool value)
        { busy = value; foreach (var p in new[] { nameof(IsBusy), nameof(CanInput), nameof(CanReceive), nameof(CanConfirm), nameof(CanRefresh), nameof(CanNew), nameof(CanClose), nameof(IsConfirming) }) Notify(p); }
        private void Notify([CallerMemberName] string property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
