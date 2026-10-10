using System.Windows;
using System.Windows.Controls;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class StaffOrdersWindow : Window
    {
        private readonly OrderService service;
        private readonly LoginSession actor;
        private readonly StaffOrdersViewModel viewModel;
        private bool openingCreate;
        public StaffOrdersWindow(OrderService service, LoginSession actor)
        {
            InitializeComponent(); this.service = service; this.actor = actor;
            viewModel = new StaffOrdersViewModel(service, actor); DataContext = viewModel;
            Loaded += async (sender, args) => await viewModel.ReloadAsync();
            Closing += (sender, args) => { if (viewModel.IsBusy || openingCreate) args.Cancel = true; };
        }
        private async void Reload_Click(object sender, RoutedEventArgs args) => await viewModel.ReloadAsync();
        private async void Session_Changed(object sender, SelectionChangedEventArgs args) => await viewModel.LoadSelectedAsync();
        private void Serve_Click(object sender, RoutedEventArgs args) => viewModel.RequestServe();
        private void Cancel_Click(object sender, RoutedEventArgs args) => viewModel.RequestCancel();
        private void Keep_Click(object sender, RoutedEventArgs args) => viewModel.Keep();
        private async void Confirm_Click(object sender, RoutedEventArgs args) => await viewModel.ConfirmAsync();
        private async void Create_Click(object sender, RoutedEventArgs args)
        {
            if (openingCreate || !viewModel.CanCreate) return; openingCreate = true;
            try { new StaffOrderCreateWindow(service, actor, viewModel.SelectedSession) { Owner = this }.ShowDialog(); }
            finally { openingCreate = false; await viewModel.ReloadAsync(); }
        }
    }
}
