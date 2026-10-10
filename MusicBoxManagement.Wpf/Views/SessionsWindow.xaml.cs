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
        private async void Search_Click(object sender, RoutedEventArgs args) => await viewModel.SearchAsync();
        private async void Bill_Click(object sender, RoutedEventArgs args)
        {
            if (!viewModel.CanOpenBill) return;
            new SessionBillWindow(viewModel.CreateBillViewModel()) { Owner = this }.ShowDialog();
            await viewModel.SearchAsync();
        }
        private async void Extend_Click(object sender, RoutedEventArgs args) => await viewModel.PreviewAsync();
        private async void Confirm_Click(object sender, RoutedEventArgs args) => await viewModel.ConfirmAsync();
        private void Keep_Click(object sender, RoutedEventArgs args) => viewModel.Keep();
    }
}
