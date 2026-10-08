using System;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxWalkInChecks
{
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } }
    private sealed class CountingClock : IClock
    {
        public DateTimeOffset Value; public int Reads;
        public DateTimeOffset UtcNow { get { Interlocked.Increment(ref Reads); return Value; } }
    }
    private sealed class GateClock : IClock
    {
        public readonly ManualResetEventSlim Entered=new ManualResetEventSlim(false), Release=new ManualResetEventSlim(false);
        public DateTimeOffset Value;
        public DateTimeOffset UtcNow {get{Entered.Set();if(!Release.Wait(10000))throw new Exception("Clock gate timeout.");return Value;}}
    }
    private static DateTimeOffset Time(int h,int m=0){return new DateTimeOffset(2026,10,7,h,m,0,TimeSpan.FromHours(7));}
    private static string Utc(DateTimeOffset t){return t.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);}
    private static void Assert(bool b,string message){if(!b)throw new Exception(message);}
    private static void Reject<T>(Action a)where T:Exception{try{a();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static object Sql(SqliteDatabase db,string sql){using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=sql;return cmd.ExecuteScalar();}}
    private static long Count(SqliteDatabase db,string sql){return Convert.ToInt64(Sql(db,sql));}
    private static void Clear(SqliteDatabase db){Sql(db,"DELETE FROM RoomSessions; DELETE FROM Reservations; DELETE FROM Customers WHERE CustomerId>3; DELETE FROM AuditLog; UPDATE Rooms SET IsActive=1,InactiveReason=NULL;");}
    private static WalkInRequest Request(int room,string phone){return new WalkInRequest{RoomId=room,FullName=" Khách trực tiếp ",PhoneNumber=phone};}
    private static int Booking(SqliteDatabase db,int room,int customer,DateTimeOffset start)
    {return Convert.ToInt32(Sql(db,"INSERT INTO Reservations(RoomId,CustomerId,StartTime,EndTime,Status,CreatedAt) VALUES("+room+","+customer+",'"+Utc(start)+"','"+Utc(start.AddHours(1))+"','Confirmed','"+Utc(Time(9))+"'); SELECT last_insert_rowid();"));}
    private static bool Attempt(RoomSessionService service,LoginSession staff,WalkInRequest request){try{service.CreateWalkIn(staff,request);return true;}catch(InvalidOperationException){return false;}}
    public static void Run()
    {
        var file=Path.Combine(Path.GetTempPath(),"MusicBoxWalkIn_"+Guid.NewGuid().ToString("N")+".db");var db=new SqliteDatabase(file);var clock=new Clock{UtcNow=Time(13,37).AddSeconds(12).AddTicks(1234)};
        const string password="WalkIn-Test-2026!";
        try
        {
            var auth=new AuthenticationService(db);var admin=auth.SetupAdminAsync("admin","Admin thử nghiệm",password).GetAwaiter().GetResult();
            Sql(db,@"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'staff','staff','STAFF',PasswordHash,'staff','Nhân viên thử',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('staff','Staff');
DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId<>(SELECT PermissionId FROM Permission WHERE Code='Session.WalkIn');
INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('R1','Phòng 1',1,'test.png',1,'test'),('R2','Phòng 2',2,'test.png',1,'test');
INSERT INTO Customers(FullName,PhoneNumber) VALUES('Tên cũ','0901111111'),('B','0902222222'),('C','0903333333');");
            var staff=auth.LoginAsync("staff",password).GetAwaiter().GetResult();var service=new RoomSessionService(db,clock);Clear(db);
            Assert(service.ListWalkInRooms(staff).Count==2,"Walk-in room catalog required unrelated permission.");
            var previewAudits=Count(db,"SELECT COUNT(*) FROM AuditLog");
            var preview=service.PreviewWalkIn(staff,Request(1,"0904444444"));
            Assert(preview.CanReceive && preview.ReturnBy==Time(23) && preview.HourlyRate==120000 && Count(db,"SELECT COUNT(*) FROM Customers")==3 && Count(db,"SELECT COUNT(*) FROM AuditLog")==previewAudits,"Preview wrote data or deadline/price incorrect.");
            Reject<UnauthorizedAccessException>(()=>service.ListWalkInRooms(null));
            Reject<UnauthorizedAccessException>(()=>service.PreviewWalkIn(null,Request(1,"0901111111")));
            Reject<UnauthorizedAccessException>(()=>service.CreateWalkIn(null,Request(1,"0900000000")));
            Reject<ArgumentException>(()=>service.CreateWalkIn(staff,null));var invalid=Request(1,"bad");Reject<ArgumentException>(()=>service.CreateWalkIn(staff,invalid));
            invalid.PhoneNumber="0900000000";invalid.FullName=" ";Reject<ArgumentException>(()=>service.CreateWalkIn(staff,invalid));invalid.FullName=new string('a',101);Reject<ArgumentException>(()=>service.CreateWalkIn(staff,invalid));
            var started=clock.UtcNow;var first=service.CreateWalkIn(staff,Request(1,"+84 901.111-111"));var id=first.Session.RoomSessionId;
            Assert(service.ReadWalkInReceipt(staff,id).Session.RoomSessionId==id,"WalkIn-only staff could not refresh own receipt.");
            Reject<UnauthorizedAccessException>(()=>service.ReadWalkInReceipt(admin,id));
            Assert(!service.PreviewWalkIn(staff,Request(1,"0902222222")).CanReceive,"Preview ignored Active room.");
            Assert(first.Session.ReservationId==null && first.Session.ExpectedEndTime==null && first.Session.ActualEndTime==null && first.Session.Status=="Active" && first.Session.ActualStartTime==started && first.Session.ActualStartTime.Offset==TimeSpan.Zero,"Walk-in created booking/duration or rounded actual.");
            Assert(first.ReturnBy==Time(23) && first.CheckedAt==started && first.Session.HourlyRate==120000 && first.Session.RoomCodeSnapshot=="R1" && first.Session.RoomTypeCodeSnapshot=="STANDARD","ReturnBy/snapshot/clock incorrect.");
            Assert((string)Sql(db,"SELECT FullName FROM Customers WHERE CustomerId=1")=="Tên cũ" && Count(db,"SELECT COUNT(*) FROM Customers")==3 && Count(db,"SELECT COUNT(*) FROM Reservations")==0,"Walk-in changed old customer or created booking.");
            Assert(Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Session.WalkIn' AND ActorType='Staff' AND UserId='staff' AND CreatedAt='"+Utc(started)+"'")==1,"Walk-in actor/time/audit incorrect.");
            Reject<InvalidOperationException>(()=>service.CreateWalkIn(staff,Request(1,"0904444444")));Assert(Count(db,"SELECT COUNT(*) FROM Customers")==3,"Rejected repeated walk-in left customer.");
            Reject<UnauthorizedAccessException>(()=>service.ReadWalkIn(staff,id));Reject<UnauthorizedAccessException>(()=>service.ReadWalkIn(null,id));
            Sql(db,"INSERT INTO RolePermission(RoleId,PermissionId) SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.View';");
            var audits=Count(db,"SELECT COUNT(*) FROM AuditLog");Assert(service.ReadWalkIn(staff,id).ReturnBy==Time(23) && Count(db,"SELECT COUNT(*) FROM AuditLog")==audits,"Read mutated audit.");
            Booking(db,1,2,Time(20));Booking(db,2,1,Time(19));Assert(service.ReadWalkIn(staff,id).ReturnBy==Time(19),"Customer next booking did not lower deadline.");
            var calendar=new CalendarService(db,clock).Read(admin,CalendarRange.Day(Time(13).Date),1)[0];
            Assert(calendar.CurrentStatus=="Occupied" && calendar.Events[0].ReturnBy==Time(19) && calendar.Holds.Count==1 && calendar.Holds[0].Start==Time(20),"Calendar walk-in deadline/holds disagreed.");
            Sql(db,"UPDATE Reservations SET Status='Cancelled' WHERE RoomId=2;");Assert(service.ReadWalkIn(staff,id).ReturnBy==Time(20),"Cancellation did not update deadline.");
            Sql(db,"UPDATE Reservations SET Status='Cancelled'; UPDATE RoomTypes SET Name='Tên mới',PricePerHour=300000 WHERE RoomTypeId=1;");
            Assert(service.ReadWalkIn(staff,id).ReturnBy==Time(23) && service.ReadWalkIn(staff,id).Session.HourlyRate==120000 && service.ReadWalkIn(staff,id).Session.RoomTypeNameSnapshot=="Standard","Read changed snapshot or kept cancelled deadline.");
            var availability=new AvailabilityService(db,clock);Assert(availability.CheckReservation(1,1,Time(20),60).CanBook,"Walk-in blocked future booking indefinitely.");
            clock.UtcNow=Time(23).AddTicks(1);var overdue=service.ReadWalkIn(staff,id);Assert(overdue.IsOverdue && overdue.ReturnBy==Time(23) && overdue.Session.Status=="Active","Deadline automatically completed walk-in.");
            clock.UtcNow=Time(13);Reject<InvalidOperationException>(()=>service.ReadWalkIn(staff,999));
            Clear(db);first=service.CreateWalkIn(staff,Request(2,"0904444444"));Assert(first.Session.RoomTypeCodeSnapshot=="VIP" && first.Session.HourlyRate==200000 && first.Session.CustomerId>3 && Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Customer.Create' AND ActorType='Staff' AND UserId='staff'")==1,"New customer/VIP snapshot/audit failed.");
            foreach(var time in new[]{Time(9),Time(11,59).AddSeconds(59).AddTicks(9999999),Time(13),Time(22,59).AddSeconds(59).AddTicks(9999999)})
            {Clear(db);clock.UtcNow=time;var result=service.CreateWalkIn(staff,Request(1,"0901111111"));Assert(result.ReturnBy==Time(time.Hour<12?12:23),"Valid shift edge rejected.");}
            foreach(var time in new[]{Time(8,59),Time(12),Time(12,59),Time(23),Time(0)})
            {Clear(db);clock.UtcNow=time;Reject<ArgumentException>(()=>service.CreateWalkIn(staff,Request(1,"0904444444")));Assert(Count(db,"SELECT COUNT(*) FROM Customers")==3 && Count(db,"SELECT COUNT(*) FROM RoomSessions")==0 && Count(db,"SELECT COUNT(*) FROM AuditLog")==0,"Closed shift left data.");}
            Clear(db);clock.UtcNow=Time(13);Reject<InvalidOperationException>(()=>service.CreateWalkIn(staff,Request(999,"0904444444")));
            Sql(db,"UPDATE Rooms SET IsActive=0,InactiveReason='Test' WHERE RoomId=1;");Reject<InvalidOperationException>(()=>service.CreateWalkIn(staff,Request(1,"0904444444")));Assert(Count(db,"SELECT COUNT(*) FROM Customers")==3,"Locked room left customer.");
            foreach(var room in new[]{1,2})
            {Clear(db);service.CreateWalkIn(staff,Request(room,"0901111111"));Reject<InvalidOperationException>(()=>service.CreateWalkIn(staff,Request(1,room==1?"0904444444":"0901111111")));Assert(Count(db,"SELECT COUNT(*) FROM RoomSessions")==1,"Active Room/Customer accepted another walk-in.");}
            foreach(var room in new[]{1,2})
            {Clear(db);Booking(db,room,room==1?2:1,Time(13));clock.UtcNow=Time(13,15).AddTicks(-1);Reject<InvalidOperationException>(()=>service.CreateWalkIn(staff,Request(1,"0901111111")));Assert(Count(db,"SELECT COUNT(*) FROM RoomSessions")==0,"Room/Customer booking in grace allowed walk-in.");}
            Clear(db);Booking(db,1,2,Time(13));clock.UtcNow=Time(13,15);first=service.CreateWalkIn(staff,Request(1,"0901111111"));Assert(Count(db,"SELECT COUNT(*) FROM Reservations WHERE Status='NoShow'")==1 && first.ReturnBy==Time(23),"Exact grace did not release room.");
            Clear(db);clock.UtcNow=Time(13);Booking(db,1,2,Time(13).AddTicks(1));Assert(service.CreateWalkIn(staff,Request(1,"0901111111")).ReturnBy==Time(13).AddTicks(1),"Future booking required minimum walk-in duration.");
            Clear(db);clock.UtcNow=Time(13);Booking(db,1,2,Time(9).AddDays(1));Assert(service.CreateWalkIn(staff,Request(1,"0901111111")).ReturnBy==Time(23),"Tomorrow booking changed current shift deadline.");
            Clear(db);Booking(db,1,2,Time(13));clock.UtcNow=Time(13,15);
            Sql(db,"CREATE TRIGGER FailWalkInAudit BEFORE INSERT ON AuditLog WHEN NEW.Action='Session.WalkIn' BEGIN SELECT RAISE(ABORT,'test failure'); END;");
            Reject<SQLiteException>(()=>service.CreateWalkIn(staff,Request(1,"0904444444")));
            Assert(Count(db,"SELECT COUNT(*) FROM Customers")==3 && Count(db,"SELECT COUNT(*) FROM RoomSessions")==0 && Count(db,"SELECT COUNT(*) FROM AuditLog")==0 && Count(db,"SELECT COUNT(*) FROM Reservations WHERE Status='Confirmed'")==1,"Audit failure did not rollback session/customer/NoShow.");Sql(db,"DROP TRIGGER FailWalkInAudit;");
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Session.WalkIn');");Reject<UnauthorizedAccessException>(()=>service.CreateWalkIn(staff,Request(1,"0904444444")));
            Sql(db,"INSERT INTO RolePermission(RoleId,PermissionId) SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.WalkIn'; UPDATE AspNetUsers SET IsActive=0 WHERE Id='staff';");Reject<UnauthorizedAccessException>(()=>service.CreateWalkIn(staff,Request(1,"0904444444")));Sql(db,"UPDATE AspNetUsers SET IsActive=1 WHERE Id='staff';");
            foreach(var sameRoom in new[]{true,false})
            {Clear(db);clock.UtcNow=Time(13);using(var gate=new ManualResetEventSlim(false)){var a=Task.Run(()=>{gate.Wait();return Attempt(service,staff,Request(1,sameRoom?"0904444444":"0901111111"));});var b=Task.Run(()=>{gate.Wait();return Attempt(service,staff,Request(sameRoom?1:2,sameRoom?"0905555555":"0901111111"));});gate.Set();Task.WaitAll(a,b);Assert(a.Result!=b.Result && Count(db,"SELECT COUNT(*) FROM RoomSessions")==1 && Count(db,"SELECT COUNT(*) FROM Customers")== (sameRoom?4:3),"Concurrent Room/Customer walk-ins both committed or left customer.");}}
            foreach(var sameRoom in new[]{true,false})
            {Clear(db);clock.UtcNow=Time(19);var source=Booking(db,sameRoom?1:2,1,Time(20));using(var gate=new ManualResetEventSlim(false)){var a=Task.Run(()=>{gate.Wait();return Attempt(service,staff,Request(1,sameRoom?"0902222222":"0901111111"));});var b=Task.Run(()=>{gate.Wait();try{service.CheckIn(admin,source);return true;}catch(InvalidOperationException){return false;}});gate.Set();Task.WaitAll(a,b);Assert(a.Result!=b.Result && Count(db,"SELECT COUNT(*) FROM RoomSessions")==1,"Check-in/walk-in Room/Customer both committed.");}}
            Clear(db);clock.UtcNow=Time(13);
            using(var gate=new ManualResetEventSlim(false)){var walk=Task.Run(()=>{gate.Wait();return Attempt(service,staff,Request(1,"0901111111"));});var booking=Task.Run(()=>{gate.Wait();try{new ReservationService(db,clock).CreateGuest(new ReservationRequest{RoomId=1,StartTime=Time(13),DurationMinutes=60,FullName="Booking",PhoneNumber="0902222222"});return true;}catch(InvalidOperationException){return false;}});gate.Set();Task.WaitAll(walk,booking);Assert(walk.Result!=booking.Result,"Immediate booking and walk-in both committed.");}
            Clear(db);clock.UtcNow=Time(13);var roomService=new RoomService(db,clock);var original=roomService.GetForEdit(admin,1);
            using(var gate=new ManualResetEventSlim(false)){var walk=Task.Run(()=>{gate.Wait();return Attempt(service,staff,Request(1,"0901111111"));});var locked=Task.Run(()=>{gate.Wait();try{roomService.Update(admin,original,new RoomEdit{Name=original.Name,Description=original.Description,IsActive=false,InactiveReason="Thử cạnh tranh"});return true;}catch(InvalidOperationException){return false;}});gate.Set();Task.WaitAll(walk,locked);Assert(walk.Result!=locked.Result && !(Count(db,"SELECT IsActive FROM Rooms WHERE RoomId=1")==0 && Count(db,"SELECT COUNT(*) FROM RoomSessions")>0),"Lock and walk-in both committed.");}
            Clear(db);Booking(db,1,2,Time(13));clock.UtcNow=Time(13,15);
            using(var gate=new ManualResetEventSlim(false)){var walk=Task.Run(()=>{gate.Wait();return service.CreateWalkIn(staff,Request(1,"0901111111"));});var noShow=Task.Run(()=>{gate.Wait();return new NoShowService(db,clock).ProcessExpired();});gate.Set();Task.WaitAll(walk,noShow);Assert(Count(db,"SELECT COUNT(*) FROM RoomSessions")==1 && Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.NoShow'")==1,"Concurrent NoShow/walk-in duplicated maintenance.");}
            Clear(db);var held=new GateClock{Value=Time(9)};var waitingClock=new CountingClock{Value=Time(11,59)};
            var holding=Task.Run(()=>new NoShowService(db,held).ProcessExpired());Assert(held.Entered.Wait(10000),"Writer gate did not acquire lock.");
            using(var startedGate=new ManualResetEventSlim(false)){var waiting=Task.Run(()=>{startedGate.Set();try{new RoomSessionService(db,waitingClock).CreateWalkIn(staff,Request(1,"0904444444"));return false;}catch(ArgumentException){return true;}});Assert(startedGate.Wait(10000) && waitingClock.Reads==0,"Clock sampled before writer.");waitingClock.Value=Time(12);held.Release.Set();Task.WaitAll(holding,waiting);Assert(waiting.Result && waitingClock.Reads==1 && Count(db,"SELECT COUNT(*) FROM Customers")==3,"Waited walk-in used stale time or left customer.");}held.Entered.Dispose();held.Release.Dispose();
            auth.Logout(staff);Reject<UnauthorizedAccessException>(()=>service.CreateWalkIn(staff,Request(1,"0904444444")));Reject<UnauthorizedAccessException>(()=>service.ReadWalkIn(staff,1));Assert(Count(db,"PRAGMA user_version")== SqliteDatabase.CurrentSchemaVersion,"Walk-in changed schema.");
            Console.WriteLine("PASS WalkIn: current independent permission, normalized/new/old customer, exact shifts/grace/actual UTC ticks, Active and arrived bookings, snapshots/dynamic read-only deadline/calendar/future booking, atomic rollback and competing Room/Customer/check-in/booking/lock/NoShow writers/clock after lock on temporary SQLite.");
        }
        finally{SQLiteConnection.ClearAllPools();foreach(var suffix in new[]{"","-wal","-shm","-journal"})if(File.Exists(file+suffix))File.Delete(file+suffix);}
    }
}
