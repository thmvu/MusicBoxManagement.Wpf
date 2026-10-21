using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MySqlConnector;

public static class MusicBoxMySqlCheckInChecks
{
    private sealed class Clock : IClock { public DateTimeOffset Value; public int Reads; public DateTimeOffset UtcNow { get { Reads++; return Value; } } }
    private sealed class GateClock : IClock
    {
        public DateTimeOffset Value;
        public readonly ManualResetEventSlim Entered=new ManualResetEventSlim(false),Release=new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow { get { Entered.Set(); if(!Release.Wait(15000))throw new Exception("Gate timeout"); return Value; } }
    }
    private static DateTimeOffset At(int h,int m=0) { return new DateTimeOffset(2026,10,21,h,m,0,TimeSpan.FromHours(7)); }
    private static string Utc(DateTimeOffset t) { return t.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture); }
    private static object Sql(MySqlDatabase db,string sql) { using(var c=db.OpenConnection())using(var q=c.CreateCommand()){q.CommandText=sql;return q.ExecuteScalar();} }
    private static void Assert(bool ok,string message) { if(!ok)throw new Exception(message); }
    private static void Reject<T>(Action action) where T:Exception { try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name); }
    private static Reservation Book(MySqlDatabase db,int room,string phone,int hour,int duration=60)
    { return new MySqlReservationService(db,new Clock{Value=At(9)}).CreateGuest(new ReservationRequest{RoomId=room,FullName="Khách thử",PhoneNumber=phone,StartTime=At(hour),DurationMinutes=duration}); }
    private static void Complete(MySqlDatabase db)
    { Sql(db,"UPDATE RoomSessions SET Status='Completed',ActualEndTime=COALESCE(ExpectedEndTime,ActualStartTime) WHERE Status='Active';UPDATE Reservations SET Status='Completed' WHERE Status='CheckedIn';"); }
    private static void Compete(MySqlRoomSessionService service,LoginSession actor,int first,int second)
    {
        using(var start=new ManualResetEventSlim(false))
        {
            Func<int,bool> attempt=id=>{start.Wait();try{service.CheckIn(actor,id);return true;}catch(InvalidOperationException){return false;}};
            var a=Task.Run(()=>attempt(first));var b=Task.Run(()=>attempt(second));start.Set();Task.WaitAll(a,b);
            Assert(a.Result!=b.Result,"Concurrent Room/Customer check-in did not have exactly one winner");
        }
    }
    public static void Run()
    {
        var name="musicbox_test_"+Guid.NewGuid().ToString("N"); var db=new MySqlDatabase(name); GateClock gate=null;
        try
        {
            const string password="CheckIn-MySql-2026!";
            var auth=new MySqlAuthenticationService(db); var admin=auth.SetupAdminAsync("admin","Admin",password).GetAwaiter().GetResult();
            Sql(db,@"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'staff','staff','STAFF',PasswordHash,'stamp','Staff',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('staff','Staff'); DELETE FROM RolePermission WHERE RoleId='Staff';
INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.CheckIn';");
            for(var r=1;r<=12;r++)Sql(db,"INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,CreatedAt) VALUES("+r+",'R"+r+"',1,'Phòng','test.png',1,'"+Utc(At(9))+"');");
            var staff=auth.LoginAsync("staff",password).GetAwaiter().GetResult();
            var clock=new Clock{Value=At(10).AddMinutes(7).AddSeconds(12).AddTicks(1234567)}; var service=new MySqlRoomSessionService(db,clock);
            var booking=Book(db,1,"0912345678",13);
            Reject<UnauthorizedAccessException>(()=>service.CheckIn(null,booking.ReservationId));
            Reject<InvalidOperationException>(()=>service.CheckIn(staff,999));
            clock.Reads=0; var first=service.CheckIn(staff,booking.ReservationId);
            Assert(clock.Reads==1&&first.ActualStartTime==clock.Value&&first.ExpectedEndTime==clock.Value.AddHours(1),"Clock/ticks/early full duration lost");
            Assert(first.CustomerId==booking.CustomerId&&first.RoomCodeSnapshot=="R1"&&first.HourlyRate>0,"Snapshot/customer lost");
            Assert((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+booking.ReservationId)=="CheckedIn","Source not checked in");
            Assert((string)Sql(db,"SELECT StartTime FROM Reservations WHERE ReservationId="+booking.ReservationId)==Utc(booking.StartTime),"Original booking time changed");
            Sql(db,"UPDATE RoomTypes SET Name='Đã đổi',PricePerHour=777777 WHERE RoomTypeId=1;");
            var replay=service.CheckIn(staff,booking.ReservationId);Assert(replay.RoomSessionId==first.RoomSessionId&&replay.HourlyRate==first.HourlyRate&&clock.Reads==1,"Replay changed snapshot/clock");
            Complete(db); Assert(service.CheckIn(staff,booking.ReservationId).Status=="Completed","Completed replay lost");
            Assert(Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Session.CheckIn'"))==1,"Replay duplicate audit");
            var late=Book(db,2,"0900000002",14); clock.Value=At(14,15).AddTicks(-1); service.CheckIn(staff,late.ReservationId); Complete(db);
            var expired=Book(db,3,"0900000003",14);clock.Value=At(14,15);
            var before=Sql(db,"SELECT COUNT(*) FROM AuditLog;");Reject<InvalidOperationException>(()=>service.CheckIn(staff,expired.ReservationId));
            Assert((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+expired.ReservationId)=="Confirmed"&&Equals(before,Sql(db,"SELECT COUNT(*) FROM AuditLog;")),"Failed grace wrote NoShow/audit");
            new MySqlNoShowService(db,clock).ProcessExpired(); Reject<InvalidOperationException>(()=>service.CheckIn(staff,expired.ReservationId));
            var lunch=Book(db,4,"0900000004",11);clock.Value=At(11,10);Reject<InvalidOperationException>(()=>service.CheckIn(staff,lunch.ReservationId));
            clock.Value=At(11);Assert(service.CheckIn(staff,lunch.ReservationId).ExpectedEndTime==At(12),"Exact shift end rejected");Complete(db);
            var blocked=Book(db,5,"0900000005",15);var sameRoom=Book(db,5,"0900000006",16);var sameCustomer=Book(db,6,"0900000005",17);
            clock.Value=At(14);service.CheckIn(staff,blocked.ReservationId);Reject<InvalidOperationException>(()=>service.CheckIn(staff,sameRoom.ReservationId));Reject<InvalidOperationException>(()=>service.CheckIn(staff,sameCustomer.ReservationId));Complete(db);
            var overlap=Book(db,7,"0900000007",15);Book(db,7,"0900000008",16);clock.Value=At(15,10);Reject<InvalidOperationException>(()=>service.CheckIn(staff,overlap.ReservationId));
            Sql(db,"UPDATE Rooms SET IsActive=0,InactiveReason='Bảo trì' WHERE RoomId=7;");clock.Value=At(15);Reject<InvalidOperationException>(()=>service.CheckIn(staff,overlap.ReservationId));
            var rollback=Book(db,8,"0900000009",18);clock.Value=At(18);
            Sql(db,"CREATE TRIGGER FailCheckIn BEFORE INSERT ON AuditLog FOR EACH ROW BEGIN IF NEW.Action='Session.CheckIn' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='test audit failure'; END IF; END;");
            Reject<MySqlException>(()=>service.CheckIn(staff,rollback.ReservationId));
            Assert(Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM RoomSessions WHERE ReservationId="+rollback.ReservationId))==0&&(string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+rollback.ReservationId)=="Confirmed","Audit rollback left session/status");Sql(db,"DROP TRIGGER FailCheckIn;");
            var parallel=Book(db,9,"0900000010",20);clock.Value=At(19,37).AddSeconds(12);
            using(var start=new ManualResetEventSlim(false))
            {
                var a=Task.Run(()=>{start.Wait();return service.CheckIn(staff,parallel.ReservationId);});var b=Task.Run(()=>{start.Wait();return service.CheckIn(staff,parallel.ReservationId);});start.Set();Task.WaitAll(a,b);
                Assert(a.Result.RoomSessionId==b.Result.RoomSessionId,"Concurrent replay created two sessions");
            }
            Complete(db);
            var race=Book(db,10,"0900000011",20);gate=new GateClock{Value=At(20,15).AddTicks(-1)};
            var checkTask=Task.Run(()=>new MySqlRoomSessionService(db,gate).CheckIn(staff,race.ReservationId));Assert(gate.Entered.Wait(15000),"Check-in gate not entered");
            var noShow=Task.Run(()=>new MySqlNoShowService(db,new Clock{Value=At(20,15)}).ProcessExpired());Thread.Sleep(150);Assert(!noShow.IsCompleted,"NoShow bypassed writer lock");gate.Release.Set();Task.WaitAll(checkTask,noShow);
            Assert((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+race.ReservationId)=="CheckedIn","NoShow overwrote check-in");Complete(db);
            var race2=Book(db,11,"0900000012",20);gate=new GateClock{Value=At(20,15)};
            var worker=Task.Run(()=>new MySqlNoShowService(db,gate).ProcessExpired());Assert(gate.Entered.Wait(15000),"NoShow gate not entered");
            var rejected=Task.Run(()=>{try{service.CheckIn(staff,race2.ReservationId);return false;}catch(InvalidOperationException){return true;}});gate.Release.Set();Task.WaitAll(worker,rejected);
            Assert(rejected.Result&&(string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+race2.ReservationId)=="NoShow","Check-in bypassed NoShow");
            var afterLock=Book(db,12,"0900000013",20);clock.Value=At(20);clock.Reads=0;
            using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))using(var q=c.CreateCommand())
            {
                q.Transaction=tx;q.CommandText="SELECT RoomId FROM Rooms WHERE RoomId=12 FOR UPDATE";q.ExecuteScalar();
                var waiting=Task.Run(()=>{try{service.CheckIn(staff,afterLock.ReservationId);return false;}catch(InvalidOperationException){return true;}});
                Thread.Sleep(200);Assert(!waiting.IsCompleted&&clock.Reads==0,"Clock read before resource lock");clock.Value=At(20,15);tx.Commit();
                Assert(waiting.GetAwaiter().GetResult()&&clock.Reads==1,"Wait through grace did not use fresh clock");
            }
            var roomA=Book(db,2,"0900000014",21);var roomB=Book(db,2,"0900000015",22);clock.Value=At(20);
            Compete(service,staff,roomA.ReservationId,roomB.ReservationId);Complete(db);
            var customerA=Book(db,3,"0900000016",21);var customerB=Book(db,4,"0900000016",22);
            Compete(service,staff,customerA.ReservationId,customerB.ReservationId);Complete(db);
            var cancelled=Book(db,1,"0900000017",21);new MySqlReservationService(db,clock).CancelStaff(admin,cancelled.ReservationId,"Khách hủy");
            Reject<InvalidOperationException>(()=>service.CheckIn(staff,cancelled.ReservationId));
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';");Reject<UnauthorizedAccessException>(()=>service.CheckIn(staff,booking.ReservationId));
            Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.CheckIn';UPDATE AspNetUsers SET SecurityStamp='new' WHERE Id='staff';");Reject<UnauthorizedAccessException>(()=>service.CheckIn(staff,booking.ReservationId));
            Assert(Convert.ToInt32(Sql(db,"SELECT Version FROM _MusicBoxSchemaVersion"))==MySqlDatabase.CurrentSchemaVersion,"Schema changed");
            Console.WriteLine("PASS MySQL CheckIn: independent live permission, early/ticks/full duration, grace/shift/overlap/Active/locked/cancelled, snapshots/replay, rollback, concurrent replay/Room/Customer, both NoShow writer orders, clock after lock, stamp; GUID DB only.");
        }
        finally
        {
            if(gate!=null)gate.Release.Set();
            if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"\Amusicbox_test_[0-9a-f]{32}\z"))throw new Exception("Unsafe cleanup");
            Sql(new MySqlDatabase("defaultdb"),"DROP DATABASE IF EXISTS `"+name+"`");
        }
    }
}
