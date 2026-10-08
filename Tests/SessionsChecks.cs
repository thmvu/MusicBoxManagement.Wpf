using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxSessionsChecks
{
    private sealed class Clock : IClock { public DateTimeOffset Value; public int Reads; public DateTimeOffset UtcNow {get{Reads++;return Value;}} }
    private static DateTimeOffset Time(int hour){return new DateTimeOffset(2026,10,7,hour,0,0,TimeSpan.FromHours(7));}
    private static object Sql(SqliteDatabase db,string sql){using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=sql;return cmd.ExecuteScalar();}}
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    public static void Run()
    {
        var file=Path.Combine(Path.GetTempPath(),"MusicBoxSessions_"+Guid.NewGuid().ToString("N")+".db");var db=new SqliteDatabase(file);const string password="Sessions-Test-2026!";
        try
        {
            var auth=new AuthenticationService(db);var admin=auth.SetupAdminAsync("admin","Admin thử",password).GetAwaiter().GetResult();
            Sql(db,@"INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('S1','Phiên 1',1,'test.png',1,'test'),('S2','Phiên 2',2,'test.png',1,'test');
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'viewer','viewer','VIEWER',PasswordHash,'viewer','Chỉ xem phiên',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('viewer','Staff');DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId NOT IN(SELECT PermissionId FROM Permission WHERE Code='Session.View');");
            var actor=auth.LoginAsync("viewer",password).GetAwaiter().GetResult();var clock=new Clock{Value=Time(9)};var service=new StaffSessionService(db,clock);var sessions=new RoomSessionService(db,clock);
            var booking=new ReservationService(db,clock).CreateGuest(new ReservationRequest{RoomId=1,FullName="Khách booking",PhoneNumber="0901111111",StartTime=Time(13),DurationMinutes=60});
            clock.Value=Time(13).AddSeconds(12).AddTicks(1234);var first=sessions.CheckIn(admin,booking.ReservationId);
            var walk=sessions.CreateWalkIn(admin,new WalkInRequest{RoomId=2,FullName="Khách trực tiếp",PhoneNumber="0902222222"});
            var audits=Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog"));clock.Reads=0;var result=service.Search(actor);
            Assert(result.Items.Count==2 && !result.CanExtend && clock.Reads==1 && result.Items.All(i=>i.CheckedAt==clock.Value) && Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog"))==audits,"View-only read required unrelated rights, wrote data or inconsistent clock.");
            var row=result.Items.First(i=>i.Session.RoomSessionId==first.RoomSessionId);
            Assert(row.IsExtendable && row.Session.ActualStartTime==first.ActualStartTime && row.Details.Contains("13:00:12") && row.Session.HourlyRate==120000 && row.PhoneNumber=="0901111111","Read lost actual/snapshot/customer.");
            Assert(service.Search(actor,"+84 901.111-111").Items.Count==1 && service.Search(actor,"0903333333").Items.Count==0,"Phone filter normalized incorrectly.");
            Reject<ArgumentException>(()=>service.Search(actor,"abc"));Reject<ArgumentException>(()=>service.Search(actor,null,"Confirmed"));
            Reject<UnauthorizedAccessException>(()=>service.Search(null));Reject<UnauthorizedAccessException>(()=>service.Preview(actor,first.RoomSessionId,30));
            Reject<UnauthorizedAccessException>(()=>service.Extend(actor,first.RoomSessionId,first.ExpectedEndTime.Value,30));
            Sql(db,"UPDATE RoomTypes SET PricePerHour=999000,Name='Loại đổi sau nhận';");
            Assert(service.Search(actor).Items.First(i=>i.Session.RoomSessionId==first.RoomSessionId).Session.HourlyRate==120000,"Read used live price instead of snapshot.");
            new ReservationService(db,clock).CreateGuest(new ReservationRequest{RoomId=2,FullName="Khách sau",PhoneNumber="0903333333",StartTime=Time(18),DurationMinutes=60});
            Assert(service.Search(actor).Items.First(i=>i.Session.RoomSessionId==walk.Session.RoomSessionId).ReturnBy==Time(18),"Walk-in deadline not dynamic.");
            clock.Value=Time(14).AddMinutes(1);Assert(!service.Search(actor).Items.First(i=>i.Session.RoomSessionId==first.RoomSessionId).IsExtendable,"Overdue row remained eligible.");clock.Value=Time(13);
            Sql(db,"INSERT INTO RolePermission(RoleId,PermissionId) SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.Extend';");
            Assert(service.Search(actor).CanExtend && service.Preview(actor,first.RoomSessionId,30).CanExtend,"View/Extend role could not preview.");
            var updated=service.Extend(actor,first.RoomSessionId,first.ExpectedEndTime.Value,30);Assert(updated.ExpectedEndTime==first.ExpectedEndTime.Value.AddMinutes(30),"UI route did not delegate extension.");
            Reject<InvalidOperationException>(()=>service.Extend(actor,first.RoomSessionId,first.ExpectedEndTime.Value,30));
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Session.View');");
            Reject<UnauthorizedAccessException>(()=>service.Search(actor));Reject<UnauthorizedAccessException>(()=>service.Preview(actor,first.RoomSessionId,30));
            Reject<UnauthorizedAccessException>(()=>service.Extend(actor,first.RoomSessionId,updated.ExpectedEndTime.Value,30));
            Assert(sessions.PreviewExtensionStaff(actor,first.RoomSessionId,30).CanExtend,"Core extension unexpectedly required View.");
            Sql(db,"INSERT INTO RolePermission(RoleId,PermissionId) SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.View'; UPDATE RoomSessions SET Status='Completed',ActualEndTime='2026-10-07T07:30:12.0001234+00:00' WHERE RoomId=1; UPDATE Rooms SET IsActive=0,InactiveReason='Lịch sử' WHERE RoomId=1;");
            result=service.Search(actor,null,"Completed");Assert(result.Items.Count==1 && !result.Items[0].IsExtendable && result.Items[0].Details.Contains("Trả thực tế") && result.Items[0].Session.HourlyRate==120000,"Completed/locked history not readable by snapshot.");
            Assert(service.Search(actor,null,null).Items.Count==2 && service.Search(actor).Items.Count==1,"Status filter wrong.");
            Sql(db,"UPDATE AspNetUsers SET SecurityStamp='revoked' WHERE Id='viewer';");Reject<UnauthorizedAccessException>(()=>service.Search(actor));
            Assert(Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog"))==audits+3 && Convert.ToInt64(Sql(db,"PRAGMA user_version"))== SqliteDatabase.CurrentSchemaVersion,"Read wrote data or changed schema.");
            Console.WriteLine("PASS Sessions: independent live View/Extend, normalized/status filters, UTC snapshots/actual/history, one read clock/dynamic WalkIn deadline, read-only, UI route permissions/stale, revoked session on temporary SQLite.");
        }
        finally{SQLiteConnection.ClearAllPools();foreach(var suffix in new[]{"","-wal","-shm","-journal"})if(File.Exists(file+suffix))File.Delete(file+suffix);}
    }
}
