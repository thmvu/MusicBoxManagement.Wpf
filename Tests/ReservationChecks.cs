using System;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxReservationChecks
{
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } }
    private static DateTimeOffset Time(int hour,int minute=0) { return new DateTimeOffset(2026,10,5,hour,minute,0,TimeSpan.FromHours(7)); }
    private static string Utc(DateTimeOffset time) { return time.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture); }
    private static void Assert(bool value,string message) { if(!value)throw new Exception(message); }
    private static void Reject<T>(Action action) where T:Exception
    { try{action();}catch(T){return;}throw new Exception("Expected rejection: "+typeof(T).Name); }
    private static object Sql(SqliteDatabase db,string sql)
    { using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=sql;return cmd.ExecuteScalar();} }
    private static long Count(SqliteDatabase db,string sql) { return Convert.ToInt64(Sql(db,sql)); }
    private static ReservationRequest Request(int room,int hour,string phone)
    { return new ReservationRequest{RoomId=room,StartTime=Time(hour),DurationMinutes=60,FullName=" Nguyễn An ",PhoneNumber=phone}; }
    private static void Clear(SqliteDatabase db) { Sql(db,"DELETE FROM RoomSessions; DELETE FROM Reservations;"); }
    public static void Run()
    {
        var file=Path.Combine(Path.GetTempPath(),"MusicBoxReservations_"+Guid.NewGuid().ToString("N")+".db");
        var db=new SqliteDatabase(file);var clock=new Clock{UtcNow=Time(9)};
        const string password="Reservations-Test-2026!";
        try
        {
            var auth=new AuthenticationService(db);var admin=auth.SetupAdminAsync("admin","Quản trị thử nghiệm",password).GetAwaiter().GetResult();
            Sql(db,@"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'staff','staff','STAFF',PasswordHash,'staff','Staff',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('staff','Staff');
INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('R1','Room 1',1,'test.png',1,'test'),('R2','Room 2',2,'test.png',1,'test');");
            var staff=auth.LoginAsync("staff",password).GetAwaiter().GetResult();var service=new ReservationService(db,clock);var noShow=new NoShowService(db,clock);
            var guest=service.CreateGuest(Request(1,13,"+84 912.345-678"));
            Assert(guest.Status=="Confirmed" && guest.CreatedByUserId==null && guest.EndTime==Time(14) && guest.CreatedAt==Time(9),"Guest confirmation fields incorrect.");
            Assert((string)Sql(db,"SELECT PhoneNumber FROM Customers WHERE CustomerId="+guest.CustomerId+";")=="0912345678" && Count(db,"SELECT COUNT(*) FROM AuditLog WHERE ActorType='Guest' AND UserId IS NULL;")==2,"Guest/customer normalization/audit incorrect.");
            var reuse=Request(2,15,"84912345678");reuse.FullName="Tên Guest vừa nhập";
            var oldCustomer=service.CreateGuest(reuse);
            Assert(oldCustomer.CustomerId==guest.CustomerId && (string)Sql(db,"SELECT FullName FROM Customers WHERE CustomerId="+guest.CustomerId+";")=="Nguyễn An","Booking overwrote existing customer name.");
            // Booking permission includes resolving/creating a customer, without opening Customer CRUD.
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId IN (SELECT PermissionId FROM Permission WHERE Code LIKE 'Customer.%');");
            var staffBooking=service.CreateStaff(staff,Request(1,16,"0987654321"));
            Assert(staffBooking.CreatedByUserId==staff.UserId && Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.Create' AND ActorType='Staff' AND UserId='staff';")==1,"Staff booking requires unrelated CRUD or lost actor.");
            Reject<UnauthorizedAccessException>(()=>service.CreateStaff(null,Request(2,16,"0900000000")));
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Reservation.Create');");
            Reject<UnauthorizedAccessException>(()=>service.CreateStaff(staff,Request(2,16,"0900000000")));
            auth.Logout(staff);Reject<UnauthorizedAccessException>(()=>service.CreateStaff(staff,Request(2,16,"0900000000")));
            Reject<ArgumentException>(()=>service.CreateGuest(null));
            var invalid=Request(2,17,"0900000000");invalid.FullName=" ";Reject<ArgumentException>(()=>service.CreateGuest(invalid));
            invalid.FullName=new string('x',101);Reject<ArgumentException>(()=>service.CreateGuest(invalid));
            invalid.FullName="A";invalid.PhoneNumber="bad";Reject<ArgumentException>(()=>service.CreateGuest(invalid));
            invalid.PhoneNumber="0900000000";invalid.DurationMinutes=30;Reject<ArgumentException>(()=>service.CreateGuest(invalid));
            var beforeCustomers=Count(db,"SELECT COUNT(*) FROM Customers;");var beforeAudit=Count(db,"SELECT COUNT(*) FROM AuditLog;");
            Reject<InvalidOperationException>(()=>service.CreateGuest(Request(1,13,"0900000000")));
            Reject<InvalidOperationException>(()=>service.CreateGuest(Request(2,13,"0912345678")));
            Reject<InvalidOperationException>(()=>service.CreateGuest(Request(999,17,"0900000000")));
            Sql(db,"UPDATE Rooms SET IsActive=0,InactiveReason='Test' WHERE RoomId=2;");
            Reject<InvalidOperationException>(()=>service.CreateGuest(Request(2,17,"0900000000")));
            Sql(db,"UPDATE Rooms SET IsActive=1,InactiveReason=NULL WHERE RoomId=2;");
            Assert(Count(db,"SELECT COUNT(*) FROM Customers;")==beforeCustomers && Count(db,"SELECT COUNT(*) FROM AuditLog;")==beforeAudit,"Rejected booking left new customer/audit.");
            service.CreateGuest(Request(1,14,"0912345678"));
            // Audit failure must roll back reservation, newly resolved customer and all logs.
            Sql(db,"CREATE TRIGGER FailBookingAudit BEFORE INSERT ON AuditLog WHEN NEW.Action='Reservation.Create' BEGIN SELECT RAISE(ABORT,'test failure'); END;");
            var beforeBookings=Count(db,"SELECT COUNT(*) FROM Reservations;");beforeAudit=Count(db,"SELECT COUNT(*) FROM AuditLog;");
            Reject<SQLiteException>(()=>service.CreateGuest(Request(2,18,"0900000000")));
            Assert(Count(db,"SELECT COUNT(*) FROM Reservations;")==beforeBookings && Count(db,"SELECT COUNT(*) FROM Customers;")==beforeCustomers && Count(db,"SELECT COUNT(*) FROM AuditLog;")==beforeAudit,"Create/audit rollback failed.");
            Sql(db,"DROP TRIGGER FailBookingAudit;");Clear(db);
            var expired=service.CreateGuest(Request(1,10,"0912345678"));
            clock.UtcNow=Time(10,15).AddTicks(-1);Assert(noShow.ProcessExpired()==0,"NoShow before boundary.");
            clock.UtcNow=Time(10,15);
            Sql(db,"CREATE TRIGGER FailNoShowAudit BEFORE INSERT ON AuditLog WHEN NEW.Action='Reservation.NoShow' BEGIN SELECT RAISE(ABORT,'test failure'); END;");
            Reject<SQLiteException>(()=>noShow.ProcessExpired());
            Assert((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+expired.ReservationId+";")=="Confirmed","NoShow audit failure left status changed.");
            Sql(db,"DROP TRIGGER FailNoShowAudit;");
            var afterExpire=Request(1,10,"0912345678");afterExpire.StartTime=Time(10,30);
            var replacement=service.CreateGuest(afterExpire);
            Assert((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+expired.ReservationId+";")=="NoShow" && replacement.Status=="Confirmed","Create did not process overdue booking in its transaction.");
            Assert(Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.NoShow' AND ActorType='System' AND UserId IS NULL;")==1 && noShow.ProcessExpired()==0,"NoShow actor/idempotence failed.");
            Clear(db);clock.UtcNow=Time(9);expired=service.CreateGuest(Request(1,10,"0912345678"));clock.UtcNow=Time(10,15);
            // If the replacement fails, the combined NoShow/create transaction also rolls back.
            var fail=Request(999,11,"0900000000");Reject<InvalidOperationException>(()=>service.CreateGuest(fail));
            Assert((string)Sql(db,"SELECT Status FROM Reservations LIMIT 1;")=="Confirmed","Failed booking partially committed maintenance.");
            Assert(noShow.ProcessExpired()==1,"Separate maintenance could not recover expired status.");
            Clear(db);clock.UtcNow=Time(9);
            var booked=service.CreateGuest(Request(1,10,"0912345678"));
            Sql(db,"UPDATE Reservations SET Status='CheckedIn'; INSERT INTO RoomSessions(RoomId,CustomerId,ReservationId,ActualStartTime,ExpectedEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES(1,"+booked.CustomerId+","+booked.ReservationId+",'"+Utc(Time(10))+"','"+Utc(Time(11))+"',100000,'R1','STANDARD','Standard','Active');");
            clock.UtcNow=Time(10,15);Assert(noShow.ProcessExpired()==0,"CheckedIn/session was marked NoShow.");
            Clear(db);clock.UtcNow=Time(9);
            using(var gate=new ManualResetEventSlim(false))
            {
                Func<int,string,Task<bool>> attempt=(room,phone)=>Task.Run(()=>{gate.Wait();try{service.CreateGuest(Request(room,20,phone));return true;}catch(InvalidOperationException){return false;}});
                var a=attempt(1,"0901111111");var b=attempt(1,"0902222222");gate.Set();Task.WaitAll(a,b);
                Assert(a.Result!=b.Result && Count(db,"SELECT COUNT(*) FROM Reservations;")==1 && Count(db,"SELECT COUNT(*) FROM Customers WHERE PhoneNumber IN ('0901111111','0902222222');")==1,"Two requests booked one room or left loser customer.");
            }
            Clear(db);
            using(var gate=new ManualResetEventSlim(false))
            {
                Func<int,string,Task<bool>> attempt=(room,phone)=>Task.Run(()=>{gate.Wait();try{service.CreateGuest(Request(room,20,phone));return true;}catch(InvalidOperationException){return false;}});
                var a=attempt(1,"0903333333");var b=attempt(2,"+84 903 333 333");gate.Set();Task.WaitAll(a,b);
                Assert(a.Result!=b.Result && Count(db,"SELECT COUNT(*) FROM Reservations;")==1 && Count(db,"SELECT COUNT(*) FROM Customers WHERE PhoneNumber='0903333333';")==1,"Same customer concurrently booked two rooms.");
            }
            Clear(db);clock.UtcNow=Time(9);booked=service.CreateGuest(Request(1,10,"0912345678"));clock.UtcNow=Time(10,15);
            var noShowCount=Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.NoShow';");
            var taskA=Task.Run(()=>noShow.ProcessExpired());var taskB=Task.Run(()=>noShow.ProcessExpired());Task.WaitAll(taskA,taskB);
            Assert(taskA.Result+taskB.Result==1 && Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.NoShow';")==noShowCount+1,"Concurrent maintenance double logged.");
            Clear(db);clock.UtcNow=Time(9);
            var rooms=new RoomService(db,clock);var original=rooms.GetForEdit(admin,1);
            using(var gate=new ManualResetEventSlim(false))
            {
                var lockTask=Task.Run(()=>{gate.Wait();try{rooms.Update(admin,original,new RoomEdit{Name=original.Name,Description=original.Description,IsActive=false,InactiveReason="Test lock"});return true;}catch(InvalidOperationException){return false;}});
                var bookTask=Task.Run(()=>{gate.Wait();try{service.CreateGuest(Request(1,20,"0912345678"));return true;}catch(InvalidOperationException){return false;}});
                gate.Set();Task.WaitAll(lockTask,bookTask);
                Assert(lockTask.Result!=bookTask.Result && !(Count(db,"SELECT IsActive FROM Rooms WHERE RoomId=1;")==0 && Count(db,"SELECT COUNT(*) FROM Reservations;")>0),"Room lock and actual booking both committed.");
            }
            Assert(Count(db,"PRAGMA user_version;")==6 && Count(db,"SELECT COUNT(*) FROM AspNetUsers;")==2,"Booking changed schema or created Guest account.");
            Console.WriteLine("PASS Reservations: Guest/Staff, customer reuse, live permission, UTC/Confirmed/audit, rollback, NoShow tick boundaries/idempotence and concurrent room/customer bookings on temporary SQLite.");
        }
        finally {SQLiteConnection.ClearAllPools();if(File.Exists(file))File.Delete(file);}
    }
}
