using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Data.SQLite;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.ViewModels
{
    public sealed class RoomEditViewModel : INotifyPropertyChanged
    {
        private readonly RoomService service;
        private readonly LoginSession session;
        private readonly Room original;
        private bool isBusy;
        private string status;
        private string replacementImageFilePath;
        public string RoomCode => original.RoomCode;
        public string RoomTypeName => original.RoomTypeName;
        public ObservableCollection<RoomType> RoomTypes { get; }
        public RoomType SelectedRoomType { get; set; }
        public bool IsActive { get; set; }
        public string InactiveReason { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string ReplacementImageFilePath
        {
            get => replacementImageFilePath;
            set { replacementImageFilePath = value; Notify(); }
        }
        public bool IsBusy => isBusy;
        public bool CanSave => !isBusy;
        public string Status => status;
        public event PropertyChangedEventHandler PropertyChanged;

        public RoomEditViewModel(RoomService service, LoginSession session, Room original, List<RoomType> types = null)
        {
            this.service = service;
            this.session = session;
            this.original = original;
            Name = original.Name;
            Description = original.Description;
            RoomTypes = new ObservableCollection<RoomType>(types ?? new List<RoomType> {
                new RoomType { RoomTypeId = original.RoomTypeId, Name = original.RoomTypeName } });
            SelectedRoomType = RoomTypes.FirstOrDefault(x => x.RoomTypeId == original.RoomTypeId);
            IsActive = original.IsActive;
            InactiveReason = original.InactiveReason;
        }

        public async Task<bool> SaveAsync()
        {
            if (isBusy) return false;
            var input = new RoomEdit { Name = Name, Description = Description, ReplacementImageFilePath = ReplacementImageFilePath,
                RoomTypeId = SelectedRoomType?.RoomTypeId ?? 0, IsActive = IsActive, InactiveReason = InactiveReason };
            isBusy = true; Notify(nameof(IsBusy)); Notify(nameof(CanSave)); SetStatus("Đang lưu phòng…");
            try
            {
                await Task.Run(() => service.Update(session, original, input));
                SetStatus("Đã lưu phòng.");
                return true;
            }
            catch (ArgumentException error) { SetStatus(error.Message); }
            catch (UnauthorizedAccessException) { SetStatus("Bạn không còn quyền quản lý phòng hoặc phiên đã hết hiệu lực."); }
            catch (InvalidOperationException error) { SetStatus(error.Message); }
            catch (SQLiteException error)
            {
                SetStatus(error.ResultCode == SQLiteErrorCode.Busy || error.ResultCode == SQLiteErrorCode.Locked
                    ? "Dữ liệu đang bận. Hãy thử lại sau." : "Không lưu được phòng. Thay đổi đã được hoàn tác; hãy thử lại.");
            }
            catch (Exception) { SetStatus("Không đọc được ảnh hoặc lưu dữ liệu. Kiểm tra file ảnh và thư mục dữ liệu rồi thử lại."); }
            finally { isBusy = false; Notify(nameof(IsBusy)); Notify(nameof(CanSave)); }
            return false;
        }

        private void SetStatus(string value) { status = value; Notify(nameof(Status)); }
        private void Notify([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
