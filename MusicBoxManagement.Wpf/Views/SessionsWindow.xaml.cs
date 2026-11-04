using System.Windows;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class SessionsWindow : Window
    {
        private readonly SessionsViewModel viewModel;
        public SessionsWindow(SessionsViewModel viewModel)
        {
            InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel;
            Loaded += async (sender, args) => await viewModel.SearchAsync();
            Closing += (sender, args) => { if (viewModel.IsBusy) args.Cancel = true; };
        }
        private async void Search_Click(object sender, RoutedEventArgs args) => await UserFeedback.RunAsync(this, () => viewModel.SearchAsync(), () => viewModel.Status);
        private async void Bill_Click(object sender, RoutedEventArgs args)
        {
            if (!viewModel.CanOpenBill) return;
            new SessionBillWindow(viewModel.CreateBillViewModel()) { Owner = this }.ShowDialog();
            await viewModel.SearchAsync();
        }
        private async void Extend_Click(object sender, RoutedEventArgs args) => await UserFeedback.RunAsync(this, () => viewModel.PreviewAsync(), () => viewModel.Status);
        private async void Confirm_Click(object sender, RoutedEventArgs args) => await UserFeedback.RunAsync(this, () => viewModel.ConfirmAsync(), () => viewModel.Status);
        private async void Keep_Click(object sender, RoutedEventArgs args) => await UserFeedback.RunAsync(this, () => viewModel.Keep(), () => viewModel.Status);
    }
}
