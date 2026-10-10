using System.Windows;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class SessionBillWindow : Window
    {
        private readonly SessionBillViewModel viewModel;
        public SessionBillWindow(SessionBillViewModel viewModel)
        {
            InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel;
            Loaded += async (sender, args) => await viewModel.RefreshAsync();
            Closing += (sender, args) => { if (viewModel.IsBusy) args.Cancel = true; };
        }
        private async void Refresh_Click(object sender, RoutedEventArgs args) => await viewModel.RefreshAsync();
        private void Close_Click(object sender, RoutedEventArgs args) { if (!viewModel.IsBusy) Close(); }
    }
}
