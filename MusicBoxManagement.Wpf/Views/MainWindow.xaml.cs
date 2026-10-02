using System.Windows;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel viewModel;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            this.viewModel = viewModel;
            DataContext = viewModel;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await viewModel.RefreshAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await viewModel.RefreshAsync();
        }
    }
}
