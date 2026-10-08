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

public static class MusicBoxGuestLookupChecks
{
    private sealed class Clock:IClock { public DateTimeOffset UtcNow {get;set;} }
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static object Sql(SqliteDatabase db,string sql){using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=sql;return cmd.ExecuteScalar();}}
    private static void Reject(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("Expected rejection.");}
    private static ReservationRequest Request(int room,int hour,string phone){return new ReservationRequest{RoomId=room,StartTime=new DateTimeOffset(2026,10,5,hour,0,0,TimeSpan.FromHours(7)),DurationMinutes=60,FullName="Khách thử nghiệm",PhoneNumber=phone};}
    public static void Run()
    {
        var file=Path.Combine(Path.GetTempPath(),"MusicBoxGuestLookup_"+Guid.NewGuid().ToString("N")+".db");
        var db=new SqliteDatabase(file);var clock=new Clock{UtcNow=new DateTimeOffset(2026,10,5,2,0,0,TimeSpan.Zero)};
        try
        {
            db.Initialize();Sql(db,"INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('R1','Room 1',1,'test.png',1,'test'),('R2','Room 2',2,'test.png',1,'test');");
            var create=new ReservationService(db,clock);var service=new GuestReservationService(db,clock);
            var first=create.CreateGuest(Request(1,13,"0912345678"));
            var second=create.CreateGuest(Request(1,15,"0912345678"));
            var other=create.CreateGuest(Request(2,13,"0987654321"));
            foreach(var phone in new[]{"0912345678","+84 912.345-678","84912345678"})
                Assert(service.Lookup(phone).Items.Select(x=>x.ReservationId).SequenceEqual(new[]{first.ReservationId,second.ReservationId}),"Phone lookup leaked/missed bookings.");
            Assert(service.Lookup("0900000000").Items.Count==0 && Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM Customers;"))==2,"Unknown lookup created a customer.");
            try{service.Lookup("");throw new Exception("Empty phone listed bookings.");}catch(ArgumentException){}
            Assert(typeof(GuestReservation).GetProperty("CustomerId")==null && typeof(GuestReservation).GetProperty("PhoneNumber")==null,"Public DTO exposed customer fields.");
            Reject(()=>service.Cancel("0987654321",first.ReservationId));Reject(()=>service.Cancel("0912345678",other.ReservationId));
            clock.UtcNow=first.StartTime.AddHours(-2).AddTicks(1);
            Assert(!service.Lookup("0912345678").Items.First().CanCancel,"One tick after boundary allowed cancellation.");
            Reject(()=>service.Cancel("0912345678",first.ReservationId));
            clock.UtcNow=first.StartTime.AddHours(-2);
            Sql(db,"CREATE TRIGGER FailCancelAudit BEFORE INSERT ON AuditLog WHEN NEW.Action='Reservation.Cancel' BEGIN SELECT RAISE(ABORT,'test failure'); END;");
            try{service.Cancel("0912345678",first.ReservationId);throw new Exception("Audit failure committed.");}catch(SQLiteException){}
            Assert((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+first.ReservationId)=="Confirmed" && Sql(db,"SELECT CancellationReason FROM Reservations WHERE ReservationId="+first.ReservationId)==DBNull.Value,"Audit failure did not roll back cancellation.");
            Sql(db,"DROP TRIGGER FailCancelAudit;");service.Cancel("+84 912345678",first.ReservationId);
            Assert((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+first.ReservationId)=="Cancelled" && (string)Sql(db,"SELECT CancellationReason FROM Reservations WHERE ReservationId="+first.ReservationId)=="Customer cancelled online","Exact boundary/reason failed.");
            Assert(Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.Cancel' AND ActorType='Guest' AND UserId IS NULL;"))==1,"Guest cancellation audit missing.");
            Reject(()=>service.Cancel("0912345678",first.ReservationId));
            Assert(service.Lookup("0912345678").Items.Count==1,"Cancelled booking remained public.");
            Sql(db,"UPDATE Customers SET PhoneNumber='0901234567' WHERE PhoneNumber='0912345678';");
            Assert(service.Lookup("0912345678").Items.Count==0 && service.Lookup("0901234567").Items.Count==1,"Lookup ignored changed customer phone.");
            Reject(()=>service.Cancel("0912345678",second.ReservationId));
            var vm=new GuestLookupViewModel(service){PhoneNumber="0901234567"};vm.SearchAsync().GetAwaiter().GetResult();vm.Selected=vm.Items.Single();
            vm.RequestCancel();vm.KeepBooking();Assert(!vm.IsConfirming && vm.CanCancel,"Keeping booking did not exit confirmation.");
            vm.PhoneNumber="0987654321";Assert(vm.Items.Count==0 && vm.Selected==null && !vm.CanCancel,"Phone change retained another customer's results.");
            var gate=new ManualResetEventSlim(false);var successes=0;var rejected=0;
            var attempts=Enumerable.Range(0,2).Select(i=>Task.Run(()=>{gate.Wait();try{service.Cancel("0901234567",second.ReservationId);Interlocked.Increment(ref successes);}catch(InvalidOperationException){Interlocked.Increment(ref rejected);}})).ToArray();
            gate.Set();Task.WaitAll(attempts);gate.Dispose();Assert(successes==1 && rejected==1,"Concurrent cancellations committed twice.");
            Assert(Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.Cancel';"))==2,"Concurrent cancellation duplicated log.");
            clock.UtcNow=other.StartTime.AddMinutes(15).AddTicks(-1);Assert(service.Lookup("0987654321").Items.Count==1,"Grace ended early.");
            clock.UtcNow=other.StartTime.AddMinutes(15);Assert(service.Lookup("0987654321").Items.Count==0 && (string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+other.ReservationId)=="NoShow","Exact grace lookup did not process NoShow.");
            Reject(()=>service.Cancel("0987654321",other.ReservationId));
            Assert(Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM Reservations;"))==3 && Convert.ToInt64(Sql(db,"PRAGMA user_version;"))== SqliteDatabase.CurrentSchemaVersion,"Cancellation deleted bookings/changed schema.");
            // A stale lookup must re-check status at write time.
            clock.UtcNow=new DateTimeOffset(2026,10,5,2,0,0,TimeSpan.Zero);
            var stale=create.CreateGuest(Request(2,17,"0987654321"));Assert(service.Lookup("0987654321").Items.Count==1,"Stale fixture lookup failed.");
            Sql(db,"UPDATE Reservations SET Status='CheckedIn' WHERE ReservationId="+stale.ReservationId);Reject(()=>service.Cancel("0987654321",stale.ReservationId));
            Assert(service.Lookup("0987654321").Items.Count==0,"CheckedIn booking exposed as Confirmed.");
            var replacement=create.CreateGuest(Request(1,13,"0901111111"));
            Assert(replacement.ReservationId!=first.ReservationId && (string)Sql(db,"SELECT StartTime FROM Reservations WHERE ReservationId="+first.ReservationId)==first.StartTime.ToUniversalTime().ToString("O"),"Cancel did not release slot or altered original interval.");
            create.CreateGuest(Request(1,10,"0902222222"));
            var nextDay=Request(1,13,"0902222222");nextDay.StartTime=nextDay.StartTime.AddDays(1);
            var future=create.CreateGuest(nextDay);
            vm=new GuestLookupViewModel(service){PhoneNumber="0902222222"};vm.SearchAsync().GetAwaiter().GetResult();
            vm.Selected=vm.Items.Single(x=>x.ReservationId==future.ReservationId);vm.RequestCancel();
            Sql(db,"CREATE TRIGGER FailRefreshNoShow BEFORE INSERT ON AuditLog WHEN NEW.Action='Reservation.NoShow' BEGIN SELECT RAISE(ABORT,'test failure'); END;");
            clock.UtcNow=clock.UtcNow.AddHours(2);
            Assert(vm.ConfirmCancelAsync().GetAwaiter().GetResult() && vm.Items.Count==0 && vm.Status.Contains("Đã hủy") && vm.Status.Contains("chưa tải"),"Post-commit refresh failure suggested retrying cancellation.");
            Assert((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+future.ReservationId)=="Cancelled","Refresh failure undid successful commit.");
            Sql(db,"DROP TRIGGER FailRefreshNoShow;");
            Console.WriteLine("PASS Guest lookup: canonical phone/ownership/current phone, effective Confirmed/grace/NoShow, exact cancellation boundary, audit rollback, duplicate/concurrent writes, stale state, VM privacy and schema on temporary SQLite.");
        }
        finally{SQLiteConnection.ClearAllPools();if(File.Exists(file))File.Delete(file);}
    }
}
