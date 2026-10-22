using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MySqlConnector;

public static class MusicBoxMySqlSessionsChecks
{
    private sealed class Clock : IClock { public DateTimeOffset Value; public int Reads; public Action Hook; public DateTimeOffset UtcNow { get { Reads++;if(Hook!=null)Hook();return Value; } } }
    private static DateTimeOffset At(int h,int m=0) { return new DateTimeOffset(2026,10,22,h,m,0,TimeSpan.FromHours(7)); }
    private static string Utc(DateTimeOffset t) { return t.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture); }
    private static object Sql(MySqlDatabase db,string sql) { using(var c=db.OpenConnection())using(var q=c.CreateCommand()){q.CommandText=sql;return q.ExecuteScalar();} }
    private static long Count(MySqlDatabase db,string sql) { return Convert.ToInt64(Sql(db,sql)); }
    private static void Assert(bool ok,string message) { if(!ok)throw new Exception(message); }
    private static void Reject<T>(Action action) where T:Exception { try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name); }
    private static Reservation Book(MySqlDatabase db,int room,string phone,int hour,int minute=0)
    { return new MySqlReservationService(db,new Clock{Value=At(9)}).CreateGuest(new ReservationRequest{RoomId=room,FullName="Khách",PhoneNumber=phone,StartTime=At(hour,minute),DurationMinutes=60}); }
    private static DateTimeOffset End(MySqlDatabase db,int id)
    { return DateTimeOffset.ParseExact((string)Sql(db,"SELECT ExpectedEndTime FROM RoomSessions WHERE RoomSessionId="+id),"O",CultureInfo.InvariantCulture); }
    private static bool Attempt(Action action) { try{action();return true;}catch(InvalidOperationException){return false;} }
    private static void Race(Func<bool> a,Func<bool> b)
    {
        using(var gate=new ManualResetEventSlim(false))
        {var x=Task.Run(()=>{gate.Wait();return a();});var y=Task.Run(()=>{gate.Wait();return b();});gate.Set();Task.WaitAll(x,y);Assert(x.Result!=y.Result,"Concurrent writers did not have exactly one winner");}
    }
    public static void Run()
    {
        var name="musicbox_test_"+Guid.NewGuid().ToString("N");var db=new MySqlDatabase(name);
        try
        {
            const string password="Sessions-MySql-2026!";var auth=new MySqlAuthenticationService(db);
            var admin=auth.SetupAdminAsync("admin","Admin",password).GetAwaiter().GetResult();
            Sql(db,@"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'extend','extend','EXTEND',PasswordHash,'stamp','Extend only',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('extend','Staff');
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'viewer','viewer','VIEWER',PasswordHash,'viewer','View only',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('viewer','Manager');DELETE FROM RolePermission WHERE RoleId IN ('Staff','Manager');
INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.Extend';
INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Session.View';");
            for(var r=1;r<=8;r++)Sql(db,"INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,CreatedAt) VALUES("+r+",'R"+r+"',1,'Phòng','test.png',1,'"+Utc(At(9))+"');");
            var extender=auth.LoginAsync("extend",password).GetAwaiter().GetResult();var viewer=auth.LoginAsync("viewer",password).GetAwaiter().GetResult();
            var clock=new Clock{Value=At(13,7).AddSeconds(12).AddTicks(1234567)};var core=new MySqlRoomSessionService(db,clock);var staff=new MySqlStaffSessionService(db,clock);
            var booking=Book(db,1,"0912345678",13);var session=core.CheckIn(admin,booking.ReservationId);
            var walk=core.CreateWalkIn(admin,new WalkInRequest{RoomId=2,FullName="Walk-in",PhoneNumber="0900000002"});
            var completedBooking=Book(db,3,"0900000003",11);var completed=new MySqlRoomSessionService(db,new Clock{Value=At(11)}).CheckIn(admin,completedBooking.ReservationId);
            Sql(db,"UPDATE RoomSessions SET Status='Completed',ActualEndTime=ExpectedEndTime WHERE RoomSessionId="+completed.RoomSessionId+";UPDATE Reservations SET Status='Completed' WHERE ReservationId="+completedBooking.ReservationId+";UPDATE Rooms SET IsActive=0,InactiveReason='Bảo trì' WHERE RoomId=3;");
            var audit=Count(db,"SELECT COUNT(*) FROM AuditLog;");clock.Reads=0;var list=staff.Search(viewer);
            Assert(clock.Reads==1&&list.Items.Count==2&&!list.CanExtend&&list.Items[0].Session.RoomSessionId==walk.Session.RoomSessionId,"View-only/order/single clock failed");
            Assert(list.Items.Single(i=>i.Session.RoomSessionId==walk.Session.RoomSessionId).ReturnBy==At(23),"Walk-in deadline missing");
            Assert(staff.Search(viewer,null,null).Items.Count==3&&staff.Search(viewer,null,"Completed").Items.Single().Session.RoomCodeSnapshot=="R3","Completed/locked history filter failed");
            Assert(staff.Search(viewer,"+84 912 345 678").Items.Single().Session.RoomSessionId==session.RoomSessionId,"Normalized phone filter failed");
            Reject<ArgumentException>(()=>staff.Search(viewer,null,"bad"));Reject<ArgumentException>(()=>staff.Search(viewer,"abc"));Reject<UnauthorizedAccessException>(()=>staff.Search(null));Reject<UnauthorizedAccessException>(()=>staff.Search(extender));
            Assert(core.PreviewExtensionStaff(extender,session.RoomSessionId,30).CanExtend,"Extend-only core preview denied");
            Reject<UnauthorizedAccessException>(()=>staff.Preview(extender,session.RoomSessionId,30));Reject<UnauthorizedAccessException>(()=>staff.Preview(viewer,session.RoomSessionId,30));
            Assert(Count(db,"SELECT COUNT(*) FROM AuditLog;")==audit,"Search/preview wrote audit");
            var old=session.ExpectedEndTime.Value;clock.Reads=0;var extended=core.ExtendStaff(extender,session.RoomSessionId,old.ToOffset(TimeSpan.FromHours(7)),30);
            Assert(clock.Reads==1&&extended.ExpectedEndTime==old.AddMinutes(30)&&extended.HourlyRate==session.HourlyRate&&extended.ActualStartTime==session.ActualStartTime,"Extension offset/ticks/snapshot/clock failed");
            Reject<InvalidOperationException>(()=>core.ExtendStaff(extender,session.RoomSessionId,old,30));
            Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.View';UPDATE RoomTypes SET Name='Giá mới',PricePerHour=999999 WHERE RoomTypeId=1;");
            Assert(staff.Search(extender).CanExtend&&staff.Preview(extender,session.RoomSessionId,60).CanExtend,"View+Extend route denied");
            extended=staff.Extend(extender,session.RoomSessionId,extended.ExpectedEndTime.Value,60);
            Assert(extended.ExpectedEndTime==old.AddMinutes(90)&&extended.RoomTypeNameSnapshot==session.RoomTypeNameSnapshot&&extended.HourlyRate==session.HourlyRate,"Viewed extension changed snapshot");
            Assert((string)Sql(db,"SELECT StartTime FROM Reservations WHERE ReservationId="+booking.ReservationId)==Utc(booking.StartTime)&&(string)Sql(db,"SELECT EndTime FROM Reservations WHERE ReservationId="+booking.ReservationId)==Utc(booking.EndTime),"Extension modified booking");
            Reject<ArgumentException>(()=>core.PreviewExtensionStaff(extender,session.RoomSessionId,45));Reject<ArgumentException>(()=>core.ExtendStaff(extender,session.RoomSessionId,extended.ExpectedEndTime.Value,45));
            Reject<InvalidOperationException>(()=>core.PreviewExtensionStaff(extender,walk.Session.RoomSessionId,30));Reject<InvalidOperationException>(()=>core.ExtendStaff(extender,walk.Session.RoomSessionId,At(23),30));
            Reject<InvalidOperationException>(()=>core.PreviewExtensionStaff(extender,completed.RoomSessionId,30));Reject<InvalidOperationException>(()=>core.ExtendStaff(extender,999,At(14),30));
            var roomNext=Book(db,1,"0900000004",16);var limit=core.PreviewExtensionStaff(extender,session.RoomSessionId,30);
            Assert(!limit.CanExtend&&limit.MaximumEndTime==At(16),"Room overlap limit lost");Reject<SessionExtensionException>(()=>core.ExtendStaff(extender,session.RoomSessionId,extended.ExpectedEndTime.Value,30));
            new MySqlReservationService(db,clock).CancelStaff(admin,roomNext.ReservationId,"Đổi giờ");
            Sql(db,"UPDATE Customers SET FullName='Tên hiện hành',PhoneNumber='0901234567' WHERE CustomerId="+session.CustomerId+";");
            Assert(staff.Search(viewer,"0912345678").Items.Count==0&&staff.Search(viewer,"0901234567").Items.Single().CustomerName=="Tên hiện hành","Current phone/name not reflected");
            var customerNext=Book(db,4,"0901234567",16);Assert(!core.PreviewExtensionStaff(extender,session.RoomSessionId,30).CanExtend,"Customer overlap ignored");new MySqlReservationService(db,clock).CancelStaff(admin,customerNext.ReservationId,"Hủy");
            var expired=Book(db,5,"0900000005",13);clock.Value=At(13,15);audit=Count(db,"SELECT COUNT(*) FROM AuditLog;");core.PreviewExtensionStaff(extender,session.RoomSessionId,30);staff.Search(viewer);
            Assert((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+expired.ReservationId)=="Confirmed"&&Count(db,"SELECT COUNT(*) FROM AuditLog;")==audit,"Read ran maintenance/audit");
            clock.Value=extended.ExpectedEndTime.Value;Assert(core.PreviewExtensionStaff(extender,session.RoomSessionId,30).CanExtend,"Exact end not extendable");
            extended=core.ExtendStaff(extender,session.RoomSessionId,extended.ExpectedEndTime.Value,30);
            clock.Value=extended.ExpectedEndTime.Value.AddTicks(1);Assert(!core.PreviewExtensionStaff(extender,session.RoomSessionId,30).CanExtend,"Overdue extension accepted");Reject<SessionExtensionException>(()=>core.ExtendStaff(extender,session.RoomSessionId,extended.ExpectedEndTime.Value,30));
            clock.Value=At(16);var observed=extended.ExpectedEndTime.Value;audit=Count(db,"SELECT COUNT(*) FROM AuditLog;");
            Sql(db,"CREATE TRIGGER FailExtend BEFORE INSERT ON AuditLog FOR EACH ROW BEGIN IF NEW.Action='Session.Extend' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='test failure'; END IF; END;");Reject<MySqlException>(()=>core.ExtendStaff(extender,session.RoomSessionId,observed,30));
            Assert(End(db,session.RoomSessionId)==observed&&Count(db,"SELECT COUNT(*) FROM AuditLog;")==audit,"Audit rollback left extension");Sql(db,"DROP TRIGGER FailExtend;");
            Race(()=>Attempt(()=>core.ExtendStaff(extender,session.RoomSessionId,observed,30)),()=>Attempt(()=>core.ExtendStaff(extender,session.RoomSessionId,observed,30)));
            Assert(End(db,session.RoomSessionId)==observed.AddMinutes(30)&&Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Session.Extend';")==4,"Duplicate extended twice or audit mismatch");
            observed=End(db,session.RoomSessionId);var nextSlot=new DateTimeOffset(observed.Year,observed.Month,observed.Day,observed.Hour,observed.Minute<30?30:0,0,TimeSpan.Zero);
            if(observed.Minute>=30)nextSlot=nextSlot.AddHours(1); // Stored UTC, round the competing booking up to a slot.
            var competing=new MySqlReservationService(db,clock);
            Race(()=>Attempt(()=>core.ExtendStaff(extender,session.RoomSessionId,observed,30)),()=>Attempt(()=>competing.CreateGuest(new ReservationRequest{RoomId=1,FullName="Khách tiếp",PhoneNumber="0900000006",StartTime=nextSlot,DurationMinutes=60})));
            var morning=Book(db,6,"0900000007",11);var morningSession=new MySqlRoomSessionService(db,new Clock{Value=At(11)}).CheckIn(admin,morning.ReservationId);clock.Value=At(11);
            Assert(!core.PreviewExtensionStaff(extender,morningSession.RoomSessionId,30).CanExtend&&core.PreviewExtensionStaff(extender,morningSession.RoomSessionId,30).MaximumEndTime==At(12),"Shift boundary ignored");
            var waitBooking=Book(db,8,"0900000008",18);var waitSession=new MySqlRoomSessionService(db,new Clock{Value=At(18)}).CheckIn(admin,waitBooking.ReservationId);clock.Value=At(18,59);clock.Reads=0;
            using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))using(var q=c.CreateCommand())
            {
                q.Transaction=tx;q.CommandText="SELECT RoomId FROM Rooms WHERE RoomId=8 FOR UPDATE;";q.ExecuteScalar();
                var waiting=Task.Run(()=>{try{core.ExtendStaff(extender,waitSession.RoomSessionId,waitSession.ExpectedEndTime.Value,30);return false;}catch(SessionExtensionException){return true;}});
                Thread.Sleep(200);Assert(!waiting.IsCompleted&&clock.Reads==0,"Extension clock before resource lock");clock.Value=At(19).AddTicks(1);tx.Commit();Assert(waiting.GetAwaiter().GetResult()&&clock.Reads==1,"Wait beyond end used stale clock");
            }
            clock.Value=At(16);clock.Hook=()=>{clock.Hook=null;Sql(db,"DELETE FROM RolePermission WHERE RoleId='Manager';UPDATE Customers SET FullName='Đổi sau snapshot' WHERE CustomerId="+session.CustomerId);};
            Assert(staff.Search(viewer,"0901234567").Items.Single().CustomerName=="Tên hiện hành","Search data escaped permission snapshot");Reject<UnauthorizedAccessException>(()=>staff.Search(viewer));
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Session.View');");
            Reject<UnauthorizedAccessException>(()=>staff.Preview(extender,session.RoomSessionId,30));Reject<UnauthorizedAccessException>(()=>staff.Extend(extender,session.RoomSessionId,End(db,session.RoomSessionId),30));core.PreviewExtensionStaff(extender,session.RoomSessionId,30);
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.View';");
            Assert(!staff.Search(extender).CanExtend,"Revoked Extend metadata remained");Reject<UnauthorizedAccessException>(()=>core.PreviewExtensionStaff(extender,session.RoomSessionId,30));Reject<UnauthorizedAccessException>(()=>core.ExtendStaff(extender,session.RoomSessionId,End(db,session.RoomSessionId),30));
            Sql(db,"UPDATE AspNetUsers SET SecurityStamp='changed' WHERE Id='extend';");Reject<UnauthorizedAccessException>(()=>staff.Search(extender));
            Assert(Count(db,"SELECT Version FROM _MusicBoxSchemaVersion;")==MySqlDatabase.CurrentSchemaVersion,"Schema changed");
            Console.WriteLine("PASS MySQL Sessions/Extension: live View/Extend independent and viewed route, protected read snapshot/current phone/filter/history/ReturnBy, +30/+60/ticks/observedEnd/shift/overlap/overdue, no writes from reads, rollback, duplicate/booking races, fresh clock after lock, revoke/stamp; GUID DB only.");
        }
        finally
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"\Amusicbox_test_[0-9a-f]{32}\z"))throw new Exception("Unsafe cleanup");
            Sql(new MySqlDatabase("defaultdb"),"DROP DATABASE IF EXISTS `"+name+"`");
        }
    }
}
