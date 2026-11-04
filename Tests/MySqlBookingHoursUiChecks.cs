using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
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
public static class MusicBoxMySqlBookingHoursUiChecks
{
 private sealed class Clock:IClock {public DateTimeOffset Now;public DateTimeOffset UtcNow{get{return Now;}}}
 private sealed class BookingCounter:IBookingService
 {
  public IBookingService Inner;public int Creates,Previews;
  public System.Collections.Generic.List<PublicRoom> ListRooms(){return Inner.ListRooms();}
  public ReservationAvailability Preview(ReservationRequest input){Previews++;return Inner.Preview(input);}
  public Reservation Create(ReservationRequest input){Creates++;return Inner.Create(input);}
  public string GetImagePath(string path){return Inner.GetImagePath(path);}
  public PublicRoomDay ReadDay(int room,DateTime date,int minutes){return Inner.ReadDay(room,date,minutes);}
 }
 private static int checkInThrows;
 private static int notices,hourThrows;private static string last;private static string output;
 private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 private static T Field<T>(object obj,string name){return (T)obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(obj);}
 private static void Pump(Func<bool> done){var sw=Stopwatch.StartNew();while(!done()){if(sw.Elapsed.TotalSeconds>90)throw new Exception("Hours UI timeout");var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(10);}}
 private static void Click(Window window,string name,Func<bool> ready){var before=notices;var b=Field<Button>(window,name);Check(b.IsEnabled,"Disabled button "+name);b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(ready);Check(notices==before+1&&window.IsVisible,"Missing popup or closed form: "+name);}
 private static object Sql(MySqlDatabase db,string sql){using(var c=db.OpenConnection())using(var q=c.CreateCommand()){q.CommandText=sql;return q.ExecuteScalar();}}
 private static string Counts(MySqlDatabase db){return Convert.ToString(Sql(db,"SELECT CONCAT((SELECT COUNT(*) FROM Reservations),':',(SELECT COUNT(*) FROM RoomSessions),':',(SELECT COUNT(*) FROM Customers),':',(SELECT COUNT(*) FROM AuditLog))"));}
 private static void Image(Window w,string name){w.UpdateLayout();var img=new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth),(int)Math.Ceiling(w.ActualHeight),96,96,PixelFormats.Pbgra32);img.Render(w);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(img));using(var stream=File.Create(Path.Combine(output,name+".png")))encoder.Save(stream);}
 private static void HoursContract()
 {
  var day=new DateTimeOffset(2026,11,4,0,0,0,BookingHours.VietnamOffset);string reason;DateTimeOffset end;
  Check(BookingHours.TryValidateSessionInterval(day.AddHours(11),day.AddHours(12),out reason),"Exact noon boundary rejected.");
  Check(!BookingHours.TryValidateSessionInterval(day.AddHours(11),day.AddHours(12).AddTicks(1),out reason)&&reason.Contains("nghỉ trưa"),"One tick past noon accepted.");
  Check(!BookingHours.TryValidateSessionInterval(day.AddHours(12),day.AddHours(13),out reason)&&reason.Contains("nghỉ trưa"),"Lunch start accepted.");
  Check(BookingHours.TryValidateSessionInterval(day.AddHours(13),day.AddHours(14),out reason),"Afternoon opening rejected.");
  Check(BookingHours.TryValidateSessionInterval(day.AddHours(22),day.AddHours(23),out reason),"Exact closing rejected.");
  Check(!BookingHours.TryValidateSessionInterval(day.AddHours(22),day.AddHours(23).AddTicks(1),out reason)&&reason.Contains("23:00"),"One tick past closing accepted.");
  Check(!BookingHours.TryValidateSessionInterval(day.AddHours(8).AddMinutes(59),day.AddHours(10),out reason)&&reason.Contains("09:00"),"Before opening accepted.");
  Check(!BookingHours.TryValidateSessionInterval(day.AddHours(22),day.AddDays(1).AddHours(1),out reason),"Overnight interval accepted.");
  Check(BookingHours.TryValidateReservation(day.AddDays(30).AddHours(9),60,day.AddHours(9),out end,out reason),"Day30 rejected.");
  Check(!BookingHours.TryValidateReservation(day.AddDays(31).AddHours(9),60,day.AddHours(9),out end,out reason),"Day31 accepted.");
  Check(!BookingHours.TryValidateReservation(day.AddHours(11).AddTicks(1),60,day.AddHours(9),out end,out reason),"Booking lost slot tick rule.");

 }
 public static void Run(string theme,string folder)
 {
  HoursContract();
  output=folder;Directory.CreateDirectory(output);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var xml=new XmlDocument();xml.Load(theme);app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse("<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>"+xml.DocumentElement.FirstChild.InnerXml+"</ResourceDictionary>");SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
  var dismiss=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(25)};dismiss.Tick+=(s,e)=>{var dialog=app.Windows.Cast<Window>().FirstOrDefault(w=>w.Name=="UserFeedbackDialog"&&w.IsVisible);if(dialog==null)return;last=((TextBlock)dialog.FindName("MessageText")).Text;notices++;if(notices==1)Image(dialog,"lunch-notice");((Button)dialog.FindName("AcceptButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));};dismiss.Start();
  EventHandler<FirstChanceExceptionEventArgs> watch=(s,e)=>{if(e.Exception is ArgumentException&&(e.Exception.StackTrace??"").Contains("BookingHours"))Interlocked.Increment(ref hourThrows);if(e.Exception is InvalidOperationException&&(e.Exception.StackTrace??"").Contains("MySqlRoomSessionService.CheckIn"))Interlocked.Increment(ref checkInThrows);};AppDomain.CurrentDomain.FirstChanceException+=watch;
  var name="musicbox_test_"+Guid.NewGuid().ToString("N");Console.WriteLine("Hours fixture: "+name);var db=new MySqlDatabase(name);var day=DateTimeOffset.UtcNow.ToOffset(BookingHours.VietnamOffset).Date;var clock=new Clock{Now=new DateTimeOffset(day.AddHours(10),BookingHours.VietnamOffset)};var root=Path.Combine(Path.GetTempPath(),"MusicBoxHours_"+Guid.NewGuid().ToString("N"));
  try {
   var admin=new MySqlAuthenticationService(db).SetupAdminAsync("admin","Admin test","Hours-2026!").GetAwaiter().GetResult();var rooms=new MySqlRoomService(db,root,clock);var image=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(theme),"..","assets","rooms","standard-oak-50mm.png"));rooms.Create(admin,new RoomCreate{RoomCode="S01",Name="Phòng test",RoomTypeId=1,ImageFilePath=image});rooms.Create(admin,new RoomCreate{RoomCode="S02",Name="Phòng tối",RoomTypeId=1,ImageFilePath=image});
   var actual=new MySqlGuestBookingService(db,root,clock);var service=new BookingCounter{Inner=actual};var vm=new GuestBookingViewModel(service,clock);var form=new GuestBookingWindow(service,vm);form.Show();Pump(()=>!vm.IsBusy&&vm.Rooms.Count==2);vm.FullName="Khách test";vm.PhoneNumber="0123456789";vm.StartTimeText="11:00";vm.Duration=90;var before=Counts(db);
   Click(form,"SubmitButton",()=>!vm.IsBusy);Check(last.Contains("nghỉ trưa")&&last.Contains("12:00")&&last.Contains("13:00"),"RED: booking must explain lunch12-13 in popup before submitting.");Check(hourThrows==0,"Booking UI reached throwing hours validation.");Check(Counts(db)==before&&vm.PhoneNumber=="0123456789","Invalid hours wrote data or erased input.");
   Click(form,"PreviewButton",()=>!vm.IsBusy);Check(service.Creates==0&&service.Previews==0,"Invalid lunch interval reached booking service.");
   vm.StartTimeText="22:00";vm.Duration=90;Click(form,"SubmitButton",()=>!vm.IsBusy);Check(last.Contains("đóng cửa")&&last.Contains("23:00")&&service.Creates==0,"Night crossing missing closing preflight.");Check(Counts(db)==before,"Closing validation wrote data.");
   vm.StartTimeText="11:00";vm.Duration=60;Click(form,"PreviewButton",()=>!vm.IsBusy);Check(last.Contains("12:00")&&last.Contains("đến sớm"),"Exact-noon preview must allow and warn to arrive early.");Click(form,"SubmitButton",()=>!vm.IsBusy);Check(vm.LastCreatedReservationId.HasValue&&last.Contains("đến sớm")&&!vm.PreviewText.Contains("11:15"),"Noon boundary promised an unusable late grace window.");var noonId=vm.LastCreatedReservationId.Value;form.Close();
   var nightVm=new GuestBookingViewModel(service,clock);var nightForm=new GuestBookingWindow(service,nightVm);nightForm.Show();Pump(()=>!nightVm.IsBusy&&nightVm.Rooms.Count==2);nightVm.SelectedRoom=nightVm.Rooms.Single(r=>r.RoomId==2);nightVm.FullName="Khách tối";nightVm.PhoneNumber="0123456788";nightVm.StartTimeText="22:00";Click(nightForm,"SubmitButton",()=>!nightVm.IsBusy);Check(nightVm.LastCreatedReservationId.HasValue&&last.Contains("23:00")&&last.Contains("đến sớm"),"Exact closing booking should remain valid with early-arrival notice.");var nightId=nightVm.LastCreatedReservationId.Value;nightForm.Close();
   var staffService=((IStaffReservationService)new MySqlStaffReservationService(db,clock,root)).ForBooking(admin);var staffCounter=new BookingCounter{Inner=staffService};var staffVm=new GuestBookingViewModel(staffCounter,clock,true);var staffForm=new GuestBookingWindow(staffCounter,staffVm);staffForm.Show();Pump(()=>!staffVm.IsBusy&&staffVm.Rooms.Count==2);staffVm.FullName="Đặt hộ test";staffVm.PhoneNumber="0123456787";staffVm.StartTimeText="11:00";staffVm.Duration=90;before=Counts(db);Click(staffForm,"SubmitButton",()=>!staffVm.IsBusy);Check(last.Contains("nghỉ trưa")&&staffCounter.Creates==0&&Counts(db)==before,"Assisted booking bypassed lunch guard.");staffVm.StartTimeText="22:00";staffVm.Duration=90;Click(staffForm,"SubmitButton",()=>!staffVm.IsBusy);Check(last.Contains("23:00")&&staffCounter.Creates==0&&Counts(db)==before,"Assisted booking bypassed closing guard.");
   staffVm.SelectedDate=day.AddDays(1);staffVm.StartTimeText="11:00";staffVm.Duration=60;Click(staffForm,"SubmitButton",()=>!staffVm.IsBusy);Check(staffVm.LastCreatedReservationId.HasValue&&last.Contains("đến sớm"),"Assisted noon boundary warning missing.");var staffNoonId=staffVm.LastCreatedReservationId.Value;Check(Convert.ToString(Sql(db,"SELECT CreatedByUserId FROM Reservations WHERE ReservationId="+staffNoonId))==admin.UserId,"Assisted booking fell back to Guest route.");staffForm.Close();
   var staffNightVm=new GuestBookingViewModel(staffCounter,clock,true);var staffNight=new GuestBookingWindow(staffCounter,staffNightVm);staffNight.Show();Pump(()=>!staffNightVm.IsBusy&&staffNightVm.Rooms.Count==2);staffNightVm.SelectedRoom=staffNightVm.Rooms.Single(r=>r.RoomId==2);staffNightVm.SelectedDate=day.AddDays(1);staffNightVm.StartTimeText="22:00";staffNightVm.Duration=60;staffNightVm.FullName="Đặt hộ tối";staffNightVm.PhoneNumber="0123456787";Click(staffNight,"SubmitButton",()=>!staffNightVm.IsBusy);Check(staffNightVm.LastCreatedReservationId.HasValue&&last.Contains("23:00")&&last.Contains("đến sớm"),"Assisted closing boundary should be valid with warning.");staffNight.Close();
   var noonPreview=actual.Preview(new ReservationRequest{RoomId=2,FullName="Test",PhoneNumber="0123456777",StartTime=new DateTimeOffset(day.AddHours(12),BookingHours.VietnamOffset),DurationMinutes=60});Check(!noonPreview.CanBook&&noonPreview.Reason.Contains("nghỉ trưa")&&hourThrows==0,"Direct preview threw or hid lunch reason.");
   Console.WriteLine("PASS Guest/Staff booking: lunch/closing no service call or writes; exact12/23 accepted with early-arrival warning; direct preview no throw.");
   // The original reservation11-12 is legal; arrival11:10 must not open confirmation.
   clock.Now=new DateTimeOffset(day.AddHours(11).AddMinutes(10),BookingHours.VietnamOffset);var staff=new MySqlStaffReservationService(db,clock,root);var receiveVm=new ReservationsViewModel(staff,admin,clock);var receive=new ReservationsWindow(receiveVm);receive.Show();Pump(()=>!receiveVm.IsBusy&&receiveVm.Items.Count>=2);receiveVm.Selected=receiveVm.Items.Single(r=>r.ReservationId==noonId);before=Counts(db);Click(receive,"CheckInButton",()=>!receiveVm.IsBusy);Check(!receiveVm.IsConfirmingCheckIn&&last.Contains("nghỉ trưa")&&last.Contains("12:10"),"Late arrival must explain actual-end crossing lunch before confirm.");Check(Counts(db)==before,"Rejected receive wrote session/audit.");receive.Close();
   // Cross the boundary after opening confirmation, before submitting.
   clock.Now=new DateTimeOffset(day.AddHours(10).AddMinutes(59),BookingHours.VietnamOffset);var changingVm=new ReservationsViewModel(staff,admin,clock);var changing=new ReservationsWindow(changingVm);changing.Show();Pump(()=>!changingVm.IsBusy&&changingVm.Items.Count>=2);changingVm.Selected=changingVm.Items.Single(r=>r.ReservationId==noonId);Click(changing,"CheckInButton",()=>!changingVm.IsBusy);Check(changingVm.IsConfirmingCheckIn,"Valid early receive did not open confirm.");clock.Now=new DateTimeOffset(day.AddHours(11).AddSeconds(1),BookingHours.VietnamOffset);Click(changing,"ConfirmCheckInButton",()=>!changingVm.IsBusy);Check(!changingVm.IsConfirmingCheckIn&&last.Contains("nghỉ trưa")&&checkInThrows==0&&hourThrows==0&&Counts(db)==before,"Confirm must recheck clock without calling throwing writer.");changing.Close();
   clock.Now=new DateTimeOffset(day.AddHours(11),BookingHours.VietnamOffset);var exactVm=new ReservationsViewModel(staff,admin,clock);var exact=new ReservationsWindow(exactVm);exact.Show();Pump(()=>!exactVm.IsBusy&&exactVm.Items.Count>=2);exactVm.Selected=exactVm.Items.Single(r=>r.ReservationId==noonId);Click(exact,"CheckInButton",()=>!exactVm.IsBusy);Click(exact,"ConfirmCheckInButton",()=>!exactVm.IsBusy);Check(Convert.ToInt32(Sql(db,"SELECT COUNT(*) FROM RoomSessions WHERE ReservationId="+noonId))==1,"Exactly ending12 was rejected.");exact.Close();before=Counts(db);
   clock.Now=new DateTimeOffset(day.AddHours(22).AddMinutes(10),BookingHours.VietnamOffset);var closingVm=new ReservationsViewModel(staff,admin,clock);var closing=new ReservationsWindow(closingVm);closing.Show();Pump(()=>!closingVm.IsBusy&&closingVm.Items.Count>=2);closingVm.Selected=closingVm.Items.Single(r=>r.ReservationId==nightId);Click(closing,"CheckInButton",()=>!closingVm.IsBusy);Check(!closingVm.IsConfirmingCheckIn&&last.Contains("23:00")&&last.Contains("23:10")&&checkInThrows==0,"Late evening receive missing closing preflight.");closing.Close();
   // Backend remains authoritative for direct callers; rejection rolls back fully.
   var rejected=false;try{new MySqlRoomSessionService(db,clock).CheckIn(admin,nightId);}catch(InvalidOperationException e){rejected=e.Message.Contains("23:00");}Check(rejected,"Server did not return closing rejection.");Check(Counts(db)==before,"Server rejection changed data/audit.");

   clock.Now=new DateTimeOffset(day.AddHours(22),BookingHours.VietnamOffset);var lateVm=new ReservationsViewModel(staff,admin,clock);var lateWindow=new ReservationsWindow(lateVm);lateWindow.Show();Pump(()=>!lateVm.IsBusy&&lateVm.Items.Count>=2);lateVm.Selected=lateVm.Items.Single(r=>r.ReservationId==nightId);Click(lateWindow,"CheckInButton",()=>!lateVm.IsBusy);Click(lateWindow,"ConfirmCheckInButton",()=>!lateVm.IsBusy);Check(Convert.ToInt32(Sql(db,"SELECT COUNT(*) FROM RoomSessions WHERE ReservationId="+nightId))==1,"Exactly ending23 was rejected.");Image(lateWindow,"checkin-exact-closing");lateWindow.Close();
   Console.WriteLine("PASS CheckIn: late lunch/closing popup/no-write; changed clock reconfirm guard/no throws; server rollback; exact12/23 accepted. Notices="+notices);
  }finally{dismiss.Stop();AppDomain.CurrentDomain.FirstChanceException-=watch;foreach(var w in app.Windows.Cast<Window>().ToArray())w.Close();using(var c=new MySqlDatabase("defaultdb").OpenConnection())using(var q=c.CreateCommand()){q.CommandText="DROP DATABASE IF EXISTS `"+name+"`";q.ExecuteNonQuery();}app.Shutdown();}
 }
}
