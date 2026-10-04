using System.Windows;
using System.Windows.Controls;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class ServicesWindow : Window
    {
        private readonly ServicesViewModel viewModel;
        public ServicesWindow(ServiceCatalogService service, LoginSession session)
        {
            InitializeComponent();
            viewModel = new ServicesViewModel(service, session);
            DataContext = viewModel;
            Closing += (sender, args) => { if (viewModel.IsBusy) args.Cancel = true; };
        }
        private async void Window_Loaded(object sender, RoutedEventArgs e) => await viewModel.RefreshAsync();
        private async void Refresh_Click(object sender, RoutedEventArgs e) => await viewModel.RefreshAsync();
        private void New_Click(object sender, RoutedEventArgs e) { ServicesTable.SelectedItem = null; viewModel.Edit(null); NameInput.Focus(); }
        private void Selection_Changed(object sender, SelectionChangedEventArgs e) { if (viewModel != null) viewModel.Edit(ServicesTable.SelectedItem as ServiceItem); }
        private void CancelEdit_Click(object sender, RoutedEventArgs e) => viewModel.Edit(ServicesTable.SelectedItem as ServiceItem);
        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (await viewModel.SaveAsync())
                foreach (var item in viewModel.Items)
                    if (item.ServiceId == viewModel.EditingId) { ServicesTable.SelectedItem = item; break; }
        }
    }
}
