using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MySqlConnector;

public static class MusicBoxMySqlWalkInChecks
{
    private sealed class Clock : IClock { public DateTimeOffset Value; public int Reads; public DateTimeOffset UtcNow { get { Reads++; return Value; } } }
    private static DateTimeOffset At(int h,int m=0) { return new DateTimeOffset(2026,10,21,h,m,0,TimeSpan.FromHours(7)); }
    private static string Utc(DateTimeOffset t) { return t.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture); }
    private static object Sql(MySqlDatabase db,string sql) { using(var c=db.OpenConnection())using(var q=c.CreateCommand()){q.CommandText=sql;return q.ExecuteScalar();} }
    private static long Count(MySqlDatabase db,string sql) { return Convert.ToInt64(Sql(db,sql)); }
    private static void Assert(bool ok,string message) { if(!ok)throw new Exception(message); }
    private static void Reject<T>(Action action) where T:Exception { try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name); }
    private static WalkInRequest Request(int room,string phone) { return new WalkInRequest{RoomId=room,FullName=" Tên gốc ",PhoneNumber=phone}; }
    private static Reservation Book(MySqlDatabase db,int room,string phone,DateTimeOffset start)
    { return new MySqlReservationService(db,new Clock{Value=At(9)}).CreateGuest(new ReservationRequest{RoomId=room,FullName="Khách booking",PhoneNumber=phone,StartTime=start,DurationMinutes=60}); }
    private static void Complete(MySqlDatabase db)
    { Sql(db,"UPDATE RoomSessions SET Status='Completed',ActualEndTime=COALESCE(ExpectedEndTime,ActualStartTime) WHERE Status='Active';UPDATE Reservations SET Status='Completed' WHERE Status='CheckedIn';"); }
    private static void Compete(Func<bool> a,Func<bool> b,bool exclusive=true,string label="writer")
    {
        using(var start=new ManualResetEventSlim(false))
        {
            var x=Task.Run(()=>{start.Wait();return a();});var y=Task.Run(()=>{start.Wait();return b();});start.Set();Task.WaitAll(x,y);
            Assert(exclusive ? x.Result!=y.Result : x.Result||y.Result,label+" unexpected winners: "+x.Result+","+y.Result);
        }
    }
    private static bool Attempt(Action action) { try{action();return true;}catch(InvalidOperationException error){Console.WriteLine("Contended writer: "+error.Message);return false;} }
    private static bool AttemptRetryable(Action action)
    {
        try { action(); return true; }
        catch(InvalidOperationException error)
        {
            var sql=error.InnerException as MySqlException;
            if(sql==null || (sql.Number!=1213 && sql.Number!=1205))throw;
            Console.WriteLine("Retryable writer conflict: "+sql.Number); return false;
        }
    }
    public static void Run()
    {
        var name="musicbox_test_"+Guid.NewGuid().ToString("N");var db=new MySqlDatabase(name);
        try
        {
            const string password="WalkIn-MySql-2026!";var auth=new MySqlAuthenticationService(db);
            var admin=auth.SetupAdminAsync("admin","Admin",password).GetAwaiter().GetResult();
            Sql(db,@"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'staff','staff','STAFF',PasswordHash,'stamp','Staff',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('staff','Staff');
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'other','other','OTHER',PasswordHash,'other','Other',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('other','Staff');
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'viewer','viewer','VIEWER',PasswordHash,'viewer','Viewer',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('viewer','Manager');DELETE FROM RolePermission WHERE RoleId IN ('Staff','Manager');
INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.WalkIn';
INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Session.View';");
            for(var r=1;r<=16;r++)Sql(db,"INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,CreatedAt) VALUES("+r+",'R"+r+"',1,'Phòng','test.png',1,'"+Utc(At(9))+"');");
            var staff=auth.LoginAsync("staff",password).GetAwaiter().GetResult();var other=auth.LoginAsync("other",password).GetAwaiter().GetResult();var viewer=auth.LoginAsync("viewer",password).GetAwaiter().GetResult();
            var clock=new Clock{Value=At(10,7).AddSeconds(12).AddTicks(1234567)};var service=new MySqlRoomSessionService(db,clock);
            var request=Request(1,"+84 912.345-678");var audit=Count(db,"SELECT COUNT(*) FROM AuditLog;");
            Assert(service.ListWalkInRooms(staff).Count==16,"WalkIn-only room list failed");clock.Reads=0;
            var preview=service.PreviewWalkIn(staff,request);
            Assert(preview.CanReceive&&preview.ReturnBy==At(12)&&clock.Reads==1&&Count(db,"SELECT COUNT(*) FROM Customers;")==0&&Count(db,"SELECT COUNT(*) FROM AuditLog;")==audit,"Preview wrote or required extra rights");
            Reject<UnauthorizedAccessException>(()=>service.ListWalkInRooms(null));Reject<UnauthorizedAccessException>(()=>service.CreateWalkIn(viewer,request));
            Reject<ArgumentException>(()=>service.CreateWalkIn(staff,null));Reject<ArgumentException>(()=>service.PreviewWalkIn(staff,Request(1,"bad")));
            var badName=Request(1,"0900000000");badName.FullName=" ";Reject<ArgumentException>(()=>service.CreateWalkIn(staff,badName));
            clock.Reads=0;var first=service.CreateWalkIn(staff,request);
            Assert(clock.Reads==1&&first.Session.ActualStartTime==clock.Value&&first.Session.ReservationId==null&&first.Session.ExpectedEndTime==null&&first.Session.ActualEndTime==null&&first.ReturnBy==At(12),"Walk-in actual/null fields/deadline failed");
            Assert((string)Sql(db,"SELECT FullName FROM Customers WHERE CustomerId="+first.Session.CustomerId)=="Tên gốc"&&(string)Sql(db,"SELECT PhoneNumber FROM Customers WHERE CustomerId="+first.Session.CustomerId)=="0912345678","Canonical customer/name failed");
            Reject<UnauthorizedAccessException>(()=>service.ReadWalkIn(staff,first.Session.RoomSessionId));Reject<UnauthorizedAccessException>(()=>service.ReadWalkInReceipt(other,first.Session.RoomSessionId));
            Assert(service.ReadWalkIn(viewer,first.Session.RoomSessionId).Session.RoomSessionId==first.Session.RoomSessionId,"View-only read failed");
            Reject<InvalidOperationException>(()=>service.CreateWalkIn(staff,request));Reject<InvalidOperationException>(()=>service.CreateWalkIn(staff,Request(1,"0900000002")));Reject<InvalidOperationException>(()=>service.CreateWalkIn(staff,Request(2,"0912345678")));
            Assert(Count(db,"SELECT COUNT(*) FROM Customers;")==1,"Rejected Active left new customer");
            Sql(db,"UPDATE RoomTypes SET Name='Đổi loại',PricePerHour=987654 WHERE RoomTypeId=1;");var receipt=service.ReadWalkInReceipt(staff,first.Session.RoomSessionId);
            Assert(receipt.Session.HourlyRate==first.Session.HourlyRate&&receipt.Session.RoomTypeNameSnapshot==first.Session.RoomTypeNameSnapshot,"Snapshot changed after catalog edit");
            var next=Book(db,1,"0900000003",At(11));Assert(service.ReadWalkInReceipt(staff,first.Session.RoomSessionId).ReturnBy==At(11),"Room deadline not dynamic");
            new MySqlReservationService(db,clock).CancelStaff(admin,next.ReservationId,"Đổi giờ");var customerNext=Book(db,3,"0912345678",At(11));
            Assert(service.ReadWalkInReceipt(staff,first.Session.RoomSessionId).ReturnBy==At(11),"Customer deadline ignored");new MySqlReservationService(db,clock).CancelStaff(admin,customerNext.ReservationId,"Hủy");
            Book(db,1,"0900000004",At(9).AddDays(1));Book(db,1,"0900000005",At(11).AddDays(1));
            Assert(service.ReadWalkInReceipt(staff,first.Session.RoomSessionId).ReturnBy==At(12),"Tomorrow booking changed original shift");
            Assert(new MySqlCalendarService(db,clock).Read(admin,CalendarRange.Day(At(9).Date),1).Single().Holds.Count==0,"Walk-in invented future hold");
            clock.Value=At(12).AddTicks(1);Assert(service.ReadWalkInReceipt(staff,first.Session.RoomSessionId).IsOverdue,"Overdue silently ended walk-in");Complete(db);
            Reject<InvalidOperationException>(()=>service.ReadWalkIn(viewer,first.Session.RoomSessionId));
            clock.Value=At(13);var reuse=Request(2,"84 912 345 678");reuse.FullName="Không ghi đè";var second=service.CreateWalkIn(staff,reuse);
            Assert(second.Session.CustomerId==first.Session.CustomerId&&(string)Sql(db,"SELECT FullName FROM Customers WHERE CustomerId="+first.Session.CustomerId)=="Tên gốc","Reuse overwrote customer");Complete(db);
            foreach(var t in new[]{At(9).AddTicks(-1),At(12),At(12,30),At(23)})
            {clock.Value=t;Assert(!service.PreviewWalkIn(staff,Request(3,"0900000006")).CanReceive,"Preview allowed closed shift");Reject<ArgumentException>(()=>service.CreateWalkIn(staff,Request(3,"0900000006")));}
            clock.Value=At(9);service.CreateWalkIn(staff,Request(3,"0900000006"));Complete(db);
            var due=Book(db,4,"0900000007",At(14));clock.Value=At(14,15).AddTicks(-1);
            Assert(!service.PreviewWalkIn(staff,Request(4,"0900000008")).CanReceive,"Due booking room allowed walk-in");Reject<InvalidOperationException>(()=>service.CreateWalkIn(staff,Request(4,"0900000008")));
            clock.Value=At(14,15);audit=Count(db,"SELECT COUNT(*) FROM AuditLog;");
            Assert(service.PreviewWalkIn(staff,Request(4,"0900000008")).CanReceive&&(string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+due.ReservationId)=="Confirmed"&&Count(db,"SELECT COUNT(*) FROM AuditLog;")==audit,"Grace preview wrote NoShow/audit");
            service.CreateWalkIn(staff,Request(4,"0900000008"));Assert((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+due.ReservationId)=="NoShow","Exact grace did not expire booking");Complete(db);
            Book(db,5,"0900000009",At(15));clock.Value=At(15);Reject<InvalidOperationException>(()=>service.CreateWalkIn(staff,Request(6,"0900000009")));
            Sql(db,"UPDATE Rooms SET IsActive=0,InactiveReason='Bảo trì' WHERE RoomId=7;");Assert(service.ListWalkInRooms(staff).Count==15&&!service.PreviewWalkIn(staff,Request(7,"0900000010")).CanReceive,"Locked room leaked in menu/preview");Reject<InvalidOperationException>(()=>service.CreateWalkIn(staff,Request(7,"0900000010")));
            var expired=Book(db,8,"0900000011",At(14));clock.Value=At(16);audit=Count(db,"SELECT COUNT(*) FROM AuditLog;");var customers=Count(db,"SELECT COUNT(*) FROM Customers;");
            Sql(db,"CREATE TRIGGER FailWalkIn BEFORE INSERT ON AuditLog FOR EACH ROW BEGIN IF NEW.Action='Session.WalkIn' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='test failure'; END IF; END;");
            Reject<MySqlException>(()=>service.CreateWalkIn(staff,Request(9,"0900000012")));
            Assert(Count(db,"SELECT COUNT(*) FROM AuditLog;")==audit&&Count(db,"SELECT COUNT(*) FROM Customers;")==customers&&(string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+expired.ReservationId)=="Confirmed"&&Count(db,"SELECT COUNT(*) FROM RoomSessions WHERE RoomId=9;")==0,"Rollback failed customer/session/maintenance/audit");Sql(db,"DROP TRIGGER FailWalkIn;");
            Compete(()=>Attempt(()=>service.CreateWalkIn(staff,Request(10,"0900000013"))),()=>Attempt(()=>service.CreateWalkIn(other,Request(10,"0900000014"))),label:"Room");Complete(db);
            Compete(()=>Attempt(()=>service.CreateWalkIn(staff,Request(11,"0900000015"))),()=>Attempt(()=>service.CreateWalkIn(other,Request(12,"+84 900 000 015"))),label:"Customer");
            Assert(Count(db,"SELECT COUNT(*) FROM Customers WHERE PhoneNumber='0900000015';")==1,"Concurrent normalized customer duplicated");Complete(db);
            var source=Book(db,13,"0900000016",At(20));clock.Value=At(19);
            Compete(()=>Attempt(()=>service.CreateWalkIn(staff,Request(13,"0900000017"))),()=>Attempt(()=>service.CheckIn(admin,source.ReservationId)),label:"CheckIn");Complete(db);
            var bookingService=new MySqlReservationService(db,clock);
            Compete(()=>Attempt(()=>service.CreateWalkIn(staff,Request(14,"0900000018"))),()=>Attempt(()=>bookingService.CreateGuest(new ReservationRequest{RoomId=14,FullName="Booking ngay",PhoneNumber="0900000019",StartTime=At(19),DurationMinutes=60})),label:"BookingNow");Complete(db);
            var roomService=new MySqlRoomService(db,Path.GetTempPath(),clock);var original=roomService.GetForEdit(admin,15);
            Compete(()=>Attempt(()=>service.CreateWalkIn(staff,Request(15,"0900000020"))),()=>Attempt(()=>roomService.Update(admin,original,new RoomEdit{Name=original.Name,IsActive=false,InactiveReason="Khóa thử"})),label:"RoomLock");Complete(db);
            var noShow=Book(db,16,"0900000021",At(19));clock.Value=At(19,15);
            Compete(()=>AttemptRetryable(()=>service.CreateWalkIn(staff,Request(16,"0900000022"))),()=>AttemptRetryable(()=>new MySqlNoShowService(db,clock).ProcessExpired()),false,"NoShow");
            // A deadlock/timeout is surfaced, not silently retried by a writer.
            // After both transactions finish, retry each unfinished operation at most once.
            if(Count(db,"SELECT COUNT(*) FROM RoomSessions WHERE RoomId=16 AND Status='Active';")==0)service.CreateWalkIn(staff,Request(16,"0900000022"));
            new MySqlNoShowService(db,clock).ProcessExpired();
            Assert((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+noShow.ReservationId)=="NoShow"&&Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.NoShow' AND EntityId='"+noShow.ReservationId+"';")==1&&Count(db,"SELECT COUNT(*) FROM RoomSessions WHERE RoomId=16;")==1,"Worker/walk-in duplicate session/NoShow");Complete(db);
            clock.Value=At(22,59);clock.Reads=0;customers=Count(db,"SELECT COUNT(*) FROM Customers;");
            using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))using(var q=c.CreateCommand())
            {
                q.Transaction=tx;q.CommandText="SELECT RoomId FROM Rooms WHERE RoomId=6 FOR UPDATE;";q.ExecuteScalar();
                var waiting=Task.Run(()=>{try{service.CreateWalkIn(staff,Request(6,"0900000023"));return false;}catch(ArgumentException){return true;}});
                Thread.Sleep(200);Assert(!waiting.IsCompleted&&clock.Reads==0,"Walk-in clock before room lock");clock.Value=At(23);tx.Commit();Assert(waiting.GetAwaiter().GetResult()&&clock.Reads==1,"Wait across close used stale clock");
            }
            Assert(Count(db,"SELECT COUNT(*) FROM Customers;")==customers,"Closed shift left customer");
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';");Reject<UnauthorizedAccessException>(()=>service.ListWalkInRooms(staff));Reject<UnauthorizedAccessException>(()=>service.PreviewWalkIn(staff,request));Reject<UnauthorizedAccessException>(()=>service.CreateWalkIn(staff,request));Reject<UnauthorizedAccessException>(()=>service.ReadWalkInReceipt(staff,first.Session.RoomSessionId));
            Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.WalkIn';UPDATE AspNetUsers SET SecurityStamp='changed' WHERE Id='staff';");Reject<UnauthorizedAccessException>(()=>service.ListWalkInRooms(staff));
            Assert(Count(db,"SELECT Version FROM _MusicBoxSchemaVersion;")==MySqlDatabase.CurrentSchemaVersion,"Schema changed");
            Console.WriteLine("PASS MySQL WalkIn: independent permissions/own receipt, read-only preview, canonical customer/snapshot/actual, shift/grace/Active/deadline/no future hold, rollback maintenance, real races Room/Customer/check-in/booking/lock/NoShow, clock after lock and revoke/stamp; GUID DB only.");
        }
        finally
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"\Amusicbox_test_[0-9a-f]{32}\z"))throw new Exception("Unsafe cleanup");
            Sql(new MySqlDatabase("defaultdb"),"DROP DATABASE IF EXISTS `"+name+"`");
        }
    }
}
