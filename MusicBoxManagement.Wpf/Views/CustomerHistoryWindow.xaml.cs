using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class CustomerHistoryWindow : Window
    {
        private readonly CustomerHistoryViewModel viewModel;
        public CustomerHistoryWindow(ICustomerHistoryService service, LoginSession actor, int customerId)
        {
            InitializeComponent(); DataContext = viewModel = new CustomerHistoryViewModel(service, actor, customerId);
            Closing += (sender, args) => { if (viewModel.IsBusy) args.Cancel = true; };
        }
        private async void Window_Loaded(object sender, RoutedEventArgs e) => await viewModel.ReloadAsync();
        private async void Reload_Click(object sender, RoutedEventArgs e) => await viewModel.ReloadAsync();
    }

    public sealed class HistoryTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is DateTimeOffset
            ? ((DateTimeOffset)value).ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy", CultureInfo.GetCultureInfo("vi-VN")) : "—";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
