using System.Windows;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;
namespace MusicBoxManagement.Wpf.Views
{
    public partial class GuestOrdersWindow : Window
    {
        private readonly GuestOrdersViewModel viewModel;
        public GuestOrdersWindow(OrderService service,string phone,int sessionId,string roomCode)
        {
            InitializeComponent();viewModel=new GuestOrdersViewModel(service,phone,sessionId,roomCode);DataContext=viewModel;
            Loaded+=async (sender,args)=>await viewModel.ReloadAsync();Closing+=(sender,args)=>{if(viewModel.IsBusy)args.Cancel=true;};
        }
        private async void Reload_Click(object sender,RoutedEventArgs args)=>await viewModel.ReloadAsync();
        private void Add_Click(object sender,RoutedEventArgs args)=>viewModel.Add();
        private void Remove_Click(object sender,RoutedEventArgs args)=>viewModel.Remove();
        private async void Preview_Click(object sender,RoutedEventArgs args)=>await viewModel.PreviewAsync();
        private async void Confirm_Click(object sender,RoutedEventArgs args)=>await viewModel.ConfirmAsync();
        private void Keep_Click(object sender,RoutedEventArgs args)=>viewModel.Keep();
        private void Cancel_Click(object sender,RoutedEventArgs args)=>viewModel.RequestCancel();
    }
}
