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

public static class MusicBoxExtensionChecks
{
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } }
    private sealed class GateClock : IClock
    {
        public readonly ManualResetEventSlim Entered=new ManualResetEventSlim(false),Release=new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow {get{Entered.Set();if(!Release.Wait(10000))throw new Exception("Clock gate timeout.");return Time(13);}}
    }
    private sealed class CountingClock : IClock
    { public DateTimeOffset Value; public int Reads; public DateTimeOffset UtcNow {get{Interlocked.Increment(ref Reads);return Value;}} }
    private static DateTimeOffset Time(int h,int m=0){return new DateTimeOffset(2026,10,7,h,m,0,TimeSpan.FromHours(7));}
    private static string Utc(DateTimeOffset time){return time.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);}
    private static object Sql(SqliteDatabase db,string sql)
    {using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=sql;return cmd.ExecuteScalar();}}
    private static long Count(SqliteDatabase db,string sql){return Convert.ToInt64(Sql(db,sql));}
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static T Reject<T>(Action action)where T:Exception
    {try{action();}catch(T error){return error;}throw new Exception("Expected "+typeof(T).Name);}
    private static void Clear(SqliteDatabase db)
    {Sql(db,"DELETE FROM RoomSessions;DELETE FROM Reservations;DELETE FROM AuditLog;UPDATE Rooms SET IsActive=1,InactiveReason=NULL;");}
    private static Reservation Book(SqliteDatabase db,Clock clock,int room,string phone,DateTimeOffset start,int minutes=60)
    {return new ReservationService(db,clock).CreateGuest(new ReservationRequest{RoomId=room,FullName="Khách thử",PhoneNumber=phone,StartTime=start,DurationMinutes=minutes});}
    private static RoomSession Start(SqliteDatabase db,Clock clock,LoginSession admin,DateTimeOffset actual)
    {
        Clear(db);clock.UtcNow=Time(9);
        var booking=Book(db,clock,1,"0901111111",Time(actual.Hour,actual.Minute>=30?30:0));
        clock.UtcNow=actual;return new RoomSessionService(db,clock).CheckIn(admin,booking.ReservationId);
    }
    private static bool Extend(RoomSessionService service,LoginSession actor,RoomSession session,int minutes)
    {try{service.ExtendStaff(actor,session.RoomSessionId,session.ExpectedEndTime.Value,minutes);return true;}catch(InvalidOperationException){return false;}}
    public static void Run()
    {
        var file=Path.Combine(Path.GetTempPath(),"MusicBoxExtension_"+Guid.NewGuid().ToString("N")+".db");
        var db=new SqliteDatabase(file);var clock=new Clock{UtcNow=Time(13)};const string password="Extension-Test-2026!";
        try
        {
            var auth=new AuthenticationService(db);var admin=auth.SetupAdminAsync("admin","Admin thử",password).GetAwaiter().GetResult();
            Sql(db,@"INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('E1','Gia hạn',1,'test.png',1,'test'),('E2','Phòng khác',2,'test.png',1,'test');
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'extender','extender','EXTENDER',PasswordHash,'extender','Nhân viên gia hạn',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('extender','Staff');
DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId<>(SELECT PermissionId FROM Permission WHERE Code='Session.Extend');");
            var staff=auth.LoginAsync("extender",password).GetAwaiter().GetResult();var service=new RoomSessionService(db,clock);
            var source=Start(db,clock,admin,Time(13,7).AddSeconds(12).AddTicks(1234));var oldEnd=source.ExpectedEndTime.Value;
            Reject<UnauthorizedAccessException>(()=>service.PreviewExtensionStaff(null,source.RoomSessionId,30));
            Reject<UnauthorizedAccessException>(()=>service.ExtendStaff(null,source.RoomSessionId,oldEnd,30));
            Reject<ArgumentException>(()=>service.PreviewExtensionStaff(staff,source.RoomSessionId,45));
            Reject<ArgumentException>(()=>service.ExtendStaff(staff,source.RoomSessionId,oldEnd,0));
            Reject<InvalidOperationException>(()=>service.PreviewExtensionStaff(staff,9999,30));
            var logs=Count(db,"SELECT COUNT(*) FROM AuditLog");var preview=service.PreviewExtensionStaff(staff,source.RoomSessionId,30);
            Assert(preview.CanExtend && preview.NewEndTime==oldEnd.AddMinutes(30) && preview.MaximumEndTime==Time(23) && preview.CheckedAt==clock.UtcNow && Count(db,"SELECT COUNT(*) FROM AuditLog")==logs,"Preview changed data or did not ignore own hold.");
            Sql(db,"UPDATE RoomTypes SET PricePerHour=777000,Name='Tên mới' WHERE RoomTypeId=1;");
            var extended=service.ExtendStaff(staff,source.RoomSessionId,oldEnd.ToOffset(TimeSpan.FromHours(-3)),30);
            Assert(extended.ExpectedEndTime==oldEnd.AddMinutes(30) && extended.ExpectedEndTime.Value.Offset==TimeSpan.Zero && extended.ActualStartTime==source.ActualStartTime && extended.HourlyRate==source.HourlyRate && extended.RoomTypeNameSnapshot==source.RoomTypeNameSnapshot && extended.Status=="Active" && extended.ReservationId==source.ReservationId,"Extension rounded ticks/changed snapshot or booking source.");
            Assert((string)Sql(db,"SELECT EndTime FROM Reservations WHERE ReservationId="+source.ReservationId)==Utc(Time(14)) && (string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+source.ReservationId)=="CheckedIn","Extension modified original booking.");
            Assert(Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Session.Extend' AND ActorType='Staff' AND UserId='extender' AND CreatedAt='"+Utc(clock.UtcNow)+"'")==1,"Extension actor/audit/time failed.");
            Reject<InvalidOperationException>(()=>service.ExtendStaff(staff,source.RoomSessionId,oldEnd,30));
            extended=service.ExtendStaff(staff,source.RoomSessionId,extended.ExpectedEndTime.Value,60);
            Assert(extended.ExpectedEndTime==oldEnd.AddMinutes(90) && Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Session.Extend'")==2,"Valid repeated extension failed or stale replay counted twice.");
            Assert(service.CheckIn(admin,source.ReservationId.Value).ExpectedEndTime==extended.ExpectedEndTime,"Check-in replay lost extended end.");
            var calendarAfter=new CalendarService(db,clock).Read(admin,CalendarRange.Day(Time(13).Date),1)[0];
            Assert(calendarAfter.Holds.Count==1 && calendarAfter.Holds[0].End==extended.ExpectedEndTime && calendarAfter.Events.Any(e=>e.SessionId==source.RoomSessionId && e.ExpectedEnd==extended.ExpectedEndTime),"Calendar did not use extended session hold/event.");
            var availabilityAfter=new AvailabilityService(db,clock);
            Assert(!availabilityAfter.CheckReservation(1,null,Time(15),60).CanBook && !availabilityAfter.CheckReservation(2,source.CustomerId,Time(15),60).CanBook && availabilityAfter.CheckReservation(1,null,Time(16),60).CanBook,"Booking availability ignored extended Room/Customer hold.");

            source=Start(db,clock,admin,Time(13));oldEnd=source.ExpectedEndTime.Value;clock.UtcNow=oldEnd;
            Assert(service.ExtendStaff(staff,source.RoomSessionId,oldEnd,30).ExpectedEndTime==Time(14,30),"Exact expected end rejected.");
            source=Start(db,clock,admin,Time(13));oldEnd=source.ExpectedEndTime.Value;clock.UtcNow=oldEnd.AddTicks(1);
            var late=Reject<SessionExtensionException>(()=>service.ExtendStaff(staff,source.RoomSessionId,oldEnd,30));
            Assert(late.MaximumEndTime==oldEnd && Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Session.Extend'")==0,"Overdue extension wrote data or wrong limit.");
            foreach(var hour in new[]{10,21})
            {
                source=Start(db,clock,admin,Time(hour));oldEnd=source.ExpectedEndTime.Value;
                var result=service.ExtendStaff(staff,source.RoomSessionId,oldEnd,60);var close=Time(hour==10?12:23);
                Assert(result.ExpectedEndTime==close,"Exact shift close rejected.");
                var rejected=Reject<SessionExtensionException>(()=>service.ExtendStaff(staff,source.RoomSessionId,close,30));
                Assert(rejected.MaximumEndTime==close,"Shift crossing limit incorrect.");
                source=Start(db,clock,admin,Time(hour).AddTicks(1));
                Reject<SessionExtensionException>(()=>service.ExtendStaff(staff,source.RoomSessionId,source.ExpectedEndTime.Value,60));
            }
            foreach(var customerConflict in new[]{false,true})
            {
                source=Start(db,clock,admin,Time(13));oldEnd=source.ExpectedEndTime.Value;
                var next=Book(db,clock,customerConflict?2:1,customerConflict?"0901111111":"0902222222",Time(14,30));
                var rejected=Reject<SessionExtensionException>(()=>service.ExtendStaff(staff,source.RoomSessionId,oldEnd,60));
                Assert(rejected.MaximumEndTime==Time(14,30) && !rejected.Message.Contains("090") && !rejected.Message.Contains("Khách thử"),"Conflict limit exposed other customer or wrong Room/Customer limit.");
                var snapshot=new CalendarService(db,clock).Read(admin,CalendarRange.Day(Time(13).Date),1)[0];
                Assert(snapshot.Events.Count>0 && service.PreviewExtensionStaff(staff,source.RoomSessionId,30).CanExtend,"Touching endpoint conflicted.");
                var result=service.ExtendStaff(staff,source.RoomSessionId,oldEnd,30);
                Assert(result.ExpectedEndTime==Time(14,30),"Touching next booking failed.");
                Sql(db,"UPDATE Reservations SET Status='Cancelled' WHERE ReservationId="+next.ReservationId);
                Assert(service.ExtendStaff(staff,source.RoomSessionId,result.ExpectedEndTime.Value,30).ExpectedEndTime==Time(15),"Cancelled booking still blocked extension.");
            }
            source=Start(db,clock,admin,Time(13));oldEnd=source.ExpectedEndTime.Value;
            var expired=Book(db,clock,1,"0902222222",Time(14));clock.UtcNow=Time(14).AddMinutes(15).AddTicks(-1);
            // Source must still be eligible, so extend its fixture end before probing grace.
            Sql(db,"UPDATE RoomSessions SET ExpectedEndTime='"+Utc(Time(14,30))+"' WHERE RoomSessionId="+source.RoomSessionId);
            preview=service.PreviewExtensionStaff(staff,source.RoomSessionId,30);Assert(!preview.CanExtend && preview.MaximumEndTime==Time(14,30),"In-progress effective booking did not block added interval.");
            clock.UtcNow=Time(14,15);logs=Count(db,"SELECT COUNT(*) FROM AuditLog");
            Assert(service.PreviewExtensionStaff(staff,source.RoomSessionId,30).CanExtend && (string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+expired.ReservationId)=="Confirmed" && Count(db,"SELECT COUNT(*) FROM AuditLog")==logs,"Exact grace did not release hold or preview wrote NoShow.");
            service.ExtendStaff(staff,source.RoomSessionId,Time(14,30),30);
            source=Start(db,clock,admin,Time(13));oldEnd=source.ExpectedEndTime.Value;
            Sql(db,"UPDATE RoomSessions SET Status='Completed',ActualEndTime='"+Utc(Time(13,30))+"';");
            Reject<InvalidOperationException>(()=>service.ExtendStaff(staff,source.RoomSessionId,oldEnd,30));
            Clear(db);clock.UtcNow=Time(13);var walk=service.CreateWalkIn(admin,new WalkInRequest{RoomId=1,FullName="Walk-in",PhoneNumber="0901111111"});
            Reject<InvalidOperationException>(()=>service.PreviewExtensionStaff(staff,walk.Session.RoomSessionId,30));
            Reject<InvalidOperationException>(()=>service.ExtendStaff(staff,walk.Session.RoomSessionId,Time(14),30));

            source=Start(db,clock,admin,Time(13));oldEnd=source.ExpectedEndTime.Value;
            Sql(db,"CREATE TRIGGER fail_extension BEFORE INSERT ON AuditLog WHEN NEW.Action='Session.Extend' BEGIN SELECT RAISE(ABORT,'test rollback'); END;");
            Reject<SQLiteException>(()=>service.ExtendStaff(staff,source.RoomSessionId,oldEnd,30));
            Assert((string)Sql(db,"SELECT ExpectedEndTime FROM RoomSessions")==Utc(oldEnd) && Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Session.Extend'")==0,"Audit failure did not roll back extension.");Sql(db,"DROP TRIGGER fail_extension;");
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';");
            Reject<UnauthorizedAccessException>(()=>service.PreviewExtensionStaff(staff,source.RoomSessionId,30));Reject<UnauthorizedAccessException>(()=>service.ExtendStaff(staff,source.RoomSessionId,oldEnd,30));
            Sql(db,"INSERT INTO RolePermission(RoleId,PermissionId) SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.Extend';UPDATE AspNetUsers SET IsActive=0 WHERE Id='extender';");
            Reject<UnauthorizedAccessException>(()=>service.ExtendStaff(staff,source.RoomSessionId,oldEnd,30));Sql(db,"UPDATE AspNetUsers SET IsActive=1 WHERE Id='extender';");

            for(int attempt=0;attempt<3;attempt++)
            {
                source=Start(db,clock,admin,Time(13));
                using(var gate=new ManualResetEventSlim(false))
                {
                    var a=Task.Run(()=>{gate.Wait();return Extend(service,staff,source,30);});var b=Task.Run(()=>{gate.Wait();return Extend(service,staff,source,30);});gate.Set();Task.WaitAll(a,b);
                    Assert(a.Result!=b.Result && Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Session.Extend'")==1 && (string)Sql(db,"SELECT ExpectedEndTime FROM RoomSessions")==Utc(Time(14,30)),"Competing stale extensions both committed.");
                }
                foreach(var customerConflict in new[]{false,true})
                {
                    source=Start(db,clock,admin,Time(13));
                    using(var gate=new ManualResetEventSlim(false))
                    {
                        var a=Task.Run(()=>{gate.Wait();return Extend(service,staff,source,60);});
                        var b=Task.Run(()=>{gate.Wait();try{Book(db,clock,customerConflict?2:1,customerConflict?"0901111111":"0902222222",Time(14,30));return true;}catch(InvalidOperationException){return false;}});
                        gate.Set();Task.WaitAll(a,b);Assert(a.Result!=b.Result,"Extension and overlapping Room/Customer booking both committed.");
                    }
                }
            }
            source=Start(db,clock,admin,Time(13));var upcoming=Book(db,clock,1,"0902222222",Time(14,30));
            using(var gate=new ManualResetEventSlim(false))
            {
                var a=Task.Run(()=>{gate.Wait();return Extend(service,staff,source,60);});var b=Task.Run(()=>{gate.Wait();new ReservationService(db,clock).CancelStaff(admin,upcoming.ReservationId,"Khách hủy");});gate.Set();Task.WaitAll(a,b);
                Assert((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+upcoming.ReservationId)=="Cancelled","Concurrent cancellation failed.");
                if(!a.Result)Assert(Extend(service,staff,source,60),"Extension could not proceed after cancellation.");
            }
            source=Start(db,clock,admin,Time(13));upcoming=Book(db,clock,1,"0902222222",Time(14));
            Sql(db,"UPDATE RoomSessions SET ExpectedEndTime='"+Utc(Time(14,30))+"';");clock.UtcNow=Time(14,15);
            using(var gate=new ManualResetEventSlim(false))
            {
                var a=Task.Run(()=>{gate.Wait();return service.ExtendStaff(staff,source.RoomSessionId,Time(14,30),30);});
                var b=Task.Run(()=>{gate.Wait();return new NoShowService(db,clock).ProcessExpired();});gate.Set();Task.WaitAll(a,b);
                Assert(a.Result.ExpectedEndTime==Time(15) && Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.NoShow'")==1,"Extension/NoShow competition failed.");
            }
            source=Start(db,clock,admin,Time(13));var original=new RoomService(db,clock).GetForEdit(admin,1);
            using(var gate=new ManualResetEventSlim(false))
            {
                var a=Task.Run(()=>{gate.Wait();return Extend(service,staff,source,30);});
                var b=Task.Run(()=>{gate.Wait();try{new RoomService(db,clock).Update(admin,original,new RoomEdit{Name=original.Name,Description=original.Description,IsActive=false,InactiveReason="Thử khóa"});return true;}catch(InvalidOperationException){return false;}});
                gate.Set();Task.WaitAll(a,b);Assert(a.Result && !b.Result && Count(db,"SELECT IsActive FROM Rooms WHERE RoomId=1")==1,"Active room lock/extension guards failed.");
            }
            source=Start(db,clock,admin,Time(13));var held=new GateClock();var waitingClock=new CountingClock{Value=Time(14)};
            var holding=Task.Run(()=>new NoShowService(db,held).ProcessExpired());Assert(held.Entered.Wait(10000),"Writer gate did not acquire lock.");
            using(var entered=new ManualResetEventSlim(false))
            {
                var waiting=Task.Run(()=>{entered.Set();try{new RoomSessionService(db,waitingClock).ExtendStaff(staff,source.RoomSessionId,source.ExpectedEndTime.Value,30);return false;}catch(SessionExtensionException){return true;}});
                Assert(entered.Wait(10000) && waitingClock.Reads==0,"Extension sampled clock before writer lock.");waitingClock.Value=Time(14).AddTicks(1);held.Release.Set();Task.WaitAll(holding,waiting);
                Assert(waiting.Result && waitingClock.Reads==1 && (string)Sql(db,"SELECT ExpectedEndTime FROM RoomSessions")==Utc(Time(14)),"Waited extension used stale eligibility time.");
            }
            held.Entered.Dispose();held.Release.Dispose();auth.Logout(staff);
            Reject<UnauthorizedAccessException>(()=>service.ExtendStaff(staff,source.RoomSessionId,source.ExpectedEndTime.Value,30));
            Assert(Count(db,"PRAGMA user_version")==6,"Extension changed schema.");
            Console.WriteLine("PASS Extension: independent live permission, read-only preview, 30/60/repeat/stale, UTC ticks/exact end/shifts, Room/Customer overlap and private limits/grace, unchanged reservation/snapshots, rollback, competing extension/booking/cancel/NoShow/lock and clock after writer on temporary SQLite.");
        }
        finally{SQLiteConnection.ClearAllPools();foreach(var suffix in new[]{"","-wal","-shm","-journal"})if(File.Exists(file+suffix))File.Delete(file+suffix);}
    }
}
