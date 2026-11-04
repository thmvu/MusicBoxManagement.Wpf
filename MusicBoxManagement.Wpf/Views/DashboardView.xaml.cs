using System.Windows;
using System.Windows.Controls;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView() { InitializeComponent(); }
        private async void Refresh_Click(object sender, RoutedEventArgs args)
        { if (DataContext is DashboardViewModel model && Window.GetWindow(this) is Window owner) await UserFeedback.RunAsync(owner, () => model.RefreshAsync(), () => model.Status); }
    }
}
