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

// Every mutation uses a private temporary database, never the application's real database.
public static class MusicBoxPermissionMatrixUiChecks
{
    private const string Password = "Matrix-Ui-Test-2026!";
    private sealed class GateClock : IClock
    {
        public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false);
        public readonly ManualResetEventSlim Release = new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow { get { Entered.Set(); if (!Release.Wait(15000)) throw new Exception("UI gate timed out."); return DateTimeOffset.UtcNow; } }
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void PumpUntil(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            if (watch.Elapsed.TotalSeconds > 35) throw new Exception("Permission matrix UI timed out.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame); Thread.Sleep(10);
        }
    }
    private static void Await(Task task) { PumpUntil(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static Button Button(Window window, string name) { return (Button)window.FindName(name); }
    private static void Click(Window window, string name) { Button(window, name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); }
    private static CheckBox FindCheckBox(DependencyObject element)
    {
        var box = element as CheckBox; if (box != null) return box;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        { box = FindCheckBox(VisualTreeHelper.GetChild(element, index)); if (box != null) return box; }
        return null;
    }
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
            Assert(!Button(main, "PermissionsButton").IsVisible, "Guest saw permissions navigation.");
            foreach (var user in new[] { "staff.matrix", "manager.matrix", "admin" })
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
                Assert(Button(main, "PermissionsButton").IsVisible == (user == "admin"), "Permissions menu did not follow role " + user);
                if (user == "admin")
                {
                    bool opened = false;
                    driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
                    driver.Tick += (sender, args) =>
                    {
                        var form = Application.Current.Windows.OfType<PermissionsWindow>().FirstOrDefault();
                        if (form == null || ((PermissionsViewModel)form.DataContext).IsBusy || ((PermissionsViewModel)form.DataContext).Rows.Count == 0) return;
                        opened = true; Image(form, outputDirectory, "permissions-main-route"); driver.Stop(); form.Close();
                    };
                    driver.Start(); Click(main, "PermissionsButton"); PumpUntil(() => opened && Button(main, "PermissionsButton").IsEnabled); driver.Stop();
                }
                Click(main, "LogoutButton"); PumpUntil(() => ((FrameworkElement)main.FindName("GuestNavigation")).IsVisible && Button(main, "LoginButton").IsEnabled);
                Assert(!Button(main, "PermissionsButton").IsVisible, "Logout retained permissions menu.");
            }
        }
        finally { if (driver != null) driver.Stop(); main.Close(); }
    }
    public static void Run(string appXaml, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var directory = Path.Combine(Path.GetTempPath(), "MusicBoxMatrixUiData_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var xml = new XmlDocument(); xml.Load(appXaml);
        app.Resources = (ResourceDictionary)XamlReader.Parse("<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" + xml.DocumentElement.FirstChild.InnerXml + "</ResourceDictionary>");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        PermissionsWindow window = null; GateClock gate = null;
        try
        {
            var db = new SqliteDatabase(Path.Combine(directory, "matrix.db"));
            var auth = new AuthenticationService(db); var admin = auth.SetupAdminAsync("admin", "Admin ma trận", Password).GetAwaiter().GetResult();
            var accounts = new AccountManagementService(db);
            accounts.CreateAsync(admin, new AccountEdit { UserName = "staff.matrix", FullName = "Staff", Role = "Staff" }, Password).GetAwaiter().GetResult();
            accounts.CreateAsync(admin, new AccountEdit { UserName = "manager.matrix", FullName = "Manager", Role = "Manager" }, Password).GetAwaiter().GetResult();
            var staff = auth.LoginAsync("staff.matrix", Password).GetAwaiter().GetResult();
            var manager = auth.LoginAsync("manager.matrix", Password).GetAwaiter().GetResult();
            VerifyMainNavigation(db, outputDirectory);
            Action<string> sql = text => { using (var c = db.OpenConnection()) using (var command = c.CreateCommand()) { command.CommandText = text; command.ExecuteNonQuery(); } };
            Func<long> audits = () => { using (var c = db.OpenConnection()) using (var command = c.CreateCommand()) { command.CommandText = "SELECT COUNT(*) FROM AuditLog WHERE Action='Permission.Update';"; return Convert.ToInt64(command.ExecuteScalar()); } };
            var rights = new PermissionService(db); var service = new PermissionManagementService(db);
            var vm = new PermissionsViewModel(service, admin); window = new PermissionsWindow(vm); window.Show();
            PumpUntil(() => !vm.IsBusy && vm.Rows.Count > 0);
            Assert(vm.Roles.SequenceEqual(new[] { "Staff", "Manager" }) && vm.SelectedRole == "Staff", "Editable roles/default changed.");
            Assert(!vm.Rows.Single(r => r.Code == "Permission.Manage").CanEdit && !vm.Rows.Single(r => r.Code == "Report.View").CanEdit, "Staff saw forbidden grants.");
            Image(window, outputDirectory, "permissions-staff");
            var before = audits(); var wasGranted = rights.HasPermission(staff, "Calendar.View");
            var calendarRow = vm.Rows.Single(r => r.Code == "Calendar.View");
            var table = (DataGrid)window.FindName("PermissionsTable"); table.ScrollIntoView(calendarRow); table.UpdateLayout();
            var rowControl = (DataGridRow)table.ItemContainerGenerator.ContainerFromItem(calendarRow);
            var checkBox = FindCheckBox(rowControl); Assert(checkBox != null && checkBox.IsEnabled, "Editable checkbox was unavailable.");
            checkBox.IsChecked = !wasGranted;
            Assert(calendarRow.IsGranted == !wasGranted, "Checkbox binding did not update selected permission.");
            Click(window, "PrepareButton"); PumpUntil(() => !vm.IsBusy);
            Assert(vm.HasConfirmation && vm.CanConfirm && audits() == before, "Preview wrote or failed to request confirmation.");
            Image(window, outputDirectory, "permissions-confirm");
            Click(window, "KeepButton"); Assert(!vm.HasConfirmation && audits() == before, "Keep wrote or retained confirmation.");
            Await(vm.PrepareAsync()); vm.Rows.Single(r => r.Code == "Dashboard.View").IsGranted = !vm.Rows.Single(r => r.Code == "Dashboard.View").IsGranted;
            Assert(!vm.HasConfirmation, "Editing grants retained old confirmation.");
            Await(vm.PrepareAsync()); Click(window, "ConfirmButton"); Click(window, "ConfirmButton"); PumpUntil(() => !vm.IsBusy);
            Assert(audits() == before + 1 && rights.HasPermission(staff, "Calendar.View") == !wasGranted && vm.Status.Contains("Đã lưu"), "Save/duplicate/current session rights failed.");
            vm.Rows.Single(r => r.Code == "Invoice.View").IsGranted = false; vm.Rows.Single(r => r.Code == "Invoice.Print").IsGranted = true;
            Await(vm.PrepareAsync()); Assert(!vm.HasConfirmation && vm.Rows.Count > 0 && audits() == before + 1, "Invalid dependency persisted or erased editable input.");
            Await(vm.ReloadAsync()); vm.SelectedRole = "Manager";
            Assert(vm.Rows.Count == 0 && !vm.HasConfirmation && !vm.CanSave, "Role switch kept Staff data.");
            Await(vm.ReloadAsync()); Assert(vm.Rows.Single(r => r.Code == "Report.View").CanEdit, "Manager report permission could not be edited.");
            Image(window, outputDirectory, "permissions-manager");
            vm.Rows.Single(r => r.Code == "Calendar.View").IsGranted = false; Await(vm.PrepareAsync());
            sql("DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Dashboard.View');");
            Await(vm.ConfirmAsync()); Assert(vm.Rows.Count == 0 && !vm.HasConfirmation && audits() == before + 1, "Stale form overwrote newer permission matrix.");
            Await(vm.ReloadAsync()); vm.Rows.Single(r => r.Code == "Calendar.View").IsGranted = false; Await(vm.PrepareAsync());
            sql("CREATE TRIGGER FailPermissionUi BEFORE INSERT ON AuditLog WHEN NEW.Action='Permission.Update' BEGIN SELECT RAISE(ABORT,'fixture');END;");
            Await(vm.ConfirmAsync()); Assert(rights.HasPermission(manager, "Calendar.View") && audits() == before + 1, "Audit failure did not roll back grant changes.");
            sql("DROP TRIGGER FailPermissionUi;"); Await(vm.ReloadAsync());
            window.Width = window.MinWidth; window.Height = window.MinHeight; window.UpdateLayout();
            foreach (var name in new[] { "ReloadButton", "PrepareButton", "CloseButton" })
            {
                var button = Button(window, name); var point = button.TransformToAncestor(window).Transform(new Point());
                Assert(button.IsVisible && point.X >= 0 && point.Y >= 0 && point.X + button.ActualWidth <= window.ActualWidth && point.Y + button.ActualHeight <= window.ActualHeight, "Compact layout hides " + name);
            }
            Image(window, outputDirectory, "permissions-compact");
            vm.Rows.Single(r => r.Code == "Calendar.View").IsGranted = false; Await(vm.PrepareAsync());
            window.UpdateLayout();
            foreach (var name in new[] { "ConfirmButton", "KeepButton" })
            {
                var button = Button(window, name); var point = button.TransformToAncestor(window).Transform(new Point());
                Assert(button.IsVisible && point.Y + button.ActualHeight <= window.ActualHeight, "Compact confirmation hides " + name);
            }
            Image(window, outputDirectory, "permissions-compact-confirm");
            sql("CREATE TRIGGER RevokePermissionUi AFTER INSERT ON AuditLog WHEN NEW.Action='Permission.Update' BEGIN UPDATE AspNetUsers SET SecurityStamp='after-save' WHERE Id='" + admin.UserId + "';END;");
            Await(vm.ConfirmAsync()); Assert(vm.Status.Contains("Đã lưu") && vm.Rows.Count == 0 && audits() == before + 2 && !rights.HasPermission(manager, "Calendar.View"), "Postcommit refresh failure lost success or retained private data.");
            Image(window, outputDirectory, "permissions-postcommit"); sql("DROP TRIGGER RevokePermissionUi;");
            window.Close(); admin = auth.LoginAsync("admin", Password).GetAwaiter().GetResult();
            foreach (var actor in new[] { staff, manager })
            { var denied = new PermissionsViewModel(service, actor); Await(denied.ReloadAsync()); Assert(denied.Rows.Count == 0 && !denied.CanSave, "Non Admin saw matrix."); }
            gate = new GateClock(); vm = new PermissionsViewModel(new PermissionManagementService(db, gate), admin); window = new PermissionsWindow(vm); window.Show();
            PumpUntil(() => !vm.IsBusy && vm.Rows.Count > 0);
            vm.Rows.Single(r => r.Code == "Calendar.View").IsGranted = !vm.Rows.Single(r => r.Code == "Calendar.View").IsGranted;
            Await(vm.PrepareAsync()); Click(window, "ConfirmButton"); PumpUntil(() => gate.Entered.IsSet);
            Click(window, "ConfirmButton"); window.Close(); Assert(vm.IsBusy && window.IsVisible, "Busy close/duplicate guard failed.");
            gate.Release.Set(); PumpUntil(() => !vm.IsBusy); Assert(audits() == before + 3, "Busy confirmation duplicated commit.");
            vm.Rows.Single(r => r.Code == "Calendar.View").IsGranted = !vm.Rows.Single(r => r.Code == "Calendar.View").IsGranted; Await(vm.PrepareAsync());
            sql("UPDATE AspNetUsers SET SecurityStamp='revoked' WHERE Id='" + admin.UserId + "';");
            Await(vm.ConfirmAsync()); Assert(vm.Rows.Count == 0 && !vm.HasConfirmation && audits() == before + 3, "Revoked confirmation wrote or retained matrix.");
            Image(window, outputDirectory, "permissions-revoked");
            Console.WriteLine("PASS Permission matrix WPF: real Admin/Staff/Manager, rights immediate, restrictions/dependencies, confirm/keep/edit, stale/rollback/revoked/postcommit, busy duplicate/close, compact. Rendered " + Directory.GetFiles(outputDirectory, "*.png").Length + " views.");
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
