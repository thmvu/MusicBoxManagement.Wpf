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
        public CustomersWindow(CustomerService service, LoginSession session)
        {
            InitializeComponent(); viewModel = new CustomersViewModel(service, session); DataContext = viewModel;
            Closing += (sender, args) => { if (viewModel.IsBusy) args.Cancel = true; };
        }
        private async void Window_Loaded(object sender, RoutedEventArgs e) => await viewModel.SearchAsync();
        private async void Search_Click(object sender, RoutedEventArgs e) => await viewModel.SearchAsync();
        private void New_Click(object sender, RoutedEventArgs e) { CustomersTable.SelectedItem = null; viewModel.Edit(null); NameInput.Focus(); }
        private void Selection_Changed(object sender, SelectionChangedEventArgs e) { if (viewModel != null) viewModel.Edit(CustomersTable.SelectedItem as Customer); }
        private void CancelEdit_Click(object sender, RoutedEventArgs e) => viewModel.Edit(CustomersTable.SelectedItem as Customer);
        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (await viewModel.SaveAsync())
                foreach (var item in viewModel.Items)
                    if (item.CustomerId == viewModel.EditingId) { CustomersTable.SelectedItem = item; break; }
        }
    }
}
