using System;
using System.Diagnostics;
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

// Authentication, fixtures and all reads use a private temporary database.
public static class MusicBoxAuditLogUiChecks
{
    private const string Password = "Audit-Ui-Test-2026!";
    private sealed class GateClock : IClock
    {
        public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false);
        public readonly ManualResetEventSlim Release = new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow { get { Entered.Set(); if (!Release.Wait(15000)) throw new Exception("Audit UI gate timed out."); return DateTimeOffset.UtcNow; } }
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void PumpUntil(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            if (watch.Elapsed.TotalSeconds > 40) throw new Exception("Audit UI timed out.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame); Thread.Sleep(10);
        }
    }
    private static void Await(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static Button Button(Window window, string name) { return (Button)window.FindName(name); }
    private static void Click(Window window, string name) { Button(window, name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); }
    private static void Image(Window window, string directory, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(directory, name + ".png"))) encoder.Save(stream);
    }
    private static void VerifyMainNavigation(SqliteDatabase db, string outputDirectory)
    {
        var auth = new AuthenticationService(db);
        var main = new MainWindow(new MainViewModel(new RoomTypeService(db)), auth, new PermissionService(db),
            new RoomTypeService(db), new RoomService(db), new ServiceCatalogService(db), new CustomerService(db),
            new GuestBookingService(db), new GuestReservationService(db), new StaffReservationService(db));
        DispatcherTimer driver = null;
        try
        {
            main.Show(); PumpUntil(() => Button(main, "LoginButton").IsEnabled);
            Assert(!Button(main, "AuditButton").IsVisible, "Guest saw audit navigation.");
            foreach (var user in new[] { "staff.audit", "manager.audit", "admin" })
            {
                Exception error = null; bool sent = false;
                driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                driver.Tick += (sender, args) =>
                {
                    var login = Application.Current.Windows.OfType<AuthenticationWindow>().FirstOrDefault();
                    if (login == null || sent) return; sent = true;
                    try
                    {
                        ((TextBox)login.FindName("UserNameBox")).Text = user;
                        ((PasswordBox)login.FindName("PasswordInput")).Password = Password;
                        Click(login, "SubmitButton");
                    }
                    catch (Exception ex) { error = ex; login.Close(); }
                };
                driver.Start(); Click(main, "LoginButton");
                PumpUntil(() => error != null || ((FrameworkElement)main.FindName("StaffNavigation")).IsVisible && Button(main, "RefreshAccessButton").IsEnabled);
                driver.Stop(); if (error != null) throw error;
                ((Expander)main.FindName("AdministrationNavigation")).IsExpanded = true; main.UpdateLayout();
                Assert(Button(main, "AuditButton").IsVisible == (user == "admin"), "Audit navigation incorrect for " + user);
                if (user == "admin")
                {
                    bool opened = false;
                    driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                    driver.Tick += (sender, args) =>
                    {
                        var form = Application.Current.Windows.OfType<AuditLogWindow>().FirstOrDefault();
                        if (form == null || ((AuditLogViewModel)form.DataContext).IsBusy || ((AuditLogViewModel)form.DataContext).Items.Count == 0) return;
                        opened = true; Image(form, outputDirectory, "audit-main-route"); driver.Stop(); form.Close();
                    };
                    driver.Start(); Click(main, "AuditButton"); PumpUntil(() => opened && Button(main, "AuditButton").IsEnabled); driver.Stop();
                }
                Click(main, "LogoutButton"); PumpUntil(() => ((FrameworkElement)main.FindName("GuestNavigation")).IsVisible && Button(main, "LoginButton").IsEnabled);
                Assert(!Button(main, "AuditButton").IsVisible, "Logout retained audit navigation.");
            }
        }
        finally { if (driver != null) driver.Stop(); main.Close(); }
    }
    public static void Run(string appXaml, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var directory = Path.Combine(Path.GetTempPath(), "MusicBoxAuditUiData_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var xml = new XmlDocument(); xml.Load(appXaml);
        app.Resources = (ResourceDictionary)XamlReader.Parse("<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" + xml.DocumentElement.FirstChild.InnerXml + "</ResourceDictionary>");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        AuditLogWindow window = null; GateClock gate = null;
        try
        {
            var db = new SqliteDatabase(Path.Combine(directory, "audit.db"));
            var auth = new AuthenticationService(db); var admin = auth.SetupAdminAsync("admin", "Admin nhật ký", Password).GetAwaiter().GetResult();
            var accounts = new AccountManagementService(db);
            accounts.CreateAsync(admin, new AccountEdit { UserName = "staff.audit", FullName = "Staff", Role = "Staff" }, Password).GetAwaiter().GetResult();
            accounts.CreateAsync(admin, new AccountEdit { UserName = "manager.audit", FullName = "Manager", Role = "Manager" }, Password).GetAwaiter().GetResult();
            var staff = auth.LoginAsync("staff.audit", Password).GetAwaiter().GetResult();
            var manager = auth.LoginAsync("manager.audit", Password).GetAwaiter().GetResult();
            Action<string> sql = text => { using (var c = db.OpenConnection()) using (var command = c.CreateCommand()) { command.CommandText = text; command.ExecuteNonQuery(); } };
            Func<long> audits = () => { using (var c = db.OpenConnection()) using (var command = c.CreateCommand()) { command.CommandText = "SELECT COUNT(*) FROM AuditLog;"; return Convert.ToInt64(command.ExecuteScalar()); } };
            // More than one full page, including same timestamps, tests stable ID pagination.
            using (var c = db.OpenConnection()) using (var transaction = c.BeginTransaction())
            using (var command = c.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO AuditLog(ActorType,UserId,Action,EntityName,EntityId,Description,CreatedAt) VALUES('Guest',NULL,'UiAudit.Probe','UiAuditEntity',@id,'Kiểm tra nhật ký dài: tên và thông tin chỉ đọc.',@now);";
                command.Parameters.AddWithValue("@id", ""); command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
                for (var i = 0; i < 110; i++) { command.Parameters["@id"].Value = i.ToString(); command.ExecuteNonQuery(); }
                transaction.Commit();
            }
            VerifyMainNavigation(db, outputDirectory);
            var before = audits(); var service = new AuditLogService(db);
            foreach (var actor in new[] { staff, manager })
            { var denied = new AuditLogViewModel(service, actor); Await(denied.SearchAsync()); Assert(denied.Items.Count == 0 && !denied.HasDetails && !denied.CanSelect && !denied.CanNextPage, "Non Admin read audit log."); }
            var vm = new AuditLogViewModel(service, admin); window = new AuditLogWindow(vm); window.Show();
            PumpUntil(() => !vm.IsBusy && vm.Items.Count > 0);
            var table = (DataGrid)window.FindName("AuditTable");
            Assert(table.IsReadOnly && !table.CanUserAddRows && !table.CanUserDeleteRows, "Audit grid allows mutation.");
            Assert(vm.Items.Count == 100 && vm.CanNextPage, "First audit page incorrect.");
            var firstIds = vm.Items.Select(x => x.AuditLogId).ToArray();
            table.SelectedItem = vm.Items[0]; PumpUntil(() => !vm.IsBusy && vm.HasDetails);
            Assert(vm.Detail != null && !String.IsNullOrWhiteSpace(vm.DetailText), "Selection did not load details.");
            Image(window, outputDirectory, "audit-detail");
            Click(window, "NextButton"); PumpUntil(() => !vm.IsBusy);
            Assert(vm.Items.Count > 0 && vm.Items.Count < 100 && !vm.CanNextPage && !vm.HasDetails && !vm.Items.Any(x => firstIds.Contains(x.AuditLogId)), "Next page overlapped or appended rows/kept old details.");
            Await(vm.SelectAsync(vm.Items[0])); Assert(vm.HasDetails, "Second-page selection failed.");
            ((TextBox)window.FindName("ActionInput")).Text = "UiAudit.Probe";
            Assert(vm.Items.Count == 0 && !vm.HasDetails && !vm.CanNextPage, "Filter retained old page/cursor.");
            ((TextBox)window.FindName("EntityInput")).Text = "UiAuditEntity";
            ((ComboBox)window.FindName("ActorInput")).SelectedValue = "Guest";
            var today = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).Date;
            ((DatePicker)window.FindName("FromInput")).SelectedDate = today;
            ((DatePicker)window.FindName("ToInput")).SelectedDate = today;
            Click(window, "SearchButton"); PumpUntil(() => !vm.IsBusy);
            Assert(vm.Items.Count == 100 && vm.Items.All(x => x.Action == "UiAudit.Probe" && x.EntityName == "UiAuditEntity"), "Control filter bindings failed.");
            Image(window, outputDirectory, "audit-filter");
            vm.ToDate = today.AddDays(-1); Await(vm.SearchAsync()); Assert(vm.Items.Count == 0 && !vm.HasDetails, "Invalid date range retained data.");
            Click(window, "ClearButton"); Assert(vm.Items.Count == 0 && !vm.HasDetails && !vm.CanNextPage && String.IsNullOrEmpty(vm.ActionQuery) && String.IsNullOrEmpty(vm.EntityQuery) && String.IsNullOrEmpty(vm.ActorType) && vm.FromDate == null && vm.ToDate == null, "Clear retained filters/private rows.");
            vm.ActionQuery = "UiAudit.NoResults"; Await(vm.SearchAsync()); Assert(vm.Items.Count == 0 && !vm.HasDetails, "Empty search retained details.");
            Image(window, outputDirectory, "audit-empty");
            vm.ClearFilters(); Await(vm.SearchAsync()); var stale = vm.Items[0];
            sql("DELETE FROM AuditLog WHERE AuditLogId=" + stale.AuditLogId + ";"); before--;
            Await(vm.SelectAsync(stale)); Assert(!vm.HasDetails && vm.Detail == null, "Deleted fixture entry retained details.");
            Await(vm.SearchAsync()); Await(vm.SelectAsync(vm.Items[0]));
            window.Width = 960; window.Height = 640; window.UpdateLayout();
            foreach (var name in new[] { "SearchButton", "ClearButton", "NextButton", "CloseButton" })
            {
                var button = Button(window, name); var point = button.TransformToAncestor(window).Transform(new Point());
                Assert(button.IsVisible && point.X >= 0 && point.Y >= 0 && point.X + button.ActualWidth <= window.ActualWidth && point.Y + button.ActualHeight <= window.ActualHeight, "Compact layout hides " + name);
            }
            Image(window, outputDirectory, "audit-compact"); Assert(audits() == before, "Read-only audit UI wrote data."); window.Close();
            gate = new GateClock(); vm = new AuditLogViewModel(new AuditLogService(db, gate), admin); window = new AuditLogWindow(vm); window.Show();
            PumpUntil(() => gate.Entered.IsSet);
            vm.ActionQuery = "blocked"; vm.EntityQuery = "blocked"; vm.ActorType = "Staff"; vm.FromDate = today.AddDays(-10); vm.ToDate = today.AddDays(10);
            vm.ClearFilters(); Await(vm.SearchAsync()); Await(vm.NextPageAsync()); Await(vm.SelectAsync(stale)); window.Close();
            Assert(vm.IsBusy && window.IsVisible && !vm.CanSearch && !vm.CanNextPage && !vm.CanSelect && vm.FromDate == null && vm.ToDate == null && String.IsNullOrEmpty(vm.ActionQuery) && String.IsNullOrEmpty(vm.EntityQuery) && String.IsNullOrEmpty(vm.ActorType), "Busy controls/close modified pending search.");
            gate.Release.Set(); PumpUntil(() => !vm.IsBusy); Assert(vm.Items.Count == 100 && audits() == before, "Busy search failed or wrote data.");
            vm.ActionQuery = "UiAudit.Probe"; Await(vm.SearchAsync()); Await(vm.SelectAsync(vm.Items[0]));
            sql("UPDATE AspNetUsers SET SecurityStamp='audit-ui-revoked' WHERE Id='" + admin.UserId + "';");
            Await(vm.NextPageAsync());
            Assert(vm.Items.Count == 0 && !vm.HasDetails && vm.Detail == null && !vm.CanSelect && !vm.CanNextPage && String.IsNullOrEmpty(vm.ActorType) && String.IsNullOrEmpty(vm.ActionQuery) && String.IsNullOrEmpty(vm.EntityQuery) && vm.FromDate == null && vm.ToDate == null, "Revoked session retained private inputs/logs.");
            Image(window, outputDirectory, "audit-revoked"); Assert(audits() == before, "Audit browsing mutated log.");
            Console.WriteLine("PASS Audit WPF: actual navigation Guest/Staff/Manager/Admin, controls/read-only grid, current access, stable pagination/filter/date/clear/empty/detail/stale, busy guards, revoked purge, compact. Rendered " + Directory.GetFiles(outputDirectory, "*.png").Length + " views.");
        }
        finally
        {
            if (gate != null) gate.Release.Set();
            if (window != null) window.Close(); app.Shutdown();
            System.Data.SQLite.SQLiteConnection.ClearAllPools(); Directory.Delete(directory, true);
            if (gate != null) { gate.Entered.Dispose(); gate.Release.Dispose(); }
        }
    }
}
