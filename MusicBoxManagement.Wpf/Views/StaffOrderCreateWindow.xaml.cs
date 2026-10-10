using System.Windows;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class StaffOrderCreateWindow : Window
    {
        private readonly StaffOrderCreateViewModel viewModel;
        public StaffOrderCreateWindow(OrderService service, LoginSession actor, OrderSessionChoice session)
        {
            InitializeComponent(); viewModel = new StaffOrderCreateViewModel(service, actor, session); DataContext = viewModel;
            Loaded += async (sender, args) => await viewModel.ReloadAsync(); Closing += (sender, args) => { if (viewModel.IsBusy) args.Cancel = true; };
        }
        private async void Reload_Click(object sender, RoutedEventArgs args) => await viewModel.ReloadAsync();
        private void Add_Click(object sender, RoutedEventArgs args) => viewModel.Add();
        private void Remove_Click(object sender, RoutedEventArgs args) => viewModel.Remove();
        private async void Preview_Click(object sender, RoutedEventArgs args) => await viewModel.PreviewAsync();
        private async void Confirm_Click(object sender, RoutedEventArgs args) => await viewModel.ConfirmAsync();
        private void Keep_Click(object sender, RoutedEventArgs args) => viewModel.Keep();
    }
}
