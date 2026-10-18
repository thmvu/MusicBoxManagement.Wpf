using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxMySqlReservationChecks
{
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } }
    private sealed class HookClock : IClock { public Func<DateTimeOffset> Read; public DateTimeOffset UtcNow { get { return Read(); } } }
    private static DateTimeOffset Time(int h,int m=0) { return new DateTimeOffset(2026,10,5,h,m,0,TimeSpan.FromHours(7)); }
    private static string Utc(DateTimeOffset t) { return t.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture); }
    private static void Assert(bool value,string message) { if(!value)throw new Exception(message); }
    private static void Reject<T>(Action action) where T:Exception { try { action(); } catch(T) {return;} throw new Exception("Expected "+typeof(T).Name); }
    private static object Sql(MySqlDatabase db,string sql) { using(var c=db.OpenConnection())using(var q=c.CreateCommand()){q.CommandText=sql;return q.ExecuteScalar();} }
    private static long Count(MySqlDatabase db,string sql) { return Convert.ToInt64(Sql(db,sql)); }
    private static ReservationRequest Input(int room,string phone,DateTimeOffset start) {return new ReservationRequest{RoomId=room,PhoneNumber=phone,FullName=" Khách mới ",StartTime=start,DurationMinutes=60};}
    private static void Clear(MySqlDatabase db) {Sql(db,"DELETE FROM RoomSessions; DELETE FROM Reservations; DELETE FROM Customers; DELETE FROM AuditLog;");}
    private static int Seed(MySqlDatabase db,DateTimeOffset start,string phone="0912345678",int room=1)
    {
        Sql(db,"INSERT INTO Customers(FullName,PhoneNumber) VALUES('Khách cũ','"+phone+"') ON DUPLICATE KEY UPDATE CustomerId=CustomerId;");
        return Convert.ToInt32(Sql(db,"INSERT INTO Reservations(RoomId,CustomerId,StartTime,EndTime,Status,CreatedAt) SELECT "+room+",CustomerId,'"+Utc(start)+"','"+Utc(start.AddHours(1))+"','Confirmed','"+Utc(Time(9))+"' FROM Customers WHERE PhoneNumber='"+phone+"'; SELECT LAST_INSERT_ID();"));
    }
    private static string Status(MySqlDatabase db,int id) {return (string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+id);}
    private static void Race(MySqlDatabase db,Func<Reservation> left,Func<Reservation> right,string message)
    {
        using(var gate=new ManualResetEventSlim(false))
        {
            Func<Func<Reservation>,Task<bool>> run=action=>Task.Run(()=>{gate.Wait();try{action();return true;}catch(InvalidOperationException){return false;}});
            var a=run(left);var b=run(right);gate.Set();Task.WaitAll(a,b);
            Assert(a.Result!=b.Result&&Count(db,"SELECT COUNT(*) FROM Reservations;")==1,message);
        }
    }
    public static void Run()
    {
        const string password="Reservation-Test-2026!";
        var name="musicbox_test_"+Guid.NewGuid().ToString("N");var db=new MySqlDatabase(name);
        var clock=new Clock{UtcNow=Time(9)}; var service=new MySqlReservationService(db,clock);var worker=new MySqlNoShowService(db,clock);
        try
        {
            var auth=new MySqlAuthenticationService(db);var admin=auth.SetupAdminAsync("admin","Admin",password).GetAwaiter().GetResult();
            Sql(db,"INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('R1','Room 1',1,'test.png',1,'"+Utc(Time(9))+"'),('R2','Room 2',2,'test.png',1,'"+Utc(Time(9))+"');");
            Sql(db,"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'staff','staff','STAFF',PasswordHash,'staff','Staff',1 FROM AspNetUsers LIMIT 1; INSERT INTO AspNetUserRoles VALUES('staff','Staff'); DELETE FROM RolePermission WHERE RoleId='Staff'; INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Reservation.Create';");
            var staff=auth.LoginAsync("staff",password).GetAwaiter().GetResult();
            Reject<UnauthorizedAccessException>(()=>service.CreateStaff(null,Input(1,"0912345678",Time(13))));
            Reject<ArgumentException>(()=>service.CreateGuest(null));
            foreach(var phone in new[]{"abc","091234567","+840912345678"})Reject<ArgumentException>(()=>service.CreateGuest(Input(1,phone,Time(13))));
            foreach(var badName in new[]{null," ",new string('x',101)}) {var r=Input(1,"0912345678",Time(13));r.FullName=badName;Reject<ArgumentException>(()=>service.CreateGuest(r));}
            foreach(var start in new[]{Time(8),Time(12),Time(11,30),Time(23),Time(13).AddTicks(1),Time(13).AddDays(31)})Reject<ArgumentException>(()=>service.CreateGuest(Input(1,"0912345678",start)));
            var invalid=Input(1,"0912345678",Time(13)); invalid.DurationMinutes=30;Reject<ArgumentException>(()=>service.CreateGuest(invalid));
            Assert(Count(db,"SELECT COUNT(*) FROM Customers;")==0,"Failed validation leaked Customer.");
            Reject<InvalidOperationException>(()=>service.CreateGuest(Input(999,"0912345678",Time(13))));
            Sql(db,"UPDATE Rooms SET IsActive=0,InactiveReason='Test' WHERE RoomId=1;");Reject<InvalidOperationException>(()=>service.CreateGuest(Input(1,"0912345678",Time(13))));Sql(db,"UPDATE Rooms SET IsActive=1,InactiveReason=NULL WHERE RoomId=1;");
            var guest=service.CreateGuest(Input(1,"+84 912.345-678",Time(13)));
            Assert(guest.Status=="Confirmed"&&guest.CreatedByUserId==null&&guest.StartTime.Offset==TimeSpan.Zero&&guest.CreatedAt==Time(9)&&guest.EndTime==Time(14),"Guest identity/time/status mismatch.");
            Assert(Count(db,"SELECT COUNT(*) FROM AuditLog WHERE ActorType='Guest' AND UserId IS NULL;")==2,"Guest customer/reservation actor mismatch.");
            var reused=Input(2,"0912345678",Time(14));reused.FullName="Tên mới không được ghi đè";
            var staffBooking=service.CreateStaff(staff,reused);
            Assert(staffBooking.CustomerId==guest.CustomerId&&staffBooking.CreatedByUserId==staff.UserId&&(string)Sql(db,"SELECT FullName FROM Customers LIMIT 1;")=="Khách mới","Create-only Staff or customer reuse failed.");
            Reject<InvalidOperationException>(()=>service.CreateGuest(Input(2,"0912345678",Time(13))));
            Reject<InvalidOperationException>(()=>service.CreateGuest(Input(1,"0987654321",Time(13))));
            Assert(Count(db,"SELECT COUNT(*) FROM Customers;")==1,"Overlap failure leaked new customer.");
            Reject<UnauthorizedAccessException>(()=>service.CancelStaff(staff,guest.ReservationId,"Test"));
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff'; INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Reservation.Cancel';");
            Reject<UnauthorizedAccessException>(()=>service.CreateStaff(staff,Input(2,"0987654321",Time(16))));
            Reject<ArgumentException>(()=>service.CancelStaff(staff,guest.ReservationId," "));
            Reject<ArgumentException>(()=>service.CancelStaff(staff,guest.ReservationId,new string('x',501)));
            service.CancelStaff(staff,guest.ReservationId,"  Khách yêu cầu  ");
            Assert(Status(db,guest.ReservationId)=="Cancelled"&&(string)Sql(db,"SELECT CancellationReason FROM Reservations WHERE ReservationId="+guest.ReservationId)=="Khách yêu cầu","Cancel-only permission/reason failed.");
            Reject<InvalidOperationException>(()=>service.CancelStaff(staff,guest.ReservationId,"Repeat"));Reject<InvalidOperationException>(()=>service.CancelStaff(admin,9999,"Missing"));
            Clear(db);clock.UtcNow=Time(9);
            var old=Seed(db,Time(10));clock.UtcNow=Time(10,15).AddTicks(-1);Assert(worker.ProcessExpired()==0&&Status(db,old)=="Confirmed","Grace ended early.");
            clock.UtcNow=Time(10,15);Reject<InvalidOperationException>(()=>service.CancelStaff(admin,old,"At grace"));
            Assert(worker.ProcessExpired()==1&&Status(db,old)=="NoShow"&&worker.ProcessExpired()==0,"NoShow exact grace/idempotence failed.");
            Assert(Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.NoShow' AND ActorType='System' AND UserId IS NULL;")==1,"NoShow actor/duplicate log failed.");
            Clear(db);old=Seed(db,Time(10));Sql(db,"INSERT INTO RoomSessions(RoomId,CustomerId,ReservationId,ActualStartTime,ExpectedEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) SELECT RoomId,CustomerId,ReservationId,'"+Utc(Time(10))+"','"+Utc(Time(11))+"',100000,'R1','STANDARD','Standard','Active' FROM Reservations WHERE ReservationId="+old);
            Assert(worker.ProcessExpired()==0&&Status(db,old)=="Confirmed","Source session not excluded.");Reject<InvalidOperationException>(()=>service.CancelStaff(admin,old,"Source exists"));
            Clear(db);old=Seed(db,Time(10));
            Sql(db,"CREATE TRIGGER FailReservationAudit BEFORE INSERT ON AuditLog FOR EACH ROW BEGIN IF NEW.EntityName='Reservation' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='test failure'; END IF; END");
            Reject<MySqlException>(()=>worker.ProcessExpired());Assert(Status(db,old)=="Confirmed","NoShow audit failure did not rollback.");
            clock.UtcNow=Time(9);Reject<MySqlException>(()=>service.CancelStaff(admin,old,"Rollback"));Assert(Status(db,old)=="Confirmed","Cancel audit failure did not rollback.");
            Reject<MySqlException>(()=>service.CreateGuest(Input(2,"0987654321",Time(15))));Assert(Count(db,"SELECT COUNT(*) FROM Customers;")==1&&Count(db,"SELECT COUNT(*) FROM Reservations;")==1,"Create audit rollback failed.");
            Sql(db,"DROP TRIGGER FailReservationAudit;");clock.UtcNow=Time(10,15);
            Reject<InvalidOperationException>(()=>service.CreateGuest(Input(999,"0987654321",Time(15))));Assert(Status(db,old)=="Confirmed"&&Count(db,"SELECT COUNT(*) FROM AuditLog;")==0,"Failed create committed maintenance NoShow.");
            var after=service.CreateGuest(Input(1,"0912345678",Time(10,30)));Assert(Status(db,old)=="NoShow"&&after.Status=="Confirmed","Create did not process stale NoShow atomically.");
            // Two actual authorized booking writers, not SQL overlap fixtures.
            Clear(db);clock.UtcNow=Time(9);Sql(db,"INSERT INTO Customers(FullName,PhoneNumber) VALUES('A','0912345678'),('B','0987654321');");
            Race(db,()=>service.CreateGuest(Input(1,"0912345678",Time(15))),()=>service.CreateGuest(Input(1,"0987654321",Time(15))),"Two bookings won same Room.");
            Clear(db);Sql(db,"INSERT INTO Customers(FullName,PhoneNumber) VALUES('A','0912345678');");
            Race(db,()=>service.CreateGuest(Input(1,"0912345678",Time(15))),()=>service.CreateGuest(Input(2,"0912345678",Time(15))),"Two rooms accepted same Customer overlap.");
            Clear(db);
            Race(db,()=>service.CreateGuest(Input(1,"+84 912345678",Time(15))),()=>service.CreateGuest(Input(2,"0912345678",Time(15))),"New canonical phone race created duplicates/overlap.");
            Assert(Count(db,"SELECT COUNT(*) FROM Customers;")==1,"Concurrent phone creation duplicated Customers.");
            // Waiting for Room lock cannot freeze now before the start passes.
            Clear(db);
            using(var called=new ManualResetEventSlim(false))using(var started=new ManualResetEventSlim(false))using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))
            {
                MySqlAvailabilityService.LockResources(c,tx,1,null);
                var delayed=new MySqlReservationService(db,new HookClock{Read=()=>{called.Set();return Time(15).AddTicks(1);}});
                var wait=Task.Run(()=>{started.Set();Reject<ArgumentException>(()=>delayed.CreateGuest(Input(1,"0912345678",Time(15))));});
                started.Wait();Assert(!called.Wait(250),"Clock read before Room lock.");tx.Commit();wait.GetAwaiter().GetResult();Assert(called.IsSet&&Count(db,"SELECT COUNT(*) FROM Customers;")==0,"Late clock validation leaked data.");
            }
            // Waiting on NoShow's reservation row must also precede Create's clock.
            Clear(db);old=Seed(db,Time(10));
            using(var called=new ManualResetEventSlim(false))using(var started=new ManualResetEventSlim(false))using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))using(var q=c.CreateCommand())
            {
                q.Transaction=tx;q.CommandText="SELECT ReservationId FROM Reservations WHERE ReservationId="+old+" FOR UPDATE";q.ExecuteScalar();
                var delayed=new MySqlReservationService(db,new HookClock{Read=()=>{called.Set();return Time(15).AddTicks(1);}});
                var wait=Task.Run(()=>{started.Set();Reject<ArgumentException>(()=>delayed.CreateGuest(Input(2,"0987654321",Time(15))));});
                started.Wait();Assert(!called.Wait(250),"Clock read before NoShow candidate locks.");tx.Commit();wait.GetAwaiter().GetResult();Assert(called.IsSet&&Status(db,old)=="Confirmed","Create stale clock/rollback failure after NoShow lock wait.");
            }
            // Worker and cancellation also obtain their own clocks after blocked row locks.
            using(var called=new ManualResetEventSlim(false))using(var started=new ManualResetEventSlim(false))using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))using(var q=c.CreateCommand())
            {
                q.Transaction=tx;q.CommandText="SELECT ReservationId FROM Reservations WHERE ReservationId="+old+" FOR UPDATE";q.ExecuteScalar();
                var delayed=new MySqlReservationService(db,new HookClock{Read=()=>{called.Set();return Time(10,15);}});
                var wait=Task.Run(()=>{started.Set();Reject<InvalidOperationException>(()=>delayed.CancelStaff(admin,old,"After wait"));});
                started.Wait();Assert(!called.Wait(250),"Cancel clock read before Reservation lock.");tx.Commit();wait.GetAwaiter().GetResult();Assert(called.IsSet&&Status(db,old)=="Confirmed","Cancel accepted expired grace after lock wait.");
            }
            using(var called=new ManualResetEventSlim(false))using(var started=new ManualResetEventSlim(false))using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))using(var q=c.CreateCommand())
            {
                q.Transaction=tx;q.CommandText="SELECT ReservationId FROM Reservations WHERE ReservationId="+old+" FOR UPDATE";q.ExecuteScalar();
                var delayed=new MySqlNoShowService(db,new HookClock{Read=()=>{called.Set();return Time(10,15).AddTicks(-1);}});
                var wait=Task.Run(()=>{started.Set();return delayed.ProcessExpired();});
                started.Wait();Assert(!called.Wait(250),"Worker clock read before Reservation lock.");tx.Commit();Assert(wait.GetAwaiter().GetResult()==0&&called.IsSet,"Worker ignored post-lock clock.");
            }
            // NoShow first, then cancel: terminal status remains NoShow.
            clock.UtcNow=Time(10,15);Assert(worker.ProcessExpired()==1,"NoShow first transition failed.");Reject<InvalidOperationException>(()=>service.CancelStaff(admin,old,"Late"));
            Clear(db);old=Seed(db,Time(10));clock.UtcNow=Time(10,14);service.CancelStaff(admin,old,"Before expiry");clock.UtcNow=Time(10,15);Assert(worker.ProcessExpired()==0&&Status(db,old)=="Cancelled","Cancel first was overwritten by NoShow.");
            // Concurrent worker replay logs once, and worker/cancel leave one terminal state.
            Clear(db);old=Seed(db,Time(10));clock.UtcNow=Time(10,15);
            using(var gate=new ManualResetEventSlim(false))
            {
                Func<Task<int>> run=()=>Task.Run(()=>{gate.Wait();return worker.ProcessExpired();});var a=run();var b=run();gate.Set();Task.WaitAll(a,b);Assert(a.Result+b.Result==1&&Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.NoShow';")==1,"Concurrent workers duplicated NoShow.");
            }
            Clear(db);old=Seed(db,Time(10));
            var cancelService=new MySqlReservationService(db,new Clock{UtcNow=Time(10,14)});
            using(var gate=new ManualResetEventSlim(false))
            {
                var a=Task.Run(()=>{gate.Wait();try{cancelService.CancelStaff(admin,old,"Race");}catch(InvalidOperationException){}});
                var b=Task.Run(()=>{gate.Wait();try{worker.ProcessExpired();}catch(InvalidOperationException){}});gate.Set();Task.WaitAll(a,b);
                var state=Status(db,old);Assert((state=="Cancelled"||state=="NoShow")&&Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action IN('Reservation.Cancel','Reservation.NoShow');")==1,"Cancel/worker race double transition.");
            }
            Clear(db);Sql(db,"UPDATE AspNetUsers SET SecurityStamp='changed' WHERE Id='staff';");Reject<UnauthorizedAccessException>(()=>service.CancelStaff(staff,old,"Stale session"));
            Sql(db,"UPDATE AspNetUsers SET IsActive=0 WHERE Id='staff';");Reject<UnauthorizedAccessException>(()=>service.CreateStaff(staff,Input(1,"0912345678",Time(15))));
            Assert(Count(db,"SELECT Version FROM _MusicBoxSchemaVersion WHERE Id=1;")==MySqlDatabase.CurrentSchemaVersion,"Schema changed.");
            Console.WriteLine("PASS MySQL Reservations: Guest/Staff Create, current permissions, canonical Customer, availability, clocks after locks, CancelStaff, NoShow/source/idempotence, rollback and real service races on GUID database.");
        }
        finally
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"\Amusicbox_test_[0-9a-f]{32}\z"))throw new Exception("Unsafe reservation cleanup.");
            Sql(new MySqlDatabase("defaultdb"),"DROP DATABASE IF EXISTS `"+name+"`");
        }
    }
}
