using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;
using System.Data.SQLite;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;
using MusicBoxManagement.Wpf.Views;

public static class MusicBoxCustomerHistoryUiChecks
{
    private const string Password = "History-Ui-Test-2026!";
    private sealed class GateClock : IClock
    {
        public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false), Release = new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow { get { Entered.Set(); if (!Release.Wait(15000)) throw new Exception("Gate timed out"); return DateTimeOffset.UtcNow; } }
    }
    private static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static object Sql(SqliteDatabase db, string sql)
    { using (var c = db.OpenConnection()) using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; return cmd.ExecuteScalar(); } }
    private static void PumpUntil(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done()) { if (watch.ElapsedMilliseconds > 20000) throw new Exception("UI timed out"); Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => {})); Thread.Sleep(10); }
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => {}));
    }
    private static void Await(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static Button Button(Window w, string name) { return (Button)w.FindName(name); }
    private static void Click(Window w, string name) { var b = Button(w, name); Assert(b.IsEnabled, name + " disabled"); b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); }
    private static void Image(Window w, string output, string name)
    {
        w.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => {}));
        var bitmap = new RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(w);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(file);
    }
    public static void Run(string appXaml, string output)
    {
        Directory.CreateDirectory(output); var file = Path.Combine(Path.GetTempPath(), "HistoryUi_" + Guid.NewGuid().ToString("N") + ".db");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var xml = new XmlDocument(); xml.Load(appXaml);
        app.Resources = (ResourceDictionary)XamlReader.Parse("<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" + xml.DocumentElement.FirstChild.InnerXml + "</ResourceDictionary>");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        GateClock gate = null;
        try
        {
            var db = new SqliteDatabase(file); var auth = new AuthenticationService(db);
            var admin = auth.SetupAdminAsync("admin", "Admin", Password).GetAwaiter().GetResult();
            Sql(db, @"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'staff','staff','STAFF',PasswordHash,'stamp','Staff',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('staff','Staff'); DELETE FROM RolePermission WHERE RoleId='Staff';
INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Customer.View';
INSERT INTO Customers VALUES(1,'Nguyễn Minh Anh','0912345678'),(2,'Khách chưa sử dụng','0900000000');
INSERT INTO Rooms(RoomId,RoomCode,Name,RoomTypeId,ImageUrl,IsActive,InactiveReason,CreatedAt)
VALUES(1,'NEW','Phòng khóa',1,'test.png',0,'Bảo trì','2026-10-01');
INSERT INTO Reservations(CustomerId,RoomId,StartTime,EndTime,Status,CreatedAt,CancellationReason) VALUES
(1,1,'2026-10-20T03:00:00.0000000+00:00','2026-10-20T04:00:00.0000000+00:00','Completed','2026-10-01',NULL),
(1,1,'2026-10-22T03:00:00.0000000+00:00','2026-10-22T04:00:00.0000000+00:00','Cancelled','2026-10-01','Khách đổi kế hoạch, hủy đặt phòng và sẽ đặt lại vào một ngày khác.');
INSERT INTO RoomSessions(CustomerId,RoomId,ActualStartTime,ActualEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES
(1,1,'2026-10-20T03:00:00.0000000+00:00','2026-10-20T04:00:00.0000000+00:00',60000,'OLD','STANDARD','Standard cũ','Completed'),
(1,1,'2026-10-21T03:01:02.1234567+00:00',NULL,90000,'WALK','VIP','VIP đã chốt','Active');");
            using (var c = db.OpenConnection()) using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO Invoices(RoomSessionId,InvoiceNumber,RoomCharge,ServiceCharge,TotalAmount,PaymentMethod,ProcessedByUserId,ProcessedByNameSnapshot,PaidAt)
VALUES(1,'HD-HISTORY','60000','0','60000','Cash',@actor,'Admin','2026-10-20T04:00:00.0000000+00:00');";
                cmd.Parameters.AddWithValue("@actor", admin.UserId); cmd.ExecuteNonQuery();
            }
            var staff = auth.LoginAsync("staff", Password).GetAwaiter().GetResult();
            var audit = Sql(db, "SELECT COUNT(*) FROM AuditLog;");
            var parent = new CustomersWindow(new CustomerService(db), staff); parent.Show();
            var customers = (CustomersViewModel)parent.DataContext; PumpUntil(() => !customers.IsBusy);
            Assert(!Button(parent,"HistoryButton").IsEnabled, "History enabled without selection");
            ((DataGrid)parent.FindName("CustomersTable")).SelectedItem = customers.Items.First();
            Assert(Button(parent,"HistoryButton").IsEnabled && !customers.CanSave, "View-only cannot open history");
            Image(parent,output,"01-customers-history-button");
            parent.Width=900; parent.Height=540; parent.UpdateLayout();
            var historyButton=Button(parent,"HistoryButton"); var historyPoint=historyButton.TransformToAncestor(parent).Transform(new Point());
            Assert(historyPoint.Y+historyButton.ActualHeight<parent.ActualHeight && historyButton.IsVisible,"Compact parent hides history button"); Image(parent,output,"11-customers-compact");
            Exception modalError = null; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            timer.Tick += (s,e) => {
                var modal = app.Windows.OfType<CustomerHistoryWindow>().FirstOrDefault();
                if (modal == null || ((CustomerHistoryViewModel)modal.DataContext).IsBusy) return;
                timer.Stop();
                try { Assert(((CustomerHistoryViewModel)modal.DataContext).History.Customer.CustomerId == 1, "Wrong selected customer"); Image(modal,output,"02-bookings-full"); }
                catch (Exception error) { modalError = error; }
                finally { modal.Close(); }
            };
            timer.Start(); Click(parent,"HistoryButton"); PumpUntil(() => !customers.IsBusy); if(modalError!=null) throw modalError;
            Assert(!Button(parent,"HistoryButton").IsEnabled, "Parent retained stale selection after modal");
            ((DataGrid)parent.FindName("CustomersTable")).SelectedItem = customers.Items.First(); customers.NameQuery="Không tồn tại"; Await(customers.SearchAsync());
            Assert(customers.Items.Count==0 && !customers.CanHistory, "Search retained old customer"); parent.Close();

            var window = new CustomerHistoryWindow(new CustomerHistoryService(db), staff, 1); window.Show();
            var vm = (CustomerHistoryViewModel)window.DataContext; PumpUntil(() => !vm.IsBusy);
            Assert(vm.History.CompletedSessionCount==1 && vm.Summary.Contains("10:01:02 21/10/2026"), "Summary timezone/count failed");
            var tabs=(TabControl)window.FindName("HistoryTabs"); tabs.SelectedIndex=1; Image(window,output,"03-sessions-full");
            Assert(((DataGrid)window.FindName("SessionsTable")).Items.Count==2, "Missing session rows");
            var converter=new HistoryTimeConverter(); Assert((string)converter.Convert(vm.History.LastUsedAt,typeof(string),null,CultureInfo.InvariantCulture)=="10:01:02 21/10/2026", "UTC+7 converter failed");
            tabs.SelectedIndex=2; Image(window,output,"04-invoices-denied");
            Assert(vm.InvoiceNotice.Contains("không có quyền") && ((DataGrid)window.FindName("InvoicesTable")).Items.Count==0,"Invoice leak");
            Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Invoice.View';"); Await(vm.ReloadAsync());
            Assert(vm.History.Invoices.Single().RoomCode=="OLD", "Invoice snapshot missing"); Image(window,output,"05-invoices-full");
            window.Width=900; window.Height=580; tabs.SelectedIndex=0; Image(window,output,"06-bookings-compact");
            tabs.SelectedIndex=1; Image(window,output,"07-sessions-compact"); tabs.SelectedIndex=2; Image(window,output,"08-invoices-compact");
            var close=Button(window,"CloseButton"); var point=close.TransformToAncestor(window).Transform(new Point()); Assert(point.X+close.ActualWidth<=window.ActualWidth && point.Y+close.ActualHeight<=window.ActualHeight,"Compact hides close");
            Sql(db,"UPDATE Customers SET FullName='Tên sau đổi số',PhoneNumber='0901234567' WHERE CustomerId=1;"); Await(vm.ReloadAsync());
            Assert(vm.CustomerLabel.Contains("0901234567") && !vm.CustomerLabel.Contains("0912345678") && vm.History.Sessions.Count==2,"Rename lost history");
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Invoice.View');"); Await(vm.ReloadAsync());
            Assert(vm.History.Invoices.Count==0 && vm.InvoiceNotice.Contains("không có quyền"),"Invoice revoke retained old rows");
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Customer.View');"); Await(vm.ReloadAsync());
            Assert(vm.History==null && vm.CustomerLabel=="" && vm.Summary=="" && ((DataGrid)window.FindName("InvoicesTable")).Items.Count==0,"Customer revoke retained PII"); Image(window,output,"09-revoked"); window.Close();
            window=new CustomerHistoryWindow(new CustomerHistoryService(db),admin,2); window.Show(); vm=(CustomerHistoryViewModel)window.DataContext; PumpUntil(()=>!vm.IsBusy);
            Assert(vm.Summary.Contains("Chưa sử dụng") && vm.InvoiceNotice.Contains("chưa có hóa đơn"),"Empty state misleading"); Image(window,output,"10-empty"); window.Close();
            vm=new CustomerHistoryViewModel(new CustomerHistoryService(db),admin,999); Await(vm.ReloadAsync()); Assert(vm.History==null && vm.Status.Contains("Không tìm thấy"),"Deleted/unknown ID retained data");
            gate=new GateClock(); window=new CustomerHistoryWindow(new CustomerHistoryService(db,gate),admin,1); window.Show(); vm=(CustomerHistoryViewModel)window.DataContext; PumpUntil(()=>gate.Entered.IsSet);
            Await(vm.ReloadAsync()); window.Close(); Assert(vm.IsBusy && !vm.CanReload && window.IsVisible && vm.History==null,"Busy allowed duplicate/close or old data");
            gate.Release.Set(); PumpUntil(()=>!vm.IsBusy); Assert(vm.History!=null,"Gate read failed");
            gate.Entered.Reset(); gate.Release.Reset(); var reload=vm.ReloadAsync();
            Assert(vm.History==null && vm.CustomerLabel=="" && vm.Summary=="", "Reload retained old private data while loading");
            PumpUntil(()=>gate.Entered.IsSet); gate.Release.Set(); Await(reload); window.Close();
            Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Customer.View';");
            parent=new CustomersWindow(new CustomerService(db),staff); parent.Show(); customers=(CustomersViewModel)parent.DataContext; PumpUntil(()=>!customers.IsBusy);
            ((DataGrid)parent.FindName("CustomersTable")).SelectedItem=customers.Items.First();
            timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(30) }; bool revoking=false;
            timer.Tick+=async(s,e)=>{
                var modal=app.Windows.OfType<CustomerHistoryWindow>().FirstOrDefault();
                if(revoking || modal==null || ((CustomerHistoryViewModel)modal.DataContext).IsBusy)return;
                revoking=true; timer.Stop();
                try { Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';"); await ((CustomerHistoryViewModel)modal.DataContext).ReloadAsync(); }
                catch(Exception error){modalError=error;}
                finally {modal.Close();}
            };
            timer.Start(); Click(parent,"HistoryButton"); PumpUntil(()=>!customers.IsBusy); if(modalError!=null)throw modalError;
            Assert(customers.Items.Count==0 && customers.FullName==null && !customers.CanHistory,"Parent kept PII after modal revoked access"); parent.Close();
            Assert(Equals(audit,Sql(db,"SELECT COUNT(*) FROM AuditLog;")),"UI history wrote audit");
            Console.WriteLine("PASS Customer history UI: selected modal, view-only, filters, summaries, UTC+7, snapshots, invoice grant/revoke, customer/parent revoke, empty/unknown, busy/duplicate/close, compact; 11 images.");
        }
        finally
        {
            if(gate!=null)gate.Release.Set();
            foreach(Window w in app.Windows.Cast<Window>().ToArray())w.Close(); app.Shutdown();
            SQLiteConnection.ClearAllPools(); if(File.Exists(file))File.Delete(file);
        }
    }
}
