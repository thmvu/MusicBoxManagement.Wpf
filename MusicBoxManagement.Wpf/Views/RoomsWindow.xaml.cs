using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class RoomsWindow : Window
    {
        private readonly RoomService service;
        private readonly LoginSession session;
        private readonly RoomTypeService roomTypes;
        private readonly RoomsViewModel viewModel;
        private bool isOpeningEditor;
        public RoomsWindow(RoomService service, LoginSession session, RoomTypeService roomTypes)
        {
            InitializeComponent();
            this.service = service;
            this.session = session;
            this.roomTypes = roomTypes;
            viewModel = new RoomsViewModel(service, session, new System.Collections.Generic.List<RoomType>());
            DataContext = viewModel;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e) => await viewModel.RefreshAsync();
        private async void Refresh_Click(object sender, RoutedEventArgs e) => await viewModel.RefreshAsync();

        private async void AddRoom_Click(object sender, RoutedEventArgs e)
        {
            if (isOpeningEditor || viewModel.IsBusy) return;
            isOpeningEditor = true;
            AddRoomButton.IsEnabled = false;
            try
            {
                // Check current permission before opening a form; Create checks it again when saving.
                await Task.Run(() => service.ListForManagement(session));
                var types = await Task.Run(() => roomTypes.List());
                if (!IsVisible) return;
                var editor = new RoomCreateWindow(new RoomsViewModel(service, session, types)) { Owner = this };
                if (editor.ShowDialog() == true) await viewModel.RefreshAsync();
            }
            catch (UnauthorizedAccessException)
            {
                if (IsVisible)
                {
                    await viewModel.RefreshAsync();
                    MessageBox.Show(this, "Bạn không còn quyền quản lý phòng hoặc phiên đã hết hiệu lực.", "Music Box");
                }
            }
            catch (Exception) { if (IsVisible) MessageBox.Show(this, "Không mở được form thêm phòng. Hãy làm mới và thử lại.", "Music Box"); }
            finally
            {
                isOpeningEditor = false;
                AddRoomButton.SetBinding(IsEnabledProperty, new System.Windows.Data.Binding("CanAddRoom"));
            }
        }

        private void Room_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RoomImage.Source = null;
            if (!(RoomsTable.SelectedItem is Room room))
            { RoomDescription.Text = "Chọn một phòng để xem ảnh và mô tả."; return; }
            RoomDescription.Text = room.Description ?? "Chưa có mô tả.";
            try
            {
                using (var stream = File.OpenRead(service.GetImagePath(room.ImageUrl)))
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.DecodePixelWidth = 400;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    RoomImage.Source = image;
                }
            }
            catch (Exception) { RoomDescription.Text += "\nKhông đọc được ảnh đã lưu."; }
        }
    }
}
