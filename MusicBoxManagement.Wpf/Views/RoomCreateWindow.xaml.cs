using System.Windows;
using Microsoft.Win32;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class RoomCreateWindow : Window
    {
        private readonly RoomsViewModel viewModel;
        public RoomCreateWindow(RoomsViewModel viewModel)
        {
            InitializeComponent();
            this.viewModel = viewModel;
            DataContext = viewModel;
            Closing += (sender, args) => { if (viewModel.IsBusy) args.Cancel = true; };
            Loaded += (sender, args) => CodeInput.Focus();
        }

        private void ChooseImage_Click(object sender, RoutedEventArgs e)
        {
            var picker = new OpenFileDialog { Title = "Chọn ảnh phòng", Filter = "Ảnh phòng|*.jpg;*.jpeg;*.png;*.webp|Tất cả file|*.*", Multiselect = false };
            if (picker.ShowDialog(this) == true) viewModel.ImageFilePath = picker.FileName;
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (await viewModel.CreateAsync()) DialogResult = true;
        }
    }
}
