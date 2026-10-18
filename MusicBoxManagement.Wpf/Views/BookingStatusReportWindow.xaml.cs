using System.ComponentModel;
using System.Windows;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class BookingStatusReportWindow : Window
    {
        private readonly BookingStatusReportViewModel model;
        public BookingStatusReportWindow(BookingStatusReportViewModel model) { InitializeComponent(); this.model = model; DataContext = model; }
        private async void Window_Loaded(object sender, RoutedEventArgs args) { await model.SearchAsync(); }
        private async void Search_Click(object sender, RoutedEventArgs args) { await model.SearchAsync(); }
        private void Close_Click(object sender, RoutedEventArgs args) { if (!model.IsBusy) Close(); }
        protected override void OnClosing(CancelEventArgs args) { if (model.IsBusy) args.Cancel = true; base.OnClosing(args); }
    }
}
