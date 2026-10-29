using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.Views;
using MusicBoxManagement.Wpf.ViewModels;
using SkiaSharp;
public static class MusicBoxMySqlOperationsUiChecks
{
    private sealed class Clock:IClock{public DateTimeOffset Value;public DateTimeOffset UtcNow{get{return Value;}}}
    private static T Field<T>(object obj,string name){return (T)obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(obj);}
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Pump(Func<bool> ready){var watch=Stopwatch.StartNew();while(!ready()){if(watch.Elapsed.TotalSeconds>60)throw new Exception("MySQL UI timeout");var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(10);}}
    private static void Await(Task task){Pump(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
    private static void Click(Button b){Check(b.IsEnabled,"Disabled button "+b.Name);b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
    private static void Image(Window window,string output,string name){window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,name+".png")))encoder.Save(file);}
    private static object Sql(MySqlDatabase db,string sql){using(var c=db.OpenConnection())using(var q=c.CreateCommand()){q.CommandText=sql;return q.ExecuteScalar();}}
    public static void Run(string appXaml,string output)
    {
        Directory.CreateDirectory(output);var name="musicbox_test_"+Guid.NewGuid().ToString("N");var db=new MySqlDatabase(name);Console.WriteLine("Operations UI test database: "+name);
        var root=Path.Combine(Path.GetTempPath(),"MusicBoxMySqlUiData_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var xml=new XmlDocument();xml.Load(appXaml);app.Resources=(ResourceDictionary)XamlReader.Parse("<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>"+xml.DocumentElement.FirstChild.InnerXml+"</ResourceDictionary>");SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        ApplicationServices runtime=null;
        try
        {
            const string password="Ui-MySql-2026!";var auth=new MySqlAuthenticationService(db);var actor=auth.SetupAdminAsync("admin","Admin UI",password).GetAwaiter().GetResult();
            var clock=new Clock{Value=new DateTimeOffset(2026,10,29,9,0,0,TimeSpan.FromHours(7))};var image=Path.Combine(root,"fixture.png");using(var bitmap=new SKBitmap(16,16)){bitmap.Erase(SKColors.Gray);using(var frame=SKImage.FromBitmap(bitmap))using(var bytes=frame.Encode(SKEncodedImageFormat.Png,100))File.WriteAllBytes(image,bytes.ToArray());}
            var room=new MySqlRoomService(db,root,clock).Create(actor,new RoomCreate{RoomCode="UI1",RoomTypeId=1,Name="Phòng UI MySQL",ImageFilePath=image});
            new MySqlServiceCatalogService(db).Save(actor,null,new ServiceEdit{Name="Trà",Category="Đồ uống",Price=25000,IsActive=true});
            clock.Value=clock.Value.AddHours(1);var sessions=new MySqlRoomSessionService(db,clock);var session=sessions.CreateWalkIn(actor,new WalkInRequest{RoomId=room,FullName="Khách UI",PhoneNumber="0901234567"}).Session;new MySqlOrderService(db,clock).CreateStaff(actor,session.RoomSessionId,new[]{new OrderLineRequest{ServiceId=1,Quantity=2}});clock.Value=clock.Value.AddMinutes(30);var paid=new MySqlCheckoutService(db,clock).ConfirmStaff(actor,session.RoomSessionId,"Cash");
            var expired=new MySqlReservationService(db,new Clock{Value=new DateTimeOffset(2026,10,28,8,0,0,TimeSpan.FromHours(7))}).CreateGuest(new ReservationRequest{RoomId=room,FullName="Worker",PhoneNumber="0909990001",StartTime=new DateTimeOffset(2026,10,28,9,0,0,TimeSpan.FromHours(7)),DurationMinutes=60});runtime=new ApplicationServices("MySql",name,root);Await(runtime.Worker.StartAsync());Check((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+expired.ReservationId)=="NoShow","Configured MySQL worker did not process expired booking");var main=runtime.Window;main.Show();Pump(()=>Field<Button>(main,"LoginButton").IsEnabled&&Field<MainViewModel>(main,"viewModel").RoomTypes.Count==2);Image(main,output,"mysql-guest-main");
            var driver=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};bool submitted=false;driver.Tick+=(s,e)=>{var dialog=app.Windows.OfType<AuthenticationWindow>().FirstOrDefault();if(dialog!=null&&!submitted){Field<TextBox>(dialog,"UserNameBox").Text="admin";Field<PasswordBox>(dialog,"PasswordInput").Password=password;submitted=true;Click(Field<Button>(dialog,"SubmitButton"));}};driver.Start();try{Click(Field<Button>(main,"LoginButton"));Pump(()=>Field<Grid>(main,"StaffPanel").Visibility==Visibility.Visible&&!Field<DashboardViewModel>(main,"dashboard").IsBusy);}finally{driver.Stop();}
            Check((string)typeof(LoginSession).GetProperty("DatabaseScope",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Field<LoginSession>(main,"session"),null)=="mysql:"+name,"Main authenticated against wrong provider");Check(Field<DashboardViewModel>(main,"dashboard").Snapshot.TodayRevenue==paid.TotalAmount,"Dashboard not bound to MySQL invoice");Image(main,output,"mysql-admin-dashboard");
            var report=new RevenueReportViewModel(new MySqlReportService(db),actor);Await(report.SearchAsync());Check(report.Report!=null&&report.Report.TotalAmount==paid.TotalAmount,"Revenue VM failed");var reportWindow=new RevenueReportWindow(report);reportWindow.Show();Pump(()=>!report.IsBusy);Image(reportWindow,output,"mysql-revenue");reportWindow.Close();
            var roomReport=new RoomUsageReportViewModel(new MySqlRoomUsageReportService(db),actor);Await(roomReport.SearchAsync());var roomWindow=new RoomUsageReportWindow(roomReport);roomWindow.Show();Pump(()=>!roomReport.IsBusy);Check(roomReport.Report.CompletedCount==1,"Room report VM failed");Image(roomWindow,output,"mysql-room-report");roomWindow.Close();
            var history=new CustomerHistoryViewModel(new MySqlCustomerHistoryService(db),actor,session.CustomerId);Await(history.ReloadAsync());Check(history.History.CompletedSessionCount==1&&history.History.Invoices.Single().TotalAmount==paid.TotalAmount,"History VM failed");var historyWindow=new CustomerHistoryWindow(new MySqlCustomerHistoryService(db),actor,session.CustomerId);historyWindow.Show();Pump(()=>!((CustomerHistoryViewModel)historyWindow.DataContext).IsBusy);Image(historyWindow,output,"mysql-customer-history");historyWindow.Close();
            Sql(db,"UPDATE AspNetUsers SET IsActive=0 WHERE Id='"+actor.UserId+"';");Await(history.ReloadAsync());Check(history.History==null&&history.CustomerLabel=="","Revoked history retained PII");
            runtime.Worker.Dispose();main.Close();
            Console.WriteLine("PASS MySQL UI: configured MainWindow/Guest/catalog/photo, actual login, dashboard real paid invoice, Revenue/Room/History VM+windows, current rights/PII purge; screenshots, no production DB");
        }
        catch(Exception error){Console.WriteLine("Operations UI failure before cleanup: "+error);throw;}
        finally
        {
            if(runtime!=null)runtime.Worker.Dispose();foreach(Window w in app.Windows.Cast<Window>().ToArray())w.Close();
            if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"\Amusicbox_test_[0-9a-f]{32}\z"))throw new Exception("Unsafe DB cleanup");Sql(new MySqlDatabase("defaultdb"),"DROP DATABASE IF EXISTS "+name);
            var full=Path.GetFullPath(root);if(!full.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("MusicBoxMySqlUiData_",StringComparison.Ordinal))throw new Exception("Unsafe folder cleanup");Directory.Delete(full,true);app.Shutdown();
        }
    }
}
