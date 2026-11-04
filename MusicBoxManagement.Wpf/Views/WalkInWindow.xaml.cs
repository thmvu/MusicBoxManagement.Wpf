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
        private async void Refresh_Click(object sender, RoutedEventArgs args) => await UserFeedback.RunAsync(this, () => viewModel.RefreshAsync(), () => viewModel.Status);
        private async void Receive_Click(object sender, RoutedEventArgs args) => await UserFeedback.RunAsync(this, () => viewModel.PrepareAsync(), () => viewModel.Status);
        private async void Confirm_Click(object sender, RoutedEventArgs args) => await UserFeedback.RunAsync(this, () => viewModel.ConfirmAsync(), () => viewModel.Status);
        private async void Keep_Click(object sender, RoutedEventArgs args) => await UserFeedback.RunAsync(this, () => viewModel.KeepEditing(), () => viewModel.Status);
        private async void New_Click(object sender, RoutedEventArgs args) => await viewModel.StartNewAsync();
        private void Close_Click(object sender, RoutedEventArgs args) => Close();
    }
}
