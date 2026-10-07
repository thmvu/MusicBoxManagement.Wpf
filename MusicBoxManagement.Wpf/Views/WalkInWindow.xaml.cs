using System.Windows;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class WalkInWindow : Window
    {
        private readonly WalkInViewModel viewModel;
        public WalkInWindow(WalkInViewModel viewModel)
        {
            InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel;
            Loaded += async (sender, args) => await viewModel.RefreshAsync();
            Closing += (sender, args) => { if (viewModel.IsBusy) args.Cancel = true; };
        }
        private async void Refresh_Click(object sender, RoutedEventArgs args) => await viewModel.RefreshAsync();
        private async void Receive_Click(object sender, RoutedEventArgs args) => await viewModel.PrepareAsync();
        private async void Confirm_Click(object sender, RoutedEventArgs args) => await viewModel.ConfirmAsync();
        private void Keep_Click(object sender, RoutedEventArgs args) => viewModel.KeepEditing();
        private async void New_Click(object sender, RoutedEventArgs args) => await viewModel.StartNewAsync();
        private void Close_Click(object sender, RoutedEventArgs args) => Close();
    }
}
