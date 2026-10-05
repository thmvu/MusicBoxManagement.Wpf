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
    public sealed class CustomersViewModel : INotifyPropertyChanged
    {
        private readonly CustomerService service;
        private readonly LoginSession session;
        private Customer original;
        private bool isBusy, canView, canCreate, canEdit;
        private string status;
        public ObservableCollection<Customer> Items { get; } = new ObservableCollection<Customer>();
        public string NameQuery { get; set; }
        public string PhoneQuery { get; set; }
        public string FullName { get; set; }
        public string PhoneNumber { get; set; }
        public bool IsBusy => isBusy;
        public bool CanSearch => !isBusy;
        public bool CanSelect => !isBusy && canView;
        public bool CanCreate => !isBusy && canView && canCreate;
        public bool CanSave => !isBusy && canView && (original == null ? canCreate : canEdit);
        public int? EditingId => original?.CustomerId;
        public string EditorTitle => original == null ? "Thêm khách hàng" : "Khách hàng #" + original.CustomerId;
        public string Status => status;
        public event PropertyChangedEventHandler PropertyChanged;
        public CustomersViewModel(CustomerService service, LoginSession session) { this.service = service; this.session = session; }
        public void Edit(Customer item)
        {
            if (isBusy) return;
            SetForm(item); SetStatus(item == null ? "Nhập khách hàng mới; chưa lưu dữ liệu." : "Chọn Lưu để cập nhật; Bỏ thay đổi không lưu.");
        }
        private void SetForm(Customer item)
        {
            original = item == null ? null : new Customer { CustomerId = item.CustomerId, FullName = item.FullName, PhoneNumber = item.PhoneNumber };
            FullName = item?.FullName; PhoneNumber = item?.PhoneNumber;
            foreach (var property in new[] { nameof(FullName), nameof(PhoneNumber), nameof(EditorTitle), nameof(EditingId), nameof(CanSave) }) Notify(property);
        }
        private async Task ReloadAsync(string name, string phone)
        {
            var result = await Task.Run(() => service.Search(session, name, phone));
            Items.Clear(); foreach (var item in result.Items) Items.Add(item);
            canView = true; canCreate = result.CanCreate; canEdit = result.CanEdit;
        }
        public async Task SearchAsync()
        {
            if (isBusy) return;
            var name = NameQuery; var phone = PhoneQuery; Busy(true);
            try { await ReloadAsync(name, phone); SetForm(null); SetStatus("Tìm thấy " + Items.Count + " khách hàng. SĐT tìm theo số đầy đủ đã chuẩn hóa."); }
            catch (ArgumentException error) { Items.Clear(); SetForm(null); SetStatus(error.Message); }
            catch (UnauthorizedAccessException) { Revoke(); }
            catch (Exception) { Revoke(); SetStatus("Không đọc được khách hàng. Hãy thử tìm lại."); }
            finally { Busy(false); }
        }
        public async Task<bool> SaveAsync()
        {
            if (!CanSave) return false;
            var input = new CustomerEdit { FullName = FullName, PhoneNumber = PhoneNumber }; var snapshot = original;
            Busy(true); SetStatus("Đang lưu khách hàng…");
            try
            {
                var saved = await Task.Run(() => service.Save(session, snapshot, input));
                SetForm(saved);
                // Reset filters so a renamed customer/new phone remains visible after a successful save.
                NameQuery = PhoneQuery = null; Notify(nameof(NameQuery)); Notify(nameof(PhoneQuery));
                try { await ReloadAsync(null, null); SetStatus("Đã lưu khách hàng; SĐT " + saved.PhoneNumber + "."); }
                catch (UnauthorizedAccessException) { Revoke(); SetStatus("Đã lưu, nhưng không còn quyền xem. Hãy đóng cửa sổ."); }
                catch (Exception) { Items.Clear(); SetStatus("Đã lưu, nhưng chưa tải lại được danh sách. Hãy bấm Tìm."); }
                return true;
            }
            catch (ArgumentException error) { SetStatus(error.Message); }
            catch (InvalidOperationException error) { SetStatus(error.Message); }
            catch (UnauthorizedAccessException) { Revoke(); }
            catch (SQLiteException error) { SetStatus(error.ResultCode == SQLiteErrorCode.Busy || error.ResultCode == SQLiteErrorCode.Locked
                ? "Dữ liệu đang bận. Hãy thử lại sau." : "Không lưu được khách hàng. Thay đổi đã hoàn tác."); }
            catch (Exception) { SetStatus("Không truy cập được dữ liệu. Hãy thử lại."); }
            finally { Busy(false); }
            return false;
        }
        private void Revoke() { canView = canCreate = canEdit = false; Items.Clear(); SetForm(null); SetStatus("Bạn không còn quyền thực hiện thao tác hoặc phiên đã hết hiệu lực. Hãy đóng cửa sổ hoặc tìm lại để kiểm tra quyền."); }
        private void Busy(bool value) { isBusy = value; foreach (var property in new[] { nameof(IsBusy), nameof(CanSearch), nameof(CanSelect), nameof(CanCreate), nameof(CanSave) }) Notify(property); }
        private void SetStatus(string value) { status = value; Notify(nameof(Status)); }
        private void Notify([CallerMemberName] string property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
