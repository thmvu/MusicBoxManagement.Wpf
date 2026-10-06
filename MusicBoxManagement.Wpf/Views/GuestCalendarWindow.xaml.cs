using System.ComponentModel;
using System.Windows;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class GuestCalendarWindow : Window
    {
        private readonly GuestCalendarViewModel viewModel;
        public string ChosenTime { get; private set; }
        public GuestCalendarWindow(GuestCalendarViewModel viewModel) { InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel; }
        private void Choose_Click(object sender, RoutedEventArgs e) { if (!viewModel.CanChoose) return; ChosenTime = viewModel.Selected.TimeLabel; Close(); }
        private async void Refresh_Click(object sender, RoutedEventArgs e) => await viewModel.RefreshAsync();
        private void Closing_Window(object sender, CancelEventArgs e) { if (viewModel.IsBusy) e.Cancel = true; }
    }
}
