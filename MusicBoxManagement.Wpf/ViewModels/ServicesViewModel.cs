using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class ServicesViewModel : INotifyPropertyChanged
    {
        private readonly ServiceCatalogService service;
        private readonly LoginSession session;
        private ServiceItem original;
        private bool isBusy;
        private bool canManage;
        private string status;
        public ObservableCollection<ServiceItem> Items { get; } = new ObservableCollection<ServiceItem>();
        public System.Collections.Generic.IReadOnlyList<string> Categories => ServiceCatalogService.Categories;
        public string Name { get; set; }
        public string Category { get; set; }
        public string PriceText { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
        public bool IsBusy => isBusy;
        public bool CanRefresh => !isBusy;
        public bool CanEdit => !isBusy && canManage;
        public int? EditingId => original?.ServiceId;
        public string EditorTitle => original == null ? "Thêm dịch vụ" : "Sửa dịch vụ #" + original.ServiceId;
        public string Status => status;
        public event PropertyChangedEventHandler PropertyChanged;
        public ServicesViewModel(ServiceCatalogService service, LoginSession session) { this.service = service; this.session = session; SetForm(null); }

        public void Edit(ServiceItem item) { if (!isBusy) { SetForm(item); SetStatus(item == null ? "Nhập dịch vụ mới; chưa lưu vào dữ liệu." : "Sửa thông tin rồi bấm Lưu. Bỏ thay đổi không lưu."); } }
        private void SetForm(ServiceItem item)
        {
            original = item;
            Name = item?.Name; Category = item?.Category ?? Categories[0]; PriceText = item?.Price.ToString(CultureInfo.InvariantCulture);
            Description = item?.Description; IsActive = item?.IsActive ?? true;
            foreach (var property in new[] { nameof(Name), nameof(Category), nameof(PriceText), nameof(Description), nameof(IsActive), nameof(EditorTitle), nameof(EditingId) }) Notify(property);
        }
        private async Task ReloadAsync()
        {
            var items = await Task.Run(() => service.ListForManagement(session));
            Items.Clear(); foreach (var item in items) Items.Add(item);
            canManage = true;
        }
        public async Task RefreshAsync()
        {
            if (isBusy) return;
            Busy(true);
            try { await ReloadAsync(); SetForm(null); SetStatus("Đã tải " + Items.Count + " dịch vụ."); }
            catch (UnauthorizedAccessException) { Revoke(); }
            catch (Exception) { canManage = false; Items.Clear(); SetForm(null); SetStatus("Không đọc được dịch vụ. Hãy thử làm mới."); }
            finally { Busy(false); }
        }
        public async Task<bool> SaveAsync()
        {
            if (!CanEdit) return false;
            if (!long.TryParse((PriceText ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var price) || price <= 0)
            { SetStatus("Giá phải là số nguyên đồng lớn hơn 0, ví dụ 15000."); return false; }
            var input = new ServiceEdit { Name = Name, Category = Category, Price = price, Description = Description, IsActive = IsActive };
            var snapshot = original;
            Busy(true); SetStatus("Đang lưu dịch vụ…");
            try
            {
                var id = await Task.Run(() => service.Save(session, snapshot, input));
                // Commit has succeeded. A reload error must not suggest saving a new service again.
                SetForm(new ServiceItem { ServiceId = id, Name = (input.Name ?? "").Trim(), Category = input.Category, Price = price,
                    Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(), IsActive = input.IsActive });
                try { await ReloadAsync(); SetForm(Items.Single(x => x.ServiceId == id)); SetStatus("Đã lưu dịch vụ."); }
                catch (UnauthorizedAccessException) { Revoke(); SetStatus("Đã lưu, nhưng không còn quyền đọc danh sách. Hãy đóng cửa sổ."); }
                catch (Exception) { SetStatus("Đã lưu, nhưng chưa tải lại được danh sách. Hãy bấm Làm mới."); }
                return true;
            }
            catch (ArgumentException error) { SetStatus(error.Message); }
            catch (InvalidOperationException error) { SetStatus(error.Message); }
            catch (UnauthorizedAccessException) { Revoke(); }
            catch (SQLiteException error) { SetStatus(error.ResultCode == SQLiteErrorCode.Busy || error.ResultCode == SQLiteErrorCode.Locked
                ? "Dữ liệu đang bận. Hãy thử lại sau." : "Không lưu được dịch vụ. Thay đổi đã hoàn tác; hãy thử lại."); }
            catch (Exception) { SetStatus("Không truy cập được dữ liệu. Hãy thử lại."); }
            finally { Busy(false); }
            return false;
        }
        private void Revoke() { canManage = false; Items.Clear(); SetForm(null); SetStatus("Bạn không còn quyền quản lý dịch vụ hoặc phiên đã hết hiệu lực."); }
        private void Busy(bool value) { isBusy = value; Notify(nameof(IsBusy)); Notify(nameof(CanRefresh)); Notify(nameof(CanEdit)); }
        private void SetStatus(string value) { status = value; Notify(nameof(Status)); }
        private void Notify([CallerMemberName] string property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
