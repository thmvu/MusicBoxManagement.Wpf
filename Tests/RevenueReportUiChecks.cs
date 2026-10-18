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
public static class MusicBoxRevenueReportUiChecks
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
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(stream);
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
            Assert(!Button(main, "ReportsButton").IsVisible, "Guest saw reports navigation.");
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
                Assert(Button(main, "ReportsButton").IsVisible == (user != "staff.report"), "Report navigation incorrect for " + user);
                if (user != "staff.report")
                {
                    bool opened = false;
                    driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                    driver.Tick += (sender, args) =>
                    {
                        var form = Application.Current.Windows.OfType<RevenueReportWindow>().FirstOrDefault();
                        if (form == null || ((RevenueReportViewModel)form.DataContext).IsBusy) return;
                        opened = true; Image(form, output, "reports-main-" + user); driver.Stop(); form.Close();
                    };
                    driver.Start(); Click(main, "ReportsButton"); PumpUntil(() => opened && Button(main, "ReportsButton").IsEnabled); driver.Stop();
                }
                Click(main, "LogoutButton"); PumpUntil(() => ((FrameworkElement)main.FindName("GuestNavigation")).IsVisible && Button(main, "LoginButton").IsEnabled);
                Assert(!Button(main, "ReportsButton").IsVisible, "Logout retained report navigation.");
            }
        }
        finally { if (driver != null) driver.Stop(); main.Close(); }
    }
    private static void CheckReport(RevenueReportWindow window, RevenueReportViewModel vm, DateTime day, decimal first, decimal huge, string output)
    {
        var grid = (DataGrid)window.FindName("ReportTable");
        Assert(grid.IsReadOnly && !grid.CanUserAddRows && !grid.CanUserDeleteRows, "Report grid allows mutations.");
        ((DatePicker)window.FindName("FromInput")).SelectedDate = day;
        ((DatePicker)window.FindName("ToInput")).SelectedDate = day;
        ((ComboBox)window.FindName("ModeInput")).SelectedValue = "Day";
        Click(window, "SearchButton"); PumpUntil(() => !vm.IsBusy);
        Assert(vm.Report != null && vm.Report.InvoiceCount == 2 && vm.Report.TotalAmount == first + huge && vm.Rows.Count == 1 && vm.Rows[0].Key == "2026-10-13", "Date controls/day report lost invoices/decimal.");
        var money = (first + huge).ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
        window.UpdateLayout(); Assert(VisualText(window).Contains(money), "Actual report visual omitted precise Vietnamese money."); Image(window, output, "reports-day");
        ((ComboBox)window.FindName("ModeInput")).SelectedValue = "Month";
        Assert(vm.Rows.Count == 0 && vm.Report == null && vm.Summary == "", "Grouping change retained stale totals.");
        Click(window, "SearchButton"); PumpUntil(() => !vm.IsBusy); Assert(vm.Rows.Count == 1 && vm.Rows[0].Key == "2026-10" && vm.Report.TotalAmount == first + huge, "Month grouping binding failed."); Image(window, output, "reports-month");
        ((ComboBox)window.FindName("ModeInput")).SelectedValue = "RoomType"; Click(window, "SearchButton"); PumpUntil(() => !vm.IsBusy);
        Assert(vm.Rows.Count == 2 && vm.Rows.Single(r => r.Key == "STANDARD").TotalAmount == first && vm.Rows.Single(r => r.Key == "VIP").TotalAmount == huge, "RoomType grouping binding failed."); Image(window, output, "reports-roomtype");
        ((ComboBox)window.FindName("RoomTypeInput")).SelectedValue = "STANDARD";
        Assert(vm.Rows.Count == 0 && vm.Report == null && vm.Summary == "", "Type change retained stale totals.");
        Click(window, "SearchButton"); PumpUntil(() => !vm.IsBusy);
        Assert(vm.Report.InvoiceCount == 1 && vm.Report.TotalAmount == first && vm.Rows.Single().Key == "STANDARD", "RoomType filter binding failed.");
        vm.ToDate = day.AddDays(-1); Await(vm.SearchAsync()); Assert(vm.Report == null && vm.Rows.Count == 0 && vm.Summary == "", "Invalid range retained totals.");
        vm.ToDate = null; Await(vm.SearchAsync()); Assert(vm.Report == null && vm.Rows.Count == 0, "Missing date retained totals.");
        vm.FromDate = day.AddYears(-1); vm.ToDate = day.AddYears(-1); Await(vm.SearchAsync());
        Assert(vm.Report != null && vm.Report.InvoiceCount == 0 && vm.Report.TotalAmount == 0 && vm.Rows.Count == 0, "Empty report did not show valid zero totals."); Image(window, output, "reports-empty");
        vm.FromDate = day; vm.ToDate = day; vm.RoomTypeCode = null; vm.Mode = "RoomType"; Await(vm.SearchAsync());
        window.Width = 960; window.Height = 640; window.UpdateLayout();
        foreach (var name in new[] { "SearchButton", "CloseButton" })
        {
            var button = Button(window, name); var point = button.TransformToAncestor(window).Transform(new Point());
            Assert(button.IsVisible && point.X >= 0 && point.Y >= 0 && point.X + button.ActualWidth <= window.ActualWidth && point.Y + button.ActualHeight <= window.ActualHeight, "Compact report hides " + name);
        }
        Assert(grid.ActualHeight > 100 && ((TextBlock)window.FindName("SummaryText")).ActualHeight > 0, "Compact report hides table/summary."); Image(window, output, "reports-compact");
    }
    public static void Run(string appXaml, string output)
    {
        Directory.CreateDirectory(output);
        var directory = Path.Combine(Path.GetTempPath(), "MusicBoxRevenueUiData_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var xml = new XmlDocument(); xml.Load(appXaml);
        app.Resources = (ResourceDictionary)XamlReader.Parse("<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" + xml.DocumentElement.FirstChild.InnerXml + "</ResourceDictionary>");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        RevenueReportWindow window = null; GateClock gate = null;
        try
        {
            var db = new SqliteDatabase(Path.Combine(directory, "report.db")); var auth = new AuthenticationService(db);
            var admin = auth.SetupAdminAsync("admin", "Admin báo cáo", Password).GetAwaiter().GetResult();
            var accounts = new AccountManagementService(db);
            accounts.CreateAsync(admin, new AccountEdit { UserName = "staff.report", FullName = "Staff", Role = "Staff" }, Password).GetAwaiter().GetResult();
            accounts.CreateAsync(admin, new AccountEdit { UserName = "manager.report", FullName = "Manager", Role = "Manager" }, Password).GetAwaiter().GetResult();
            Sql(db, "DELETE FROM RolePermission WHERE RoleId IN ('Staff','Manager');INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.View';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code IN ('Report.View','Report.Export');");
            var staff = auth.LoginAsync("staff.report", Password).GetAwaiter().GetResult(); var manager = auth.LoginAsync("manager.report", Password).GetAwaiter().GetResult();
            var day = new DateTime(2026, 10, 13); var clock = new Clock { Value = new DateTimeOffset(2026, 10, 13, 13, 0, 0, TimeSpan.FromHours(7)) };
            Sql(db, "INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('R1','Phòng chuẩn',1,'test.png',1,'test'),('R2','Phòng VIP',2,'test.png',1,'test');INSERT INTO Services(Name,Category,Price,IsActive) VALUES('Món lớn','Đồ uống',9223372036854775807,1);");
            var sessions = new RoomSessionService(db, clock); var checkout = new CheckoutService(db, clock);
            var session = sessions.CreateWalkIn(admin, new WalkInRequest { RoomId = 1, FullName = "Khách chuẩn", PhoneNumber = "0901111111" }).Session;
            clock.Value = clock.Value.AddHours(1); var first = checkout.ConfirmStaff(admin, session.RoomSessionId, "Cash");
            clock.Value = clock.Value.AddHours(1); session = sessions.CreateWalkIn(admin, new WalkInRequest { RoomId = 2, FullName = "Khách VIP", PhoneNumber = "0902222222" }).Session;
            new OrderService(db, clock).CreateStaff(admin, session.RoomSessionId, new[] { new OrderLineRequest { ServiceId = 1, Quantity = 10 } });
            clock.Value = clock.Value.AddHours(1); var huge = checkout.ConfirmStaff(admin, session.RoomSessionId, "BankTransfer");
            VerifyMainNavigation(db, output);
            var before = Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM AuditLog;"));
            var service = new ReportService(db, clock);
            var denied = new RevenueReportViewModel(service, staff); Await(denied.SearchAsync()); Assert(denied.Rows.Count == 0 && denied.Report == null, "Misgranted Staff saw reports.");
            var vm = new RevenueReportViewModel(service, manager); window = new RevenueReportWindow(vm); window.Show(); PumpUntil(() => !vm.IsBusy);
            // Further report assertions follow the implementation's public DTO contract.
            CheckReport(window, vm, day, first.TotalAmount, huge.TotalAmount, output);
            Assert(!vm.CanExport && !Button(window,"ExportButton").IsEnabled,"View-only enabled export.");
            Sql(db,"INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.Export';");
            Await(vm.SearchAsync());Assert(vm.CanExport&&Button(window,"ExportButton").IsEnabled,"Export grant not applied.");
            var exportPath=Path.Combine(output,"RevenueUi.xlsx");var prompts=0;
            Await(vm.ExportAsync(suggested=>{prompts++;Assert(vm.IsBusy&&!vm.CanExport&&!Button(window,"ExportButton").IsEnabled,"Export did not lock UI.");var oldMode=vm.Mode;vm.Mode="Day";Await(vm.ExportAsync(_=>{throw new Exception("Duplicate export opened dialog.");}));window.Close();Assert(window.IsVisible&&vm.Mode==oldMode,"Export accepted close/filter.");return exportPath;}));
            Assert(prompts==1&&File.Exists(exportPath)&&File.ReadAllBytes(exportPath)[0]==80&&vm.Status.Contains("Đã xuất"),"Export save failed.");
            Await(vm.ExportAsync(_=>null));Assert(vm.Status.Contains("hủy"),"Cancel did not report no write.");
            var blocked=Path.Combine(directory,"blocked.xlsx");File.WriteAllText(blocked,"original");
            Await(vm.ExportAsync(_=>{Sql(db,"DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Report.Export');");return blocked;}));
            Assert(File.ReadAllText(blocked)=="original"&&vm.Report==null&&!vm.CanExport&&vm.Rows.Count==0,"Revoked during dialog wrote/retained report.");
            vm.FromDate=day;vm.ToDate=day;Await(vm.SearchAsync());Assert(vm.Report!=null&&!vm.CanExport,"View after export revocation failed.");
            Sql(db,"INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.Export';");Await(vm.SearchAsync());
            window.Width=960;window.Height=640;window.UpdateLayout();var exportButton=Button(window,"ExportButton");var exportPoint=exportButton.TransformToAncestor(window).Transform(new Point());
            Assert(exportPoint.X>=0&&exportPoint.X+exportButton.ActualWidth<=window.ActualWidth&&exportPoint.Y+exportButton.ActualHeight<=window.ActualHeight,"Compact hides Export button.");Image(window,output,"reports-excel-compact");
            Assert(Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM AuditLog;")) == before, "Report UI wrote audit/business data."); window.Close();
            gate = new GateClock(); vm = new RevenueReportViewModel(new ReportService(db, gate), manager); window = new RevenueReportWindow(vm); window.Show();
            PumpUntil(() => gate.Entered.IsSet); var oldFrom = vm.FromDate; var oldTo = vm.ToDate; var oldType = vm.RoomTypeCode;
            vm.FromDate = day.AddYears(-1); vm.ToDate = day.AddYears(1); vm.RoomTypeCode = "VIP"; Await(vm.SearchAsync()); window.Close();
            Assert(vm.IsBusy && !vm.CanSearch && window.IsVisible && vm.FromDate == oldFrom && vm.ToDate == oldTo && vm.RoomTypeCode == oldType, "Busy search accepted duplicate/filter/close.");
            gate.Release.Set(); PumpUntil(() => !vm.IsBusy);
            vm.FromDate = day; vm.ToDate = day; Await(vm.SearchAsync()); Assert(vm.Rows.Count > 0, "Busy release failed.");
            Sql(db, "DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Report.View');");
            Await(vm.SearchAsync()); Assert(vm.Rows.Count == 0 && vm.Report == null, "Revoked Report.View retained figures."); Image(window, output, "reports-revoked");
            Sql(db, "INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.View';"); vm.FromDate = day; vm.ToDate = day; Await(vm.SearchAsync());
            Sql(db, "UPDATE AspNetUserRoles SET RoleId='Staff' WHERE UserId='" + manager.UserId + "';"); Await(vm.SearchAsync()); Assert(vm.Rows.Count == 0 && vm.Report == null, "Changed current role retained report.");
            Sql(db, "UPDATE AspNetUserRoles SET RoleId='Manager' WHERE UserId='" + manager.UserId + "';UPDATE AspNetUsers SET SecurityStamp='report-revoked' WHERE Id='" + manager.UserId + "';");
            vm.FromDate = day; vm.ToDate = day; Await(vm.SearchAsync()); Assert(vm.Rows.Count == 0 && vm.Report == null, "Changed stamp retained report.");
            Assert(Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM AuditLog;")) == before && Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM Invoices;")) == 2, "Report browsing mutated database.");
            Console.WriteLine("PASS Revenue WPF: actual navigation Guest/Staff/Manager/Admin, Report.View-only, paid checkout fixtures, date/type/group controls, decimal rendering, stale/empty/invalid filters, revoked role/stamp, busy/duplicate/close and compact layout. Rendered " + Directory.GetFiles(output, "*.png").Length + " views.");
        }
        finally
        {
            if (gate != null) gate.Release.Set(); if (window != null) window.Close(); app.Shutdown();
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            var target = Path.GetFullPath(directory); var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("MusicBoxRevenueUiData_", StringComparison.Ordinal)) throw new Exception("Unsafe report UI cleanup.");
            Directory.Delete(target, true); if (gate != null) { gate.Entered.Dispose(); gate.Release.Dispose(); }
        }
    }
}
