using System;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxCheckInChecks
{
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } }
    private sealed class CountingClock : IClock
    {
        public DateTimeOffset Value;
        public int Reads;
        public DateTimeOffset UtcNow { get { Interlocked.Increment(ref Reads); return Value; } }
    }
    private sealed class GateClock : IClock
    {
        public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false);
        public readonly ManualResetEventSlim Release = new ManualResetEventSlim(false);
        public DateTimeOffset Value;
        public DateTimeOffset UtcNow { get { Entered.Set(); if (!Release.Wait(10000)) throw new Exception("Clock gate timeout."); return Value; } }
    }
    private static DateTimeOffset Time(int h, int m = 0) { return new DateTimeOffset(2026,10,6,h,m,0,TimeSpan.FromHours(7)); }
    private static string Utc(DateTimeOffset t) { return t.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture); }
    private static void Assert(bool b,string message) { if(!b) throw new Exception(message); }
    private static void Reject<T>(Action action) where T:Exception
    { try { action(); } catch(T) { return; } throw new Exception("Expected "+typeof(T).Name); }
    private static object Sql(SqliteDatabase db,string sql)
    { using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=sql;return cmd.ExecuteScalar();} }
    private static long Count(SqliteDatabase db,string sql) { return Convert.ToInt64(Sql(db,sql)); }
    private static string Status(SqliteDatabase db,int id) { return (string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+id); }
    private static void Clear(SqliteDatabase db)
    { Sql(db,"DELETE FROM RoomSessions; DELETE FROM Reservations; DELETE FROM AuditLog; UPDATE Rooms SET IsActive=1,InactiveReason=NULL;"); }
    private static int Booking(SqliteDatabase db,int room,int customer,int hour,int duration=60,string status="Confirmed")
    { return Convert.ToInt32(Sql(db,"INSERT INTO Reservations(RoomId,CustomerId,StartTime,EndTime,Status,CreatedAt) VALUES("+room+","+customer+",'"+Utc(Time(hour))+"','"+Utc(Time(hour).AddMinutes(duration))+"','"+status+"','"+Utc(Time(9))+"'); SELECT last_insert_rowid();")); }
    private static void Active(SqliteDatabase db,int room,int customer)
    { Sql(db,"INSERT INTO RoomSessions(RoomId,CustomerId,ActualStartTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES("+room+","+customer+",'"+Utc(Time(9))+"',100000,'R','STANDARD','Standard','Active');"); }
    private static bool Attempt(RoomSessionService service,LoginSession staff,int id)
    { try {service.CheckIn(staff,id);return true;}catch(InvalidOperationException){return false;} }
    public static void Run()
    {
        var file=Path.Combine(Path.GetTempPath(),"MusicBoxCheckIn_"+Guid.NewGuid().ToString("N")+".db");
        var db=new SqliteDatabase(file);var clock=new Clock {UtcNow=Time(19)};
        const string password="CheckIn-Test-2026!";
        try
        {
            var auth=new AuthenticationService(db);
            var admin=auth.SetupAdminAsync("admin","Quản trị thử",password).GetAwaiter().GetResult();
            Sql(db,@"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'staff','staff','STAFF',PasswordHash,'staff','Nhân viên thử',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('staff','Staff');
DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId<>(SELECT PermissionId FROM Permission WHERE Code='Session.CheckIn');
INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('R1','Room 1',1,'test.png',1,'test'),('R2','Room 2',2,'test.png',1,'test');
INSERT INTO Customers(FullName,PhoneNumber) VALUES('A','0901111111'),('B','0902222222'),('C','0903333333');");
            var staff=auth.LoginAsync("staff",password).GetAwaiter().GetResult();
            var service=new RoomSessionService(db,clock);
            Clear(db);
            var id=Booking(db,1,1,20,120);
            Reject<UnauthorizedAccessException>(()=>service.CheckIn(null,id));
            Reject<InvalidOperationException>(()=>service.CheckIn(staff,999));
            clock.UtcNow=Time(19,37).AddSeconds(12).AddTicks(3456);
            Sql(db,"UPDATE RoomTypes SET PricePerHour=234567,Name='Tên chốt' WHERE RoomTypeId=1;");
            var actual=clock.UtcNow;var session=service.CheckIn(staff,id);
            Assert(session.ActualStartTime==actual && session.ExpectedEndTime==actual.AddHours(2) && session.ActualStartTime.Offset==TimeSpan.Zero,"Actual time/duration/UTC rounded or changed.");
            Assert(session.HourlyRate==234567 && session.RoomCodeSnapshot=="R1" && session.RoomTypeCodeSnapshot=="STANDARD" && session.RoomTypeNameSnapshot=="Tên chốt" && session.ReservationId==id && session.Status=="Active" && session.ActualEndTime==null,"Snapshot or session fields incorrect.");
            Assert(Status(db,id)=="CheckedIn" && Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Session.CheckIn' AND ActorType='Staff' AND UserId='staff' AND CreatedAt='"+Utc(actual)+"'")==1,"Status/audit did not share transaction/time/actor.");
            var calendar=new CalendarService(db,clock).Read(admin,CalendarRange.Day(Time(20).Date),1)[0];
            Assert(calendar.CurrentStatus=="Occupied" && calendar.Holds.Count==1 && calendar.Holds[0].Start==actual && calendar.Holds[0].End==actual.AddHours(2),"Check-in calendar doubled old booking or kept old times.");
            Sql(db,"UPDATE RoomTypes SET PricePerHour=345678,Name='Tên mới' WHERE RoomTypeId=1;");
            clock.UtcNow=Time(23);
            var repeated=service.CheckIn(staff,id);
            Assert(repeated.RoomSessionId==session.RoomSessionId && repeated.HourlyRate==234567 && repeated.RoomTypeNameSnapshot=="Tên chốt" && repeated.ActualStartTime==actual && Count(db,"SELECT COUNT(*) FROM AuditLog")==1,"Replay changed snapshot/time or wrote extra audit.");
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';");
            Reject<UnauthorizedAccessException>(()=>service.CheckIn(staff,id));
            Sql(db,"INSERT INTO RolePermission(RoleId,PermissionId) SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.CheckIn'; UPDATE AspNetUsers SET IsActive=0 WHERE Id='staff';");
            Reject<UnauthorizedAccessException>(()=>service.CheckIn(staff,id));
            Sql(db,"UPDATE AspNetUsers SET IsActive=1 WHERE Id='staff'; UPDATE RoomSessions SET Status='Completed',ActualEndTime='"+Utc(Time(22))+"'; UPDATE Reservations SET Status='Completed';");
            Assert(service.CheckIn(staff,id).Status=="Completed" && Count(db,"SELECT COUNT(*) FROM RoomSessions")==1,"Completed source replay created another session.");
            foreach(var state in new[]{"Cancelled","NoShow","Completed","CheckedIn"})
            { Clear(db);id=Booking(db,1,1,20,60,state);clock.UtcNow=Time(20);Reject<InvalidOperationException>(()=>service.CheckIn(staff,id));Assert(Count(db,"SELECT COUNT(*) FROM RoomSessions")==0 && Status(db,id)==state,"Invalid status modified."); }
            Clear(db);id=Booking(db,1,1,20);clock.UtcNow=Time(20,15).AddTicks(-1);
            Assert(service.CheckIn(staff,id).ExpectedEndTime==clock.UtcNow.AddHours(1),"Last tick before grace rejected.");
            Clear(db);id=Booking(db,1,1,20);clock.UtcNow=Time(20,15);
            Reject<InvalidOperationException>(()=>service.CheckIn(staff,id));
            Assert(Status(db,id)=="Confirmed" && Count(db,"SELECT COUNT(*) FROM RoomSessions")==0 && Count(db,"SELECT COUNT(*) FROM AuditLog")==0,"Expired rejection changed data.");
            Assert(new NoShowService(db,clock).ProcessExpired()==1 && Status(db,id)=="NoShow","Worker did not recover expired booking.");
            // Late arrival must preserve full duration, and never move the later booking.
            foreach(var otherRoom in new[]{1,2})
            { Clear(db);id=Booking(db,1,1,20,120);var later=Booking(db,otherRoom,otherRoom==1?2:1,22);clock.UtcNow=Time(20,10);Reject<InvalidOperationException>(()=>service.CheckIn(staff,id));Assert(Status(db,id)=="Confirmed" && Status(db,later)=="Confirmed" && Count(db,"SELECT COUNT(*) FROM RoomSessions")==0,"Late overlap partially changed bookings."); }
            Clear(db);id=Booking(db,1,1,20,120);clock.UtcNow=Time(20,10);
            Assert(service.CheckIn(staff,id).ExpectedEndTime==Time(22,10),"Unblocked late check-in cut duration.");
            foreach(var t in new[]{Time(8,59),Time(12,59),Time(11,10),Time(22,10)})
            { Clear(db);id=Booking(db,1,1,t.Hour==11?11:t.Hour==22?22:20);clock.UtcNow=t;Reject<InvalidOperationException>(()=>service.CheckIn(staff,id));Assert(Status(db,id)=="Confirmed" && Count(db,"SELECT COUNT(*) FROM AuditLog")==0,"Outside/crossing shift changed data."); }
            Clear(db);id=Booking(db,1,1,11);clock.UtcNow=Time(11);Assert(service.CheckIn(staff,id).ExpectedEndTime==Time(12),"Exact shift end rejected.");
            Clear(db);id=Booking(db,1,1,20);clock.UtcNow=Time(13);Assert(service.CheckIn(staff,id).ExpectedEndTime==Time(14),"Early check-in was limited to 30 minutes.");
            Clear(db);id=Booking(db,1,1,15);Booking(db,1,2,13,180);clock.UtcNow=Time(14);Assert(service.CheckIn(staff,id).Status=="Active","Expired Confirmed still blocks check-in.");
            foreach(var room in new[]{1,2})
            { Clear(db);id=Booking(db,1,1,20);Active(db,room,room==1?2:1);clock.UtcNow=Time(19);Reject<InvalidOperationException>(()=>service.CheckIn(staff,id));Assert(Status(db,id)=="Confirmed","Active Room/Customer allowed check-in."); }
            Clear(db);id=Booking(db,1,1,20);clock.UtcNow=Time(19);Sql(db,"UPDATE Rooms SET IsActive=0,InactiveReason='Test' WHERE RoomId=1;");Reject<InvalidOperationException>(()=>service.CheckIn(staff,id));
            Clear(db);id=Booking(db,1,1,20);
            Sql(db,"CREATE TRIGGER FailCheckInAudit BEFORE INSERT ON AuditLog WHEN NEW.Action='Session.CheckIn' BEGIN SELECT RAISE(ABORT,'test failure'); END;");
            Reject<SQLiteException>(()=>service.CheckIn(staff,id));
            Assert(Status(db,id)=="Confirmed" && Count(db,"SELECT COUNT(*) FROM RoomSessions")==0 && Count(db,"SELECT COUNT(*) FROM AuditLog")==0,"Audit failure did not roll back session and status.");Sql(db,"DROP TRIGGER FailCheckInAudit;");
            Sql(db,"CREATE TRIGGER FailCheckInStatus BEFORE UPDATE ON Reservations WHEN NEW.Status='CheckedIn' BEGIN SELECT RAISE(ABORT,'test failure'); END;");
            Reject<SQLiteException>(()=>service.CheckIn(staff,id));
            Assert(Status(db,id)=="Confirmed" && Count(db,"SELECT COUNT(*) FROM RoomSessions")==0,"Status failure left session inserted.");Sql(db,"DROP TRIGGER FailCheckInStatus;");
            Sql(db,"UPDATE Rooms SET RoomTypeId=2 WHERE RoomId=1;");
            var changedType=service.CheckIn(staff,id);
            Assert(changedType.RoomTypeCodeSnapshot=="VIP" && changedType.RoomTypeNameSnapshot=="VIP" && changedType.HourlyRate==200000,"Check-in did not use current room type.");
            Clear(db);Sql(db,"UPDATE Rooms SET RoomTypeId=1 WHERE RoomId=1;");
            Clear(db);id=Booking(db,1,1,20);
            using(var gate=new ManualResetEventSlim(false))
            { var first=Task.Run(()=>{gate.Wait();return service.CheckIn(staff,id);});var second=Task.Run(()=>{gate.Wait();return service.CheckIn(staff,id);});gate.Set();Task.WaitAll(first,second);Assert(first.Result.RoomSessionId==second.Result.RoomSessionId && Count(db,"SELECT COUNT(*) FROM RoomSessions")==1 && Count(db,"SELECT COUNT(*) FROM AuditLog")==1,"Concurrent replay created duplicate session/log."); }
            foreach(var sameRoom in new[]{true,false})
            {
                Clear(db);var a=Booking(db,1,1,20);var b=Booking(db,sameRoom?1:2,sameRoom?2:1,21);clock.UtcNow=Time(19);
                using(var gate=new ManualResetEventSlim(false))
                {var one=Task.Run(()=>{gate.Wait();return Attempt(service,staff,a);});var two=Task.Run(()=>{gate.Wait();return Attempt(service,staff,b);});gate.Set();Task.WaitAll(one,two);Assert(one.Result!=two.Result && Count(db,"SELECT COUNT(*) FROM RoomSessions")==1,"Concurrent Room/Customer check-ins both committed.");}
            }
            // Force both serial orders: each clock is sampled while its writer owns the lock.
            foreach(var checkInFirst in new[]{true,false})
            {
                Clear(db);id=Booking(db,1,1,20);
                var held=new GateClock{Value=checkInFirst?Time(20,15).AddTicks(-1):Time(20,15)};
                Task first;Task second;
                if(checkInFirst)
                {first=Task.Run(()=>new RoomSessionService(db,held).CheckIn(staff,id));Assert(held.Entered.Wait(10000),"Check-in did not acquire writer.");second=Task.Run(()=>new NoShowService(db,new Clock{UtcNow=Time(20,15)}).ProcessExpired());}
                else
                {first=Task.Run(()=>new NoShowService(db,held).ProcessExpired());Assert(held.Entered.Wait(10000),"NoShow did not acquire writer.");second=Task.Run(()=>Attempt(new RoomSessionService(db,new Clock{UtcNow=Time(20,15).AddTicks(-1)}),staff,id));}
                held.Release.Set();Task.WaitAll(first,second);
                Assert(Status(db,id)==(checkInFirst?"CheckedIn":"NoShow") && Count(db,"SELECT COUNT(*) FROM RoomSessions")== (checkInFirst?1:0),"NoShow/check-in committed contradictory states.");
                held.Entered.Dispose();held.Release.Dispose();
            }
            Clear(db);id=Booking(db,1,1,20);
            var blocker=new GateClock{Value=Time(19)};
            var delayed=new CountingClock{Value=Time(20,15).AddTicks(-1)};
            var writer=Task.Run(()=>new NoShowService(db,blocker).ProcessExpired());
            Assert(blocker.Entered.Wait(10000),"Blocker did not acquire writer.");
            using(var started=new ManualResetEventSlim(false))
            {
                var waiting=Task.Run(()=>{started.Set();return Attempt(new RoomSessionService(db,delayed),staff,id);});
                Assert(started.Wait(10000) && delayed.Reads==0,"Clock was sampled before writer lock.");
                delayed.Value=Time(20,15);blocker.Release.Set();Task.WaitAll(writer,waiting);
                Assert(!waiting.Result && delayed.Reads==1 && Status(db,id)=="Confirmed","Waited check-in used stale time or sampled clock more than once.");
            }
            blocker.Entered.Dispose();blocker.Release.Dispose();
            // Cancellation and room locking use the same writer contract as check-in.
            Clear(db);id=Booking(db,1,1,20);clock.UtcNow=Time(19);
            using(var gate=new ManualResetEventSlim(false))
            {
                var check=Task.Run(()=>{gate.Wait();return Attempt(service,staff,id);});
                var cancel=Task.Run(()=>{gate.Wait();try{new ReservationService(db,clock).CancelStaff(admin,id,"Thử cạnh tranh");return true;}catch(InvalidOperationException){return false;}});
                gate.Set();Task.WaitAll(check,cancel);
                Assert(check.Result!=cancel.Result && Status(db,id)==(check.Result?"CheckedIn":"Cancelled") && Count(db,"SELECT COUNT(*) FROM RoomSessions")== (check.Result?1:0),"Cancellation/check-in both committed.");
            }
            Clear(db);id=Booking(db,1,1,20);var rooms=new RoomService(db,clock);var original=rooms.GetForEdit(admin,1);
            using(var gate=new ManualResetEventSlim(false))
            {
                var check=Task.Run(()=>{gate.Wait();return Attempt(service,staff,id);});
                var locking=Task.Run(()=>{gate.Wait();try{rooms.Update(admin,original,new RoomEdit{Name=original.Name,Description=original.Description,IsActive=false,InactiveReason="Thử cạnh tranh"});return true;}catch(InvalidOperationException){return false;}});
                gate.Set();Task.WaitAll(check,locking);
                Assert(check.Result && !locking.Result && Count(db,"SELECT IsActive FROM Rooms WHERE RoomId=1")==1,"Room was locked with effective booking/Active session.");
            }
            Clear(db);id=Booking(db,1,1,20);clock.UtcNow=Time(19);
            auth.Logout(staff);Reject<UnauthorizedAccessException>(()=>service.CheckIn(staff,id));
            Assert(Count(db,"PRAGMA user_version")== SqliteDatabase.CurrentSchemaVersion,"Check-in changed schema.");
            Console.WriteLine("PASS CheckIn: current permission, actual UTC/ticks/full duration, grace/shift/Room/Customer overlap, snapshot/replay, status/audit rollback, concurrent replay/Room/Customer/NoShow/cancel/lock and clock after writer on temporary SQLite.");
        }
        finally { SQLiteConnection.ClearAllPools();foreach(var suffix in new[]{"","-wal","-shm","-journal"})if(File.Exists(file+suffix))File.Delete(file+suffix); }
    }
}
