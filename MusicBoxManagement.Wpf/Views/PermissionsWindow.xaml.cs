using System.ComponentModel;
using System.Windows;
using MusicBoxManagement.Wpf.ViewModels;
namespace MusicBoxManagement.Wpf.Views
{
    public partial class PermissionsWindow : Window
    {
        private readonly PermissionsViewModel model;
        public PermissionsWindow(PermissionsViewModel model){InitializeComponent();this.model=model;DataContext=model;}
        private async void Window_Loaded(object sender,RoutedEventArgs args){await model.ReloadAsync();}
        private async void Reload_Click(object sender,RoutedEventArgs args){await model.ReloadAsync();}
        private async void Prepare_Click(object sender,RoutedEventArgs args){await model.PrepareAsync();}
        private async void Confirm_Click(object sender,RoutedEventArgs args){await model.ConfirmAsync();}
        private void Keep_Click(object sender,RoutedEventArgs args){model.KeepEditing();}
        private void Close_Click(object sender,RoutedEventArgs args){if(!model.IsBusy)Close();}
        protected override void OnClosing(CancelEventArgs e){if(model.IsBusy)e.Cancel=true;base.OnClosing(e);}
    }
}
