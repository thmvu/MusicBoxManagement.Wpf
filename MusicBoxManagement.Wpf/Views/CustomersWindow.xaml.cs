using System.Windows;
using System.Windows.Controls;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class CustomersWindow : Window
    {
        private readonly CustomersViewModel viewModel;
        private readonly ICustomerHistoryService historyService;
        private readonly LoginSession session;
        private bool historyOpen;
        public CustomersWindow(ICustomerService service, LoginSession session)
        {
            InitializeComponent(); viewModel = new CustomersViewModel(service, session); DataContext = viewModel;
            historyService = service.ForHistory(); this.session = session;
            Closing += (sender, args) => { if (viewModel.IsBusy) args.Cancel = true; };
        }
        private async void Window_Loaded(object sender, RoutedEventArgs e) => await viewModel.SearchAsync();
        private async void Search_Click(object sender, RoutedEventArgs e) => await viewModel.SearchAsync();
        private void New_Click(object sender, RoutedEventArgs e) { CustomersTable.SelectedItem = null; viewModel.Edit(null); NameInput.Focus(); }
        private void Selection_Changed(object sender, SelectionChangedEventArgs e) { if (viewModel != null) viewModel.Edit(CustomersTable.SelectedItem as Customer); }
        private void CancelEdit_Click(object sender, RoutedEventArgs e) => viewModel.Edit(CustomersTable.SelectedItem as Customer);
        private async void History_Click(object sender, RoutedEventArgs e)
        {
            if (historyOpen || !viewModel.CanHistory || !viewModel.EditingId.HasValue) return;
            historyOpen = true;
            try { new CustomerHistoryWindow(historyService, session, viewModel.EditingId.Value) { Owner = this }.ShowDialog(); }
            finally
            {
                // Refresh permissions/current names after the modal, including a revoked session.
                await viewModel.SearchAsync(); historyOpen = false;
            }
        }
        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (await viewModel.SaveAsync())
                foreach (var item in viewModel.Items)
                    if (item.CustomerId == viewModel.EditingId) { CustomersTable.SelectedItem = item; break; }
        }
    }
}
