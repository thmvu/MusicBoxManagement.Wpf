using System.Windows;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class CheckoutWindow : Window
    {
        private readonly CheckoutViewModel viewModel;
        public CheckoutWindow(CheckoutViewModel viewModel)
        {
            InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel;
            Loaded += async (sender, args) => await viewModel.ReloadAsync();
            Closing += (sender, args) => { if (viewModel.IsBusy) args.Cancel = true; };
        }
        private async void Reload_Click(object sender, RoutedEventArgs args) => await UserFeedback.RunAsync(this, () => viewModel.ReloadAsync(), () => viewModel.Status);
        private async void Preview_Click(object sender, RoutedEventArgs args) => await UserFeedback.RunAsync(this, () => viewModel.PreviewAsync(), () => viewModel.Status);
        private async void Confirm_Click(object sender, RoutedEventArgs args) => await UserFeedback.RunAsync(this, () => viewModel.ConfirmAsync(), () => viewModel.Status);
        private async void Keep_Click(object sender, RoutedEventArgs args) => await UserFeedback.RunAsync(this, () => viewModel.Keep(), () => viewModel.Status);
        private void Close_Click(object sender, RoutedEventArgs args) { if (!viewModel.IsBusy) Close(); }
    }
}
