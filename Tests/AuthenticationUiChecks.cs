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

    private static void VerifyNavigationLayout(Window window, bool staff, string directory)
    {
        window.UpdateLayout();
        var sidebar = Field<Border>(window, "Sidebar");
        var panel = Field<Grid>(window, staff ? "StaffPanel" : "GuestPanel");
        Assert(panel.TransformToAncestor(window).Transform(new Point()).X >= sidebar.ActualWidth,
            "Content overlapped the sidebar.");
        Assert(panel.ActualWidth > 650, "Main content was too narrow.");
        Assert(Field<StackPanel>(window, "StaffNavigation").IsVisible == staff,
            "Staff navigation visibility did not follow the session.");
        Assert(Field<StackPanel>(window, "GuestNavigation").IsVisible != staff,
            "Guest navigation visibility did not follow the session.");
        var names = staff ? new[] { "RoomTypesButton", "RoomsButton", "ServicesButton", "CustomersButton",
            "ReservationsButton", "StaffBookingButton", "CalendarButton", "RefreshAccessButton" }
            : new[] { "BookingButton", "LookupButton" };
        double previousBottom = -1;
        foreach (var name in names)
        {
            var button = Field<Button>(window, name);
            var point = button.TransformToAncestor(sidebar).Transform(new Point());
            Assert(button.IsVisible && point.X >= 0 && point.X + button.ActualWidth <= sidebar.ActualWidth,
                "Navigation action escaped the sidebar: " + name);
            Assert(point.Y >= previousBottom, "Navigation actions overlapped: " + name);
            previousBottom = point.Y + button.ActualHeight;
        }
        var width = window.Width; var height = window.Height;
        try
        {
            window.Width = window.MinWidth; window.Height = window.MinHeight;
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            window.UpdateLayout();
            Assert(panel.ActualWidth > 650, "Compact content was too narrow.");
            var footer = Field<Button>(window, staff ? "LogoutButton" : "LoginButton");
            var point = footer.TransformToAncestor(window).Transform(new Point());
            Assert(footer.IsVisible && point.Y + footer.ActualHeight <= window.ActualHeight,
                "Compact window hid the login/logout action.");
            Image(window, directory, staff ? "staff-compact" : "guest-compact");
        }
        finally { window.Width = width; window.Height = height; window.UpdateLayout(); }
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
            Assert(Field<Button>(window, "StaffBookingButton").Visibility == Visibility.Collapsed, "Guest saw Staff create route.");
            Assert(Field<Button>(window, "CalendarButton").Visibility == Visibility.Collapsed, "Guest saw internal calendar.");
            Image(window, outputDirectory, "guest");
            VerifyNavigationLayout(window, false, outputDirectory);
            VerifyGuestBooking(Path.Combine(testDirectory,"booking.db"),outputDirectory);
            VerifyGuestLookup(Path.Combine(testDirectory,"lookup.db"),outputDirectory);
            VerifyStaffReservations(Path.Combine(testDirectory,"staff-booking.db"),outputDirectory);
            VerifyCheckInUi(Path.Combine(testDirectory,"checkin-ui.db"),outputDirectory);
            VerifyStaffBooking(Path.Combine(testDirectory,"staff-create.db"),outputDirectory);
            VerifyCalendarDay(Path.Combine(testDirectory,"calendar-day.db"),outputDirectory);
            VerifyGuestCalendar(Path.Combine(testDirectory,"guest-calendar.db"),outputDirectory);
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
            Assert(Field<Button>(window,"CalendarButton").Visibility==Visibility.Visible,"Admin did not see calendar entry.");
            var calendarOpened=false;
            driver=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
            driver.Tick+=(sender,args)=>{
                var form=application.Windows.OfType<CalendarDayWindow>().FirstOrDefault();if(form==null)return;
                var vm=(CalendarDayViewModel)form.DataContext;if(vm.IsBusy || !vm.Status.Contains("Chưa có phòng"))return;
                Assert(vm.Rooms.Count==0,"Main calendar empty database wrong.");Image(form,outputDirectory,"calendar-empty");calendarOpened=true;driver.Stop();form.Close();
            };
            driver.Start();Click(Field<Button>(window,"CalendarButton"));PumpUntil(()=>calendarOpened && Field<Button>(window,"CalendarButton").IsEnabled);driver.Stop();
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
            // Create and View are independent: the shortcut must remain for a Create-only role.
            Action<string> staffEntrySql=statement=>{using(var c=database.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=statement;cmd.ExecuteNonQuery();}};
            staffEntrySql("UPDATE AspNetUserRoles SET RoleId='Staff'; DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId IN(SELECT PermissionId FROM Permission WHERE Code IN('Reservation.View','Calendar.View'));");
            Click(Field<Button>(window,"RefreshAccessButton"));PumpUntil(()=>Field<Button>(window,"RefreshAccessButton").IsEnabled);
            Assert(Field<Button>(window,"ReservationsButton").Visibility==Visibility.Collapsed && Field<Button>(window,"StaffBookingButton").Visibility==Visibility.Visible,"Create-only shortcut required View.");
            var staffCreateStage=0;
            driver=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
            driver.Tick+=(sender,args)=>{
                var form=application.Windows.OfType<GuestBookingWindow>().FirstOrDefault();if(form==null)return;
                try
                {
                    var vm=(GuestBookingViewModel)form.DataContext;if(vm.IsBusy)return;
                    Assert(vm.FormTitle.Contains("Đặt hộ") && vm.Rooms.Count==0 && !vm.CanBook,"Main shortcut opened Guest route/wrong empty state.");
                    Image(form,outputDirectory,"staff-create-empty");staffCreateStage=1;driver.Stop();form.Close();
                }
                catch(Exception error){driverError=error;driver.Stop();form.Close();}
            };
            driver.Start();Click(Field<Button>(window,"StaffBookingButton"));
            PumpUntil(()=>driverError!=null || (staffCreateStage==1 && Field<Button>(window,"StaffBookingButton").IsEnabled && Field<Button>(window,"RefreshAccessButton").IsEnabled));
            driver.Stop();if(driverError!=null)throw driverError;
            Assert(Field<Button>(window,"CalendarButton").Visibility==Visibility.Collapsed,"Missing Calendar.View still exposed main calendar button.");
            staffEntrySql("UPDATE AspNetUserRoles SET RoleId='Admin'; INSERT OR IGNORE INTO RolePermission(RoleId,PermissionId) SELECT 'Staff',PermissionId FROM Permission WHERE Code IN('Reservation.View','Calendar.View');");
            Click(Field<Button>(window,"RefreshAccessButton"));PumpUntil(()=>Field<Button>(window,"RefreshAccessButton").IsEnabled);
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
            VerifyNavigationLayout(window, true, outputDirectory);

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
            Console.WriteLine("PASS WPF UI: sidebar and compact layout, check-in confirmation/keep/actual/snapshot/duplicate/stale/permission/refresh, Guest calendar, internal Day/Week, Staff/Guest booking/lookup/cancel, NoShow, auth and catalogs. Rendered fifty-seven views.");
        }
        finally
        {
            if (driver != null) driver.Stop();
            window.Close();
            application.Shutdown();
            Directory.Delete(testDirectory, true);
        }
    }

    private static void VerifyGuestCalendar(string file,string outputDirectory)
    {
        var db=new SqliteDatabase(file);db.Initialize();
        Action<string> sql=statement=>{using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=statement;cmd.ExecuteNonQuery();}};
        Func<string,object> value=statement=>{using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=statement;return cmd.ExecuteScalar();}};
        var imageUrl="Content/uploads/rooms/"+Guid.NewGuid().ToString("N")+".png";
        var imagePath=Path.Combine(Path.GetDirectoryName(file),imageUrl.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(imagePath));
        var bitmap=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[]{80,100,120,255,80,100,120,255,80,100,120,255,80,100,120,255},8);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(imagePath))encoder.Save(stream);
        sql("INSERT INTO Rooms(RoomId,RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES(1,'P01','Phòng demo',1,'"+imageUrl+"',1,'test'); INSERT INTO Customers VALUES(1,'Tên bí mật','0912345678');");
        sql(@"INSERT INTO Reservations VALUES(1,1,1,'2026-10-06T06:00:00.0000000+00:00','2026-10-06T07:00:00.0000000+00:00','Confirmed',NULL,NULL,'test');");
        var clock=new LookupClock{UtcNow=new DateTimeOffset(2026,10,6,2,0,0,TimeSpan.Zero)};var service=new GuestBookingService(db,clock);var vm=new GuestBookingViewModel(service,clock);var parent=new GuestBookingWindow(service,vm);
        DispatcherTimer driver=null;Exception failure=null;
        Action<int> open=scene=>{
            var done=false;driver=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};
            driver.Tick+=(sender,args)=>{
                var child=Application.Current.Windows.OfType<GuestCalendarWindow>().FirstOrDefault();if(child==null)return;
                try
                {
                    var model=(GuestCalendarViewModel)child.DataContext;Assert(model.Slots.Count==28,"Guest calendar not 28 half-hour rows.");
                    Assert(Field<DataGrid>(child,"SlotsTable").Columns.Count==2 && !model.Title.Contains("Tên bí mật") && !model.Status.Contains("0912345678"),"Public UI exposed customer data.");
                    if(scene==0)
                    {
                        Field<DataGrid>(child,"SlotsTable").SelectedItem=model.Slots.Single(s=>s.TimeLabel=="13:00");Assert(!Field<Button>(child,"ChooseButton").IsEnabled && model.Selected.State=="Busy","Busy row selectable.");
                        Field<DataGrid>(child,"SlotsTable").SelectedItem=model.Slots.Single(s=>s.TimeLabel=="12:00");Assert(!model.CanChoose,"Rest row selectable.");
                        Field<DataGrid>(child,"SlotsTable").ScrollIntoView(model.Slots.Single(s=>s.TimeLabel=="15:00"));child.UpdateLayout();Image(child,outputDirectory,"public-calendar");
                        Field<DataGrid>(child,"SlotsTable").SelectedItem=model.Slots.Single(s=>s.TimeLabel=="15:00");Assert(model.CanChoose,"Free row not selectable.");driver.Stop();done=true;Click(Field<Button>(child,"ChooseButton"));
                    }
                    else if(scene==1)
                    {
                        Assert(model.Title.Contains("180 phút") && model.Slots.Single(s=>s.TimeLabel=="21:00").State=="Closed","Duration not reflected in UI calendar.");
                        Field<DataGrid>(child,"SlotsTable").ScrollIntoView(model.Slots.Last());child.UpdateLayout();Image(child,outputDirectory,"public-calendar-duration");driver.Stop();done=true;child.Close();
                    }
                    else if(scene==2)
                    {
                        Assert(model.Slots.Single(s=>s.TimeLabel=="13:00").State=="Available" && model.Title.Contains("07/10/2026"),"Tomorrow uses today's holds.");Image(child,outputDirectory,"public-calendar-tomorrow");driver.Stop();done=true;child.Close();
                    }
                    else
                    {
                        driver.Stop();sql("UPDATE Rooms SET IsActive=0,InactiveReason='Private reason' WHERE RoomId=1;");Click(Field<Button>(child,"RefreshButton"));PumpUntil(()=>!model.IsBusy && model.Slots.Count==0);
                        Assert(!model.CanChoose && model.CanClose && !model.Status.Contains("Private reason"),"Locked room kept selectable data/leaked reason.");Image(child,outputDirectory,"public-calendar-locked");done=true;child.Close();
                    }
                }
                catch(Exception error){failure=error;driver.Stop();child.Close();}
            };
            driver.Start();Click(Field<Button>(parent,"CalendarButton"));PumpUntil(()=>done || failure!=null);driver.Stop();if(failure!=null)throw failure;
        };
        try
        {
            parent.Show();PumpUntil(()=>!vm.IsBusy && vm.CanBook);Assert(string.IsNullOrEmpty(vm.FullName) && string.IsNullOrEmpty(vm.PhoneNumber),"Fixture name/phone unexpectedly needed.");
            open(0);Assert(vm.StartTimeText=="15:00" && Convert.ToInt64(value("SELECT COUNT(*) FROM Reservations;"))==1 && Convert.ToInt64(value("SELECT COUNT(*) FROM Customers;"))==1,"Picking time wrote booking/customer or did not update parent.");
            service.Create(new MusicBoxManagement.Wpf.Models.ReservationRequest{RoomId=1,StartTime=new DateTimeOffset(2026,10,6,15,0,0,TimeSpan.FromHours(7)),DurationMinutes=60,FullName="Other",PhoneNumber="0987654321"});
            Field<TextBox>(parent,"NameInput").Text="Another";Field<TextBox>(parent,"PhoneInput").Text="0901234567";Click(Field<Button>(parent,"SubmitButton"));PumpUntil(()=>!vm.IsBusy && vm.Status.Contains("trùng"));
            Assert(Convert.ToInt64(value("SELECT COUNT(*) FROM Customers;"))==2,"Stale calendar submit left customer.");
            Field<ComboBox>(parent,"DurationInput").SelectedItem=180;open(1);
            Field<DatePicker>(parent,"DateInput").SelectedDate=new DateTime(2026,10,7);Field<ComboBox>(parent,"DurationInput").SelectedItem=60;open(2);
            Field<DatePicker>(parent,"DateInput").SelectedDate=new DateTime(2026,10,6);open(3);
            Assert(Convert.ToInt64(value("SELECT COUNT(*) FROM Reservations;"))==2 && Convert.ToInt64(value("SELECT COUNT(*) FROM AspNetUsers;"))==0,"Public calendar required login or wrote reservation.");
        }
        finally{if(driver!=null)driver.Stop();parent.Close();}
    }

    private static void VerifyCalendarDay(string file,string outputDirectory)
    {
        var db=new SqliteDatabase(file);var auth=new AuthenticationService(db);
        var setup=auth.SetupAdminAsync("admin","Admin lịch",Password);PumpUntil(()=>setup.IsCompleted);var admin=setup.GetAwaiter().GetResult();
        Action<string> sql=statement=>{using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=statement;cmd.ExecuteNonQuery();}};
        sql(@"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'calendar','calendar','CALENDAR',PasswordHash,'calendar','Nhân viên lịch',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('calendar','Staff');
DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId IN(SELECT PermissionId FROM Permission WHERE Code<>'Calendar.View');
INSERT INTO Rooms(RoomId,RoomCode,Name,RoomTypeId,ImageUrl,IsActive,InactiveReason,CreatedAt) VALUES
(1,'P01','Standard',1,'test.png',1,NULL,'test'),(2,'P02','VIP',2,'test.png',1,NULL,'test'),(3,'P03','Walk-in',1,'test.png',1,NULL,'test'),(4,'P04','Phòng khóa',1,'test.png',0,'Kiểm tra','test');
INSERT INTO Customers VALUES(1,'Nguyễn An','0912345678'),(2,'Trần Bình','0987654321'),(3,'Lê Minh','0901234567'),(4,'Khách lịch sử','0901112223');
INSERT INTO Reservations VALUES
(1,1,1,'2026-10-06T03:30:00.0000000+00:00','2026-10-06T04:30:00.0000000+00:00','CheckedIn',NULL,NULL,'test'),
(2,4,1,'2026-10-06T06:30:00.0000000+00:00','2026-10-06T07:30:00.0000000+00:00','Confirmed',NULL,NULL,'test'),
(3,2,2,'2026-10-06T06:00:00.0000000+00:00','2026-10-06T07:30:00.0000000+00:00','CheckedIn',NULL,NULL,'test'),
(4,3,2,'2026-10-06T09:00:00.0000000+00:00','2026-10-06T10:00:00.0000000+00:00','Confirmed',NULL,NULL,'test'),
(5,4,2,'2026-10-07T02:00:00.0000000+00:00','2026-10-07T03:00:00.0000000+00:00','Confirmed',NULL,NULL,'test');
INSERT INTO RoomSessions(CustomerId,RoomId,ReservationId,ActualStartTime,ExpectedEndTime,ActualEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES
(1,1,1,'2026-10-06T03:37:00.0000000+00:00','2026-10-06T04:37:00.0000000+00:00',NULL,120000,'P01','STANDARD','Standard','Active'),
(2,2,3,'2026-10-06T06:07:00.0000000+00:00','2026-10-06T07:37:00.0000000+00:00',NULL,200000,'P02','VIP','VIP','Active'),
(3,3,NULL,'2026-10-06T06:17:00.0000000+00:00',NULL,NULL,120000,'P03','STANDARD','Standard','Active'),
(4,4,NULL,'2026-10-06T03:37:00.0000000+00:00',NULL,'2026-10-06T04:27:00.0000000+00:00',120000,'P04','STANDARD','Standard','Completed');");
        var login=auth.LoginAsync("calendar",Password);PumpUntil(()=>login.IsCompleted);var staff=login.GetAwaiter().GetResult();
        var clock=new LookupClock{UtcNow=new DateTimeOffset(2026,10,6,6,37,0,TimeSpan.Zero)};
        var vm=new CalendarDayViewModel(new CalendarService(db,clock),staff,clock);var form=new CalendarDayWindow(vm);
        try
        {
            form.Show();PumpUntil(()=>!vm.IsBusy && vm.Rooms.Count==4);
            var timeline=Field<Canvas>(form,"Timeline");Assert(timeline.Children.OfType<Button>().Count()==6,"Timeline omitted/doubled events or Calendar-only requires unrelated rights.");
            Assert(timeline.Children.OfType<TextBlock>().Any(t=>t.Text=="12:00–13:00 · Giờ nghỉ"),"Rest marker missing.");
            var button=timeline.Children.OfType<Button>().First(b=>((MusicBoxManagement.Wpf.Models.RoomScheduleEvent)b.Tag).IsOverdue);
            Assert(Math.Abs(Canvas.GetTop(button)-(62+97*1.4))<0.01,"Actual 10:37 rounded to a calendar slot.");
            Click(button);Assert(vm.Details.Contains("10:37:00") && vm.Details.Contains("QUÁ GIỜ") && Field<TextBlock>(form,"DetailsText").Text.Contains("0912345678"),"Event click/details binding failed.");
            Image(form,outputDirectory,"calendar-day-all");
            Field<ComboBox>(form,"RoomInput").SelectedItem=vm.RoomChoices.Single(r=>r.RoomId==3);
            Assert(vm.Rooms.Count==0 && !vm.Details.Contains("0912345678"),"Filter change retained old calendar/details.");
            Click(Field<Button>(form,"LoadButton"));PumpUntil(()=>!vm.IsBusy && vm.Rooms.Count==1);
            button=timeline.Children.OfType<Button>().Single();Click(button);Assert(vm.Details.Contains("16:00:00") && vm.Details.Contains("chưa chốt"),"Walk-in customer deadline/details lost.");
            Image(form,outputDirectory,"calendar-day-walkin");
            Click(Field<Button>(form,"NextButton"));PumpUntil(()=>!vm.IsBusy && vm.Date==new DateTime(2026,10,7) && vm.Rooms.Count==1);
            Assert(timeline.Children.OfType<Button>().Count()==0 && vm.Rooms[0].CurrentStatus=="Occupied","Current occupied filled tomorrow calendar.");
            Image(form,outputDirectory,"calendar-day-tomorrow");
            Click(Field<Button>(form,"PreviousButton"));PumpUntil(()=>!vm.IsBusy && vm.Date==new DateTime(2026,10,6) && vm.Rooms.Count==1);
            Field<ComboBox>(form,"RoomInput").SelectedItem=vm.RoomChoices.Single(r=>r.RoomId==4);Click(Field<Button>(form,"LoadButton"));PumpUntil(()=>!vm.IsBusy && vm.Rooms.Count==1);
            Click(timeline.Children.OfType<Button>().Single());Assert(vm.Details.Contains("11:27:00") && vm.Rooms[0].CurrentStatus=="Inactive","Locked completed history failed.");
            form.Width=1050;form.Height=660;Image(form,outputDirectory,"calendar-day-history");
            Field<DatePicker>(form,"DateInput").SelectedDate=null;Click(Field<Button>(form,"LoadButton"));PumpUntil(()=>!vm.IsBusy);Assert(vm.Status.Contains("Cần chọn ngày") && timeline.Children.OfType<Button>().Count()==0,"Invalid date left old rendering.");
            Click(Field<Button>(form,"TodayButton"));PumpUntil(()=>!vm.IsBusy && vm.Rooms.Count==1);
            // Week edges and completed usage crossing midnight. The original timestamps must survive visual clipping.
            sql(@"INSERT INTO Reservations VALUES
(6,4,2,'2026-10-11T02:00:00.0000000+00:00','2026-10-11T03:00:00.0000000+00:00','Confirmed',NULL,NULL,'test'),
(7,4,2,'2026-10-12T02:00:00.0000000+00:00','2026-10-12T03:00:00.0000000+00:00','Confirmed',NULL,NULL,'test');
INSERT INTO RoomSessions(CustomerId,RoomId,ReservationId,ActualStartTime,ExpectedEndTime,ActualEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES
(4,4,NULL,'2026-10-04T15:37:00.0000000+00:00',NULL,'2026-10-05T03:07:00.0000000+00:00',120000,'P04','STANDARD','Standard','Completed'),
(4,4,NULL,'2026-10-05T15:37:00.0000000+00:00',NULL,'2026-10-06T03:07:00.0000000+00:00',120000,'P04','STANDARD','Standard','Completed');");
            Field<RadioButton>(form,"WeekMode").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpUntil(()=>!vm.IsBusy && vm.IsWeek && vm.Rooms.Count==1);
            Assert(vm.Rooms[0].Range.Start.ToOffset(TimeSpan.FromHours(7)).Date==new DateTime(2026,10,5) && vm.Rooms[0].Range.End.ToOffset(TimeSpan.FromHours(7)).Date==new DateTime(2026,10,12),"Week UI range wrong.");
            Assert(timeline.Children.OfType<Border>().Count()==7 && timeline.Children.OfType<TextBlock>().Count(t=>t.Text=="12:00–13:00 · Giờ nghỉ")==7,"Week did not draw seven columns/rest bands.");
            Assert(timeline.Children.OfType<Button>().Count()==4,"Cross-day completed intervals omitted or drawn on every day.");
            var clipped=timeline.Children.OfType<Button>().First(b=>((MusicBoxManagement.Wpf.Models.RoomScheduleEvent)b.Tag).Start.ToOffset(TimeSpan.FromHours(7)).Date==new DateTime(2026,10,4));
            Assert(Math.Abs(Canvas.GetTop(clipped)-62)<0.01 && Math.Abs(clipped.Height-67*1.4)<0.01,"Week start usage not clipped to Monday 09:00–10:07.");
            Click(clipped);Assert(vm.Details.Contains("22:37:00 04/10/2026") && vm.Details.Contains("10:07:00 05/10/2026"),"Clipping changed raw times/details.");
            Image(form,outputDirectory,"calendar-week-history");
            Field<ComboBox>(form,"RoomInput").SelectedItem=vm.RoomChoices.First();Click(Field<Button>(form,"LoadButton"));PumpUntil(()=>!vm.IsBusy && vm.Rooms.Count==4);
            Assert(timeline.Children.OfType<Button>().Any(b=>((MusicBoxManagement.Wpf.Models.RoomScheduleEvent)b.Tag).ReservationId==6) && !timeline.Children.OfType<Button>().Any(b=>((MusicBoxManagement.Wpf.Models.RoomScheduleEvent)b.Tag).ReservationId==7),"Week excluded Sunday/included following Monday.");
            Image(form,outputDirectory,"calendar-week-all");
            Field<DatePicker>(form,"DateInput").SelectedDate=new DateTime(2026,10,11);Click(Field<Button>(form,"LoadButton"));PumpUntil(()=>!vm.IsBusy && vm.Rooms.Count==4);
            Assert(vm.Status.Contains("05/10/2026") && vm.Status.Contains("11/10/2026"),"Sunday changed week range.");
            Field<ComboBox>(form,"RoomInput").SelectedItem=vm.RoomChoices.Single(r=>r.RoomId==2);Click(Field<Button>(form,"LoadButton"));PumpUntil(()=>!vm.IsBusy && vm.Rooms.Count==1);
            var sunday=timeline.Children.OfType<Button>().Single(b=>((MusicBoxManagement.Wpf.Models.RoomScheduleEvent)b.Tag).ReservationId==6);
            Assert(Math.Abs(Canvas.GetLeft(sunday)-(68+6*240+6))<0.01,"Sunday event placed in wrong column.");
            Click(sunday);Field<ScrollViewer>(form,"TimelineScroll").ScrollToRightEnd();form.UpdateLayout();Image(form,outputDirectory,"calendar-week-sunday");
            Click(Field<Button>(form,"NextButton"));PumpUntil(()=>!vm.IsBusy && vm.Rooms.Count==1 && vm.Status.Contains("12/10/2026"));
            Assert(vm.Rooms[0].Events.Single().ReservationId==7 && timeline.Children.OfType<Button>().Count()==1,"Next Week did not advance seven days/clear old events.");
            Field<ScrollViewer>(form,"TimelineScroll").ScrollToLeftEnd();form.UpdateLayout();Image(form,outputDirectory,"calendar-week-next");
            Click(Field<Button>(form,"PreviousButton"));PumpUntil(()=>!vm.IsBusy && vm.Rooms.Count==1 && vm.Status.Contains("05/10/2026"));
            Click(Field<Button>(form,"TodayButton"));PumpUntil(()=>!vm.IsBusy && vm.Date==new DateTime(2026,10,6) && vm.Rooms.Count==1);
            Field<RadioButton>(form,"DayMode").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpUntil(()=>!vm.IsBusy && vm.IsDay && vm.Rooms.Count==1);
            Assert(timeline.Children.OfType<Border>().Count()==1 && vm.Rooms[0].Range.End-vm.Rooms[0].Range.Start==TimeSpan.FromDays(1),"Switch back to Day still shows Week.");
            Field<ComboBox>(form,"RoomInput").SelectedItem=vm.RoomChoices.Single(r=>r.RoomId==4);Click(Field<Button>(form,"LoadButton"));PumpUntil(()=>!vm.IsBusy && vm.Rooms.Count==1);
            Field<RadioButton>(form,"WeekMode").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpUntil(()=>!vm.IsBusy && vm.IsWeek && vm.Rooms.Count==1);
            Click(timeline.Children.OfType<Button>().First());sql("DELETE FROM RolePermission WHERE RoleId='Staff';");
            Click(Field<Button>(form,"LoadButton"));PumpUntil(()=>!vm.IsBusy && vm.Status.Contains("Không còn quyền"));
            Assert(vm.Rooms.Count==0 && vm.RoomChoices.Count==0 && timeline.Children.OfType<Button>().Count()==0 && !vm.Details.Contains("0901112223") && Field<Button>(form,"LoadButton").IsEnabled,"Revoked calendar retained private render/details or trapped controls.");
            Image(form,outputDirectory,"calendar-day-denied");
        }
        finally{form.Close();auth.Logout(admin);}
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

    private static void VerifyStaffBooking(string file,string outputDirectory)
    {
        var db=new SqliteDatabase(file);var auth=new AuthenticationService(db);
        var setup=auth.SetupAdminAsync("admin","Admin thử nghiệm",Password);PumpUntil(()=>setup.IsCompleted);var admin=setup.GetAwaiter().GetResult();
        Action<string> sql=statement=>{using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=statement;cmd.ExecuteNonQuery();}};
        Func<string,object> value=statement=>{using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=statement;return cmd.ExecuteScalar();}};
        var imageUrl="Content/uploads/rooms/"+Guid.NewGuid().ToString("N")+".png";
        var imagePath=Path.Combine(Path.GetDirectoryName(file),imageUrl.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(imagePath));
        var bitmap=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[]{80,100,120,255,80,100,120,255,80,100,120,255,80,100,120,255},8);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(imagePath))encoder.Save(stream);
        sql(@"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'creator','creator','CREATOR',PasswordHash,'creator','Nhân viên đặt hộ',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('creator','Staff');
DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId IN(SELECT PermissionId FROM Permission WHERE Code LIKE 'Customer.%' OR Code='Reservation.View');
INSERT INTO Customers(FullName,PhoneNumber) VALUES('Tên khách đã lưu','0912345678');");
        sql("INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('P01','Phòng đặt hộ',1,'"+imageUrl+"',1,'test');");
        var login=auth.LoginAsync("creator",Password);PumpUntil(()=>login.IsCompleted);var creator=login.GetAwaiter().GetResult();
        var clock=new LookupClock{UtcNow=new DateTimeOffset(2026,10,5,2,0,0,TimeSpan.Zero)};
        var route=new StaffBookingService(db,creator,clock);var vm=new GuestBookingViewModel(route,clock,true);var form=new GuestBookingWindow(route,vm);
        try
        {
            form.Show();PumpUntil(()=>!vm.IsBusy && vm.CanBook);
            Assert(vm.FormTitle.Contains("Đặt hộ") && Field<Image>(form,"RoomImage").Source!=null,"Staff form title/image failed.");
            Field<DatePicker>(form,"DateInput").SelectedDate=new DateTime(2026,10,5);Field<ComboBox>(form,"TimeInput").SelectedItem="13:00";
            Field<TextBox>(form,"NameInput").Text="Tên vừa nhập";Field<TextBox>(form,"PhoneInput").Text="bad";
            Click(Field<Button>(form,"PreviewButton"));PumpUntil(()=>!vm.IsBusy);Assert(vm.Status.Contains("SĐT"),"Staff invalid phone preview accepted.");
            Field<TextBox>(form,"PhoneInput").Text="+84 912.345-678";Click(Field<Button>(form,"PreviewButton"));PumpUntil(()=>!vm.IsBusy);
            Assert(vm.Status.Contains("Có thể đặt") && Convert.ToInt64(value("SELECT COUNT(*) FROM Reservations;"))==0,"Staff preview wrote data.");
            Image(form,outputDirectory,"staff-create-form");
            new ReservationService(db,clock).CreateGuest(new MusicBoxManagement.Wpf.Models.ReservationRequest{RoomId=1,StartTime=new DateTimeOffset(2026,10,5,13,0,0,TimeSpan.FromHours(7)),DurationMinutes=60,FullName="Khách khác",PhoneNumber="0987654321"});
            Click(Field<Button>(form,"SubmitButton"));PumpUntil(()=>!vm.IsBusy);Assert(vm.Status.Contains("trùng"),"Staff submit trusted stale preview.");
            Image(form,outputDirectory,"staff-create-conflict");
            Field<ComboBox>(form,"TimeInput").SelectedItem="15:00";Click(Field<Button>(form,"SubmitButton"));PumpUntil(()=>!vm.IsBusy);
            Assert(vm.Status.Contains("Đặt hộ thành công") && !vm.CanInput && (string)value("SELECT CreatedByUserId FROM Reservations WHERE ReservationId=2;")==creator.UserId,"Staff submit used Guest creator.");
            Assert((string)value("SELECT FullName FROM Customers WHERE PhoneNumber='0912345678';")=="Tên khách đã lưu","Staff form overwrote old customer name.");
            Image(form,outputDirectory,"staff-create-confirmed");Click(Field<Button>(form,"SubmitButton"));Assert(Convert.ToInt64(value("SELECT COUNT(*) FROM Reservations;"))==2,"Staff duplicate submit wrote again.");
            Click(Field<Button>(form,"NewButton"));PumpUntil(()=>!vm.IsBusy);Assert(string.IsNullOrEmpty(vm.FullName) && string.IsNullOrEmpty(vm.PhoneNumber),"Staff new draft retained customer fields.");
            Field<ComboBox>(form,"TimeInput").SelectedItem="17:00";Field<TextBox>(form,"NameInput").Text="Chưa lưu";Field<TextBox>(form,"PhoneInput").Text="0900000000";
            sql("DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Reservation.Create');");
            Click(Field<Button>(form,"SubmitButton"));PumpUntil(()=>!vm.IsBusy);
            Assert(!vm.CanInput && vm.CanClose && vm.Rooms.Count==0 && string.IsNullOrEmpty(vm.PhoneNumber) && Convert.ToInt64(value("SELECT COUNT(*) FROM Reservations;"))==2,"Stale Staff form bypassed permission or retained private fields.");
            Image(form,outputDirectory,"staff-create-denied");
        }
        finally{form.Close();}
        var list=new StaffReservationService(db,clock);var parentVm=new ReservationsViewModel(list,admin,clock){StatusQuery="Cancelled",PhoneQuery="0900000000"};
        var parent=new ReservationsWindow(parentVm);DispatcherTimer driver=null;Exception driverError=null;var stage=0;
        try
        {
            parent.Show();PumpUntil(()=>!parentVm.IsBusy);Assert(parentVm.CanCreate && parentVm.Items.Count==0,"Parent create/filter fixture failed.");
            driver=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
            driver.Tick+=(sender,args)=>{
                var child=Application.Current.Windows.OfType<GuestBookingWindow>().FirstOrDefault();if(child==null)return;
                try
                {
                    var childVm=(GuestBookingViewModel)child.DataContext;if(childVm.IsBusy)return;
                    if(stage==0){Assert(childVm.FormTitle.Contains("Đặt hộ") && childVm.CanBook,"Parent opened Guest route.");
                        Field<DatePicker>(child,"DateInput").SelectedDate=new DateTime(2026,10,6);Field<ComboBox>(child,"TimeInput").SelectedItem="13:00";
                        Field<TextBox>(child,"NameInput").Text="Khách ngày mai";Field<TextBox>(child,"PhoneInput").Text="0901234567";stage=1;Click(Field<Button>(child,"SubmitButton"));}
                    else{Assert(childVm.LastCreatedReservationId.HasValue,"Parent child failed: "+childVm.Status);stage=2;driver.Stop();child.Close();}
                }
                catch(Exception error){driverError=error;driver.Stop();child.Close();}
            };
            driver.Start();Click(Field<Button>(parent,"CreateButton"));PumpUntil(()=>driverError!=null || (stage==2 && !parentVm.IsBusy && parentVm.Status.Contains("Đã đặt hộ")));
            driver.Stop();if(driverError!=null)throw driverError;
            Assert(parentVm.StatusQuery=="Tất cả" && parentVm.PhoneQuery==null && parentVm.Selected!=null && parentVm.Selected.ReservationId==3 && (string)value("SELECT CreatedByUserId FROM Reservations WHERE ReservationId=3;")==admin.UserId,"Parent did not refresh/select saved Staff row/reset filters.");
            Image(parent,outputDirectory,"staff-create-parent");
        }
        finally{if(driver!=null)driver.Stop();parent.Close();}
    }

    private static void VerifyCheckInUi(string file,string outputDirectory)
    {
        var db=new SqliteDatabase(file);var auth=new AuthenticationService(db);
        var setup=auth.SetupAdminAsync("admin","Admin thử nghiệm",Password);PumpUntil(()=>setup.IsCompleted);var admin=setup.GetAwaiter().GetResult();
        Action<string> sql=statement=>{using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=statement;cmd.ExecuteNonQuery();}};
        Func<string,long> count=statement=>{using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=statement;return Convert.ToInt64(cmd.ExecuteScalar());}};
        sql(@"INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('P01','Nhận phòng',1,'test.png',1,'test'),('P02','Phòng khác',2,'test.png',1,'test');
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'receiver','receiver','RECEIVER',PasswordHash,'receiver','Nhân viên nhận phòng',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('receiver','Staff');
DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId NOT IN(SELECT PermissionId FROM Permission WHERE Code IN('Reservation.View','Session.CheckIn'));");
        var login=auth.LoginAsync("receiver",Password);PumpUntil(()=>login.IsCompleted);var staff=login.GetAwaiter().GetResult();
        var clock=new LookupClock{UtcNow=new DateTimeOffset(2026,10,5,9,0,0,TimeSpan.FromHours(7))};var create=new ReservationService(db,clock);
        Func<int,int,string,MusicBoxManagement.Wpf.Models.Reservation> book=(room,hour,phone)=>create.CreateGuest(new MusicBoxManagement.Wpf.Models.ReservationRequest{RoomId=room,StartTime=new DateTimeOffset(2026,10,5,hour,0,0,TimeSpan.FromHours(7)),DurationMinutes=60,FullName="Khách nhận phòng",PhoneNumber=phone});
        var first=book(1,13,"0901111111");var stale=book(1,16,"0902222222");book(2,13,"0903333333");var after=book(2,16,"0904444444");
        clock.UtcNow=new DateTimeOffset(2026,10,5,13,7,12,TimeSpan.FromHours(7));
        var service=new StaffReservationService(db,clock);var vm=new ReservationsViewModel(service,staff,clock);var form=new ReservationsWindow(vm);
        try
        {
            form.Show();PumpUntil(()=>!vm.IsBusy);vm.Selected=vm.Items.First(x=>x.ReservationId==first.ReservationId);
            Assert(vm.CanCheckIn && !vm.CanCancel && !vm.CanCreate,"Check-in UI required unrelated permissions.");Image(form,outputDirectory,"checkin-list");
            Click(Field<Button>(form,"CheckInButton"));Assert(vm.IsConfirmingCheckIn && !vm.CanSelect && !vm.CanSearch && vm.CheckInNotice.Contains("13:07:12") && vm.CheckInNotice.Contains("14:07:12"),"Check-in confirmation rounded time/failed to lock selection.");
            Click(Field<Button>(form,"KeepCheckInButton"));Assert(!vm.IsConfirmingCheckIn && count("SELECT COUNT(*) FROM RoomSessions")==0,"Keep created session.");
            Click(Field<Button>(form,"CheckInButton"));Image(form,outputDirectory,"checkin-confirm");
            sql("UPDATE RoomTypes SET PricePerHour=150000 WHERE RoomTypeId=1;");
            Click(Field<Button>(form,"ConfirmCheckInButton"));PumpUntil(()=>!vm.IsBusy);
            Assert(vm.Status.Contains("Đã nhận") && vm.Selected.Status=="CheckedIn" && vm.CheckInResult.Contains("14:07:12") && vm.CheckInResult.Contains("150") && !vm.CanCheckIn,"Check-in result/refresh/current price incorrect.");
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>form.UpdateLayout()));
            Image(form,outputDirectory,"checkin-result");Click(Field<Button>(form,"ConfirmCheckInButton"));Assert(count("SELECT COUNT(*) FROM RoomSessions")==1 && count("SELECT COUNT(*) FROM AuditLog WHERE Action='Session.CheckIn'")==1,"Duplicate confirm created session/log.");
            vm.Selected=vm.Items.First(x=>x.ReservationId==stale.ReservationId);Assert(vm.CheckInResult=="","Selection retained old session result.");
            Click(Field<Button>(form,"CheckInButton"));create.CancelStaff(admin,stale.ReservationId,"Khách đổi kế hoạch");Click(Field<Button>(form,"ConfirmCheckInButton"));PumpUntil(()=>!vm.IsBusy);
            Assert(!vm.IsConfirmingCheckIn && count("SELECT COUNT(*) FROM RoomSessions")==1 && vm.Status.Contains("Confirmed"),"Stale cancelled booking received session.");Image(form,outputDirectory,"checkin-stale");
            vm.Selected=vm.Items.First(x=>x.ReservationId==after.ReservationId);Click(Field<Button>(form,"CheckInButton"));
            sql("DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Session.CheckIn');");
            Click(Field<Button>(form,"ConfirmCheckInButton"));PumpUntil(()=>!vm.IsBusy);Assert(vm.Items.Count==0 && vm.Selected==null && vm.CheckInResult=="" && !vm.CanConfirmCheckIn,"Revoked permission retained customer/result/confirmation.");Image(form,outputDirectory,"checkin-revoked");
            Click(Field<Button>(form,"SearchButton"));PumpUntil(()=>!vm.IsBusy);vm.Selected=vm.Items.First(x=>x.ReservationId==after.ReservationId);Assert(!vm.CanCheckIn,"View-only account could receive room.");
            sql("INSERT INTO RolePermission(RoleId,PermissionId) SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.CheckIn';");
            Click(Field<Button>(form,"SearchButton"));PumpUntil(()=>!vm.IsBusy);vm.Selected=vm.Items.First(x=>x.ReservationId==after.ReservationId);Click(Field<Button>(form,"CheckInButton"));
            sql("CREATE TRIGGER FailCheckInRefresh BEFORE INSERT ON AuditLog WHEN NEW.Action='Reservation.NoShow' BEGIN SELECT RAISE(ABORT,'test refresh failure'); END;");
            clock.UtcNow=new DateTimeOffset(2026,10,5,13,15,0,TimeSpan.FromHours(7));
            Click(Field<Button>(form,"ConfirmCheckInButton"));PumpUntil(()=>!vm.IsBusy);
            Assert(vm.Items.Count==0 && vm.Status.Contains("Đã nhận") && vm.Status.Contains("chưa tải") && count("SELECT COUNT(*) FROM RoomSessions")==2,"Post-commit refresh failure reported check-in failed.");
        }
        finally{form.Close();}
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
DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId IN(SELECT PermissionId FROM Permission WHERE Code IN('Reservation.Cancel','Reservation.Create'));");
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
            Assert(vm.Items.Count==2 && !vm.CanCancel && !vm.CanCreate && !Field<Button>(form,"CreateButton").IsEnabled && vm.Details.Contains("Khách đã đặt"),"Read-only Staff could not view or could cancel/create.");
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
