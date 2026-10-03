using System;
using System.ComponentModel;
using System.Data.SQLite;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class RoomTypeEditViewModel : INotifyPropertyChanged
    {
        private readonly RoomTypeService service;
        private readonly LoginSession session;
        private readonly RoomType original;
        private bool isBusy;
        private string status;

        public string Code => original.Code;
        public string Name { get; set; }
        public string CapacityText { get; set; }
        public string PriceText { get; set; }
        public string Amenities { get; set; }
        public string Description { get; set; }
        public bool IsBusy => isBusy;
        public bool CanSave => !isBusy;
        public string Status => status;
        public event PropertyChangedEventHandler PropertyChanged;

        public RoomTypeEditViewModel(RoomTypeService service, LoginSession session, RoomType original)
        {
            this.service = service;
            this.session = session;
            this.original = original;
            Name = original.Name;
            CapacityText = original.Capacity.ToString(CultureInfo.InvariantCulture);
            PriceText = original.PricePerHour.ToString(CultureInfo.InvariantCulture);
            Amenities = original.Amenities;
            Description = original.Description;
        }

        public async Task<bool> SaveAsync()
        {
            if (isBusy) return false;
            if (!int.TryParse((CapacityText ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var capacity) || capacity <= 0)
            { SetStatus("Sức chứa phải là số nguyên lớn hơn 0."); return false; }
            if (!long.TryParse((PriceText ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var price) || price <= 0)
            { SetStatus("Giá mỗi giờ phải là số nguyên đồng lớn hơn 0, ví dụ 120000."); return false; }
            var changes = new RoomTypeEdit { Name = Name, Capacity = capacity, PricePerHour = price,
                Amenities = Amenities, Description = Description };
            isBusy = true;
            Notify(nameof(IsBusy));
            Notify(nameof(CanSave));
            SetStatus("Đang lưu loại phòng…");
            try
            {
                await Task.Run(() => service.Update(session, original, changes));
                SetStatus("Đã lưu loại phòng.");
                return true;
            }
            catch (ArgumentException error) { SetStatus(error.Message); }
            catch (UnauthorizedAccessException) { SetStatus("Bạn không còn quyền sửa loại phòng hoặc phiên đã hết hiệu lực. Hãy đóng cửa sổ và kiểm tra lại quyền."); }
            catch (InvalidOperationException error) { SetStatus(error.Message); }
            catch (SQLiteException error)
            {
                SetStatus(error.ResultCode == SQLiteErrorCode.Busy || error.ResultCode == SQLiteErrorCode.Locked
                    ? "Dữ liệu đang bận. Hãy thử lại sau."
                    : "Không lưu được loại phòng. Thay đổi đã được hoàn tác; hãy thử lại.");
            }
            catch (Exception) { SetStatus("Không truy cập được dữ liệu. Kiểm tra thư mục dữ liệu rồi thử lại."); }
            finally
            {
                isBusy = false;
                Notify(nameof(IsBusy));
                Notify(nameof(CanSave));
            }
            return false;
        }

        private void SetStatus(string value) { status = value; Notify(nameof(Status)); }
        private void Notify([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
