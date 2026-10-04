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
    public sealed class RoomsViewModel : INotifyPropertyChanged
    {
        private readonly RoomService service;
        private readonly LoginSession session;
        private bool isBusy;
        private bool canManage;
        private string status;
        private string imageFilePath;
        public ObservableCollection<Room> Rooms { get; } = new ObservableCollection<Room>();
        public ObservableCollection<RoomType> RoomTypes { get; }
        public string RoomCode { get; set; }
        public string Name { get; set; }
        public RoomType SelectedRoomType { get; set; }
        public string Description { get; set; }
        public string ImageFilePath
        {
            get => imageFilePath;
            set { imageFilePath = value; Notify(); }
        }
        public bool IsBusy => isBusy;
        public bool CanOperate => !isBusy;
        public bool CanAddRoom => !isBusy && canManage;
        public string Status => status;
        public event PropertyChangedEventHandler PropertyChanged;

        public RoomsViewModel(RoomService service, LoginSession session, System.Collections.Generic.List<RoomType> types)
        {
            this.service = service;
            this.session = session;
            RoomTypes = new ObservableCollection<RoomType>(types);
        }

        public async Task RefreshAsync()
        {
            if (isBusy) return;
            SetBusy(true);
            Rooms.Clear();
            try
            {
                var items = await Task.Run(() => service.ListForManagement(session));
                canManage = true;
                foreach (var item in items) Rooms.Add(item);
                SetStatus(items.Count == 0 ? "Chưa có phòng. Bấm Thêm phòng để bắt đầu." : "Đã tải " + items.Count + " phòng.");
            }
            catch (UnauthorizedAccessException) { canManage = false; SetStatus("Bạn không còn quyền quản lý phòng hoặc phiên đã hết hiệu lực."); }
            catch (Exception) { canManage = false; SetStatus("Không đọc được danh sách phòng. Hãy thử làm mới."); }
            finally { SetBusy(false); }
        }

        public async Task<bool> CreateAsync()
        {
            if (isBusy) return false;
            var input = new RoomCreate { RoomCode = RoomCode, Name = Name, RoomTypeId = SelectedRoomType?.RoomTypeId ?? 0,
                Description = Description, ImageFilePath = ImageFilePath };
            SetBusy(true);
            SetStatus("Đang kiểm tra ảnh và lưu phòng…");
            try
            {
                await Task.Run(() => service.Create(session, input));
                SetStatus("Đã thêm phòng.");
                return true;
            }
            catch (ArgumentException error) { SetStatus(error.Message); }
            catch (UnauthorizedAccessException) { SetStatus("Bạn không còn quyền quản lý phòng hoặc phiên đã hết hiệu lực."); }
            catch (SQLiteException error)
            {
                SetStatus(error.ResultCode == SQLiteErrorCode.Busy || error.ResultCode == SQLiteErrorCode.Locked
                    ? "Dữ liệu đang bận. Hãy thử lại sau."
                    : "Không lưu được phòng. Thay đổi đã được hoàn tác; hãy thử lại.");
            }
            catch (Exception) { SetStatus("Không đọc được ảnh hoặc lưu dữ liệu. Kiểm tra file ảnh và thư mục dữ liệu rồi thử lại."); }
            finally { SetBusy(false); }
            return false;
        }

        private void SetBusy(bool value) { isBusy = value; Notify(nameof(IsBusy)); Notify(nameof(CanOperate)); Notify(nameof(CanAddRoom)); }
        private void SetStatus(string value) { status = value; Notify(nameof(Status)); }
        private void Notify([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
