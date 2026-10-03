using System.Windows;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class RoomTypeEditWindow : Window
    {
        private readonly RoomTypeEditViewModel viewModel;
        public RoomTypeEditWindow(RoomTypeEditViewModel viewModel)
        {
            InitializeComponent();
            this.viewModel = viewModel;
            DataContext = viewModel;
            Closing += (sender, args) => { if (viewModel.IsBusy) args.Cancel = true; };
            Loaded += (sender, args) => NameInput.Focus();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (await viewModel.SaveAsync()) DialogResult = true;
        }
    }
}
