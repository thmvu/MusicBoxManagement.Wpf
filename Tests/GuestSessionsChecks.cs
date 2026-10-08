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

public static class MusicBoxGuestSessionsChecks
{
    private sealed class Clock : IClock
    { public DateTimeOffset Value; public int Reads; public DateTimeOffset UtcNow {get{Interlocked.Increment(ref Reads);return Value;}} }
    private sealed class GateClock : IClock
    {
        public readonly ManualResetEventSlim Entered=new ManualResetEventSlim(false),Release=new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow {get{Entered.Set();if(!Release.Wait(10000))throw new Exception("Writer gate timeout");return Time(13);}}
    }
    private static DateTimeOffset Time(int h,int m=0){return new DateTimeOffset(2026,10,8,h,m,0,TimeSpan.FromHours(7));}
    private static string Utc(DateTimeOffset value){return value.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);}
    private static object Sql(SqliteDatabase db,string sql){using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=sql;return cmd.ExecuteScalar();}}
    private static long Count(SqliteDatabase db,string sql){return Convert.ToInt64(Sql(db,sql));}
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static T Reject<T>(Action action)where T:Exception{try{action();}catch(T error){return error;}throw new Exception("Expected "+typeof(T).Name);}
    private static Reservation Book(SqliteDatabase db,Clock clock,int room,string phone,DateTimeOffset start)
    {return new ReservationService(db,clock).CreateGuest(new ReservationRequest{RoomId=room,FullName="Tên riêng không công khai",PhoneNumber=phone,StartTime=start,DurationMinutes=60});}
    private static RoomSession Start(SqliteDatabase db,Clock clock,LoginSession admin,DateTimeOffset actual)
    {
        Sql(db,"DELETE FROM RoomSessions;DELETE FROM Reservations;DELETE FROM AuditLog;UPDATE Customers SET PhoneNumber='0901111111' WHERE PhoneNumber='0909999999';UPDATE RoomTypes SET PricePerHour=120000 WHERE Code='STANDARD';");
        clock.Value=Time(9);var source=Book(db,clock,1,"0901111111",Time(actual.Hour,actual.Minute>=30?30:0));
        clock.Value=actual;return new RoomSessionService(db,clock).CheckIn(admin,source.ReservationId);
    }
    private static bool Extend(GuestSessionService service,RoomSession source,int minutes)
    {try{service.Extend("0901111111",source.RoomSessionId,source.ExpectedEndTime.Value,minutes);return true;}catch(InvalidOperationException){return false;}}
    public static void Run()
    {
        var file=Path.Combine(Path.GetTempPath(),"MusicBoxGuestSessions_"+Guid.NewGuid().ToString("N")+".db");var db=new SqliteDatabase(file);
        try
        {
            var auth=new AuthenticationService(db);var admin=auth.SetupAdminAsync("admin","Admin thử","Guest-Sessions-2026!").GetAwaiter().GetResult();
            Sql(db,"INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('G1','Phòng 1',1,'test.png',1,'test'),('G2','Phòng 2',2,'test.png',1,'test');");
            var clock=new Clock();var service=new GuestSessionService(db,clock);var staff=new RoomSessionService(db,clock);
            var source=Start(db,clock,admin,Time(13).AddSeconds(12).AddTicks(1234));var oldEnd=source.ExpectedEndTime.Value;
            var logs=Count(db,"SELECT COUNT(*) FROM AuditLog");clock.Reads=0;
            var found=service.Lookup("+84 901.111-111");
            Assert(found.SessionId==source.RoomSessionId && found.FromBooking && found.CanExtend && found.ActualStartTime==source.ActualStartTime && found.ExpectedEndTime==oldEnd && found.ReturnBy==oldEnd && found.CheckedAt==clock.Value && clock.Reads==1,"Lookup lost ownership/normalization/ticks/single clock.");
            Assert(service.Lookup("0902222222")==null,"Lookup returned another phone's active session.");
            foreach(var name in new[]{"CustomerId","RoomId","ReservationId","FullName","PhoneNumber","Session"})
                Assert(typeof(GuestSession).GetProperty(name)==null,"Public DTO leaks internal property: "+name);
            Reject<ArgumentException>(()=>service.Lookup("901"));Reject<ArgumentException>(()=>service.Extend("bad",source.RoomSessionId,oldEnd,30));
            var wrong=Reject<InvalidOperationException>(()=>service.PreviewExtension("0902222222",source.RoomSessionId,30));
            Assert(wrong.Message==Reject<InvalidOperationException>(()=>service.PreviewExtension("0902222222",99999,30)).Message,"Unknown and foreign IDs disclosed membership.");
            Reject<InvalidOperationException>(()=>service.Extend("0902222222",source.RoomSessionId,oldEnd,30));
            Reject<ArgumentException>(()=>service.PreviewExtension("0901111111",source.RoomSessionId,45));
            Reject<ArgumentException>(()=>service.Extend("0901111111",source.RoomSessionId,oldEnd,0));
            var preview=service.PreviewExtension("0901111111",source.RoomSessionId,30);
            Assert(preview.CanExtend && preview.NewEndTime==oldEnd.AddMinutes(30) && Count(db,"SELECT COUNT(*) FROM AuditLog")==logs,"Preview changed data.");
            Sql(db,"UPDATE RoomTypes SET PricePerHour=999000,Name='Tên loại mới';");
            found=service.Extend("+84 901111111",source.RoomSessionId,oldEnd,30);
            Assert(found.ExpectedEndTime==oldEnd.AddMinutes(30) && found.HourlyRate==source.HourlyRate && found.RoomTypeName==source.RoomTypeNameSnapshot && found.ActualStartTime==source.ActualStartTime,"Extension changed snapshots/actual.");
            Assert(Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Session.Extend' AND ActorType='Guest' AND UserId IS NULL AND EntityName='RoomSession'")==1 && (string)Sql(db,"SELECT EndTime FROM Reservations")==Utc(Time(14)),"Guest audit/source end wrong.");
            Reject<InvalidOperationException>(()=>service.Extend("0901111111",source.RoomSessionId,oldEnd,30));
            found=service.Extend("0901111111",source.RoomSessionId,found.ExpectedEndTime.Value,60);
            Assert(found.ExpectedEndTime==oldEnd.AddMinutes(90),"Repeated fresh extension failed.");
            Sql(db,"UPDATE Customers SET PhoneNumber='0909999999' WHERE PhoneNumber='0901111111';");
            Assert(service.Lookup("0901111111")==null && service.Lookup("0909999999")!=null,"Lookup ignored current phone.");
            Reject<InvalidOperationException>(()=>service.Extend("0901111111",source.RoomSessionId,found.ExpectedEndTime.Value,30));
            Reject<InvalidOperationException>(()=>service.PreviewExtension("0901111111",source.RoomSessionId,30));
            source=Start(db,clock,admin,Time(13));oldEnd=source.ExpectedEndTime.Value;
            Sql(db,"CREATE TRIGGER GuestAuditFail BEFORE INSERT ON AuditLog WHEN NEW.ActorType='Guest' AND NEW.Action='Session.Extend' BEGIN SELECT RAISE(ABORT,'audit failure'); END;");
            Reject<SQLiteException>(()=>service.Extend("0901111111",source.RoomSessionId,oldEnd,30));
            Assert((string)Sql(db,"SELECT ExpectedEndTime FROM RoomSessions")==Utc(oldEnd),"Audit failure did not rollback extension.");Sql(db,"DROP TRIGGER GuestAuditFail;");
            clock.Value=oldEnd;Assert(service.Extend("0901111111",source.RoomSessionId,oldEnd,30).ExpectedEndTime==oldEnd.AddMinutes(30),"Exact expected end rejected.");
            clock.Value=oldEnd.AddMinutes(30).AddTicks(1);Assert(!service.Lookup("0901111111").CanExtend,"Overdue lookup extend enabled.");
            Reject<SessionExtensionException>(()=>service.Extend("0901111111",source.RoomSessionId,oldEnd.AddMinutes(30),30));
            foreach(var hour in new[]{11,22})
            {source=Start(db,clock,admin,Time(hour));preview=service.PreviewExtension("0901111111",source.RoomSessionId,30);Assert(!preview.CanExtend && preview.MaximumEndTime==Time(hour+1),"Extension crossed shift.");}
            foreach(var customerConflict in new[]{false,true})
            {
                source=Start(db,clock,admin,Time(13));Book(db,clock,customerConflict?2:1,customerConflict?"0901111111":"0902222222",Time(14,30));
                preview=service.PreviewExtension("0901111111",source.RoomSessionId,60);
                Assert(!preview.CanExtend && preview.MaximumEndTime==Time(14,30) && !preview.Reason.Contains("090") && !preview.Reason.Contains("Tên riêng"),"Room/customer limit leaked customer data or failed.");
                Reject<SessionExtensionException>(()=>service.Extend("0901111111",source.RoomSessionId,source.ExpectedEndTime.Value,60));
            }
            source=Start(db,clock,admin,Time(13));Book(db,clock,1,"0902222222",Time(14));
            Sql(db,"UPDATE RoomSessions SET ExpectedEndTime='"+Utc(Time(14,30))+"';");clock.Value=Time(14,15);logs=Count(db,"SELECT COUNT(*) FROM AuditLog");
            Assert(service.PreviewExtension("0901111111",source.RoomSessionId,30).CanExtend && (string)Sql(db,"SELECT Status FROM Reservations WHERE Status='Confirmed'")=="Confirmed" && Count(db,"SELECT COUNT(*) FROM AuditLog")==logs,"Grace read wrote NoShow or did not release expired booking.");
            Sql(db,"UPDATE RoomSessions SET Status='Completed',ActualEndTime='"+Utc(Time(14,15))+"';");
            Assert(service.Lookup("0901111111")==null,"Completed session disclosed.");Reject<InvalidOperationException>(()=>service.Extend("0901111111",source.RoomSessionId,Time(14,30),30));
            source=Start(db,clock,admin,Time(13));Sql(db,"DELETE FROM RoomSessions;DELETE FROM Reservations;");
            var walk=staff.CreateWalkIn(admin,new WalkInRequest{RoomId=1,FullName="Walk-in",PhoneNumber="0901111111"});
            found=service.Lookup("0901111111");Assert(!found.FromBooking && !found.CanExtend && found.ExpectedEndTime==null && found.ReturnBy==Time(23),"Walk-in DTO incorrect.");
            Book(db,clock,2,"0901111111",Time(18));Assert(service.Lookup("0901111111").ReturnBy==Time(18),"Walk-in deadline ignored customer's next room.");
            Reject<InvalidOperationException>(()=>service.PreviewExtension("0901111111",walk.Session.RoomSessionId,30));Reject<InvalidOperationException>(()=>service.Extend("0901111111",walk.Session.RoomSessionId,Time(18),30));
            for(var iteration=0;iteration<3;iteration++)
            {
                source=Start(db,clock,admin,Time(13));var captured=source;
                using(var gate=new ManualResetEventSlim(false))
                {
                    var a=Task.Run(()=>{gate.Wait();return Extend(service,captured,30);});
                    var b=Task.Run(()=>{gate.Wait();try{staff.ExtendStaff(admin,captured.RoomSessionId,captured.ExpectedEndTime.Value,30);return true;}catch(InvalidOperationException){return false;}});
                    gate.Set();Task.WaitAll(a,b);Assert(a.Result!=b.Result && Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Session.Extend'")==1,"Guest/staff stale writers both committed.");
                }
                foreach(var customerConflict in new[]{false,true})
                {
                    source=Start(db,clock,admin,Time(13));captured=source;
                    using(var gate=new ManualResetEventSlim(false))
                    {
                        var a=Task.Run(()=>{gate.Wait();return Extend(service,captured,60);});
                        var b=Task.Run(()=>{gate.Wait();try{Book(db,clock,customerConflict?2:1,customerConflict?"0901111111":"0902222222",Time(14,30));return true;}catch(InvalidOperationException){return false;}});
                        gate.Set();Task.WaitAll(a,b);Assert(a.Result!=b.Result,"Guest extension and Room/customer booking both committed.");
                    }
                }
            }
            source=Start(db,clock,admin,Time(13));var held=new GateClock();var waitingClock=new Clock{Value=Time(14)};
            var holding=Task.Run(()=>new NoShowService(db,held).ProcessExpired());Assert(held.Entered.Wait(10000),"Writer gate did not start.");
            using(var entered=new ManualResetEventSlim(false))
            {
                var captured=source;
                var waiting=Task.Run(()=>{entered.Set();try{new GuestSessionService(db,waitingClock).Extend("0901111111",captured.RoomSessionId,captured.ExpectedEndTime.Value,30);return false;}catch(SessionExtensionException){return true;}});
                try{Assert(entered.Wait(10000) && waitingClock.Reads==0,"Guest sampled clock before lock.");waitingClock.Value=Time(14).AddTicks(1);}
                finally{held.Release.Set();}
                Task.WaitAll(holding,waiting);Assert(waiting.Result && waitingClock.Reads==1 && (string)Sql(db,"SELECT ExpectedEndTime FROM RoomSessions")==Utc(Time(14)),"Guest used time before writer wait.");
            }
            held.Entered.Dispose();held.Release.Dispose();
            Assert(Count(db,"PRAGMA user_version")==6,"Guest changed schema.");
            Console.WriteLine("PASS GuestSessions: current phone/private DTO/Active only, normalized lookup, snapshots/ticks/walk-in deadline, read-only preview, 30/60/fresh/stale/exact end/shifts/Room/customer/grace, Guest audit rollback, concurrent Guest/Staff/booking and clock after writer on temporary SQLite.");
        }
        finally{SQLiteConnection.ClearAllPools();foreach(var suffix in new[]{"","-wal","-shm","-journal"})if(File.Exists(file+suffix))File.Delete(file+suffix);}
    }
}
