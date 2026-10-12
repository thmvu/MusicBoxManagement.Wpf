using System.Windows;
using System.Windows.Controls;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class InvoicesWindow : Window
    {
        private readonly InvoicesViewModel viewModel;
        public InvoicesWindow(InvoicesViewModel viewModel)
        {
            InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel;
            Loaded += async (sender, args) => await viewModel.SearchAsync();
            Closing += (sender, args) => { if (viewModel.IsBusy) args.Cancel = true; };
        }
        private async void Search_Click(object sender, RoutedEventArgs args) => await viewModel.SearchAsync();
        private async void Selection_Changed(object sender, SelectionChangedEventArgs args)
        { if (viewModel != null) await viewModel.SelectAsync(InvoicesTable.SelectedItem as InvoiceSummary); }
        private void Clear_Click(object sender, RoutedEventArgs args) => viewModel.ClearFilters();
        private void Close_Click(object sender, RoutedEventArgs args) { if (!viewModel.IsBusy) Close(); }
    }
}
