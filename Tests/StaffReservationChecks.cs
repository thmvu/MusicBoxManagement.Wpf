using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

public static class MusicBoxStaffReservationChecks
{
    private sealed class Clock : IClock { public DateTimeOffset UtcNow {get;set;} }
    private static DateTimeOffset Time(int hour,int minute=0){return new DateTimeOffset(2026,10,5,hour,minute,0,TimeSpan.FromHours(7));}
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject<T>(Action action) where T:Exception {try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static object Sql(SqliteDatabase db,string sql){using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=sql;return cmd.ExecuteScalar();}}
    private static long Count(SqliteDatabase db,string sql){return Convert.ToInt64(Sql(db,sql));}
    private static ReservationRequest Request(int room,int hour,string phone){return new ReservationRequest{RoomId=room,StartTime=Time(hour),DurationMinutes=60,FullName="Khách thử nghiệm",PhoneNumber=phone};}
    public static void Run()
    {
        var file=Path.Combine(Path.GetTempPath(),"MusicBoxStaffReservations_"+Guid.NewGuid().ToString("N")+".db");
        var db=new SqliteDatabase(file);var clock=new Clock{UtcNow=Time(9)};const string password="StaffReservations-Test!";
        try
        {
            var auth=new AuthenticationService(db);var admin=auth.SetupAdminAsync("admin","Admin thử nghiệm",password).GetAwaiter().GetResult();
            Sql(db,@"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'staff','staff','STAFF',PasswordHash,'staff','Nhân viên thử nghiệm',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('staff','Staff');
INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('R1','Room 1',1,'test.png',1,'test'),('R2','Room 2',2,'test.png',1,'test');
DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId IN(SELECT PermissionId FROM Permission WHERE Code LIKE 'Customer.%');");
            var staff=auth.LoginAsync("staff",password).GetAwaiter().GetResult();var create=new ReservationService(db,clock);var service=new StaffReservationService(db,clock);
            var first=create.CreateGuest(Request(1,13,"0912345678"));var second=create.CreateStaff(staff,Request(1,15,"0987654321"));
            var later=Request(1,13,"0912345678");later.StartTime=later.StartTime.AddDays(1);create.CreateGuest(later);
            Reject<UnauthorizedAccessException>(()=>service.Search(null));Reject<UnauthorizedAccessException>(()=>service.Cancel(null,first.ReservationId,"Khách đổi kế hoạch"));
            var result=service.Search(staff,new DateTime(2026,10,5),new DateTime(2026,10,5));
            Assert(result.Items.Count==2 && result.CanCancel && result.Items[1].CreatedByName=="Nhân viên thử nghiệm","Protected list/date/creator required unrelated Customer permissions.");
            Assert(service.Search(staff,phoneNumber:"+84 912.345-678",status:"Confirmed").Items.Count==2,"Phone/status filter missed equivalent phone.");
            Assert(service.Search(staff,new DateTime(2026,10,6),new DateTime(2026,10,6)).Items.Count==1,"Inclusive Vietnam date filter failed.");
            Reject<ArgumentException>(()=>service.Search(staff,new DateTime(2026,10,6),new DateTime(2026,10,5)));
            Reject<ArgumentException>(()=>service.Search(staff,phoneNumber:"bad"));Reject<ArgumentException>(()=>service.Search(staff,status:"Other"));
            Reject<ArgumentException>(()=>service.Cancel(staff,first.ReservationId," "));Reject<ArgumentException>(()=>service.Cancel(staff,first.ReservationId,new string('x',501)));
            Sql(db,"CREATE TRIGGER FailStaffCancel BEFORE INSERT ON AuditLog WHEN NEW.Action='Reservation.Cancel' BEGIN SELECT RAISE(ABORT,'test failure'); END;");
            Reject<SQLiteException>(()=>service.Cancel(staff,first.ReservationId,"Khách đổi kế hoạch"));
            Assert((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+first.ReservationId)=="Confirmed" && Sql(db,"SELECT CancellationReason FROM Reservations WHERE ReservationId="+first.ReservationId)==DBNull.Value,"Audit failure committed cancellation.");
            Sql(db,"DROP TRIGGER FailStaffCancel;");clock.UtcNow=Time(12,59);
            Reject<InvalidOperationException>(()=>new GuestReservationService(db,clock).Cancel("0912345678",first.ReservationId));
            service.Cancel(staff,first.ReservationId,"  Khách đổi kế hoạch  ");
            Assert((string)Sql(db,"SELECT CancellationReason FROM Reservations WHERE ReservationId="+first.ReservationId)=="Khách đổi kế hoạch" && Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.Cancel' AND ActorType='Staff' AND UserId='staff';")==1,"Staff two-hour distinction/reason/actor incorrect.");
            Assert(service.Search(staff,status:"Cancelled").Items.Single().ReservationId==first.ReservationId,"Cancelled history hidden internally.");
            Reject<InvalidOperationException>(()=>service.Cancel(staff,first.ReservationId,"Lặp"));
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Reservation.Cancel');");
            Assert(!service.Search(staff).CanCancel,"Cancel grant cached.");Reject<UnauthorizedAccessException>(()=>service.Cancel(staff,second.ReservationId,"Không còn quyền"));
            Sql(db,"INSERT INTO RolePermission(RoleId,PermissionId) SELECT 'Staff',PermissionId FROM Permission WHERE Code='Reservation.Cancel';");
            var gate=new ManualResetEventSlim(false);var successes=0;var rejected=0;
            var attempts=Enumerable.Range(0,2).Select(i=>Task.Run(()=>{gate.Wait();try{service.Cancel(staff,second.ReservationId,"Hủy đồng thời");Interlocked.Increment(ref successes);}catch(InvalidOperationException){Interlocked.Increment(ref rejected);}})).ToArray();
            gate.Set();Task.WaitAll(attempts);gate.Dispose();Assert(successes==1 && rejected==1 && Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.Cancel';")==2,"Concurrent cancellations duplicated writes/audit.");
            clock.UtcNow=Time(9);var grace=create.CreateGuest(Request(2,13,"0900000000"));
            clock.UtcNow=Time(13,15).AddTicks(-1);service.Cancel(admin,grace.ReservationId,"Trong grace trước nhận phòng");
            clock.UtcNow=Time(9);var expired=create.CreateGuest(Request(2,15,"0900000000"));clock.UtcNow=Time(15,15);
            Reject<InvalidOperationException>(()=>service.Cancel(admin,expired.ReservationId,"Hết hạn"));
            Assert(service.Search(admin,status:"NoShow").Items.Single().ReservationId==expired.ReservationId,"Search did not process exact +15 NoShow.");
            clock.UtcNow=Time(9);var stale=create.CreateGuest(Request(2,17,"0900000000"));
            foreach(var state in new[]{"CheckedIn","Completed","NoShow","Cancelled"})
            {Sql(db,"UPDATE Reservations SET Status='"+state+"' WHERE ReservationId="+stale.ReservationId);Reject<InvalidOperationException>(()=>service.Cancel(admin,stale.ReservationId,"Không Confirmed"));}
            Sql(db,"UPDATE Reservations SET Status='Confirmed' WHERE ReservationId="+stale.ReservationId+@";
INSERT INTO RoomSessions(CustomerId,RoomId,ReservationId,ActualStartTime,ExpectedEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status)
SELECT CustomerId,RoomId,ReservationId,StartTime,EndTime,120000,'R2','VIP','VIP','Active' FROM Reservations WHERE ReservationId="+stale.ReservationId);
            Reject<InvalidOperationException>(()=>service.Cancel(admin,stale.ReservationId,"Đã có phiên"));
            Assert(!service.Search(admin).Items.Single(x=>x.ReservationId==stale.ReservationId).IsCancellable,"Source session cancellation not disabled.");
            var vm=new ReservationsViewModel(service,staff,clock);vm.SearchAsync().GetAwaiter().GetResult();
            vm.Selected=vm.Items.First(x=>x.IsCancellable);vm.RequestCancel();vm.KeepBooking();Assert(!vm.IsConfirming && vm.CanCancel,"Keep changed selection/state.");
            vm.RequestCancel();Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Reservation.Cancel');");
            vm.Reason="Quyền đã thay đổi";Assert(!vm.ConfirmCancelAsync().GetAwaiter().GetResult() && vm.Items.Count==0 && !vm.CanConfirm,"Stale VM retained private data/actions after revocation.");
            vm.SearchAsync().GetAwaiter().GetResult();Assert(vm.Items.Count>0 && !vm.CanCancel,"Read-only view failed after cancel revocation.");
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Reservation.View');");
            Reject<UnauthorizedAccessException>(()=>service.Search(staff));vm.SearchAsync().GetAwaiter().GetResult();Assert(vm.Items.Count==0 && vm.Selected==null,"View revocation retained customer data.");
            auth.Logout(staff);Reject<UnauthorizedAccessException>(()=>service.Search(staff));
            // A successful commit must not be reported as failed when maintenance blocks refresh.
            create.CreateGuest(Request(1,10,"0901111111"));
            vm=new ReservationsViewModel(service,admin,clock);vm.SearchAsync().GetAwaiter().GetResult();
            vm.Selected=vm.Items.First(x=>x.StartTime.Date==Time(9).AddDays(1).Date);vm.RequestCancel();vm.Reason="Khách báo hủy";
            Sql(db,"CREATE TRIGGER FailStaffRefresh BEFORE INSERT ON AuditLog WHEN NEW.Action='Reservation.NoShow' BEGIN SELECT RAISE(ABORT,'test failure'); END;");
            clock.UtcNow=Time(11);Assert(vm.ConfirmCancelAsync().GetAwaiter().GetResult() && vm.Items.Count==0 && vm.Status.Contains("Đã hủy") && vm.Status.Contains("chưa tải"),"Post-commit refresh failure misreported successful cancellation.");
            Sql(db,"DROP TRIGGER FailStaffRefresh;");
            clock.UtcNow=Time(9);var race=create.CreateGuest(Request(1,13,"0903333333"));
            var replacementCreated=false;gate=new ManualResetEventSlim(false);
            var cancelTask=Task.Run(()=>{gate.Wait();service.Cancel(admin,race.ReservationId,"Đổi lượt");});
            var createTask=Task.Run(()=>{gate.Wait();try{create.CreateGuest(Request(1,13,"0904444444"));replacementCreated=true;}catch(InvalidOperationException){}});
            gate.Set();Task.WaitAll(cancelTask,createTask);gate.Dispose();
            Assert(Count(db,"SELECT COUNT(*) FROM Reservations WHERE RoomId=1 AND StartTime='"+Time(13).ToUniversalTime().ToString("O")+"' AND Status='Confirmed';")==(replacementCreated?1:0),"Cancel/create race left overlapping Confirmed bookings.");
            Assert(Count(db,"SELECT COUNT(*) FROM Customers WHERE PhoneNumber='0904444444';")==(replacementCreated?1:0),"Rejected replacement left a new customer.");
            Assert(Count(db,"PRAGMA user_version;")== SqliteDatabase.CurrentSchemaVersion && (string)Sql(db,"SELECT StartTime FROM Reservations WHERE ReservationId="+first.ReservationId)==first.StartTime.ToString("O"),"Staff cancel changed schema/old interval.");
            Console.WriteLine("PASS Staff reservations: protected/current permissions, date/phone/status/details, mandatory reasons, Guest/Staff deadline difference, grace/source/state guards, audit rollback, concurrent cancellations, VM privacy and schema on temporary SQLite.");
        }
        finally{SQLiteConnection.ClearAllPools();if(File.Exists(file))File.Delete(file);}
    }
}
