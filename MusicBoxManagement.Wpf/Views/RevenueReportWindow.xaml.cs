using System.ComponentModel;
using System.Windows;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class RevenueReportWindow : Window
    {
        private readonly RevenueReportViewModel model;
        public RevenueReportWindow(RevenueReportViewModel model) { InitializeComponent(); this.model = model; DataContext = model; }
        private async void Window_Loaded(object sender, RoutedEventArgs args) { await model.SearchAsync(); }
        private async void Search_Click(object sender, RoutedEventArgs args) { await model.SearchAsync(); }
        private async void Export_Click(object sender, RoutedEventArgs args)
        {
            await model.ExportAsync(fileName =>
            {
                var dialog = new Microsoft.Win32.SaveFileDialog {
                    Title = "Lưu báo cáo doanh thu Excel", FileName = fileName,
                    Filter = "Excel Workbook (*.xlsx)|*.xlsx", DefaultExt = ".xlsx",
                    AddExtension = true, OverwritePrompt = true, CheckPathExists = true
                };
                return dialog.ShowDialog(this) == true ? dialog.FileName : null;
            });
        }
        private void Close_Click(object sender, RoutedEventArgs args) { if (!model.IsBusy) Close(); }
        protected override void OnClosing(CancelEventArgs args) { if (model.IsBusy) args.Cancel = true; base.OnClosing(args); }
    }
}
