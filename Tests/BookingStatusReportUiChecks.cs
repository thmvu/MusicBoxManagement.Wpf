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
public static class MusicBoxBookingStatusReportUiChecks
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
            Assert(!Button(main, "BookingReportsButton").IsVisible, "Guest saw reports navigation.");
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
                Assert(Button(main, "BookingReportsButton").IsVisible == (user != "staff.report"), "Report navigation incorrect for " + user);
                if (user != "staff.report")
                {
                    bool opened = false;
                    driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                    driver.Tick += (sender, args) =>
                    {
                        var form = Application.Current.Windows.OfType<BookingStatusReportWindow>().FirstOrDefault();
                        if (form == null || ((BookingStatusReportViewModel)form.DataContext).IsBusy) return;
                        opened = true; Image(form, output, "booking-reports-main-" + user); driver.Stop(); form.Close();
                    };
                    driver.Start(); Click(main, "BookingReportsButton"); PumpUntil(() => opened && Button(main, "BookingReportsButton").IsEnabled); driver.Stop();
                }
                Click(main, "LogoutButton"); PumpUntil(() => ((FrameworkElement)main.FindName("GuestNavigation")).IsVisible && Button(main, "LoginButton").IsEnabled);
                Assert(!Button(main, "BookingReportsButton").IsVisible, "Logout retained report navigation.");
            }
        }
        finally { if (driver != null) driver.Stop(); main.Close(); }
    }
    private static int Count(BookingStatusReportViewModel vm, string status) { return vm.Rows.Single(r => r.Status == status).Count; }
    private static void CheckReport(BookingStatusReportWindow window, BookingStatusReportViewModel vm, DateTime day, string output)
    {
        var grid = (DataGrid)window.FindName("ReportTable");
        Assert(grid.IsReadOnly && !grid.CanUserAddRows && !grid.CanUserDeleteRows, "Booking report grid permits writes.");
        ((DatePicker)window.FindName("FromInput")).SelectedDate = day;
        ((DatePicker)window.FindName("ToInput")).SelectedDate = day;
        Click(window, "SearchButton"); PumpUntil(() => !vm.IsBusy);
        Assert(vm.Report != null && vm.Rows.Count == 5, "Date controls did not load five fixed statuses.");
        Assert(vm.Rows.Select(r => r.Status).SequenceEqual(new[] { "Confirmed", "CheckedIn", "Completed", "Cancelled", "NoShow" }), "Fixed status display order changed.");
        Assert(Count(vm,"Confirmed") == 2 && Count(vm,"CheckedIn") == 1 && Count(vm,"Completed") == 1 && Count(vm,"Cancelled") == 1 && Count(vm,"NoShow") == 2, "Booking status counts/grace/day boundaries are incorrect.");
        Assert(vm.Rows.Sum(r => r.Count) == 7, "Report included CreatedAt cohort or next-day booking.");
        window.UpdateLayout(); Assert(!String.IsNullOrWhiteSpace(((TextBlock)window.FindName("SummaryText")).Text), "Actual summary is blank.");
        Image(window,output,"bookings-full");
        ((DatePicker)window.FindName("FromInput")).SelectedDate = day.AddDays(2);
        Assert(vm.Report == null && vm.Rows.Count == 0 && vm.Summary == "", "Changing date retained stale totals.");
        ((DatePicker)window.FindName("ToInput")).SelectedDate = day.AddDays(2);
        Click(window,"SearchButton"); PumpUntil(() => !vm.IsBusy);
        Assert(vm.Report != null && vm.Rows.Count == 5 && vm.Rows.All(r => r.Count == 0), "Empty date range omitted zero statuses.");
        Image(window,output,"bookings-empty");
        vm.ToDate = day; Await(vm.SearchAsync()); Assert(vm.Report == null && vm.Rows.Count == 0 && vm.Summary == "", "Invalid range retained report.");
        vm.ToDate = null; Await(vm.SearchAsync()); Assert(vm.Report == null && vm.Rows.Count == 0, "Missing date retained report.");
        vm.FromDate = day; vm.ToDate = day; Await(vm.SearchAsync());
        window.Width = 960; window.Height = 640; window.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => {})); window.UpdateLayout();
        foreach (var name in new[] { "SearchButton", "CloseButton" })
        {
            var button=Button(window,name); var point=button.TransformToAncestor(window).Transform(new Point());
            Assert(button.IsVisible && point.X >= 0 && point.Y >= 0 && point.X + button.ActualWidth <= window.ActualWidth && point.Y + button.ActualHeight <= window.ActualHeight,"Compact booking report hides " + name);
        }
        Assert(grid.ActualHeight > 190, "Compact report table is too short for five statuses.");
        var countCell = grid.Columns[1].GetCellContent(vm.Rows[0]);
        Assert(countCell != null, "Compact report omitted count cells.");
        var countPoint = countCell.TransformToAncestor(window).Transform(new Point());
        Assert(countPoint.X >= 0 && countPoint.X + countCell.ActualWidth <= window.ActualWidth, "Compact report count column is clipped.");
        Image(window,output,"bookings-compact");
    }
    public static void Run(string appXaml, string output)
    {
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        Directory.CreateDirectory(output);
        var directory = Path.Combine(Path.GetTempPath(), "MusicBoxBookingStatusUiData_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var xml = new XmlDocument(); xml.Load(appXaml);
        app.Resources = (ResourceDictionary)XamlReader.Parse("<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" + xml.DocumentElement.FirstChild.InnerXml + "</ResourceDictionary>");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        BookingStatusReportWindow window = null; GateClock gate = null;
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
            Sql(db, @"INSERT INTO Rooms(RoomId,RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES(1,'R1','Phòng test',1,'test.png',1,'2026-10-01T00:00:00.0000000+00:00');
INSERT INTO Customers(CustomerId,FullName,PhoneNumber) VALUES(1,'A','0901111111');
INSERT INTO Reservations(ReservationId,CustomerId,RoomId,StartTime,EndTime,Status,CreatedAt) VALUES
(1,1,1,'2026-10-12T17:00:00.0000000+00:00','2026-10-12T18:00:00.0000000+00:00','Cancelled','2026-10-01'),
(2,1,1,'2026-10-13T06:45:00.0000000+00:00','2026-10-13T07:45:00.0000000+00:00','Confirmed','2026-10-01'),
(3,1,1,'2026-10-13T06:45:00.0000001+00:00','2026-10-13T07:45:00.0000001+00:00','Confirmed','2026-10-01'),
(4,1,1,'2026-10-13T08:00:00.0000000+00:00','2026-10-13T09:00:00.0000000+00:00','Confirmed','2026-10-01'),
(5,1,1,'2026-10-13T01:00:00.0000000+00:00','2026-10-13T02:00:00.0000000+00:00','NoShow','2026-10-01'),
(6,1,1,'2026-10-13T03:00:00.0000000+00:00','2026-10-13T04:00:00.0000000+00:00','CheckedIn','2026-10-01'),
(7,1,1,'2026-10-13T04:00:00.0000000+00:00','2026-10-13T05:00:00.0000000+00:00','Completed','2026-10-01'),
(8,1,1,'2026-10-13T17:00:00.0000000+00:00','2026-10-13T18:00:00.0000000+00:00','Completed','2026-10-01'),
(9,1,1,'2026-10-12T16:59:59.9999999+00:00','2026-10-12T18:00:00.0000000+00:00','Cancelled','2026-10-13');");
            VerifyMainNavigation(db, output);
            var before = Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM AuditLog;"));
            var service = new BookingStatusReportService(db, clock);
            var denied = new BookingStatusReportViewModel(service, staff); Await(denied.SearchAsync()); Assert(denied.Rows.Count == 0 && denied.Report == null, "Misgranted Staff saw report.");
            window = new BookingStatusReportWindow(new BookingStatusReportViewModel(service, manager)); var vm = (BookingStatusReportViewModel)window.DataContext;
            window.Show(); PumpUntil(() => !vm.IsBusy); CheckReport(window, vm, day, output); window.Close();
            gate = new GateClock(); vm = new BookingStatusReportViewModel(new BookingStatusReportService(db, gate), manager); window = new BookingStatusReportWindow(vm); window.Show();
            PumpUntil(() => gate.Entered.IsSet); var oldFrom = vm.FromDate; var oldTo = vm.ToDate;
            vm.FromDate = day.AddYears(-1); vm.ToDate = day.AddYears(1); Await(vm.SearchAsync()); window.Close();
            Assert(vm.IsBusy && !vm.CanSearch && window.IsVisible && vm.FromDate == oldFrom && vm.ToDate == oldTo, "Busy read accepted duplicate/filter/close.");
            gate.Release.Set(); PumpUntil(() => !vm.IsBusy);
            vm.FromDate = day; vm.ToDate = day; Await(vm.SearchAsync()); Assert(vm.Rows.Count > 0, "Busy release failed.");
            Sql(db, "DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Report.View');");
            Await(vm.SearchAsync()); Assert(vm.Rows.Count == 0 && vm.Report == null, "Revoked Report.View retained booking figures."); Image(window, output, "bookings-revoked");
            Sql(db, "INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.View';"); vm.FromDate = day; vm.ToDate = day; Await(vm.SearchAsync());
            Sql(db, "UPDATE AspNetUserRoles SET RoleId='Staff' WHERE UserId='" + manager.UserId + "';"); Await(vm.SearchAsync()); Assert(vm.Rows.Count == 0 && vm.Report == null, "Changed current role retained report.");
            Sql(db, "UPDATE AspNetUserRoles SET RoleId='Manager' WHERE UserId='" + manager.UserId + "';UPDATE AspNetUsers SET SecurityStamp='report-revoked' WHERE Id='" + manager.UserId + "';");
            vm.FromDate = day; vm.ToDate = day; Await(vm.SearchAsync()); Assert(vm.Rows.Count == 0 && vm.Report == null, "Changed stamp retained report.");
            Assert(Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM AuditLog;")) == before && Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM Reservations;")) == 9 && Convert.ToString(Sql(db, "SELECT Status FROM Reservations WHERE ReservationId=2;")) == "Confirmed", "Report browsing mutated database.");
            Console.WriteLine("PASS Booking status WPF: actual Guest/Staff/Manager/Admin navigation, independent Report.View, dates/UTC+7 StartTime cohort, grace without writes, fixed statuses/zero rows, invalid/empty/stale filters, revoked role/stamp, busy/duplicate/close, read-only/compact/full layout. Rendered " + Directory.GetFiles(output, "*.png").Length + " views.");
        }
        finally
        {
            if (gate != null) gate.Release.Set(); if (window != null) window.Close(); app.Shutdown();
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            var target = Path.GetFullPath(directory); var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("MusicBoxBookingStatusUiData_", StringComparison.Ordinal)) throw new Exception("Unsafe report UI cleanup.");
            Directory.Delete(target, true); if (gate != null) { gate.Entered.Dispose(); gate.Release.Dispose(); }
        }
    }
}
