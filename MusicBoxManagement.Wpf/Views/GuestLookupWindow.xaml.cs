using System.Windows;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
 public partial class GuestLookupWindow:Window
 {
  private readonly GuestLookupViewModel viewModel;
  private readonly OrderService orders;
  private readonly BillingService billing;
  public GuestLookupWindow(GuestReservationService service)
  {InitializeComponent();orders=service.ForOrders();billing=service.ForBilling();viewModel=new GuestLookupViewModel(service);DataContext=viewModel;Closing+=(sender,args)=>{if(viewModel.IsBusy)args.Cancel=true;};}
  private async void Bill_Click(object sender,RoutedEventArgs args)
  {
   if(!viewModel.CanOpenOrders)return;
   var active=viewModel.ActiveSession;var phone=viewModel.CurrentPhone;
   new SessionBillWindow(new SessionBillViewModel(()=>billing.ReadGuest(phone,active.SessionId))){Owner=this}.ShowDialog();
   await viewModel.SearchAsync();
  }
  private async void Orders_Click(object sender,RoutedEventArgs args)
  {
   if(!viewModel.CanOpenOrders)return;
   var active=viewModel.ActiveSession;
   new GuestOrdersWindow(orders,viewModel.CurrentPhone,active.SessionId,active.RoomCode){Owner=this}.ShowDialog();
   await viewModel.SearchAsync();
  }
  private async void Search_Click(object sender,RoutedEventArgs args)=>await viewModel.SearchAsync();
  private void Cancel_Click(object sender,RoutedEventArgs args)=>viewModel.RequestCancel();
  private void Keep_Click(object sender,RoutedEventArgs args)=>viewModel.KeepBooking();
  private async void Confirm_Click(object sender,RoutedEventArgs args)=>await viewModel.ConfirmCancelAsync();
  private async void PreviewExtension_Click(object sender,RoutedEventArgs args)=>await viewModel.PreviewExtensionAsync();
  private async void ConfirmExtension_Click(object sender,RoutedEventArgs args)=>await viewModel.ConfirmExtensionAsync();
  private void KeepSession_Click(object sender,RoutedEventArgs args)=>viewModel.KeepSession();
 }
}
