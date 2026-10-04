using System.Windows;
using Microsoft.Win32;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class RoomEditWindow : Window
    {
        private readonly RoomEditViewModel viewModel;
        public RoomEditWindow(RoomEditViewModel viewModel)
        {
            InitializeComponent();
            this.viewModel = viewModel;
            DataContext = viewModel;
            Closing += (sender, args) => { if (viewModel.IsBusy) args.Cancel = true; };
            Loaded += (sender, args) => NameInput.Focus();
        }
        private void ChooseImage_Click(object sender, RoutedEventArgs e)
        {
            var picker = new OpenFileDialog { Title = "Chọn ảnh phòng mới", Filter = "Ảnh phòng|*.jpg;*.jpeg;*.png;*.webp|Tất cả file|*.*", Multiselect = false };
            if (picker.ShowDialog(this) == true) viewModel.ReplacementImageFilePath = picker.FileName;
        }
        private void KeepImage_Click(object sender, RoutedEventArgs e) => viewModel.ReplacementImageFilePath = null;
        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (await viewModel.SaveAsync()) DialogResult = true;
        }
    }
}
