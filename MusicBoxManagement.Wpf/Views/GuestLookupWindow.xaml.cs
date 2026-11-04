using System.Windows;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

namespace MusicBoxManagement.Wpf.Views
{
 public partial class GuestLookupWindow:Window
 {
  private readonly GuestLookupViewModel viewModel;
  private readonly IOrderService orders;
  private readonly IBillingService billing;
  public GuestLookupWindow(IGuestReservationService service)
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
  private async void Search_Click(object sender,RoutedEventArgs args)=>await UserFeedback.RunAsync(this, () => viewModel.SearchAsync(), () => viewModel.Status);
  private async void Cancel_Click(object sender,RoutedEventArgs args) => await UserFeedback.RunAsync(this, () => viewModel.RequestCancel(), () => viewModel.Status);
  private async void Keep_Click(object sender,RoutedEventArgs args) => await UserFeedback.RunAsync(this, () => viewModel.KeepBooking(), () => viewModel.Status);
  private async void Confirm_Click(object sender,RoutedEventArgs args)=>await UserFeedback.RunAsync(this, () => viewModel.ConfirmCancelAsync(), () => viewModel.Status);
  private async void PreviewExtension_Click(object sender,RoutedEventArgs args)=>await UserFeedback.RunAsync(this, () => viewModel.PreviewExtensionAsync(), () => viewModel.Status);
  private async void ConfirmExtension_Click(object sender,RoutedEventArgs args)=>await UserFeedback.RunAsync(this, () => viewModel.ConfirmExtensionAsync(), () => viewModel.Status);
  private async void KeepSession_Click(object sender,RoutedEventArgs args) => await UserFeedback.RunAsync(this, () => viewModel.KeepSession(), () => viewModel.Status);
 }
}
