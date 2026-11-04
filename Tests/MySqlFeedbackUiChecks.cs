using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;
using MusicBoxManagement.Wpf.Views;

public static class MusicBoxMySqlFeedbackUiChecks
{
    private sealed class Clock:IClock {public DateTimeOffset Now;public DateTimeOffset UtcNow {get{return Now;}}}
    private static int notices,phoneThrows;private static string last;private static string images;
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static T Field<T>(object owner,string name){return (T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(owner);}
    private static void Pump(Func<bool> done){var watch=Stopwatch.StartNew();while(!done()){if(watch.Elapsed.TotalSeconds>90)throw new Exception("Feedback UI wait timeout");var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(10);}}
    private static void Await(Task task){Pump(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
    private static void Click(Button b){Check(b.IsEnabled,"Test clicked disabled button: "+b.Name);b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
    private static void Image(Window w,string name){w.UpdateLayout();var img=new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth),(int)Math.Ceiling(w.ActualHeight),96,96,PixelFormats.Pbgra32);img.Render(w);var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(img));using(var stream=File.Create(Path.Combine(images,name+".png")))enc.Save(stream);}
    private static object Sql(MySqlDatabase db,string sql){using(var c=db.OpenConnection())using(var q=c.CreateCommand()){q.CommandText=sql;return q.ExecuteScalar();}}
    private static string Counts(MySqlDatabase db){return Convert.ToString(Sql(db,"SELECT CONCAT((SELECT COUNT(*) FROM Reservations),':',(SELECT COUNT(*) FROM RoomSessions),':',(SELECT COUNT(*) FROM Customers),':',(SELECT COUNT(*) FROM Orders),':',(SELECT COUNT(*) FROM Invoices),':',(SELECT COUNT(*) FROM AuditLog));"));}
    private static void Command(Window form,string button,Func<bool> ready){var before=notices;Click(Field<Button>(form,button));Pump(ready);Check(notices==before+1,"Expected exactly one popup for "+form.GetType().Name+"."+button);Check(form.IsVisible,"Command closed its editable form.");}
    private static void Online(string theme,Application app)
    {
        var name="musicbox_test_"+Guid.NewGuid().ToString("N");var db=new MySqlDatabase(name);var root=Path.Combine(Path.GetTempPath(),"MusicBoxFeedback_"+Guid.NewGuid().ToString("N"));Console.WriteLine("Feedback fixture: "+name);
        try {
            var auth=new MySqlAuthenticationService(db);var admin=auth.SetupAdminAsync("admin","Admin test","Feedback-2026!").GetAwaiter().GetResult();
            var day=DateTimeOffset.UtcNow.ToOffset(BookingHours.VietnamOffset).Date;var clock=new Clock{Now=new DateTimeOffset(day.AddHours(9),BookingHours.VietnamOffset)};
            var roomService=new MySqlRoomService(db,root,clock);var roomId=roomService.Create(admin,new RoomCreate{RoomCode="S01",Name="Phòng test",RoomTypeId=1,ImageFilePath=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(theme),"..","assets","rooms","standard-oak-50mm.png"))});
            var service=new MySqlGuestBookingService(db,root,clock);var bookingVm=new GuestBookingViewModel(service,clock);var booking=new GuestBookingWindow(service,bookingVm);booking.Show();Pump(()=>!bookingVm.IsBusy&&bookingVm.Rooms.Count==1);bookingVm.FullName="Khách test";bookingVm.StartTimeText="09:30";
            var original=Counts(db);var exceptionStart=phoneThrows;
            foreach(var invalid in new[]{"012345678","01234567890","1123456789","01234abc89",""}) {
                bookingVm.PhoneNumber=invalid;Command(booking,"SubmitButton",()=>!bookingVm.IsBusy);Check(Counts(db)==original,"Invalid booking changed data/audit.");Check(bookingVm.FullName=="Khách test"&&bookingVm.PhoneNumber==invalid,"Validation cleared entered data.");
            }
            bookingVm.PhoneNumber="012345678";Command(booking,"PreviewButton",()=>!bookingVm.IsBusy);Check(last.Contains("10"),"Preview missing phone error.");
            bookingVm.PhoneNumber="0123456789";bookingVm.FullName="";Command(booking,"SubmitButton",()=>!bookingVm.IsBusy);Check(last.Contains("họ tên"),"Missing-name popup unclear.");bookingVm.FullName="Khách test";
            bookingVm.SelectedDate=null;Command(booking,"SubmitButton",()=>!bookingVm.IsBusy);Check(last.Contains("ngày"),"Blank booking date message missing.");bookingVm.SelectedDate=day;
            Check(phoneThrows==exceptionStart,"Phone preflight threw in booking.");Check(Counts(db)==original,"Validation wrote customer/audit.");
            var settle=Task.Delay(200);Pump(()=>settle.IsCompleted);Image(booking,"booking-after-invalid");
            bookingVm.PhoneNumber="+84 123.456-789";Command(booking,"SubmitButton",()=>!bookingVm.IsBusy);Check(bookingVm.LastCreatedReservationId.HasValue&&last.Contains("thành công"),"Valid corrected booking did not save.");var reservationId=bookingVm.LastCreatedReservationId.Value;booking.Close();
            var reservationsAfter=Convert.ToInt32(Sql(db,"SELECT COUNT(*) FROM Reservations"));
            var conflictVm=new GuestBookingViewModel(service,clock);var conflict=new GuestBookingWindow(service,conflictVm);conflict.Show();Pump(()=>!conflictVm.IsBusy&&conflictVm.Rooms.Count==1);conflictVm.StartTimeText="09:30";conflictVm.FullName="Khách trùng";conflictVm.PhoneNumber="0123456788";Command(conflict,"SubmitButton",()=>!conflictVm.IsBusy);Check(!conflictVm.LastCreatedReservationId.HasValue&&Convert.ToInt32(Sql(db,"SELECT COUNT(*) FROM Reservations"))==reservationsAfter,"Conflict was saved.");conflict.Close();
            var walkVm=new WalkInViewModel(new MySqlRoomSessionService(db,clock),admin);var walk=new WalkInWindow(walkVm);walk.Show();Pump(()=>!walkVm.IsBusy&&walkVm.Rooms.Count==1);walkVm.FullName="Khách test";walkVm.PhoneNumber="012345678";original=Counts(db);Command(walk,"ReceiveButton",()=>!walkVm.IsBusy);Check(last.Contains("10")&&Counts(db)==original,"Walk-in phone validation wrote data.");walk.Close();
            var customers=new CustomersWindow(new MySqlCustomerService(db),admin);customers.Show();var customerVm=(CustomersViewModel)customers.DataContext;Pump(()=>!customerVm.IsBusy&&customerVm.CanSave);customerVm.FullName="Khách test";customerVm.PhoneNumber="012345678";Command(customers,"SaveButton",()=>!customerVm.IsBusy);customerVm.PhoneQuery="012345678";Command(customers,"SearchButton",()=>!customerVm.IsBusy);Check(Counts(db)==original,"Invalid customer save/search wrote data.");customerVm.FullName="Khách thêm mới";customerVm.PhoneNumber="0123456766";Command(customers,"SaveButton",()=>!customerVm.IsBusy);Check(last.StartsWith("Đã lưu khách hàng"),"RED review: saved customer popup was overwritten by row selection.");original=Counts(db);customers.Close();
            Console.WriteLine("PASS booking corrections/conflict, walk-in/customer preflight; checking phone filters");
            var reservationVm=new ReservationsViewModel(new MySqlStaffReservationService(db,clock,root),admin);var reservationWindow=new ReservationsWindow(reservationVm);reservationWindow.Show();Pump(()=>!reservationVm.IsBusy);reservationVm.PhoneQuery="012345678";Command(reservationWindow,"SearchButton",()=>!reservationVm.IsBusy);Check(reservationVm.Items.Count==0&&last.Contains("10"),"Reservation phone filter kept stale results.");reservationWindow.Close();
            var sessionVm=new SessionsViewModel(new MySqlStaffSessionService(db,clock),admin);var sessionWindow=new SessionsWindow(sessionVm);sessionWindow.Show();Pump(()=>!sessionVm.IsBusy);sessionVm.PhoneQuery="012345678";Command(sessionWindow,"SearchButton",()=>!sessionVm.IsBusy);Check(sessionVm.Items.Count==0&&last.Contains("10"),"Session phone filter ignored invalid phone.");sessionWindow.Close();
            var invoiceVm=new InvoicesViewModel(new MySqlInvoiceService(db,clock),admin);var invoiceWindow=new InvoicesWindow(invoiceVm);invoiceWindow.Show();Pump(()=>!invoiceVm.IsBusy);invoiceVm.PhoneQuery="012345678";Command(invoiceWindow,"SearchButton",()=>!invoiceVm.IsBusy);Check(invoiceVm.Items.Count==0&&last.Contains("10"),"Invoice phone filter ignored invalid phone.");invoiceWindow.Close();
            Check(Counts(db)==original,"Invalid staff filters changed data/audit.");
            var roomSession=new MySqlRoomSessionService(db,clock).CheckIn(admin,reservationId);
            var lookup=new GuestLookupWindow(new MySqlGuestReservationService(db,clock));lookup.Show();var lookupVm=(GuestLookupViewModel)lookup.DataContext;lookupVm.PhoneNumber="+84123456789";Command(lookup,"SearchButton",()=>!lookupVm.IsBusy);Check(lookupVm.HasActiveSession,"Valid lookup lost active session.");
            Command(lookup,"PreviewExtensionButton",()=>!lookupVm.IsBusy);Command(lookup,"ConfirmExtensionButton",()=>!lookupVm.IsBusy);Check(last.Contains("gia hạn"),"Extension confirmation popup missing.");lookup.Close();
            var servicesWindow=new ServicesWindow(new MySqlServiceCatalogService(db),admin);servicesWindow.Show();var servicesVm=(ServicesViewModel)servicesWindow.DataContext;Pump(()=>!servicesVm.IsBusy&&servicesVm.CanEdit);servicesVm.Name="Nước test";servicesVm.Category="Đồ uống";servicesVm.PriceText="10000";servicesVm.IsActive=true;Command(servicesWindow,"SaveButton",()=>!servicesVm.IsBusy);Check(last.StartsWith("Đã lưu"),"Saved service popup was overwritten by row selection.");servicesWindow.Close();
            Console.WriteLine("PASS saved customer/service popup text; staff phone filters");
            var orders=new GuestOrdersWindow(new MySqlOrderService(db,clock),"0123456789",roomSession.RoomSessionId,"S01");orders.Show();var ordersVm=(GuestOrdersViewModel)orders.DataContext;Pump(()=>!ordersVm.IsBusy&&ordersVm.Menu.Count==1);ordersVm.SelectedMenu=ordersVm.Menu[0];Command(orders,"AddButton",()=>!ordersVm.IsBusy);ordersVm.SelectedCart=ordersVm.Cart[0];Command(orders,"RemoveButton",()=>!ordersVm.IsBusy);Check(last.Contains("bỏ"),"Remove popup shows previous add message.");Command(orders,"AddButton",()=>!ordersVm.IsBusy);Command(orders,"PreviewButton",()=>!ordersVm.IsBusy);Command(orders,"ConfirmButton",()=>!ordersVm.IsBusy);Check(ordersVm.Orders.Count==1,"Guest order not created.");Console.WriteLine("PASS lookup/extension and Guest order popups");var orderId=ordersVm.Orders[0].OrderId;orders.Close();new MySqlOrderService(db,clock).ConfirmStaff(admin,orderId);
            clock.Now=clock.Now.AddMinutes(7);var checkoutVm=new CheckoutViewModel(new MySqlCheckoutService(db,clock),admin);var checkout=new CheckoutWindow(checkoutVm);checkout.Show();Pump(()=>!checkoutVm.IsBusy&&checkoutVm.Sessions.Count==1);checkoutVm.Selected=checkoutVm.Sessions[0];Command(checkout,"PreviewButton",()=>!checkoutVm.IsBusy);Check(!checkoutVm.CanConfirm,"Checkout skipped payment acknowledgment.");checkoutVm.PaymentMethod="Cash";checkoutVm.Acknowledged=true;Command(checkout,"ConfirmButton",()=>!checkoutVm.IsBusy);Check(checkoutVm.HasReceipt&&Convert.ToInt32(Sql(db,"SELECT COUNT(*) FROM Invoices"))==1,"Checkout did not create exactly one invoice.");Console.WriteLine("PASS checkout confirmed, one real invoice");Image(checkout,"checkout-after-notice");checkout.Close();
            // Cancel a separate future booking through the public buttons and confirm popup.
            var future=service.Create(new ReservationRequest{RoomId=roomId,FullName="Khách hủy",PhoneNumber="0123456787",StartTime=new DateTimeOffset(day.AddHours(14),BookingHours.VietnamOffset),DurationMinutes=60});
            var cancellation=new GuestLookupWindow(new MySqlGuestReservationService(db,clock));cancellation.Show();var cancellationVm=(GuestLookupViewModel)cancellation.DataContext;cancellationVm.PhoneNumber="0123456787";Command(cancellation,"SearchButton",()=>!cancellationVm.IsBusy);cancellationVm.Selected=cancellationVm.Items.Single();Command(cancellation,"CancelButton",()=>!cancellationVm.IsBusy);Command(cancellation,"ConfirmButton",()=>!cancellationVm.IsBusy);Check(Convert.ToString(Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+future.ReservationId))=="Cancelled","Guest cancel not committed.");cancellation.Close();
            // Current rights remain enforced; UI popup must not bypass a deactivated actor.
            Sql(db,"UPDATE AspNetUsers SET IsActive=0 WHERE UserName='admin'");var denied=new CustomersWindow(new MySqlCustomerService(db),admin);denied.Show();var deniedVm=(CustomersViewModel)denied.DataContext;Pump(()=>!deniedVm.IsBusy);Command(denied,"SearchButton",()=>!deniedVm.IsBusy);Check(deniedVm.Items.Count==0&&!deniedVm.CanSave,"Revoked account retained CRUD access.");denied.Close();
            Console.WriteLine("PASS MySQL feedback workflow: invalid booking/preview/walk-in/customer inputs preserve form/data/audit; correction +84; conflict popup; lookup/extension/order/checkout/invoice/cancel; current rights. Notices="+notices);
        }finally{foreach(var w in app.Windows.Cast<Window>().ToArray())w.Close();using(var c=new MySqlDatabase("defaultdb").OpenConnection())using(var q=c.CreateCommand()){q.CommandText="DROP DATABASE IF EXISTS `"+name+"`";q.ExecuteNonQuery();}}
    }
    public static void Run(string theme,string output,bool preflightOnly)
    {
        images=output;Directory.CreateDirectory(images);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var xml=new XmlDocument();xml.Load(theme);
        app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse("<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>"+xml.DocumentElement.FirstChild.InnerXml+"</ResourceDictionary>");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        EventHandler<FirstChanceExceptionEventArgs> watch=(s,e)=>{if(e.Exception is ArgumentException&&(e.Exception.StackTrace??"").Contains("PhoneNumberNormalizer"))Interlocked.Increment(ref phoneThrows);};AppDomain.CurrentDomain.FirstChanceException+=watch;
        var dismiss=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(25)};dismiss.Tick+=(s,e)=>{var popup=app.Windows.Cast<Window>().FirstOrDefault(w=>w.Name=="UserFeedbackDialog"&&w.IsVisible);if(popup==null)return;last=((TextBlock)popup.FindName("MessageText")).Text;notices++;if(notices==1){Image(popup,"phone-nine-notice");Field<Button>(popup.Owner,"SearchButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}((Button)popup.FindName("AcceptButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};dismiss.Start();
        try {
            // No database exists: invalid input must be handled entirely by the UI preflight.
            var db=new MySqlDatabase("musicbox_test_"+Guid.NewGuid().ToString("N"));var form=new GuestLookupWindow(new MySqlGuestReservationService(db));form.Show();
            var vm=(GuestLookupViewModel)form.DataContext;Field<TextBox>(form,"PhoneInput").Text="012345678";Click(Field<Button>(form,"SearchButton"));Pump(()=>vm.CanSearch);
            Check(notices==1&&last.Contains("10"),"RED: 9-digit lookup must show a visible phone validation notice.");Check(form.IsVisible&&vm.PhoneNumber=="012345678","Invalid phone closed form or erased input.");Check(phoneThrows==0,"Invalid phone reached throwing service normalizer.");form.Close();
            string normalized;Check(!PhoneNumberNormalizer.TryNormalize(null,out normalized)&&normalized==null,"Null phone accepted.");Check(!PhoneNumberNormalizer.TryNormalize("01234567890",out normalized),"11-digit phone accepted.");Check(!PhoneNumberNormalizer.TryNormalize("1123456789",out normalized),"Wrong prefix accepted.");Check(PhoneNumberNormalizer.TryNormalize("+84 123.456-789",out normalized)&&normalized=="0123456789"&&PhoneNumberNormalizer.Normalize("84123456789")==normalized,"+84 normalization drifted.");
            var cartVm=new GuestOrdersViewModel(new MySqlOrderService(new MySqlDatabase("musicbox_test_"+Guid.NewGuid().ToString("N"))),"0123456789",1,"S01");cartVm.Cart.Add(new ServiceOrderItem());cartVm.SelectedCart=cartVm.Cart[0];cartVm.Remove();Check(cartVm.Cart.Count==0&&cartVm.Status.Contains("bỏ"),"RED review: removed cart item must not retain previous action status.");
            var boundaryOwner=new Window{Title="UI boundary test",Width=500,Height=300};boundaryOwner.Show();var boundary=typeof(GuestLookupWindow).Assembly.GetType("MusicBoxManagement.Wpf.Views.UserFeedback");var run=boundary.GetMethod("RunAsync",BindingFlags.Static|BindingFlags.NonPublic,null,new[]{typeof(Window),typeof(Func<Task>),typeof(Func<string>)},null);Func<Task> failure=()=>{throw new IOException("private connection details");};Await((Task)run.Invoke(null,new object[]{boundaryOwner,failure,new Func<string>(()=>"old status")}));Check(boundaryOwner.IsVisible&&last.Contains("kiểm tra trạng thái")&&!last.Contains("private"),"Unexpected UI failure escaped or leaked details.");boundaryOwner.Close();
            if (!preflightOnly) Online(theme,app);
            Console.WriteLine("PASS Feedback UI: 9-digit lookup popup/no phone throw/input retained; duplicate command ignored; safe unexpected-error popup. Online="+(!preflightOnly));
        }finally{dismiss.Stop();AppDomain.CurrentDomain.FirstChanceException-=watch;foreach(var w in app.Windows.Cast<Window>().ToArray())w.Close();app.Shutdown();}
    }
}
