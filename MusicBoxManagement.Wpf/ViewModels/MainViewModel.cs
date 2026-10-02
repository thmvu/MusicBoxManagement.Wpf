using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        private readonly RoomTypeService service;
        private bool isBusy;
        private string status = "Đang chuẩn bị danh mục…";

        public ObservableCollection<RoomType> RoomTypes { get; } = new ObservableCollection<RoomType>();
        public bool CanRefresh => !isBusy;
        public string Status => status;
        public event PropertyChangedEventHandler PropertyChanged;

        public MainViewModel(RoomTypeService service)
        {
            this.service = service;
        }

        public async Task RefreshAsync()
        {
            if (isBusy) return;
            isBusy = true;
            status = "Đang đọc danh mục…";
            Notify(nameof(CanRefresh));
            Notify(nameof(Status));
            try
            {
                var items = await Task.Run(() => service.List());
                RoomTypes.Clear();
                foreach (var item in items) RoomTypes.Add(item);
                status = "Đã tải " + items.Count + " loại phòng từ dữ liệu trên máy.";
            }
            catch (Exception error)
            {
                status = "Không đọc được danh mục. Kiểm tra quyền truy cập thư mục dữ liệu và thử làm mới.";
                System.Diagnostics.Trace.TraceError(error.ToString());
            }
            finally
            {
                isBusy = false;
                Notify(nameof(CanRefresh));
                Notify(nameof(Status));
            }
        }

        private void Notify([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
