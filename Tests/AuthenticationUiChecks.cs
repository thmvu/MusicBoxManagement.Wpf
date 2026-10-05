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
    private sealed class BookingClock : IClock
    { public DateTimeOffset UtcNow { get { return new DateTimeOffset(2026,10,5,2,0,0,TimeSpan.Zero); } } }
    private sealed class LookupClock : IClock { public DateTimeOffset UtcNow {get;set;} }
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
        var window = new MainWindow(viewModel, auth, new PermissionService(database), new RoomTypeService(database), new RoomService(database), new ServiceCatalogService(database), new CustomerService(database), new GuestBookingService(database), new GuestReservationService(database), new StaffReservationService(database));
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
            Assert(Field<Button>(window, "ReservationsButton").Visibility == Visibility.Collapsed, "Guest saw internal booking data.");
            Image(window, outputDirectory, "guest");
            VerifyGuestBooking(Path.Combine(testDirectory,"booking.db"),outputDirectory);
            VerifyGuestLookup(Path.Combine(testDirectory,"lookup.db"),outputDirectory);
            VerifyStaffReservations(Path.Combine(testDirectory,"staff-booking.db"),outputDirectory);
            driver = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            driver.Tick += (sender,args) => {
                var form=application.Windows.OfType<GuestBookingWindow>().FirstOrDefault();
                if(form==null)return;
                var vm=(GuestBookingViewModel)form.DataContext;
                if(vm.IsBusy || !vm.Status.Contains("Chưa có phòng"))return;
                Assert(!vm.CanBook && vm.Rooms.Count==0,"Empty guest catalog allowed submission.");
                Image(form,outputDirectory,"booking-empty");driver.Stop();form.Close();
            };
            driver.Start();Click(Field<Button>(window,"BookingButton"));driver.Stop();

            var lookupStage=0;Exception lookupError=null;
            driver=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
            driver.Tick+=(sender,args)=>{
                var form=application.Windows.OfType<GuestLookupWindow>().FirstOrDefault();if(form==null)return;
                try
                {
                    var vm=(GuestLookupViewModel)form.DataContext;
                    if(lookupStage==0){Field<TextBox>(form,"PhoneInput").Text="0912345678";lookupStage=1;Click(Field<Button>(form,"SearchButton"));}
                    else if(!vm.IsBusy){Assert(vm.Items.Count==0 && vm.Status.Contains("Không có"),"Main lookup entry returned wrong results.");Image(form,outputDirectory,"lookup-empty");lookupStage=2;driver.Stop();form.Close();}
                }
                catch(Exception error){lookupError=error;driver.Stop();form.Close();}
            };
            driver.Start();Click(Field<Button>(window,"LookupButton"));driver.Stop();
            if(lookupError!=null)throw lookupError;
            Assert(lookupStage==2 && Field<Button>(window,"LookupButton").IsEnabled,"Lookup did not open/reset from main window.");

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
            Assert(Field<Button>(window,"ReservationsButton").Visibility==Visibility.Visible,"Admin did not see internal booking entry.");
            var reservationStage=0;
            driver=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
            driver.Tick+=(sender,args)=>{
                var catalog=application.Windows.OfType<ReservationsWindow>().FirstOrDefault();if(catalog==null)return;
                try
                {
                    var vm=(ReservationsViewModel)catalog.DataContext;if(vm.IsBusy)return;
                    Assert(vm.Items.Count==0 && vm.Status.Contains("Tìm thấy"),"Main internal booking entry loaded wrong data.");
                    Image(catalog,outputDirectory,"staff-booking-empty");reservationStage=1;driver.Stop();catalog.Close();
                }
                catch(Exception error){driverError=error;driver.Stop();catalog.Close();}
            };
            driver.Start();Click(Field<Button>(window,"ReservationsButton"));
            PumpUntil(()=>reservationStage==1 || driverError!=null);driver.Stop();if(driverError!=null)throw driverError;
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
            Console.WriteLine("PASS WPF UI: Staff booking list/details/filters/reasons/cancel/readonly/revocation/main entry, Guest lookup/booking, NoShow, auth, rooms/types/images, services, customers. Rendered thirty views.");
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

    private static void VerifyStaffReservations(string file,string outputDirectory)
    {
        var db=new SqliteDatabase(file);var auth=new AuthenticationService(db);
        var setup=auth.SetupAdminAsync("admin","Admin thử nghiệm",Password);PumpUntil(()=>setup.IsCompleted);var admin=setup.GetAwaiter().GetResult();
        Action<string> sql=statement=>{using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=statement;cmd.ExecuteNonQuery();}};
        Func<string,object> value=statement=>{using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=statement;return cmd.ExecuteScalar();}};
        sql(@"INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('P01','Phòng nội bộ',1,'test.png',1,'test');
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'reader','reader','READER',PasswordHash,'reader','Nhân viên chỉ xem',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('reader','Staff');
DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Reservation.Cancel');");
        var clock=new LookupClock{UtcNow=new DateTimeOffset(2026,10,5,2,0,0,TimeSpan.Zero)};var create=new ReservationService(db,clock);
        foreach(var hour in new[]{10,13})create.CreateGuest(new MusicBoxManagement.Wpf.Models.ReservationRequest{RoomId=1,StartTime=new DateTimeOffset(2026,10,5,hour,0,0,TimeSpan.FromHours(7)),DurationMinutes=60,FullName="Khách đã đặt",PhoneNumber="0912345678"});
        var service=new StaffReservationService(db,clock);var vm=new ReservationsViewModel(service,admin,clock);var form=new ReservationsWindow(vm);
        try
        {
            form.Show();PumpUntil(()=>!vm.IsBusy);Assert(vm.Items.Count==2,"Internal booking list did not load.");
            Field<DataGrid>(form,"ReservationsTable").SelectedIndex=0;Assert(vm.CanCancel && vm.Details.Contains("0912345678"),"Staff below two hours/details failed.");
            Image(form,outputDirectory,"staff-booking-list");
            Field<TextBox>(form,"PhoneInput").Text="bad";Click(Field<Button>(form,"SearchButton"));PumpUntil(()=>!vm.IsBusy);
            Assert(vm.Items.Count==0 && vm.Status.Contains("SĐT"),"Invalid staff phone filter accepted.");
            Field<TextBox>(form,"PhoneInput").Text="+84 912.345-678";Field<ComboBox>(form,"StateInput").SelectedItem="Confirmed";
            Click(Field<Button>(form,"SearchButton"));PumpUntil(()=>!vm.IsBusy);Assert(vm.Items.Count==2,"Staff filters failed.");
            Field<DataGrid>(form,"ReservationsTable").SelectedIndex=0;Click(Field<Button>(form,"CancelButton"));Click(Field<Button>(form,"KeepButton"));
            Assert(!vm.IsConfirming && Convert.ToInt64(value("SELECT COUNT(*) FROM Reservations WHERE Status='Cancelled';"))==0,"Staff keep booking saved changes.");
            Click(Field<Button>(form,"CancelButton"));Click(Field<Button>(form,"ConfirmButton"));PumpUntil(()=>!vm.IsBusy);
            Assert(vm.IsConfirming && vm.Status.Contains("Lý do"),"Missing reason was not rejected/left confirmation.");
            Field<TextBox>(form,"ReasonInput").Text="Khách gọi đổi kế hoạch";Image(form,outputDirectory,"staff-booking-reason");
            Click(Field<Button>(form,"ConfirmButton"));PumpUntil(()=>!vm.IsBusy);
            Assert(vm.Items.Count==1 && vm.Status.Contains("Đã hủy"),"Staff UI did not cancel below two hours.");
            Field<ComboBox>(form,"StateInput").SelectedItem="Cancelled";Click(Field<Button>(form,"SearchButton"));PumpUntil(()=>!vm.IsBusy);
            Field<DataGrid>(form,"ReservationsTable").SelectedIndex=0;
            Assert(!vm.CanCancel && vm.Details.Contains("Khách gọi đổi kế hoạch"),"Cancelled details/reason/repeat guard failed.");
            Image(form,outputDirectory,"staff-booking-cancelled");
        }
        finally{form.Close();}
        var login=auth.LoginAsync("reader",Password);PumpUntil(()=>login.IsCompleted);var reader=login.GetAwaiter().GetResult();
        vm=new ReservationsViewModel(service,reader,clock);form=new ReservationsWindow(vm);
        try
        {
            form.Show();PumpUntil(()=>!vm.IsBusy);Field<DataGrid>(form,"ReservationsTable").SelectedIndex=1;
            Assert(vm.Items.Count==2 && !vm.CanCancel && vm.Details.Contains("Khách đã đặt"),"Read-only Staff could not view or could cancel.");
            Image(form,outputDirectory,"staff-booking-readonly");
            sql("DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Reservation.View');");
            Click(Field<Button>(form,"SearchButton"));PumpUntil(()=>!vm.IsBusy);
            Assert(vm.Items.Count==0 && vm.Selected==null && !vm.CanSelect,"Revoked view left private booking/customer details.");
        }
        finally{form.Close();}
    }

    private static void VerifyGuestLookup(string file,string outputDirectory)
    {
        var db=new SqliteDatabase(file);db.Initialize();
        Action<string> sql=statement=>{using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=statement;cmd.ExecuteNonQuery();}};
        Func<string,object> value=statement=>{using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=statement;return cmd.ExecuteScalar();}};
        sql("INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('P01','Phòng tra cứu',1,'test.png',1,'test');");
        var clock=new LookupClock{UtcNow=new DateTimeOffset(2026,10,5,2,0,0,TimeSpan.Zero)};
        var create=new ReservationService(db,clock);
        foreach(var hour in new[]{10,13,15})create.CreateGuest(new MusicBoxManagement.Wpf.Models.ReservationRequest{RoomId=1,StartTime=new DateTimeOffset(2026,10,5,hour,0,0,TimeSpan.FromHours(7)),DurationMinutes=60,FullName="Khách thử nghiệm",PhoneNumber="0912345678"});
        var form=new GuestLookupWindow(new GuestReservationService(db,clock));var vm=(GuestLookupViewModel)form.DataContext;
        try
        {
            form.Show();Field<TextBox>(form,"PhoneInput").Text="bad";Click(Field<Button>(form,"SearchButton"));PumpUntil(()=>!vm.IsBusy);
            Assert(vm.Items.Count==0 && vm.Status.Contains("SĐT"),"Invalid phone lookup accepted.");
            Field<TextBox>(form,"PhoneInput").Text="+84 912.345-678";Click(Field<Button>(form,"SearchButton"));PumpUntil(()=>!vm.IsBusy);
            Assert(vm.Items.Count==3,"Real lookup controls missed bookings.");
            Field<DataGrid>(form,"ReservationsTable").SelectedIndex=0;
            Assert(!vm.CanCancel && vm.Details.Contains("liên hệ"),"Under two hours did not show contact store.");
            Image(form,outputDirectory,"lookup-list");
            Field<DataGrid>(form,"ReservationsTable").SelectedIndex=1;Click(Field<Button>(form,"CancelButton"));
            Assert(vm.IsConfirming && !vm.CanSearch,"Inline confirmation did not protect selection.");
            Image(form,outputDirectory,"lookup-confirm");Click(Field<Button>(form,"KeepButton"));
            Assert(!vm.IsConfirming && Convert.ToInt64(value("SELECT COUNT(*) FROM Reservations WHERE Status='Cancelled';"))==0,"Keep booking wrote cancellation.");
            Click(Field<Button>(form,"CancelButton"));clock.UtcNow=new DateTimeOffset(2026,10,5,4,0,0,TimeSpan.Zero);
            Click(Field<Button>(form,"ConfirmButton"));PumpUntil(()=>!vm.IsBusy);
            Assert(vm.Status.Contains("Đã hủy") && vm.Items.Count==1 && Convert.ToInt64(value("SELECT COUNT(*) FROM Reservations WHERE Status='Cancelled';"))==1,"Exact boundary UI cancellation failed.");
            Image(form,outputDirectory,"lookup-cancelled");
            Field<DataGrid>(form,"ReservationsTable").SelectedIndex=0;Click(Field<Button>(form,"CancelButton"));
            clock.UtcNow=new DateTimeOffset(2026,10,5,6,0,0,TimeSpan.Zero).AddTicks(1);
            Click(Field<Button>(form,"ConfirmButton"));PumpUntil(()=>!vm.IsBusy);
            Assert(vm.Status.Contains("liên hệ") && vm.Items.Count==1 && !vm.Items[0].CanCancel,"Stale confirmation bypassed time guard.");
            Field<TextBox>(form,"PhoneInput").Text="0987654321";
            Assert(vm.Items.Count==0 && vm.Selected==null && !vm.CanCancel,"Changing phone left old results.");
            Click(Field<Button>(form,"SearchButton"));PumpUntil(()=>!vm.IsBusy);
            Assert(vm.Items.Count==0 && vm.Status.Contains("Không có"),"Unknown phone returned others' bookings.");
        }
        finally{form.Close();}
    }

    private static void VerifyGuestBooking(string file,string outputDirectory)
    {
        var db=new SqliteDatabase(file);db.Initialize();
        var imageUrl="Content/uploads/rooms/"+Guid.NewGuid().ToString("N")+".png";
        var imagePath=Path.Combine(Path.GetDirectoryName(file),imageUrl.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(imagePath));
        var bitmap=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[]{80,100,120,255,80,100,120,255,80,100,120,255,80,100,120,255},8);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(imagePath))encoder.Save(stream);
        Action<string> sql=statement=>{using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=statement;cmd.ExecuteNonQuery();}};
        Func<string,object> value=statement=>{using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=statement;return cmd.ExecuteScalar();}};
        sql("INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,InactiveReason,CreatedAt) VALUES('P01','Phòng demo',1,'"+imageUrl+"',1,NULL,'test'),('LOCK','Phòng khóa',2,'"+imageUrl+"',0,'Bảo trì','test'); INSERT INTO Customers(FullName,PhoneNumber) VALUES('Tên đã có','0912345678');");
        var clock=new BookingClock();var service=new GuestBookingService(db,clock);var vm=new GuestBookingViewModel(service,clock);
        var form=new GuestBookingWindow(service,vm);
        Action<Func<bool>,string> wait=(ready,step)=>{try{PumpUntil(ready);}catch(Exception error){throw new Exception("Guest booking "+step+": "+vm.Status+"; "+vm.PreviewText+"; room="+(vm.SelectedRoom==null?"null":vm.SelectedRoom.RoomCode)+"; time="+vm.StartTimeText+"; duration="+vm.Duration,error);}};
        try
        {
            form.Show();wait(()=>!vm.IsBusy && vm.CanBook,"load");
            Assert(vm.Rooms.Count==1 && Field<Image>(form,"RoomImage").Source!=null,"Public room filter/image failed.");
            Field<DatePicker>(form,"DateInput").SelectedDate=new DateTime(2026,10,5);
            Field<ComboBox>(form,"TimeInput").SelectedItem="13:00";Field<TextBox>(form,"NameInput").Text="Tên vừa nhập";
            Field<TextBox>(form,"PhoneInput").Text="bad";Click(Field<Button>(form,"PreviewButton"));wait(()=>!vm.IsBusy && vm.Status.Contains("SĐT"),"invalid phone");
            Field<TextBox>(form,"PhoneInput").Text="+84 912.345-678";
            Field<ComboBox>(form,"TimeInput").SelectedItem="11:00";Field<ComboBox>(form,"DurationInput").SelectedItem=180;
            Click(Field<Button>(form,"PreviewButton"));wait(()=>!vm.IsBusy && vm.PreviewText.Contains("một ca"),"shift");
            Field<ComboBox>(form,"TimeInput").SelectedItem="13:00";Field<ComboBox>(form,"DurationInput").SelectedItem=60;
            Click(Field<Button>(form,"PreviewButton"));wait(()=>!vm.IsBusy && vm.Status.Contains("Có thể đặt"),"preview");
            Assert(Convert.ToInt64(value("SELECT COUNT(*) FROM Reservations;"))==0 && Convert.ToInt64(value("SELECT COUNT(*) FROM Customers;"))==1,"Preview wrote a booking/customer.");
            Image(form,outputDirectory,"booking-form");
            service.Create(new MusicBoxManagement.Wpf.Models.ReservationRequest{RoomId=1,StartTime=new DateTimeOffset(2026,10,5,13,0,0,TimeSpan.FromHours(7)),DurationMinutes=60,FullName="Người khác",PhoneNumber="0987654321"});
            Click(Field<Button>(form,"SubmitButton"));wait(()=>!vm.IsBusy && vm.Status.Contains("trùng"),"conflict");
            Image(form,outputDirectory,"booking-conflict");
            Assert(Convert.ToInt64(value("SELECT COUNT(*) FROM Reservations;"))==1,"Submit trusted stale preview.");
            Field<ComboBox>(form,"TimeInput").SelectedItem="15:00";
            Assert(!vm.PreviewText.Contains("Khoảng giờ hợp lệ"),"Changing input left old preview valid.");
            Click(Field<Button>(form,"SubmitButton"));wait(()=>!vm.IsBusy && vm.Status.Contains("thành công"),"confirmation");
            Assert(!vm.CanInput && !vm.CanBook && (string)value("SELECT FullName FROM Customers WHERE PhoneNumber='0912345678';")=="Tên đã có","Confirmation/old customer name failed.");
            Assert(Convert.ToInt64(value("SELECT COUNT(*) FROM AspNetUsers;"))==0 && Convert.ToInt64(value("SELECT COUNT(*) FROM Reservations WHERE Status='Confirmed' AND CreatedByUserId IS NULL;"))==2,"Guest booking required account or created wrong state.");
            Image(form,outputDirectory,"booking-confirmed");Click(Field<Button>(form,"SubmitButton"));
            Assert(Convert.ToInt64(value("SELECT COUNT(*) FROM Reservations;"))==2,"Repeated successful submit created duplicate.");
            Click(Field<Button>(form,"NewButton"));PumpUntil(()=>!vm.IsBusy && vm.CanInput);
            Assert(string.IsNullOrEmpty(vm.FullName) && string.IsNullOrEmpty(vm.PhoneNumber),"New booking retained previous guest details.");
            Field<TextBox>(form,"NameInput").Text="Không lưu";form.Close();
            Assert(Convert.ToInt64(value("SELECT COUNT(*) FROM Reservations;"))==2,"Closing form submitted booking.");
        }
        finally{form.Close();}
    }
}
