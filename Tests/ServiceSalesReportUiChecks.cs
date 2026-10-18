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
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;
using MusicBoxManagement.Wpf.Views;

// All authentication and real checkout fixtures use a private temporary database.
public static class MusicBoxServiceSalesReportUiChecks
{
    private const string Password = "Report-Ui-Test-2026!";
    private sealed class Clock : IClock { public DateTimeOffset Value; public DateTimeOffset UtcNow { get { return Value; } } }
    private sealed class GateClock : IClock
    {
        public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false), Release = new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow { get { Entered.Set(); if (!Release.Wait(15000)) throw new Exception("Report UI gate timed out."); return DateTimeOffset.UtcNow; } }
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Sql(SqliteDatabase db, string sql) { using (var c = db.OpenConnection()) using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; return cmd.ExecuteScalar(); } }
    private static void PumpUntil(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            if (watch.Elapsed.TotalSeconds > 40) throw new Exception("Report UI timed out.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame); Thread.Sleep(10);
        }
    }
    private static void Await(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static Button Button(Window window, string name) { return (Button)window.FindName(name); }
    private static void Click(Window window, string name) { Button(window, name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); }
    private static string VisualText(DependencyObject node)
    {
        var text = node as TextBlock; var value = text == null ? "" : text.Text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) value += "\n" + VisualText(VisualTreeHelper.GetChild(node, i));
        return value;
    }
    private static void Image(Window window, string output, string name)
    {
        window.UpdateLayout(); var renderReady = DateTime.UtcNow.AddMilliseconds(100); PumpUntil(() => DateTime.UtcNow >= renderReady); window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var visible = 0; for (var pixel = 0; pixel < pixels.Length; pixel += 4) if (pixels[pixel + 3] > 0 && pixels[pixel] + pixels[pixel + 1] + pixels[pixel + 2] > 60) visible++;
        Assert(visible > bitmap.PixelWidth * bitmap.PixelHeight / 4, "Rendered report image is blank/black: " + name);
        var stable = BitmapSource.Create(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, bitmap.PixelWidth * 4); stable.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(stable));
        var path = Path.Combine(output, name + ".png");
        using (var stream = File.Create(path)) encoder.Save(stream);
        using (var stream = File.OpenRead(path))
        {
            var frame = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var decoded = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0); decoded.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            visible = 0; for (var pixel = 0; pixel < pixels.Length; pixel += 4) if (pixels[pixel + 3] > 0 && pixels[pixel] + pixels[pixel + 1] + pixels[pixel + 2] > 60) visible++;
            Assert(visible > bitmap.PixelWidth * bitmap.PixelHeight / 4, "Encoded PNG is blank/black: " + name);
        }
    }
    private static void VerifyMainNavigation(SqliteDatabase db, string output)
    {
        var auth = new AuthenticationService(db);
        var main = new MainWindow(new MainViewModel(new RoomTypeService(db)), auth, new PermissionService(db),
            new RoomTypeService(db), new RoomService(db), new ServiceCatalogService(db), new CustomerService(db),
            new GuestBookingService(db), new GuestReservationService(db), new StaffReservationService(db));
        DispatcherTimer driver = null;
        try
        {
            main.Show(); PumpUntil(() => Button(main, "LoginButton").IsEnabled);
            Assert(!Button(main, "ServiceReportsButton").IsVisible, "Guest saw reports navigation.");
            foreach (var user in new[] { "staff.report", "manager.report", "admin" })
            {
                Exception error = null; bool sent = false;
                driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                driver.Tick += (sender, args) =>
                {
                    var login = Application.Current.Windows.OfType<AuthenticationWindow>().FirstOrDefault(); if (login == null || sent) return; sent = true;
                    try { ((TextBox)login.FindName("UserNameBox")).Text = user; ((PasswordBox)login.FindName("PasswordInput")).Password = Password; Click(login, "SubmitButton"); }
                    catch (Exception ex) { error = ex; login.Close(); }
                };
                driver.Start(); Click(main, "LoginButton");
                PumpUntil(() => error != null || ((FrameworkElement)main.FindName("StaffNavigation")).IsVisible && Button(main, "RefreshAccessButton").IsEnabled);
                driver.Stop(); if (error != null) throw error;
                Assert(Button(main, "ServiceReportsButton").IsVisible == (user != "staff.report"), "Report navigation incorrect for " + user);
                if (user != "staff.report")
                {
                    bool opened = false;
                    driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                    driver.Tick += (sender, args) =>
                    {
                        var form = Application.Current.Windows.OfType<ServiceSalesReportWindow>().FirstOrDefault();
                        if (form == null || ((ServiceSalesReportViewModel)form.DataContext).IsBusy) return;
                        opened = true; Image(form, output, "booking-reports-main-" + user); driver.Stop(); form.Close();
                    };
                    driver.Start(); Click(main, "ServiceReportsButton"); PumpUntil(() => opened && Button(main, "ServiceReportsButton").IsEnabled); driver.Stop();
                }
                Click(main, "LogoutButton"); PumpUntil(() => ((FrameworkElement)main.FindName("GuestNavigation")).IsVisible && Button(main, "LoginButton").IsEnabled);
                Assert(!Button(main, "ServiceReportsButton").IsVisible, "Logout retained report navigation.");
            }
        }
        finally { if (driver != null) driver.Stop(); main.Close(); }
    }
    public static void Run(string appXaml, string output)
    {
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        Directory.CreateDirectory(output);
        var root=Path.Combine(Path.GetTempPath(),"MusicBoxSalesUiData_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var xml=new XmlDocument();xml.Load(appXaml);
        app.Resources=(ResourceDictionary)XamlReader.Parse("<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>"+xml.DocumentElement.FirstChild.InnerXml+"</ResourceDictionary>");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        ServiceSalesReportWindow window=null;GateClock gate=null;
        try
        {
            var db=new SqliteDatabase(Path.Combine(root,"report.db"));var auth=new AuthenticationService(db);
            var admin=auth.SetupAdminAsync("admin","Admin",Password).GetAwaiter().GetResult();var accounts=new AccountManagementService(db);
            accounts.CreateAsync(admin,new AccountEdit{UserName="staff.report",FullName="Staff",Role="Staff"},Password).GetAwaiter().GetResult();
            accounts.CreateAsync(admin,new AccountEdit{UserName="manager.report",FullName="Manager",Role="Manager"},Password).GetAwaiter().GetResult();
            Sql(db,"DELETE FROM RolePermission WHERE RoleId IN ('Staff','Manager');INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.View';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Report.View';");
            var manager=auth.LoginAsync("manager.report",Password).GetAwaiter().GetResult();var staff=auth.LoginAsync("staff.report",Password).GetAwaiter().GetResult();
            var day=new DateTime(2026,10,18);var clock=new Clock{Value=new DateTimeOffset(2026,10,18,10,0,0,TimeSpan.FromHours(7))};
            Sql(db,@"INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,CreatedAt) VALUES(1,'R1',1,'Phòng', 'test.png',1,'2026-10-01');
INSERT INTO Customers VALUES(1,'Private','0901111111');
INSERT INTO RoomSessions(RoomSessionId,CustomerId,RoomId,ActualStartTime,ActualEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES(1,1,1,'2026-10-18T02:00:00.0000000+00:00','2026-10-18T03:00:00.0000000+00:00',120000,'R1','STANDARD','Standard','Completed');
INSERT INTO Services(ServiceId,Name,Category,Price,IsActive) VALUES(1,'Tên hiện tại','Đồ uống',90000,0),(2,'Bánh','Đồ ăn',50000,1);
INSERT INTO Orders(OrderId,RoomSessionId,Status,CreatedAt) VALUES(1,1,'Completed','2026-10-18T02:30:00.0000000+00:00');
INSERT INTO OrderItems(OrderId,ServiceId,ServiceNameSnapshot,Quantity,UnitPrice) VALUES(1,1,'Trà đào đã bán',4,25000),(1,2,'Bánh đã bán',2,50000);");
            Sql(db,"INSERT INTO Invoices(RoomSessionId,InvoiceNumber,RoomCharge,ServiceCharge,TotalAmount,PaymentMethod,ProcessedByUserId,ProcessedByNameSnapshot,PaidAt) VALUES(1,'UI-1','120000','200000','320000','Cash','"+admin.UserId+"','Admin','2026-10-18T03:00:00.0000000+00:00');");
            VerifyMainNavigation(db,output);
            var service=new ServiceSalesReportService(db,clock);var denied=new ServiceSalesReportViewModel(service,staff);Await(denied.SearchAsync());Assert(denied.Report==null&&denied.Services.Count==0,"Staff saw catalog/report.");
            var vm=new ServiceSalesReportViewModel(service,manager);window=new ServiceSalesReportWindow(vm);window.Show();PumpUntil(()=>!vm.IsBusy);
            vm.FromDate=day;vm.ToDate=day;Await(vm.SearchAsync());
            Assert(vm.Rows.Count==2&&vm.TopItems[0].ServiceId==1&&vm.Summary.Contains("200.000"),"Rows/top/vi-VN summary failed.");
            Assert(vm.Services.Single(s=>s.ServiceId==1).Label.Contains("ngừng bán"),"Inactive service filter missing.");Image(window,output,"sales-full");
            var input=(ComboBox)window.FindName("ServiceInput");input.SelectedValue=1;PumpUntil(()=>vm.SelectedServiceId==1);
            Assert(vm.Report==null&&vm.Rows.Count==0&&vm.TopItems.Count==0,"Filter retained old data.");
            Await(vm.SearchAsync());Assert(vm.Report.ServiceId==1&&vm.Rows.Count==1&&vm.Rows[0].Name=="Trà đào đã bán"&&vm.Report.TotalRevenue==100000,"Service binding/snapshot failed.");
            vm.SelectedServiceId=null;Await(vm.SearchAsync());var tabs=(TabControl)window.FindName("ReportTabs");tabs.SelectedIndex=1;
            window.Width=960;window.Height=640;window.UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));window.UpdateLayout();
            foreach(var name in new[]{"SearchButton","CloseButton"})
            {var button=Button(window,name);var p=button.TransformToAncestor(window).Transform(new Point());Assert(button.IsVisible&&p.X>=0&&p.Y>=0&&p.X+button.ActualWidth<=window.ActualWidth&&p.Y+button.ActualHeight<=window.ActualHeight,"Compact hides "+name);}
            var grid=(DataGrid)window.FindName("TopTable");Assert(grid.ActualHeight>170&&grid.Items.Count==2,"Compact top table missing.");
            var last=grid.Columns[3].GetCellContent(vm.TopItems[0]);Assert(last!=null,"Revenue cell absent.");var point=last.TransformToAncestor(window).Transform(new Point());Assert(point.X+last.ActualWidth<=window.ActualWidth,"Revenue clipped.");Image(window,output,"sales-top-compact");
            tabs.SelectedIndex=0;Image(window,output,"sales-revenue-compact");
            vm.FromDate=day.AddDays(1);vm.ToDate=day.AddDays(1);Await(vm.SearchAsync());Assert(vm.Rows.Count==0&&vm.Report.TotalRevenue==0,"Empty report failed.");Image(window,output,"sales-empty");
            vm.FromDate=day.AddDays(2);Await(vm.SearchAsync());Assert(vm.Report==null&&vm.Rows.Count==0,"Invalid dates retained data.");window.Close();
            gate=new GateClock();vm=new ServiceSalesReportViewModel(new ServiceSalesReportService(db,gate),manager);window=new ServiceSalesReportWindow(vm);window.Show();PumpUntil(()=>gate.Entered.IsSet);
            var original=vm.FromDate;vm.FromDate=day;Await(vm.SearchAsync());window.Close();Assert(vm.IsBusy&&window.IsVisible&&vm.FromDate==original,"Busy accepted duplicate/filter/close.");
            gate.Release.Set();PumpUntil(()=>!vm.IsBusy);vm.FromDate=day;vm.ToDate=day;Await(vm.SearchAsync());
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Manager';");Await(vm.SearchAsync());
            Assert(vm.Report==null&&vm.Rows.Count==0&&vm.TopItems.Count==0&&vm.Services.Count==0&&vm.FromDate==null&&vm.ToDate==null,"Revoked access retained private report/catalog/filter.");Image(window,output,"sales-revoked");
            Console.WriteLine("PASS Service sales WPF: real main navigation, Report.View only, inactive/snapshot filter, revenue/top tabs, vi-VN, empty/invalid/stale, revoked, busy/duplicate/close, full/compact. Images="+Directory.GetFiles(output,"*.png").Length);
        }
        finally
        {
            if(gate!=null)gate.Release.Set();if(window!=null)window.Close();app.Shutdown();System.Data.SQLite.SQLiteConnection.ClearAllPools();
            var target=Path.GetFullPath(root);var temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!target.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(target).StartsWith("MusicBoxSalesUiData_",StringComparison.Ordinal))throw new Exception("Unsafe UI cleanup.");
            Directory.Delete(target,true);
        }
    }
}
