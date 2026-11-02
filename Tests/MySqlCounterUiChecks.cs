using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;
using MusicBoxManagement.Wpf.Views;

public static class MusicBoxMySqlCounterUiChecks
{
    private sealed class Clock:IClock { public DateTimeOffset Now;public DateTimeOffset UtcNow { get {return Now;} } }
    private sealed class CountingCalendar:ICalendarService
    {
        public ICalendarService Inner;public int Reads;
        public System.Collections.Generic.IReadOnlyList<RoomSchedule> Read(LoginSession session,CalendarRange range,int? roomId=null)
        {Interlocked.Increment(ref Reads);return Inner.Read(session,range,roomId);}
    }
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    private static T Field<T>(object owner,string name){return (T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(owner);}
    private static void Pump(Func<bool> done){var watch=Stopwatch.StartNew();while(!done()){if(watch.Elapsed.TotalSeconds>90)throw new Exception("Counter UI wait timeout");var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(10);}}
    private static void Await(Task t){Pump(()=>t.IsCompleted);t.GetAwaiter().GetResult();}
    private static void Click(Button b){b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
    private static void Image(Window window,string folder,string name){window.UpdateLayout();var width=(int)Math.Ceiling(window.ActualWidth);var height=(int)Math.Ceiling(window.ActualHeight);var image=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);image.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(Path.Combine(folder,name+".png")))encoder.Save(stream);}
    private static object Sql(MySqlDatabase db,string sql){using(var c=db.OpenConnection())using(var q=c.CreateCommand()){q.CommandText=sql;return q.ExecuteScalar();}}
    private static MainWindow OpenMain(MySqlDatabase db,string root,LoginSession actor)
    {
        var types=new MySqlRoomTypeService(db);
        var window=new MainWindow(new MainViewModel(types),new MySqlAuthenticationService(db),new MySqlPermissionService(db),types,new MySqlRoomService(db,root),new MySqlServiceCatalogService(db),new MySqlCustomerService(db),new MySqlGuestBookingService(db,root),new MySqlGuestReservationService(db),new MySqlStaffReservationService(db,new Clock{Now=DateTimeOffset.UtcNow},root));
        window.Show();Pump(()=>Field<Button>(window,"LoginButton").IsEnabled&&Field<MainViewModel>(window,"viewModel").RoomTypes.Count==2);
        typeof(MainWindow).GetField("session",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(window,actor);
        Await((Task)typeof(MainWindow).GetMethod("RefreshAccessAsync",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(window,null));return window;
    }
    public static void Run(string theme,string output)
    {
        // RED pins the requested interaction, not decorative pixels or an unrelated backend.
        Check(typeof(CalendarDayWindow).GetField("MonthCalendar",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!=null,"Missing timetable month picker MonthCalendar.");
        Directory.CreateDirectory(output);
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var xml=new XmlDocument();xml.Load(theme);
        app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse("<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>"+xml.DocumentElement.FirstChild.InnerXml+"</ResourceDictionary>");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var name="musicbox_test_"+Guid.NewGuid().ToString("N");var db=new MySqlDatabase(name);
        var root=Path.Combine(Path.GetTempPath(),"MusicBoxCounter_"+Guid.NewGuid().ToString("N"));Console.WriteLine("Counter UI fixture: "+name);
        try {
            var auth=new MySqlAuthenticationService(db);var admin=auth.SetupAdminAsync("admin","Admin thử UI","Counter-Ui-2026!").GetAwaiter().GetResult();
            var today=DateTimeOffset.UtcNow.ToOffset(BookingHours.VietnamOffset).Date;
            var clock=new Clock{Now=new DateTimeOffset(today.AddHours(9),BookingHours.VietnamOffset)};
            Sql(db,"INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,InactiveReason,CreatedAt) VALUES(1,'S01',1,'Phòng Mộc','Content/uploads/rooms/11111111111111111111111111111111.png',1,NULL,'"+clock.Now.ToUniversalTime().ToString("O")+"'),(2,'S02',1,'Phòng Nắng','Content/uploads/rooms/22222222222222222222222222222222.png',1,NULL,'"+clock.Now.ToUniversalTime().ToString("O")+"'),(3,'V01',2,'Phòng Aurora','Content/uploads/rooms/33333333333333333333333333333333.png',0,'Bảo trì','"+clock.Now.ToUniversalTime().ToString("O")+"');");
            new MySqlReservationService(db,clock).CreateGuest(new ReservationRequest{RoomId=1,FullName="Khách lịch",PhoneNumber="0000000111",StartTime=clock.Now.AddMinutes(90),DurationMinutes=60});
            clock.Now=clock.Now.AddHours(1);new MySqlRoomSessionService(db,clock).CreateWalkIn(admin,new WalkInRequest{RoomId=2,FullName="Khách trực tiếp",PhoneNumber="0000000222"});clock.Now=clock.Now.AddMinutes(35);
            var counted=new CountingCalendar{Inner=new MySqlCalendarService(db,clock)};var vm=new CalendarDayViewModel(counted,admin,clock);var calendar=new CalendarDayWindow(vm);calendar.Show();Pump(()=>!vm.IsBusy&&vm.Rooms.Count==3);
            Check(counted.Reads==1,"Initial load duplicated.");Check(vm.IsDay&&vm.SelectedRoom.RoomId==null,"Default must be day/all rooms.");
            var month=Field<Calendar>(calendar,"MonthCalendar");Check(month.SelectedDate==today,"Month selection not synchronized.");
            var header=Field<Canvas>(calendar,"TimelineHeaders");Check(header.Children.Count>=3,"Missing sticky room headers.");
            var timeline=Field<Canvas>(calendar,"Timeline");var block=timeline.Children.OfType<Button>().First();Click(block);Check(vm.Details.Contains("Khách"),"Event details click lost.");
            var scroll=Field<ScrollViewer>(calendar,"TimelineScroll");scroll.ScrollToVerticalOffset(180);calendar.UpdateLayout();Check(header.IsVisible,"Headers disappeared on vertical scroll.");Image(calendar,output,"calendar-scrolled");scroll.ScrollToVerticalOffset(0);calendar.UpdateLayout();Image(calendar,output,"calendar-day");
            Check(counted.Reads==1,"Scrolling or selection re-read data.");Click(Field<Button>(calendar,"TodayButton"));Pump(()=>!vm.IsBusy&&vm.Rooms.Count==3);Check(counted.Reads==2,"Today duplicated load.");calendar.Width=1100;calendar.Height=720;calendar.UpdateLayout();Check(counted.Reads==2,"Resize re-read data.");Image(calendar,output,"calendar-compact");
            Field<RadioButton>(calendar,"WeekMode").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Pump(()=>!vm.IsBusy&&vm.IsWeek);Check(vm.SelectedRoom.RoomId.HasValue,"Week should initially focus one room.");Check(counted.Reads==3,"Mode change duplicated load.");scroll.ScrollToVerticalOffset(0);calendar.UpdateLayout();Image(calendar,output,"calendar-week");
            month.SelectedDate=today.AddDays(10);Pump(()=>!vm.IsBusy&&vm.Date==today.AddDays(10));Check(counted.Reads==4,"Month selection duplicated or skipped load.");Check(vm.Rooms.Count==1&&vm.Rooms.All(r=>r.Range.Start==CalendarRange.Week(today.AddDays(10)).Start),"Month selection retained previous week.");
            Field<ComboBox>(calendar,"RoomInput").SelectedItem=vm.RoomChoices.Single(r=>r.RoomId==2);Pump(()=>!vm.IsBusy);Check(counted.Reads==5&&vm.Rooms.Count==1&&vm.Rooms[0].RoomId==2,"Room selection skipped, duplicated or retained previous room.");
            calendar.Close();
            var main=OpenMain(db,root,admin);Check((string)Field<Button>(main,"ReservationsButton").Content=="Đặt phòng","Unified booking caption missing.");Check(Field<Button>(main,"StaffBookingButton").Visibility==Visibility.Collapsed,"Duplicate booking entry still visible.");Check(Field<Button>(main,"QuickCalendarButton").IsVisible,"Calendar entry not accessible.");
            var opener=Field<Button>(main,"StaffBookingButton");opener.IsEnabled=false;Check(!Field<Button>(main,"QuickBookingButton").IsEnabled,"Quick create did not reflect opening busy state.");opener.IsEnabled=true;
            var dashboard=Field<DashboardView>(main,"DashboardHost");Check(dashboard.FindName("RoomStatusMeter")!=null,"Missing 2D room overview meter.");Image(main,output,"dashboard-admin");
            main.Width=1000;main.Height=680;main.UpdateLayout();Image(main,output,"dashboard-compact");main.Close();
            var accounts=new MySqlAccountManagementService(db);accounts.CreateAsync(admin,new AccountEdit{UserName="staff",FullName="Nhân viên thử",Role="Staff"},"Counter-Ui-2026!").GetAwaiter().GetResult();
            var staff=auth.LoginAsync("staff","Counter-Ui-2026!").GetAwaiter().GetResult();var staffMain=OpenMain(db,root,staff);
            Check(!Field<DashboardViewModel>(staffMain,"dashboard").HasRevenue,"Staff received aggregate revenue.");Image(staffMain,output,"dashboard-staff");staffMain.Close();
            // Create-only route remains usable; View-only is not silently granted Create.
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff'; INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Reservation.Create';");
            var createOnly=OpenMain(db,root,staff);Check(Field<Button>(createOnly,"ReservationsButton").Visibility==Visibility.Visible,"Create-only lost unified entry.");
            var closeDraft=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};bool opened=false;closeDraft.Tick+=(s,e)=>{var draft=app.Windows.OfType<GuestBookingWindow>().FirstOrDefault();if(draft!=null){opened=true;draft.Close();}};closeDraft.Start();try{Click(Field<Button>(createOnly,"ReservationsButton"));Pump(()=>opened&&Field<Button>(createOnly,"ReservationsButton").IsEnabled);}finally{closeDraft.Stop();}createOnly.Close();
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff'; INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Reservation.View';");
            var viewOnly=OpenMain(db,root,staff);Check(Field<Button>(viewOnly,"ReservationsButton").Visibility==Visibility.Visible,"View-only lost unified entry.");Check(Field<Button>(viewOnly,"QuickBookingButton").Visibility==Visibility.Collapsed,"View-only received create shortcut.");
            var closeList=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};bool listOpened=false;closeList.Tick+=(s,e)=>{var list=app.Windows.OfType<ReservationsWindow>().FirstOrDefault();if(list!=null){listOpened=true;Check(!Field<Button>(list,"CreateButton").IsEnabled,"View-only received create permission.");list.Close();}};closeList.Start();try{Click(Field<Button>(viewOnly,"ReservationsButton"));Pump(()=>listOpened&&Field<Button>(viewOnly,"ReservationsButton").IsEnabled);}finally{closeList.Stop();}viewOnly.Close();
            Sql(db,"UPDATE AspNetUsers SET IsActive=0 WHERE UserName='admin';");Await(vm.LoadAsync());Check(vm.Rooms.Count==0&&!vm.Details.Contains("Khách lịch"),"Revoked calendar retained private details.");var revoked=new CalendarDayWindow(vm);revoked.Show();Pump(()=>!vm.IsBusy);Image(revoked,output,"calendar-revoked");revoked.Close();
            Console.WriteLine("PASS Counter UI MySQL: timetable day/week/month/scroll/details/compact,2Ddashboard,currentrights/StaffnoRevenue,unifiedCreateOnly/ViewOnly routes; fakeGUID only.");
        } finally {
            foreach(Window w in app.Windows.Cast<Window>().ToArray())w.Close();using(var c=new MySqlDatabase("defaultdb").OpenConnection())using(var q=c.CreateCommand()){q.CommandText="DROP DATABASE IF EXISTS `"+name+"`";q.ExecuteNonQuery();}app.Shutdown();
        }
    }
}
