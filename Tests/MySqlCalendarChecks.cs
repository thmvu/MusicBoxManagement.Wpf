using System;
using System.Globalization;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxMySqlCalendarChecks
{
    private sealed class Clock : IClock { public DateTimeOffset Value; public int Reads; public Action Hook;
        public DateTimeOffset UtcNow { get { Reads++; if(Hook != null) Hook(); return Value; } } }
    private static DateTimeOffset At(int hour) { return new DateTimeOffset(2026,10,20,hour,0,0,TimeSpan.FromHours(7)); }
    private static string Utc(DateTimeOffset value) { return value.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture); }
    private static object Sql(MySqlDatabase db,string sql) { using(var c=db.OpenConnection()) using(var q=c.CreateCommand()) { q.CommandText=sql; return q.ExecuteScalar(); } }
    private static void Assert(bool value,string message) { if(!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T:Exception { try { action(); } catch(T) { return; } throw new Exception("Expected "+typeof(T).Name); }
    public static void Run()
    {
        var name="musicbox_test_"+Guid.NewGuid().ToString("N"); var db=new MySqlDatabase(name);
        try
        {
            const string password="Calendar-2026!";
            var auth=new MySqlAuthenticationService(db); var admin=auth.SetupAdminAsync("admin","Admin",password).GetAwaiter().GetResult();
            Sql(db,"INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,InactiveReason,CreatedAt) VALUES(1,'R1',1,'Phòng','test.png',1,NULL,'"+Utc(At(9))+"'),(2,'R2',1,'Khóa','test.png',0,'Bảo trì','"+Utc(At(9))+"');INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'staff','staff','STAFF',PasswordHash,'staff','Staff',1 FROM AspNetUsers LIMIT 1;INSERT INTO AspNetUserRoles VALUES('staff','Staff');DELETE FROM RolePermission WHERE RoleId='Staff';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Calendar.View';");
            var staff=auth.LoginAsync("staff",password).GetAwaiter().GetResult(); var clock=new Clock{Value=At(9)};
            var service=new MySqlCalendarService(db,clock); var range=CalendarRange.Day(At(9).Date);
            var booking=new MySqlReservationService(db,clock).CreateGuest(new ReservationRequest{RoomId=1,FullName="Khách",PhoneNumber="0912345678",StartTime=At(13),DurationMinutes=60});
            clock.Reads=0; var day=service.Read(staff,range);
            Assert(clock.Reads==1 && day.Count==2,"Single clock/calendar-only access failed.");
            Assert(day[0].Holds.Single().Start==At(13) && day[0].Events.Single().PhoneNumber=="0912345678","Booking hold/private details lost.");
            Assert(day[1].CurrentStatus=="Inactive" && day[1].Events.Count==0,"Locked room missing.");
            Assert(service.Read(staff,CalendarRange.Week(At(9).Date),1).Single().Range.Start.ToOffset(TimeSpan.FromHours(7)).DayOfWeek==DayOfWeek.Monday,"Week/filter failed.");
            Reject<UnauthorizedAccessException>(()=>service.Read(null,range)); Reject<ArgumentException>(()=>service.Read(staff,null)); Reject<ArgumentException>(()=>service.Read(staff,range,999));
            var audit=Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog"));
            clock.Value=At(13).AddMinutes(15).AddTicks(-1);
            Assert(service.Read(staff,range,1).Single().CurrentStatus=="Reserved","Grace ended early.");
            clock.Value=clock.Value.AddTicks(1);
            Assert(service.Read(staff,range,1).Single().Holds.Count==0 && service.Read(staff,range,1).Single().Events.Count==0,"Expired Confirmed still holds.");
            Assert((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+booking.ReservationId)=="Confirmed" && Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog"))==audit,"Calendar wrote maintenance/audit.");
            // Session fixtures exercise projection only; MySQL check-in/checkout are not implemented here.
            Sql(db,"UPDATE Reservations SET Status='CheckedIn' WHERE ReservationId="+booking.ReservationId+";INSERT INTO RoomSessions(CustomerId,RoomId,ReservationId,ActualStartTime,ExpectedEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES("+booking.CustomerId+",1,"+booking.ReservationId+",'"+Utc(At(13).AddTicks(7))+"','"+Utc(At(14))+"',120000,'R1','STANDARD','Standard','Active');");
            clock.Value=At(15); var active=service.Read(staff,range,1).Single();
            Assert(active.CurrentStatus=="Occupied" && active.Holds.Count==1 && active.Holds[0].End==At(14),"Active hold extended or double counted.");
            Assert(active.Events.Single().End==At(15) && active.Events.Single().Start==At(13).AddTicks(7) && active.Events.Single().IsOverdue,"Actual/expected/ticks lost.");
            Sql(db,"UPDATE RoomSessions SET Status='Completed',ActualEndTime='"+Utc(At(15))+"';UPDATE Rooms SET IsActive=0,InactiveReason='Khóa' WHERE RoomId=1;");
            Assert(service.Read(staff,range,1).Single().Events.Single().Kind=="Completed","Locked history lost.");
            Sql(db,"UPDATE Rooms SET IsActive=1 WHERE RoomId=1;");
            new MySqlReservationService(db,clock).CreateGuest(new ReservationRequest{RoomId=1,FullName="Khách tiếp",PhoneNumber="0987654321",StartTime=At(17),DurationMinutes=60});
            Sql(db,"INSERT INTO RoomSessions(CustomerId,RoomId,ActualStartTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES("+booking.CustomerId+",1,'"+Utc(At(15))+"',120000,'R1','STANDARD','Standard','Active');");
            var walk=service.Read(staff,range,1).Single();
            Assert(walk.Holds.Count==1 && walk.Events.Single(e=>e.Kind=="WalkIn").ReturnBy==At(17),"Walk-in invented hold/next booking deadline wrong.");
            clock.Value=At(17).AddMinutes(15);
            walk=service.Read(staff,range,1).Single();
            Assert(walk.Holds.Count==0 && walk.Events.Single(e=>e.Kind=="WalkIn").ReturnBy==At(23),"Expired booking did not release dynamic deadline.");
            // Mutate after protected authorization SELECT: current read stays in its snapshot.
            clock.Hook=()=>{clock.Hook=null;Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';UPDATE Customers SET FullName='Đổi tên' WHERE CustomerId="+booking.CustomerId);};
            Assert(service.Read(staff,range,1).Single().Events.All(e=>e.CustomerName=="Khách"),"Private data escaped permission snapshot.");
            Reject<UnauthorizedAccessException>(()=>service.Read(staff,range));
            Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Calendar.View';UPDATE AspNetUsers SET SecurityStamp='changed' WHERE Id='staff';");
            Reject<UnauthorizedAccessException>(()=>service.Read(staff,range));
            Assert(Convert.ToInt32(Sql(db,"SELECT Version FROM _MusicBoxSchemaVersion"))==MySqlDatabase.CurrentSchemaVersion,"Schema changed.");
            Console.WriteLine("PASS MySQL Calendar: protected snapshot, Calendar-only, day/week/filter, grace ticks/no writes, actual/expected/overdue, locked history, walk-in, revoke/stamp.");
        }
        finally
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"\Amusicbox_test_[0-9a-f]{32}\z")) throw new Exception("Unsafe cleanup.");
            Sql(new MySqlDatabase("defaultdb"),"DROP DATABASE IF EXISTS `"+name+"`");
        }
    }
}
