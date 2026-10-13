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
    public sealed class AccountsViewModel : INotifyPropertyChanged
    {
        private readonly AccountManagementService service;
        private readonly LoginSession actor;
        private bool busy, access, active = true;
        private string mode = "", name, full, role = "Staff", status = "Đang tải tài khoản…";
        private UserAccount selected;
        private AccountEdit pending;
        private bool confirming;
        public AccountsViewModel(AccountManagementService service, LoginSession actor) { this.service = service; this.actor = actor; }
        public ObservableCollection<UserAccount> Items { get; } = new ObservableCollection<UserAccount>();
        public string[] Roles { get; } = new[] { "Staff", "Manager", "Admin" };
        public string UserName { get => name; set { if (!CanInput) return; name = value; Notify(); } }
        public string FullName { get => full; set { if (!CanInput) return; full = value; Notify(); } }
        public string Role { get => role; set { if (!CanInput) return; role = value; Notify(); } }
        public bool IsActive { get => active; set { if (!CanInput) return; active = value; Notify(); } }
        public UserAccount Selected => selected;
        public bool IsBusy => busy;
        public bool RequiresRelogin { get; private set; }
        public bool CanReload => !busy && !RequiresRelogin;
        public bool CanSelect => !busy && access && !confirming && !RequiresRelogin;
        public bool CanNew => CanSelect;
        public bool CanReset => CanSelect && selected != null;
        public bool CanInput => !busy && access && !confirming && (mode == "Create" || mode == "Edit") && !RequiresRelogin;
        public bool CanChangeRoleState => CanInput && selected?.UserId != actor.UserId;
        public bool HasForm => mode != "" && access;
        public bool NeedsPassword => mode == "Create" || mode == "Reset";
        public bool CanPassword => !busy && access && NeedsPassword && !RequiresRelogin;
        public bool CanPrepare => !busy && access && !confirming && mode != "" && !RequiresRelogin;
        public bool CanConfirm => !busy && access && confirming && !RequiresRelogin;
        public bool IsConfirming => confirming;
        public string FormTitle => mode == "Create" ? "Thêm nhân viên" : mode == "Reset" ? "Đặt lại mật khẩu: " + selected?.UserName : "Sửa tài khoản";
        public string FormIdentity => mode + ":" + selected?.UserId;
        public string Confirmation => !confirming ? "" : mode == "Reset" ? "Đặt lại mật khẩu cho " + selected.UserName + "? Phiên đăng nhập cũ sẽ mất hiệu lực."
            : (mode == "Create" ? "Tạo " : "Cập nhật ") + pending.UserName + " — " + pending.FullName + ", " + pending.Role + ", " + (pending.IsActive ? "đang hoạt động" : "đã khóa") + "?";
        public string Status => status;
        public event PropertyChangedEventHandler PropertyChanged;
        public async Task ReloadAsync()
        {
            if (!CanReload) return; busy = true; Clear(); Changed();
            try { await LoadAsync(); status = "Có " + Items.Count + " tài khoản. Chọn để sửa/khóa/gán role, hoặc Thêm nhân viên."; }
            catch (Exception error) { Fail(error); }
            finally { busy = false; Changed(); }
        }
        private async Task LoadAsync()
        { var rows = await Task.Run(() => service.List(actor)); foreach (var row in rows) Items.Add(row); access = true; }
        public void Select(UserAccount value)
        {
            if (!CanSelect || selected == value) return; selected = value; pending = null; confirming = false;
            mode = value == null ? "" : "Edit"; name = value?.UserName; full = value?.FullName; role = value?.Role ?? "Staff"; active = value?.IsActive ?? true; Changed();
        }
        public void StartNew()
        { if (!CanNew) return; selected = null; mode = "Create"; name = full = ""; role = "Staff"; active = true; Changed(); }
        public void StartReset()
        { if (!CanReset) return; mode = "Reset"; pending = null; confirming = false; Changed(); }
        public void Keep()
        { if (busy) return; confirming = false; pending = null; status = "Chưa lưu thay đổi. Có thể sửa lại rồi xác nhận."; Changed(); }
        public async Task PrepareAsync()
        {
            if (!CanPrepare) return;
            var edit = new AccountEdit { UserName = name, FullName = full, Role = role, IsActive = active };
            busy = true; Changed();
            try
            {
                var rows = await Task.Run(() => service.List(actor));
                if (selected != null && !rows.Any(u => u.UserId == selected.UserId && u.Revision == selected.Revision))
                    throw new InvalidOperationException("Tài khoản đã thay đổi. Hãy tải lại danh sách.");
                pending = edit; confirming = true; status = "Kiểm tra nội dung rồi bấm Xác nhận lưu, hoặc Quay lại.";
            }
            catch (Exception error) { Fail(error); }
            finally { busy = false; Changed(); }
        }
        public async Task<bool> ConfirmAsync(string password)
        {
            if (!CanConfirm) return false;
            var operation = mode; var original = selected; var input = pending; busy = true; Changed();
            UserAccount saved;
            try
            {
                saved = await Task.Run(async () => operation == "Create" ? await service.CreateAsync(actor, input, password)
                    : operation == "Reset" ? await service.ResetPasswordAsync(actor, original, password) : await service.UpdateAsync(actor, original, input));
            }
            catch (Exception error) { Fail(error); busy = false; Changed(); return false; }
            var notice = operation == "Reset" ? "Đã đặt lại mật khẩu cho " + saved.UserName + "." : "Đã lưu tài khoản " + saved.UserName + ".";
            Clear();
            if (operation == "Reset" && saved.UserId == actor.UserId)
            { RequiresRelogin = true; status = notice + " Phiên hiện tại đã hết hiệu lực. Đóng cửa sổ và đăng nhập lại bằng mật khẩu mới."; }
            else
            {
                try { await LoadAsync(); status = notice; }
                catch { Clear(); status = notice + " Chưa tải lại được danh sách; hãy thử Làm mới. Không gửi lại thao tác vừa lưu."; }
            }
            busy = false; Changed(); return true;
        }
        public void PasswordError() { status = "Hai mật khẩu chưa khớp. Nhập lại trước khi xác nhận."; Changed(); }
        private void Clear() { selected = null; pending = null; confirming = false; mode = ""; name = full = ""; role = "Staff"; active = true; Items.Clear(); access = false; }
        private void Fail(Exception error)
        {
            Clear(); status = error is UnauthorizedAccessException ? "Không còn quyền quản trị hoặc phiên đã hết hiệu lực. Hãy đóng/đăng nhập lại."
                : error is ArgumentException || error is InvalidOperationException ? error.Message + " Hãy tải lại trước khi thử tiếp."
                : "Chưa lưu được tài khoản. Hãy tải lại để kiểm tra; không giữ dữ liệu form cũ.";
        }
        private void Changed()
        { foreach (var property in new[] { nameof(Selected), nameof(UserName), nameof(FullName), nameof(Role), nameof(IsActive), nameof(IsBusy), nameof(RequiresRelogin), nameof(CanReload), nameof(CanSelect), nameof(CanNew), nameof(CanReset), nameof(CanInput), nameof(CanChangeRoleState), nameof(HasForm), nameof(NeedsPassword), nameof(CanPassword), nameof(CanPrepare), nameof(CanConfirm), nameof(IsConfirming), nameof(FormTitle), nameof(FormIdentity), nameof(Confirmation), nameof(Status) }) Notify(property); }
        private void Notify([CallerMemberName] string property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
