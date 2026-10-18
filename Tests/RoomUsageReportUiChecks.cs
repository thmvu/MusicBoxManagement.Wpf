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
public static class MusicBoxRoomUsageReportUiChecks
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
            Assert(!Button(main, "RoomReportsButton").IsVisible, "Guest saw reports navigation.");
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
                Assert(Button(main, "RoomReportsButton").IsVisible == (user != "staff.report"), "Report navigation incorrect for " + user);
                if (user != "staff.report")
                {
                    bool opened = false;
                    driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                    driver.Tick += (sender, args) =>
                    {
                        var form = Application.Current.Windows.OfType<RoomUsageReportWindow>().FirstOrDefault();
                        if (form == null || ((RoomUsageReportViewModel)form.DataContext).IsBusy) return;
                        opened = true; Image(form, output, "room-reports-main-" + user); driver.Stop(); form.Close();
                    };
                    driver.Start(); Click(main, "RoomReportsButton"); PumpUntil(() => opened && Button(main, "RoomReportsButton").IsEnabled); driver.Stop();
                }
                Click(main, "LogoutButton"); PumpUntil(() => ((FrameworkElement)main.FindName("GuestNavigation")).IsVisible && Button(main, "LoginButton").IsEnabled);
                Assert(!Button(main, "RoomReportsButton").IsVisible, "Logout retained report navigation.");
            }
        }
        finally { if (driver != null) driver.Stop(); main.Close(); }
    }
    private static void CheckReport(RoomUsageReportWindow window, RoomUsageReportViewModel vm, DateTime day, string output)
    {
        var grid = (DataGrid)window.FindName("ReportTable");
        Assert(grid.IsReadOnly && !grid.CanUserAddRows && !grid.CanUserDeleteRows, "Room usage grid allows mutation.");
        ((DatePicker)window.FindName("FromInput")).SelectedDate = day;
        ((DatePicker)window.FindName("ToInput")).SelectedDate = day;
        Click(window, "SearchButton"); PumpUntil(() => !vm.IsBusy);
        Assert(vm.Report != null && vm.Rows.Count == 3, "Actual date controls lost rooms/inactive history.");
        var first = vm.Rows.Single(r => r.RoomId == 1); var locked = vm.Rows.Single(r => r.RoomId == 2); var future = vm.Rows.Single(r => r.RoomId == 3);
        Assert(first.CompletedCount == 1 && first.UsedMinutes == 180m && first.AvailableMinutes == 240m && first.UtilizationPercent == 75m, "Actual rendering data did not distinguish completed count and Active occupancy clipped to today/opening hours.");
        Assert(locked.CompletedCount == 1 && locked.UsedMinutes == 0m && locked.AvailableMinutes == 240m && locked.UtilizationPercent == 0m, "Inactive room history/count incorrectly excluded or counted outside opening hours.");
        Assert(future.CompletedCount == 0 && future.AvailableMinutes == 0m && !future.UtilizationPercent.HasValue && future.UtilizationLabel == "Không có dữ liệu", "Future-created room denominator did not show no data.");
        Assert(vm.Report.CompletedCount == 2 && vm.Report.UsedMinutes == 180m && vm.Report.AvailableMinutes == 480m && vm.Report.UtilizationPercent == 37.5m, "Overall report totals differ from row totals.");
        window.UpdateLayout(); Assert(VisualText(window).Contains("75,00 %") && VisualText(window).Contains("Không có dữ liệu"), "Actual visuals omitted decimal percent or null-ratio label.");
        Assert(VisualText(window).IndexOf("lịch mở cửa tiêu chuẩn", StringComparison.OrdinalIgnoreCase) >= 0, "Report omitted standard opening-hours qualification.");
        Image(window, output, "rooms-all");
        ((ComboBox)window.FindName("RoomInput")).SelectedValue = 2;
        Assert(vm.Report == null && vm.Rows.Count == 0 && vm.Summary == "", "Room change retained stale report.");
        Click(window, "SearchButton"); PumpUntil(() => !vm.IsBusy);
        Assert(vm.Report != null && vm.Rows.Count == 1 && vm.Rows[0].RoomId == 2, "Inactive room filter binding failed.");
        Image(window, output, "rooms-inactive");
        vm.RoomId = 987654; Await(vm.SearchAsync());
        Assert(vm.Rows.Count == 0, "Unknown room exposed other-room results.");
        vm.RoomId = null; vm.ToDate = day.AddDays(-1); Await(vm.SearchAsync());
        Assert(vm.Report == null && vm.Rows.Count == 0 && vm.Summary == "", "Invalid range retained totals.");
        vm.ToDate = null; Await(vm.SearchAsync()); Assert(vm.Report == null && vm.Rows.Count == 0, "Missing date retained totals.");
        vm.FromDate = day; vm.ToDate = day; vm.RoomId = null; Await(vm.SearchAsync());
        window.Width = 960; window.Height = 640; window.UpdateLayout();
        foreach (var name in new[] { "SearchButton", "CloseButton" })
        {
            var button = Button(window, name); var point = button.TransformToAncestor(window).Transform(new Point());
            Assert(button.IsVisible && point.X >= 0 && point.Y >= 0 && point.X + button.ActualWidth <= window.ActualWidth && point.Y + button.ActualHeight <= window.ActualHeight, "Compact room report hides " + name);
        }
        Assert(grid.ActualHeight > 100, "Compact report hides results."); Image(window, output, "rooms-compact");
        grid.ScrollIntoView(vm.Rows[0], grid.Columns[5]); window.UpdateLayout();
        var ratioCell = grid.Columns[5].GetCellContent(vm.Rows[0]);
        Assert(ratioCell != null, "Compact ratio column could not be reached by scrolling.");
        var ratioPoint = ratioCell.TransformToAncestor(window).Transform(new Point());
        Assert(ratioPoint.X >= 0 && ratioPoint.X + ratioCell.ActualWidth <= window.ActualWidth, "Compact ratio column remains clipped after horizontal navigation.");
        Image(window, output, "rooms-compact-ratio");
    }
    private static void CheckExcel(RoomUsageReportWindow window, RoomUsageReportViewModel vm, SqliteDatabase db, DateTime day, string output)
    {
        Assert(!vm.CanExport && !Button(window,"ExportButton").IsEnabled,"View-only enabled export.");
        Sql(db,"INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.Export';");Await(vm.SearchAsync());
        Assert(vm.CanExport && Button(window,"ExportButton").IsEnabled,"Export grant not applied.");
        var target=Path.Combine(output,"RoomUsageReportExport.xlsx");var prompts=0;
        Await(vm.ExportAsync(suggested=>{prompts++;Assert(vm.IsBusy&&!vm.CanExport&&!Button(window,"ExportButton").IsEnabled,"Export did not lock UI.");var start=vm.FromDate;vm.FromDate=day.AddYears(-1);Await(vm.ExportAsync(_=>{throw new Exception("Duplicate export opened dialog.");}));window.Close();Assert(window.IsVisible&&vm.FromDate==start,"Export accepted filter/close.");return target;}));
        Assert(prompts==1&&File.Exists(target)&&File.ReadAllBytes(target)[0]==80&&vm.Status.Contains("Đã xuất"),"Export save failed.");

        Await(vm.ExportAsync(_=>null));Assert(vm.Status.Contains("hủy"),"Cancel failed.");
        var blocked=Path.Combine(output,"RoomUsageReportBlocked.xlsx");File.WriteAllText(blocked,"original");
        Await(vm.ExportAsync(_=>{Sql(db,"DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Report.Export');");return blocked;}));
        Assert(File.ReadAllText(blocked)=="original"&&vm.Report==null&&!vm.CanExport&&vm.Rows.Count==0,"Revoked during dialog wrote/retained report.");
        vm.FromDate=day;vm.ToDate=day;Await(vm.SearchAsync());Assert(vm.Report!=null&&!vm.CanExport,"View after export revoke failed.");
        Sql(db,"INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.Export';");Await(vm.SearchAsync());
        window.Width=960;window.Height=640;window.UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));window.UpdateLayout();
        var button=Button(window,"ExportButton");var point=button.TransformToAncestor(window).Transform(new Point());Assert(button.IsVisible&&point.X>=0&&point.X+button.ActualWidth<=window.ActualWidth&&point.Y+button.ActualHeight<=window.ActualHeight,"Compact hides Excel button.");Image(window,output,"RoomUsageReport-excel-compact");
    }
    public static void Run(string appXaml, string output)
    {
        Directory.CreateDirectory(output);
        var directory = Path.Combine(Path.GetTempPath(), "MusicBoxRoomUsageUiData_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var xml = new XmlDocument(); xml.Load(appXaml);
        app.Resources = (ResourceDictionary)XamlReader.Parse("<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" + xml.DocumentElement.FirstChild.InnerXml + "</ResourceDictionary>");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        RoomUsageReportWindow window = null; GateClock gate = null;
        try
        {
            var db = new SqliteDatabase(Path.Combine(directory, "report.db")); var auth = new AuthenticationService(db);
            var admin = auth.SetupAdminAsync("admin", "Admin báo cáo", Password).GetAwaiter().GetResult();
            var accounts = new AccountManagementService(db);
            accounts.CreateAsync(admin, new AccountEdit { UserName = "staff.report", FullName = "Staff", Role = "Staff" }, Password).GetAwaiter().GetResult();
            accounts.CreateAsync(admin, new AccountEdit { UserName = "manager.report", FullName = "Manager", Role = "Manager" }, Password).GetAwaiter().GetResult();
            Sql(db, "DELETE FROM RolePermission WHERE RoleId IN ('Staff','Manager');INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.View';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code IN ('Report.View','Report.Export');");
            var staff = auth.LoginAsync("staff.report", Password).GetAwaiter().GetResult(); var manager = auth.LoginAsync("manager.report", Password).GetAwaiter().GetResult();
            var day = new DateTime(2026, 10, 13); var clock = new Clock { Value = new DateTimeOffset(2026, 10, 13, 14, 0, 0, TimeSpan.FromHours(7)) };
            Sql(db, @"INSERT INTO Rooms(RoomId,RoomCode,Name,RoomTypeId,ImageUrl,IsActive,InactiveReason,CreatedAt) VALUES
(1,'R1','Phòng chuẩn',1,'test.png',1,NULL,'2026-10-01T00:00:00.0000000+00:00'),
(2,'R2','Phòng đã khóa',2,'test.png',0,'Đóng thử nghiệm','2026-10-01T00:00:00.0000000+00:00'),
(3,'R3','Phòng tạo sau kỳ',1,'test.png',1,NULL,'2026-10-14T00:00:00.0000000+00:00');
INSERT INTO Customers(CustomerId,FullName,PhoneNumber) VALUES(1,'A','0901111111'),(2,'B','0902222222'),(3,'C','0903333333');
INSERT INTO RoomSessions(CustomerId,RoomId,ActualStartTime,ActualEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES
(1,1,'2026-10-13T02:00:00.0000000+00:00','2026-10-13T03:00:00.0000000+00:00',100000,'R1','STANDARD','Standard','Completed'),
(2,1,'2026-10-13T04:00:00.0000000+00:00',NULL,100000,'R1','STANDARD','Standard','Active'),
(3,2,'2026-10-12T16:00:00.0000000+00:00','2026-10-13T01:00:00.0000000+00:00',200000,'R2','VIP','VIP','Completed');");
            VerifyMainNavigation(db, output);
            var before = Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM AuditLog;"));
            var service = new RoomUsageReportService(db, clock);
            var denied = new RoomUsageReportViewModel(service, staff); Await(denied.SearchAsync()); Assert(denied.Rows.Count == 0 && denied.Report == null, "Misgranted Staff saw report.");
            window = new RoomUsageReportWindow(new RoomUsageReportViewModel(service, manager)); var vm = (RoomUsageReportViewModel)window.DataContext;
            window.Show(); PumpUntil(() => !vm.IsBusy); CheckReport(window, vm, day, output); CheckExcel(window,vm,db,day,output); window.Close();
            gate = new GateClock(); vm = new RoomUsageReportViewModel(new RoomUsageReportService(db, gate), manager); window = new RoomUsageReportWindow(vm); window.Show();
            PumpUntil(() => gate.Entered.IsSet); var oldFrom = vm.FromDate; var oldTo = vm.ToDate; var oldRoom = vm.RoomId;
            vm.FromDate = day.AddYears(-1); vm.ToDate = day.AddYears(1); vm.RoomId = 2; Await(vm.SearchAsync()); window.Close();
            Assert(vm.IsBusy && !vm.CanSearch && window.IsVisible && vm.FromDate == oldFrom && vm.ToDate == oldTo && vm.RoomId == oldRoom, "Busy read accepted duplicate/filter/close.");
            gate.Release.Set(); PumpUntil(() => !vm.IsBusy);
            vm.FromDate = day; vm.ToDate = day; Await(vm.SearchAsync()); Assert(vm.Rows.Count > 0, "Busy release failed.");
            Sql(db, "DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Report.View');");
            Await(vm.SearchAsync()); Assert(vm.Rows.Count == 0 && vm.Report == null && vm.Rooms.Count == 1 && !vm.Rooms[0].RoomId.HasValue, "Revoked Report.View retained figures or room choices."); Image(window, output, "rooms-revoked");
            Sql(db, "INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.View';"); vm.FromDate = day; vm.ToDate = day; Await(vm.SearchAsync());
            Sql(db, "UPDATE AspNetUserRoles SET RoleId='Staff' WHERE UserId='" + manager.UserId + "';"); Await(vm.SearchAsync()); Assert(vm.Rows.Count == 0 && vm.Report == null, "Changed current role retained report.");
            Sql(db, "UPDATE AspNetUserRoles SET RoleId='Manager' WHERE UserId='" + manager.UserId + "';UPDATE AspNetUsers SET SecurityStamp='report-revoked' WHERE Id='" + manager.UserId + "';");
            vm.FromDate = day; vm.ToDate = day; Await(vm.SearchAsync()); Assert(vm.Rows.Count == 0 && vm.Report == null, "Changed stamp retained report.");
            Assert(Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM AuditLog;")) == before && Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM RoomSessions;")) == 3, "Report browsing mutated database.");
            Console.WriteLine("PASS Room usage WPF: actual Guest/Staff/Manager/Admin navigation, Report.View-only, date/room controls, inactive history, count/occupancy distinction, today/future denominator, standard-hours labels, invalid/unknown filters, revoked role/stamp, busy/duplicate/close and compact layout. Rendered " + Directory.GetFiles(output, "*.png").Length + " views.");
        }
        finally
        {
            if (gate != null) gate.Release.Set(); if (window != null) window.Close(); app.Shutdown();
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            var target = Path.GetFullPath(directory); var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("MusicBoxRoomUsageUiData_", StringComparison.Ordinal)) throw new Exception("Unsafe report UI cleanup.");
            Directory.Delete(target, true); if (gate != null) { gate.Entered.Dispose(); gate.Release.Dispose(); }
        }
    }
}
