using System.Windows;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
 public partial class GuestLookupWindow:Window
 {
  private readonly GuestLookupViewModel viewModel;
  public GuestLookupWindow(GuestReservationService service)
  {InitializeComponent();viewModel=new GuestLookupViewModel(service);DataContext=viewModel;Closing+=(sender,args)=>{if(viewModel.IsBusy)args.Cancel=true;};}
  private async void Search_Click(object sender,RoutedEventArgs args)=>await viewModel.SearchAsync();
  private void Cancel_Click(object sender,RoutedEventArgs args)=>viewModel.RequestCancel();
  private void Keep_Click(object sender,RoutedEventArgs args)=>viewModel.KeepBooking();
  private async void Confirm_Click(object sender,RoutedEventArgs args)=>await viewModel.ConfirmCancelAsync();
 }
}
