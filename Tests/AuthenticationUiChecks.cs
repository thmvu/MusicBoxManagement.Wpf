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
    private sealed class WorkerClock : IClock
    { public DateTimeOffset UtcNow { get { return new DateTimeOffset(2026,10,5,3,15,0,TimeSpan.Zero); } } }
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
        var testDirectory = Path.Combine(Path.GetTempPath(), "MusicBoxUiData_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        var file = Path.Combine(testDirectory, "test.db");
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var xml = new XmlDocument();
        xml.Load(appXaml);
        var resources = xml.DocumentElement.FirstChild;
        application.Resources = (ResourceDictionary)XamlReader.Parse(
            "<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
            "xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>" + resources.InnerXml + "</ResourceDictionary>");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        VerifyNoShowWorker(Path.Combine(testDirectory, "worker.db"));
        var database = new SqliteDatabase(file);
        var auth = new AuthenticationService(database);
        var viewModel = new MainViewModel(new RoomTypeService(database));
        var window = new MainWindow(viewModel, auth, new PermissionService(database), new RoomTypeService(database), new RoomService(database), new ServiceCatalogService(database), new CustomerService(database));
        DispatcherTimer driver = null;
        try
        {
            window.Show();
            PumpUntil(() => Field<Button>(window, "LoginButton").IsEnabled && viewModel.RoomTypes.Count == 2);
            Assert(Field<Grid>(window, "GuestPanel").Visibility == Visibility.Visible, "App did not start in Guest mode.");
            Assert(Field<Grid>(window, "StaffPanel").Visibility == Visibility.Collapsed, "Staff area was exposed before login.");
            Assert(Field<Button>(window, "EditRoomTypeButton").Visibility == Visibility.Collapsed, "Guest saw an editing action.");
            Assert(Field<Button>(window, "RoomsButton").Visibility == Visibility.Collapsed, "Guest saw room management.");
            Assert(Field<Button>(window, "ServicesButton").Visibility == Visibility.Collapsed, "Guest saw service management.");
            Assert(Field<Button>(window, "CustomersButton").Visibility == Visibility.Collapsed, "Guest saw customer management.");
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
            // This window contains both the list and the real add/edit form.
            var serviceStage = 0;
            driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            driver.Tick += (sender, args) =>
            {
                var catalog = application.Windows.OfType<ServicesWindow>().FirstOrDefault();
                if (catalog == null) return;
                try
                {
                    var vm = (ServicesViewModel)catalog.DataContext;
                    if (serviceStage == 0 && vm.CanEdit)
                    {
                        Field<TextBox>(catalog, "NameInput").Text = "Trà chanh";
                        Field<TextBox>(catalog, "PriceInput").Text = "15000.5";
                        Click(Field<Button>(catalog, "SaveButton"));
                        Assert(vm.Status.Contains("số nguyên đồng"), "Fractional service price accepted by UI.");
                        Image(catalog, outputDirectory, "service-price-error");
                        Field<TextBox>(catalog, "PriceInput").Text = "15000";
                        serviceStage = 1; Click(Field<Button>(catalog, "SaveButton"));
                    }
                    else if (serviceStage == 1 && vm.CanEdit && vm.Items.Count == 1)
                    {
                        Field<DataGrid>(catalog, "ServicesTable").SelectedIndex = 0;
                        Assert(vm.Items[0].Price == 15000 && vm.Items[0].Name == "Trà chanh", "Service create did not refresh.");
                        Field<TextBox>(catalog, "NameInput").Text = "Trà sữa";
                        Field<TextBox>(catalog, "PriceInput").Text = "25000";
                        Field<ComboBox>(catalog, "CategoryInput").SelectedIndex = 1;
                        Field<CheckBox>(catalog, "ActiveInput").IsChecked = false;
                        serviceStage = 2; Click(Field<Button>(catalog, "SaveButton"));
                    }
                    else if (serviceStage == 2 && vm.CanEdit && vm.Items[0].Price == 25000)
                    {
                        Assert(vm.Items.Count == 1 && !vm.Items[0].IsActive && vm.Items[0].Category == "Đồ ăn", "Service edit/toggle changed identity or lost fields.");
                        Image(catalog, outputDirectory, "service-catalog");
                        Click(Field<Button>(catalog, "NewButton"));
                        Field<TextBox>(catalog, "NameInput").Text = "Không lưu";
                        Click(Field<Button>(catalog, "CancelEditButton"));
                        Assert(string.IsNullOrEmpty(vm.Name) && vm.Items.Count == 1, "Cancel/new service wrote data.");
                        serviceStage = 3; driver.Stop(); catalog.Close();
                    }
                }
                catch (Exception error) { driverError = error; driver.Stop(); catalog.Close(); }
            };
            driver.Start(); Click(Field<Button>(window, "ServicesButton"));
            PumpUntil(() => (serviceStage == 3 && Field<Button>(window, "ServicesButton").IsEnabled) || driverError != null);
            driver.Stop(); if (driverError != null) throw driverError;
            var customerStage = 0;
            driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            driver.Tick += (sender, args) =>
            {
                var customers = application.Windows.OfType<CustomersWindow>().FirstOrDefault();
                if (customers == null) return;
                try
                {
                    var vm = (CustomersViewModel)customers.DataContext;
                    if (customerStage == 0 && vm.CanCreate)
                    {
                        Field<TextBox>(customers, "NameInput").Text = " Nguyễn An ";
                        Field<TextBox>(customers, "PhoneInput").Text = "+84 912.345-678";
                        customerStage = 1; Click(Field<Button>(customers, "SaveButton"));
                    }
                    else if (customerStage == 1 && vm.CanSelect && vm.Items.Count == 1)
                    {
                        Assert(vm.Items[0].PhoneNumber == "0912345678" && vm.Items[0].FullName == "Nguyễn An", "Customer normalization failed in UI.");
                        Field<TextBox>(customers, "PhoneQueryInput").Text = "84 912 345 678";
                        customerStage = 2; Click(Field<Button>(customers, "SearchButton"));
                    }
                    else if (customerStage == 2 && vm.CanSelect)
                    {
                        Assert(vm.Items.Count == 1, "Normalized phone lookup failed.");
                        Field<DataGrid>(customers, "CustomersTable").SelectedIndex = 0;
                        Field<TextBox>(customers, "NameInput").Text = "Chưa lưu";
                        Click(Field<Button>(customers, "CancelEditButton"));
                        Assert(vm.FullName == "Nguyễn An", "Cancel edit did not restore customer.");
                        Field<TextBox>(customers, "PhoneInput").Text = "abc";
                        customerStage = 3; Click(Field<Button>(customers, "SaveButton"));
                    }
                    else if (customerStage == 3 && vm.CanSelect && vm.Status.Contains("10 chữ số"))
                    {
                        Image(customers, outputDirectory, "customer-phone-error");
                        Field<TextBox>(customers, "NameInput").Text = "Nguyễn Bình";
                        Field<TextBox>(customers, "PhoneInput").Text = "0987-654-321";
                        customerStage = 4; Click(Field<Button>(customers, "SaveButton"));
                    }
                    else if (customerStage == 4 && vm.CanSelect && vm.Items[0].PhoneNumber == "0987654321")
                    {
                        Assert(vm.Items.Count == 1 && vm.Items[0].FullName == "Nguyễn Bình", "Customer edit inserted a new customer.");
                        Image(customers, outputDirectory, "customers");
                        Click(Field<Button>(customers, "NewButton")); Field<TextBox>(customers, "NameInput").Text = "Không lưu";
                        Click(Field<Button>(customers, "CancelEditButton"));
                        Assert(string.IsNullOrEmpty(vm.FullName) && vm.Items.Count == 1, "Cancel new customer wrote data.");
                        customerStage = 5; driver.Stop(); customers.Close();
                    }
                }
                catch (Exception error) { driverError = error; driver.Stop(); customers.Close(); }
            };
            driver.Start(); Click(Field<Button>(window, "CustomersButton"));
            PumpUntil(() => (customerStage == 5 && Field<Button>(window, "CustomersButton").IsEnabled) || driverError != null);
            driver.Stop(); if (driverError != null) throw driverError;
            Assert(Field<DataGrid>(window, "PermissionsTable").Items.Count == 27, "Admin UI did not show all current permissions.");
            Assert(Field<TextBlock>(window, "StaffIdentity").Text.Contains("Admin"), "Admin identity was not shown.");
            Image(window, outputDirectory, "staff");

            Assert(Field<Button>(window, "RoomTypesButton").Visibility == Visibility.Visible, "Admin did not receive the catalog action.");
            Click(Field<Button>(window, "RoomTypesButton"));
            PumpUntil(() => Field<Button>(window, "EditRoomTypeButton").Visibility == Visibility.Visible &&
                Field<Button>(window, "EditRoomTypeButton").IsEnabled);
            var editHandled = false;
            driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            driver.Tick += (sender, args) =>
            {
                var dialog = application.Windows.OfType<RoomTypeEditWindow>().FirstOrDefault();
                if (dialog == null || editHandled) return;
                editHandled = true;
                try
                {
                    Field<TextBox>(dialog, "PriceInput").Text = "145000.5";
                    Click(Field<Button>(dialog, "SaveButton"));
                    var editViewModel = (RoomTypeEditViewModel)dialog.DataContext;
                    Assert(editViewModel.Status.Contains("số nguyên đồng"), "Fractional VND UI validation did not display an error.");
                    Image(dialog, outputDirectory, "roomtype-edit-error");
                    Field<TextBox>(dialog, "NameInput").Text = "Standard học tập";
                    Field<TextBox>(dialog, "PriceInput").Text = "145000";
                    Field<TextBox>(dialog, "CapacityInput").Text = "5";
                    Field<TextBox>(dialog, "DescriptionInput").Text = "Đã sửa từ giao diện thử nghiệm.";
                    Click(Field<Button>(dialog, "SaveButton"));
                }
                catch (Exception error) { driverError = error; driver.Stop(); dialog.Close(); }
            };
            driver.Start();
            Click(Field<Button>(window, "EditRoomTypeButton"));
            PumpUntil(() => (viewModel.RoomTypes.Count == 2 && viewModel.RoomTypes[0].PricePerHour == 145000 &&
                Field<Button>(window, "EditRoomTypeButton").IsEnabled) || driverError != null);
            driver.Stop();
            if (driverError != null) throw driverError;
            Assert(viewModel.RoomTypes[0].Name == "Standard học tập" && viewModel.RoomTypes[0].Code == "STANDARD", "Saved editor data did not refresh the catalog.");
            Image(window, outputDirectory, "roomtype-catalog");
            var cancelHandled = false;
            driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            driver.Tick += (sender, args) =>
            {
                var dialog = application.Windows.OfType<RoomTypeEditWindow>().FirstOrDefault();
                if (dialog == null || cancelHandled) return;
                cancelHandled = true;
                Field<TextBox>(dialog, "NameInput").Text = "Không lưu tên này";
                driver.Stop();
                dialog.DialogResult = false;
            };
            driver.Start();
            Click(Field<Button>(window, "EditRoomTypeButton"));
            PumpUntil(() => cancelHandled && Field<Button>(window, "EditRoomTypeButton").IsEnabled);
            Assert(new RoomTypeService(database).List()[0].Name == "Standard học tập", "Canceling the editor saved changes.");

            // Open the actual room catalog and create form, including image validation.
            Click(Field<Button>(window, "BackToStaffButton"));
            PumpUntil(() => Field<Grid>(window, "StaffPanel").Visibility == Visibility.Visible);
            Assert(Field<Button>(window, "RoomsButton").Visibility == Visibility.Visible, "Admin room action hidden.");
            var fixtureImage = Path.Combine(testDirectory, "fixture.png");
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(140, 158, 150)), null, new Rect(0, 0, 160, 120));
                drawing.DrawRectangle(Brushes.DarkSlateGray, null, new Rect(30, 20, 100, 45));
                drawing.DrawRectangle(Brushes.LightGray, null, new Rect(20, 85, 120, 20));
            }
            var fixtureBitmap = new RenderTargetBitmap(160, 120, 96, 96, PixelFormats.Pbgra32);
            fixtureBitmap.Render(visual);
            var fixtureEncoder = new PngBitmapEncoder(); fixtureEncoder.Frames.Add(BitmapFrame.Create(fixtureBitmap));
            using (var stream = File.Create(fixtureImage)) fixtureEncoder.Save(stream);
            var roomStage = 0;
            string originalImageUrl = null;
            RoomsWindow catalogWindow = null;
            driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            driver.Tick += (sender, args) =>
            {
                try
                {
                    var catalog = application.Windows.OfType<RoomsWindow>().FirstOrDefault();
                    var dialog = application.Windows.OfType<RoomCreateWindow>().FirstOrDefault();
                    var editDialog = application.Windows.OfType<RoomEditWindow>().FirstOrDefault();
                    if (roomStage == 0 && catalog != null && Field<Button>(catalog, "AddRoomButton").IsEnabled)
                    {
                        catalogWindow = catalog;
                        roomStage = 1;
                        Click(Field<Button>(catalog, "AddRoomButton"));
                    }
                    else if (roomStage == 1 && dialog != null)
                    {
                        roomStage = 2;
                        Field<TextBox>(dialog, "CodeInput").Text = "P01";
                        Field<TextBox>(dialog, "NameInput").Text = "Phòng thử nghiệm";
                        Field<ComboBox>(dialog, "TypeInput").SelectedIndex = 0;
                        Click(Field<Button>(dialog, "SaveButton"));
                    }
                    else if (roomStage == 2 && dialog != null && Field<Button>(dialog, "SaveButton").IsEnabled)
                    {
                        Assert(Field<TextBlock>(dialog, "StatusText").Text.Contains("ảnh"), "Missing image validation was not shown.");
                        Image(dialog, outputDirectory, "room-create-error");
                        ((RoomsViewModel)dialog.DataContext).ImageFilePath = fixtureImage;
                        Field<TextBox>(dialog, "DescriptionInput").Text = "Phòng thử nghiệm từ giao diện.";
                        roomStage = 3;
                        Click(Field<Button>(dialog, "SaveButton"));
                        Assert(!Field<Button>(dialog, "SaveButton").IsEnabled, "Duplicate room submits were not blocked.");
                    }
                    else if (roomStage == 3 && dialog == null && catalog != null && Field<DataGrid>(catalog, "RoomsTable").Items.Count == 1)
                    {
                        Field<DataGrid>(catalog, "RoomsTable").SelectedIndex = 0;
                        Assert(Field<System.Windows.Controls.Image>(catalog, "RoomImage").Source != null, "Saved room image did not display.");
                        Image(catalog, outputDirectory, "room-catalog");
                        roomStage = 4;
                        Click(Field<Button>(catalog, "AddRoomButton"));
                    }
                    else if (roomStage == 4 && dialog != null)
                    {
                        Field<TextBox>(dialog, "CodeInput").Text = "CANCELLED";
                        roomStage = 5;
                        Assert(Field<Button>(dialog, "CancelButton").IsCancel, "Room form has no cancel action.");
                        dialog.DialogResult = false;
                    }
                    else if (roomStage == 5 && dialog == null && catalog != null)
                    {
                        Assert(Field<DataGrid>(catalog, "RoomsTable").Items.Count == 1, "Cancel created another room.");
                        Field<DataGrid>(catalog, "RoomsTable").SelectedIndex = 0;
                        originalImageUrl = ((MusicBoxManagement.Wpf.Models.Room)Field<DataGrid>(catalog, "RoomsTable").SelectedItem).ImageUrl;
                        roomStage = 6;
                        Click(Field<Button>(catalog, "EditRoomButton"));
                    }
                    else if (roomStage == 6 && editDialog != null)
                    {
                        Field<TextBox>(editDialog, "NameInput").Text = " ";
                        roomStage = 7;
                        Click(Field<Button>(editDialog, "SaveButton"));
                    }
                    else if (roomStage == 7 && editDialog != null && Field<Button>(editDialog, "SaveButton").IsEnabled)
                    {
                        Assert(Field<TextBlock>(editDialog, "StatusText").Text.Contains("Tên phòng"), "Invalid room name did not display error.");
                        Image(editDialog, outputDirectory, "room-edit-error");
                        Field<TextBox>(editDialog, "NameInput").Text = "Phòng đã sửa từ UI";
                        Field<TextBox>(editDialog, "DescriptionInput").Text = "Mô tả đã sửa từ UI.";
                        ((RoomEditViewModel)editDialog.DataContext).ReplacementImageFilePath = fixtureImage;
                        roomStage = 8;
                        Click(Field<Button>(editDialog, "SaveButton"));
                        Assert(!Field<Button>(editDialog, "SaveButton").IsEnabled, "Room edit did not block duplicate saves.");
                    }
                    else if (roomStage == 8 && editDialog == null && catalog != null && Field<Button>(catalog, "EditRoomButton").IsEnabled)
                    {
                        var savedRoom = (MusicBoxManagement.Wpf.Models.Room)Field<DataGrid>(catalog, "RoomsTable").SelectedItem;
                        Assert(savedRoom.Name == "Phòng đã sửa từ UI" && savedRoom.Description == "Mô tả đã sửa từ UI." &&
                            savedRoom.RoomCode == "P01" && savedRoom.ImageUrl != originalImageUrl &&
                            Field<System.Windows.Controls.Image>(catalog, "RoomImage").Source != null, "Edited room/image did not refresh.");
                        Image(catalog, outputDirectory, "room-edited-catalog");
                        roomStage = 9;
                        Click(Field<Button>(catalog, "EditRoomButton"));
                    }
                    else if (roomStage == 9 && editDialog != null)
                    {
                        var editVm = (RoomEditViewModel)editDialog.DataContext;
                        editVm.ReplacementImageFilePath = fixtureImage;
                        Click(Field<Button>(editDialog, "KeepImageButton"));
                        Assert(editVm.ReplacementImageFilePath == null, "Keep current image did not clear replacement.");
                        Field<ComboBox>(editDialog, "TypeInput").SelectedIndex = 1;
                        Field<CheckBox>(editDialog, "ActiveInput").IsChecked = false;
                        roomStage = 10;
                        Click(Field<Button>(editDialog, "SaveButton"));
                    }
                    else if (roomStage == 10 && editDialog != null && Field<Button>(editDialog, "SaveButton").IsEnabled)
                    {
                        Assert(Field<TextBlock>(editDialog, "StatusText").Text.Contains("lý do"), "Missing lock reason was not shown.");
                        Image(editDialog, outputDirectory, "room-lock-error");
                        Field<TextBox>(editDialog, "ReasonInput").Text = "Vệ sinh phòng";
                        roomStage = 11; Click(Field<Button>(editDialog, "SaveButton"));
                    }
                    else if (roomStage == 11 && editDialog == null && catalog != null && Field<Button>(catalog, "EditRoomButton").IsEnabled)
                    {
                        var locked = (MusicBoxManagement.Wpf.Models.Room)Field<DataGrid>(catalog, "RoomsTable").SelectedItem;
                        Assert(!locked.IsActive && locked.InactiveReason == "Vệ sinh phòng" && locked.RoomTypeId == 2, "UI did not change type/lock with reason.");
                        Image(catalog, outputDirectory, "room-locked-catalog");
                        roomStage = 12; Click(Field<Button>(catalog, "EditRoomButton"));
                    }
                    else if (roomStage == 12 && editDialog != null)
                    {
                        Field<CheckBox>(editDialog, "ActiveInput").IsChecked = true;
                        roomStage = 13; Click(Field<Button>(editDialog, "SaveButton"));
                    }
                    else if (roomStage == 13 && editDialog == null && catalog != null && Field<Button>(catalog, "EditRoomButton").IsEnabled)
                    {
                        var opened = (MusicBoxManagement.Wpf.Models.Room)Field<DataGrid>(catalog, "RoomsTable").SelectedItem;
                        Assert(opened.IsActive && opened.InactiveReason == null && opened.RoomTypeId == 2, "Unlock UI did not clear reason/preserve type.");
                        roomStage = 14; Click(Field<Button>(catalog, "EditRoomButton"));
                    }
                    else if (roomStage == 14 && editDialog != null)
                    {
                        Field<TextBox>(editDialog, "NameInput").Text = "Không lưu tên này";
                        roomStage = 15;
                        Assert(Field<Button>(editDialog, "CancelButton").IsCancel, "Editor has no cancel action.");
                        editDialog.DialogResult = false;
                    }
                    else if (roomStage == 15 && editDialog == null && catalog != null)
                    {
                        using (var connection = database.OpenConnection())
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = "SELECT Name FROM Rooms WHERE RoomCode='P01';";
                            Assert((string)command.ExecuteScalar() == "Phòng đã sửa từ UI", "Canceling editor saved changes.");
                        }
                        roomStage = 16; driver.Stop(); catalog.Close();
                    }
                }
                catch (Exception error)
                {
                    driverError = error; driver.Stop();
                    foreach (var dialog in application.Windows.OfType<RoomCreateWindow>().ToArray()) dialog.Close();
                    foreach (var editor in application.Windows.OfType<RoomEditWindow>().ToArray()) editor.Close();
                    if (catalogWindow != null) catalogWindow.Close();
                }
            };
            driver.Start();
            Click(Field<Button>(window, "RoomsButton"));
            PumpUntil(() => (roomStage == 16 && Field<Button>(window, "RoomsButton").IsEnabled) || driverError != null);
            driver.Stop();
            if (driverError != null) throw driverError;
            Click(Field<Button>(window, "LogoutButton"));
            PumpUntil(() => Field<Grid>(window, "GuestPanel").Visibility == Visibility.Visible && Field<Button>(window, "LoginButton").IsEnabled);
            Assert(Field<DataGrid>(window, "PermissionsTable").Items.Count == 0, "Logout left employee data in the UI.");
            Assert(Field<Button>(window, "EditRoomTypeButton").Visibility == Visibility.Collapsed, "Logout left an editing action in Guest mode.");
            Assert(Field<Button>(window, "RoomsButton").Visibility == Visibility.Collapsed, "Logout exposed room management.");

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
            Console.WriteLine("PASS WPF UI: NoShow worker startup/retry, Guest/auth, rooms/types/images, services, customers. Rendered seventeen views.");
        }
        finally
        {
            if (driver != null) driver.Stop();
            window.Close();
            application.Shutdown();
            Directory.Delete(testDirectory, true);
        }
    }

    private static void VerifyNoShowWorker(string file)
    {
        var db = new SqliteDatabase(file); db.Initialize();
        Action<string> sql = statement => { using (var c=db.OpenConnection()) using(var cmd=c.CreateCommand()) { cmd.CommandText=statement;cmd.ExecuteNonQuery(); } };
        Func<string,object> value = statement => { using(var c=db.OpenConnection()) using(var cmd=c.CreateCommand()) { cmd.CommandText=statement;return cmd.ExecuteScalar(); } };
        sql(@"INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('WORKER','Worker',1,'test.png',1,'test');
INSERT INTO Customers(FullName,PhoneNumber) VALUES('Worker','0912345678');
INSERT INTO Reservations(CustomerId,RoomId,StartTime,EndTime,Status,CreatedAt) VALUES(1,1,'2026-10-05T03:00:00.0000000+00:00','2026-10-05T04:00:00.0000000+00:00','Confirmed','test');
CREATE TRIGGER FailWorkerAudit BEFORE INSERT ON AuditLog WHEN NEW.Action='Reservation.NoShow' BEGIN SELECT RAISE(ABORT,'test failure'); END;");
        using(var worker=new NoShowWorker(db,new WorkerClock()))
        {
            var run=worker.StartAsync();PumpUntil(()=>run.IsCompleted);run.GetAwaiter().GetResult();
            Assert((string)value("SELECT Status FROM Reservations;")=="Confirmed", "Worker failure left status changed.");
            sql("DROP TRIGGER FailWorkerAudit;");
            run=worker.StartAsync();PumpUntil(()=>run.IsCompleted);run.GetAwaiter().GetResult();
            Assert((string)value("SELECT Status FROM Reservations;")=="NoShow", "Worker startup/retry did not process expired booking.");
            run=worker.StartAsync();PumpUntil(()=>run.IsCompleted);run.GetAwaiter().GetResult();
            Assert(Convert.ToInt64(value("SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.NoShow';"))==1,"Worker rerun duplicated audit.");
        }
    }
}
