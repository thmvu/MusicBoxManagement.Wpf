using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;
using MusicBoxManagement.Wpf.Views;

// In-process WPF component checks. All credentials and data are test fixtures.
public static class MusicBoxAuthenticationUiChecks
{
    private const string Password = "MusicBox-Ui-Test!";
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static T Field<T>(Window window, string name) { return (T)window.FindName(name); }
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }

    private static void PumpUntil(Func<bool> ready)
    {
        var watch = Stopwatch.StartNew();
        while (!ready())
        {
            if (watch.Elapsed.TotalSeconds > 30) throw new Exception("WPF test timed out.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
    }

    private static void Image(Window window, string directory, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),
            (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(directory, name + ".png"))) encoder.Save(stream);
    }

    public static void Run(string appXaml, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var file = Path.Combine(Path.GetTempPath(), "MusicBoxUi_" + Guid.NewGuid().ToString("N") + ".db");
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var xml = new XmlDocument();
        xml.Load(appXaml);
        var resources = xml.DocumentElement.FirstChild;
        application.Resources = (ResourceDictionary)XamlReader.Parse(
            "<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
            "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" + resources.InnerXml + "</ResourceDictionary>");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var database = new SqliteDatabase(file);
        var auth = new AuthenticationService(database);
        var viewModel = new MainViewModel(new RoomTypeService(database));
        var window = new MainWindow(viewModel, auth, new PermissionService(database));
        DispatcherTimer driver = null;
        try
        {
            window.Show();
            PumpUntil(() => Field<Button>(window, "LoginButton").IsEnabled && viewModel.RoomTypes.Count == 2);
            Assert(Field<Grid>(window, "GuestPanel").Visibility == Visibility.Visible, "App did not start in Guest mode.");
            Assert(Field<Grid>(window, "StaffPanel").Visibility == Visibility.Collapsed, "Staff area was exposed before login.");
            Image(window, outputDirectory, "guest");

            // Drive the component's real event handlers using synthetic fields.
            // No OS input, UI Automation, or user database is involved.
            Exception driverError = null;
            var handled = false;
            driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            driver.Tick += (sender, args) =>
            {
                var dialog = application.Windows.OfType<AuthenticationWindow>().FirstOrDefault();
                if (dialog == null || handled) return;
                handled = true;
                try
                {
                    Field<TextBox>(dialog, "UserNameBox").Text = "admin";
                    Field<TextBox>(dialog, "FullNameBox").Text = "Quản trị viên thử nghiệm";
                    Field<PasswordBox>(dialog, "PasswordInput").Password = Password;
                    Field<PasswordBox>(dialog, "ConfirmInput").Password = "mismatch";
                    Click(Field<Button>(dialog, "SubmitButton"));
                    Assert(Field<TextBlock>(dialog, "StatusText").Text.Contains("chưa khớp"), "Confirmation mismatch was not shown.");
                    Image(dialog, outputDirectory, "setup");
                    Field<PasswordBox>(dialog, "ConfirmInput").Password = Password;
                    Click(Field<Button>(dialog, "SubmitButton"));
                    Assert(!Field<Button>(dialog, "SubmitButton").IsEnabled, "Repeated submits were not blocked while hashing.");
                }
                catch (Exception error) { driverError = error; dialog.Close(); }
            };
            driver.Start();
            Click(Field<Button>(window, "LoginButton"));
            PumpUntil(() => Field<Grid>(window, "StaffPanel").Visibility == Visibility.Visible || driverError != null);
            driver.Stop();
            if (driverError != null) throw driverError;
            Assert(Field<DataGrid>(window, "PermissionsTable").Items.Count == 27, "Admin UI did not show all current permissions.");
            Assert(Field<TextBlock>(window, "StaffIdentity").Text.Contains("Admin"), "Admin identity was not shown.");
            Image(window, outputDirectory, "staff");
            Click(Field<Button>(window, "LogoutButton"));
            PumpUntil(() => Field<Grid>(window, "GuestPanel").Visibility == Visibility.Visible && Field<Button>(window, "LoginButton").IsEnabled);
            Assert(Field<DataGrid>(window, "PermissionsTable").Items.Count == 0, "Logout left employee data in the UI.");

            var stage = 0;
            driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            driver.Tick += (sender, args) =>
            {
                var dialog = application.Windows.OfType<AuthenticationWindow>().FirstOrDefault();
                if (dialog == null) return;
                try
                {
                    if (stage == 0)
                    {
                        Field<TextBox>(dialog, "UserNameBox").Text = "ADMIN";
                        Field<PasswordBox>(dialog, "PasswordInput").Password = "incorrect";
                        Click(Field<Button>(dialog, "SubmitButton"));
                        stage = 1;
                    }
                    else if (stage == 1 && Field<Button>(dialog, "SubmitButton").IsEnabled)
                    {
                        Assert(Field<TextBlock>(dialog, "StatusText").Text.Contains("không đúng"), "Login error was not displayed.");
                        Assert(Field<PasswordBox>(dialog, "PasswordInput").Password == "", "Failed login left its password in the field.");
                        Image(dialog, outputDirectory, "login-error");
                        Field<PasswordBox>(dialog, "PasswordInput").Password = Password;
                        Click(Field<Button>(dialog, "SubmitButton"));
                        stage = 2;
                    }
                }
                catch (Exception error) { driverError = error; driver.Stop(); dialog.Close(); }
            };
            driver.Start();
            Click(Field<Button>(window, "LoginButton"));
            PumpUntil(() => Field<Grid>(window, "StaffPanel").Visibility == Visibility.Visible || driverError != null);
            driver.Stop();
            if (driverError != null) throw driverError;
            Assert(stage == 2 && Field<DataGrid>(window, "PermissionsTable").Items.Count == 27, "Correct login did not enter staff area.");
            Click(Field<Button>(window, "LogoutButton"));
            PumpUntil(() => Field<Grid>(window, "GuestPanel").Visibility == Visibility.Visible && Field<Button>(window, "LoginButton").IsEnabled);

            // Canceling a login dialog must leave the application in Guest mode.
            driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            driver.Tick += (sender, args) =>
            {
                var dialog = application.Windows.OfType<AuthenticationWindow>().FirstOrDefault();
                if (dialog == null) return;
                Assert(Field<StackPanel>(dialog, "ConfirmationFields").Visibility == Visibility.Collapsed, "Login still showed bootstrap fields.");
                Image(dialog, outputDirectory, "login");
                driver.Stop();
                dialog.DialogResult = false;
            };
            driver.Start();
            Click(Field<Button>(window, "LoginButton"));
            PumpUntil(() => !driver.IsEnabled && Field<Button>(window, "LoginButton").IsEnabled);
            Assert(Field<Grid>(window, "GuestPanel").Visibility == Visibility.Visible, "Canceling login left Guest mode.");
            Console.WriteLine("PASS WPF UI: Guest, bootstrap validation, async submit, Admin permissions, wrong/correct login, logout and cancel. Rendered five views.");
        }
        finally
        {
            if (driver != null) driver.Stop();
            window.Close();
            application.Shutdown();
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
