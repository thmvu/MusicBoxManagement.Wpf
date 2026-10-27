using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MySqlConnector;

public static class MusicBoxMySqlGuestSessionsChecks
{
    private sealed class Clock : IClock
    {
        public DateTimeOffset Value; public int Reads; public Action<int> Hook;
        public DateTimeOffset UtcNow { get { var n=Interlocked.Increment(ref Reads); if(Hook!=null)Hook(n); return Value; } }
    }
    private sealed class GateClock : IClock, IDisposable
    {
        public DateTimeOffset Value;
        public readonly ManualResetEventSlim Entered=new ManualResetEventSlim(false), Release=new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow { get { Entered.Set(); if(!Release.Wait(10000))throw new Exception("Writer gate timeout"); return Value; } }
        public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
    }
    private static DateTimeOffset At(int h,int m=0) { return new DateTimeOffset(2026,10,28,h,m,0,TimeSpan.FromHours(7)); }
    private static string Utc(DateTimeOffset t) { return t.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture); }
    private static object Sql(MySqlDatabase db,string sql)
    { using(var c=db.OpenConnection())using(var q=c.CreateCommand()){q.CommandText=sql;return q.ExecuteScalar();} }
    private static long Count(MySqlDatabase db,string sql) { return Convert.ToInt64(Sql(db,sql)); }
    private static void Assert(bool ok,string message) { if(!ok)throw new Exception(message); }
    private static T Reject<T>(Action action) where T:Exception
    { try{action();}catch(T e){return e;}throw new Exception("Expected "+typeof(T).Name); }
    private static bool Attempt(Action action) { try{action();return true;}catch(InvalidOperationException){return false;} }
    private static void Race(Action a,Action b)
    {
        using(var gate=new ManualResetEventSlim(false))
        {
            var x=Task.Run(()=>{gate.Wait();return Attempt(a);});var y=Task.Run(()=>{gate.Wait();return Attempt(b);});
            gate.Set();Task.WaitAll(x,y);Assert(x.Result!=y.Result,"Concurrent writers did not have exactly one winner");
        }
    }
    private static Reservation Book(MySqlDatabase db,int room,string phone,int hour,int minute=0)
    { return new MySqlReservationService(db,new Clock{Value=At(9)}).CreateGuest(new ReservationRequest{RoomId=room,FullName="Tên riêng không public",PhoneNumber=phone,StartTime=At(hour,minute),DurationMinutes=60}); }
    private static void Reset(MySqlDatabase db)
    {
        Sql(db,"DELETE FROM RoomSessions;DELETE FROM Reservations;DELETE FROM Customers;DELETE FROM AuditLog;UPDATE RoomTypes SET Name='Standard',PricePerHour=120000 WHERE RoomTypeId=1;");
    }
    private static RoomSession Start(MySqlDatabase db,LoginSession admin,Clock clock,int hour=13,int minute=0)
    {
        Reset(db);var b=Book(db,1,"0901111111",hour,minute);clock.Value=At(hour,minute);
        return new MySqlRoomSessionService(db,clock).CheckIn(admin,b.ReservationId);
    }
    private static DateTimeOffset End(MySqlDatabase db,int id)
    { return DateTimeOffset.ParseExact((string)Sql(db,"SELECT ExpectedEndTime FROM RoomSessions WHERE RoomSessionId="+id),"O",CultureInfo.InvariantCulture); }
    private static void ChangePhone(MySqlDatabase db,LoginSession admin,string oldPhone,string newPhone)
    {
        var customers=new MySqlCustomerService(db);var current=customers.Search(admin,null,oldPhone).Items.Single();
        customers.Save(admin,current,new CustomerEdit{FullName=current.FullName,PhoneNumber=newPhone});
    }

    public static void Run()
    {
        var name="musicbox_test_"+Guid.NewGuid().ToString("N");var db=new MySqlDatabase(name);
        try
        {
            var admin=new MySqlAuthenticationService(db).SetupAdminAsync("admin","Admin test","Guest-MySql-2026!").GetAwaiter().GetResult();
            for(var i=1;i<=3;i++)Sql(db,"INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,CreatedAt) VALUES("+i+",'G"+i+"',1,'Phòng "+i+"','test.png',1,'"+Utc(At(9))+"');");
            var clock=new Clock{Value=At(9)};var guest=new MySqlGuestSessionService(db,clock);var lookup=new MySqlGuestReservationService(db,clock);
            SessionReadsAndExtension(db,admin,clock,guest,lookup);
            Console.WriteLine("PASS Guest session reads, extension, snapshots, rollback and walk-in guards");
            Cancellation(db,admin,clock,lookup);
            Console.WriteLine("PASS Guest booking lookup, cancellation, exact cutoff/grace and rollback");
            ConcurrentWriters(db,admin,clock,guest,lookup);
            Console.WriteLine("PASS Guest/Staff/booking/check-in/NoShow concurrent writers");
            SnapshotsAndWaits(db,admin,clock,guest,lookup);
            Assert(Count(db,"SELECT Version FROM _MusicBoxSchemaVersion;")==MySqlDatabase.CurrentSchemaVersion,"Schema changed");
            Console.WriteLine("PASS MySQL Guest: current normalized phone and public DTOs; Active/bookings one snapshot, separate NoShow; +30/+60/ticks/stale/shift/Room-Customer holds; exact cancel/grace; Guest audit rollback; duplicate/Staff/booking/check-in/NoShow writers; phone edits and fresh clock after waits. GUID DB only; no checkout/provider/import claims.");
        }
        finally
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"\Amusicbox_test_[0-9a-f]{32}\z"))throw new Exception("Unsafe cleanup");
            Sql(new MySqlDatabase("defaultdb"),"DROP DATABASE IF EXISTS `"+name+"`");
        }
    }

    private static void SessionReadsAndExtension(MySqlDatabase db,LoginSession admin,Clock clock,MySqlGuestSessionService guest,MySqlGuestReservationService lookup)
    {
        Reset(db);var b=Book(db,1,"0901111111",13);clock.Value=At(13).AddSeconds(12).AddTicks(1234567);
        var session=new MySqlRoomSessionService(db,clock).CheckIn(admin,b.ReservationId);var old=session.ExpectedEndTime.Value;
        var future=Book(db,2,"0901111111",18);var audit=Count(db,"SELECT COUNT(*) FROM AuditLog;");clock.Reads=0;
        var found=guest.Lookup("+84 901.111-111");
        Assert(clock.Reads==1&&found.SessionId==session.RoomSessionId&&found.ExpectedEndTime==old&&found.ReturnBy==old&&found.ActualStartTime==session.ActualStartTime&&found.CanExtend,"Lookup lost ticks/ownership/single clock");
        clock.Reads=0;var all=lookup.Lookup("84901111111");
        Assert(clock.Reads==2&&all.Items.Single().ReservationId==future.ReservationId&&all.ActiveSession.SessionId==session.RoomSessionId&&all.CheckedAt==all.ActiveSession.CheckedAt,"Combined lookup missing Active/bookings/read clock");
        Assert(guest.Lookup("0902222222")==null&&lookup.Lookup("0902222222").Items.Count==0&&Count(db,"SELECT COUNT(*) FROM Customers;")==1,"Unknown phone leaked or created customer");
        foreach(var property in new[]{"CustomerId","RoomId","ReservationId","FullName","PhoneNumber","Session"})
            Assert(typeof(GuestSession).GetProperty(property)==null,"Public DTO leak: "+property);
        Assert(typeof(GuestReservation).GetProperty("CustomerId")==null&&typeof(GuestReservation).GetProperty("PhoneNumber")==null,"Public booking DTO leak");
        Reject<ArgumentException>(()=>guest.Lookup("bad"));Reject<ArgumentException>(()=>lookup.Lookup(""));Reject<ArgumentException>(()=>lookup.Cancel("bad",b.ReservationId));
        var foreign=Reject<InvalidOperationException>(()=>guest.PreviewExtension("0902222222",session.RoomSessionId,30));
        Assert(foreign.Message==Reject<InvalidOperationException>(()=>guest.PreviewExtension("0902222222",999999,30)).Message,"Foreign ID disclosed existence");
        Assert(Reject<InvalidOperationException>(()=>guest.Extend("0902222222",session.RoomSessionId,old,30)).Message==Reject<InvalidOperationException>(()=>guest.Extend("0902222222",999999,old,30)).Message,"Write ID disclosed existence");
        Reject<ArgumentException>(()=>guest.PreviewExtension("0901111111",session.RoomSessionId,45));Reject<ArgumentException>(()=>guest.Extend("0901111111",session.RoomSessionId,old,0));
        var check=guest.PreviewExtension("0901111111",session.RoomSessionId,30);
        Assert(check.CanExtend&&check.NewEndTime==old.AddMinutes(30)&&Count(db,"SELECT COUNT(*) FROM AuditLog;")==audit,"Preview wrote data/audit");
        Sql(db,"UPDATE RoomTypes SET Name='Tên mới',PricePerHour=999999 WHERE RoomTypeId=1;");clock.Reads=0;
        found=guest.Extend("+84 901111111",session.RoomSessionId,old.ToOffset(TimeSpan.FromHours(7)),30);
        Assert(clock.Reads==1&&found.ExpectedEndTime==old.AddMinutes(30)&&found.HourlyRate==session.HourlyRate&&found.RoomTypeName==session.RoomTypeNameSnapshot&&found.ActualStartTime==session.ActualStartTime,"Extension changed snapshot/ticks or clock");
        Assert((string)Sql(db,"SELECT EndTime FROM Reservations WHERE ReservationId="+b.ReservationId)==Utc(b.EndTime),"Guest extension changed source booking");
        Assert(Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Session.Extend' AND ActorType='Guest' AND UserId IS NULL;")==1,"Guest audit missing");
        Reject<InvalidOperationException>(()=>guest.Extend("0901111111",session.RoomSessionId,old,30));
        found=guest.Extend("0901111111",session.RoomSessionId,found.ExpectedEndTime.Value,60);Assert(found.ExpectedEndTime==old.AddMinutes(90),"Repeated +60 failed");
        ChangePhone(db,admin,"0901111111","0909999999");
        Assert(guest.Lookup("0901111111")==null&&guest.Lookup("0909999999").SessionId==session.RoomSessionId&&lookup.Lookup("0901111111").Items.Count==0&&lookup.Lookup("0909999999").Items.Count==1,"Current phone not used");
        Reject<InvalidOperationException>(()=>guest.PreviewExtension("0901111111",session.RoomSessionId,30));Reject<InvalidOperationException>(()=>guest.Extend("0901111111",session.RoomSessionId,found.ExpectedEndTime.Value,30));

        session=Start(db,admin,clock);old=session.ExpectedEndTime.Value;audit=Count(db,"SELECT COUNT(*) FROM AuditLog;");
        Sql(db,"CREATE TRIGGER FailGuestExtend BEFORE INSERT ON AuditLog FOR EACH ROW BEGIN IF NEW.Action='Session.Extend' AND NEW.ActorType='Guest' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='test failure'; END IF; END;");
        Reject<MySqlException>(()=>guest.Extend("0901111111",session.RoomSessionId,old,30));
        Assert(End(db,session.RoomSessionId)==old&&Count(db,"SELECT COUNT(*) FROM AuditLog;")==audit,"Audit failure left partial extension");Sql(db,"DROP TRIGGER FailGuestExtend;");
        clock.Value=old;found=guest.Extend("0901111111",session.RoomSessionId,old,30);Assert(found.ExpectedEndTime==old.AddMinutes(30),"Exact end denied");
        clock.Value=found.ExpectedEndTime.Value.AddTicks(1);Assert(!guest.Lookup("0901111111").CanExtend&&!guest.PreviewExtension("0901111111",session.RoomSessionId,30).CanExtend,"Overdue allowed");
        Reject<SessionExtensionException>(()=>guest.Extend("0901111111",session.RoomSessionId,found.ExpectedEndTime.Value,30));
        foreach(var h in new[]{11,22})
        { session=Start(db,admin,clock,h);check=guest.PreviewExtension("0901111111",session.RoomSessionId,30);Assert(!check.CanExtend&&check.MaximumEndTime==At(h+1),"Shift boundary ignored"); }
        foreach(var customerConflict in new[]{false,true})
        {
            session=Start(db,admin,clock);Book(db,customerConflict?2:1,customerConflict?"0901111111":"0902222222",14,30);
            check=guest.PreviewExtension("0901111111",session.RoomSessionId,60);
            Assert(!check.CanExtend&&check.MaximumEndTime==At(14,30)&&!check.Reason.Contains("090")&&!check.Reason.Contains("Tên riêng"),"Overlap limit wrong or leaked PII");
            Reject<SessionExtensionException>(()=>guest.Extend("0901111111",session.RoomSessionId,session.ExpectedEndTime.Value,60));
        }
        session=Start(db,admin,clock);var expired=Book(db,3,"0902222222",13);
        clock.Value=At(13,15);audit=Count(db,"SELECT COUNT(*) FROM AuditLog;");
        Assert(guest.PreviewExtension("0901111111",session.RoomSessionId,30).CanExtend,"Unrelated expired booking blocked extension");guest.Lookup("0901111111");
        Assert((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+expired.ReservationId)=="Confirmed"&&Count(db,"SELECT COUNT(*) FROM AuditLog;")==audit,"Pure session reads ran NoShow/audit");
        all=lookup.Lookup("0901111111");Assert(all.ActiveSession!=null&&(string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+expired.ReservationId)=="NoShow","Combined lookup skipped separate maintenance");

        Reset(db);clock.Value=At(16);var walk=new MySqlRoomSessionService(db,clock).CreateWalkIn(admin,new WalkInRequest{RoomId=1,FullName="Walk-in",PhoneNumber="0901111111"});
        Assert(!guest.Lookup("0901111111").FromBooking&&!guest.Lookup("0901111111").CanExtend&&guest.Lookup("0901111111").ReturnBy==At(23),"Walk-in projection wrong");
        Book(db,1,"0902222222",18);Assert(lookup.Lookup("0901111111").ActiveSession.ReturnBy==At(18),"Walk-in Room ReturnBy stale");
        Book(db,2,"0901111111",17);Assert(guest.Lookup("0901111111").ReturnBy==At(17),"Walk-in Customer ReturnBy stale");
        Reject<InvalidOperationException>(()=>guest.PreviewExtension("0901111111",walk.Session.RoomSessionId,30));Reject<InvalidOperationException>(()=>guest.Extend("0901111111",walk.Session.RoomSessionId,At(17),30));
        // Read/guard fixture only: MySQL checkout is not ported or claimed by this test.
        Sql(db,"UPDATE RoomSessions SET Status='Completed',ActualEndTime='"+Utc(At(16,30))+"' WHERE RoomSessionId="+walk.Session.RoomSessionId+";");
        Assert(guest.Lookup("0901111111")==null&&lookup.Lookup("0901111111").ActiveSession==null,"Completed leaked as Active");
        Reject<InvalidOperationException>(()=>guest.Extend("0901111111",walk.Session.RoomSessionId,At(17),30));
    }

    private static void Cancellation(MySqlDatabase db,LoginSession admin,Clock clock,MySqlGuestReservationService lookup)
    {
        Reset(db);clock.Value=At(9);var first=Book(db,1,"0901111111",13);var second=Book(db,1,"0901111111",15);var other=Book(db,2,"0902222222",13);
        Assert(lookup.Lookup("+84 901.111-111").Items.Select(i=>i.ReservationId).SequenceEqual(new[]{first.ReservationId,second.ReservationId}),"Booking order/normalization wrong");
        Assert(Reject<InvalidOperationException>(()=>lookup.Cancel("0902222222",first.ReservationId)).Message==Reject<InvalidOperationException>(()=>lookup.Cancel("0902222222",999999)).Message,"Foreign booking existence leaked");
        clock.Value=At(11).AddTicks(1);Assert(!lookup.Lookup("0901111111").Items.First().CanCancel,"One tick after cutoff enabled cancel");Reject<InvalidOperationException>(()=>lookup.Cancel("0901111111",first.ReservationId));
        clock.Value=At(11);var audit=Count(db,"SELECT COUNT(*) FROM AuditLog;");
        Sql(db,"CREATE TRIGGER FailGuestCancel BEFORE INSERT ON AuditLog FOR EACH ROW BEGIN IF NEW.Action='Reservation.Cancel' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='test failure'; END IF; END;");
        Reject<MySqlException>(()=>lookup.Cancel("0901111111",first.ReservationId));
        Assert((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+first.ReservationId)=="Confirmed"&&Sql(db,"SELECT CancellationReason FROM Reservations WHERE ReservationId="+first.ReservationId)==DBNull.Value&&Count(db,"SELECT COUNT(*) FROM AuditLog;")==audit,"Cancel audit rollback failed");Sql(db,"DROP TRIGGER FailGuestCancel;");
        lookup.Cancel("84901111111",first.ReservationId);Reject<InvalidOperationException>(()=>lookup.Cancel("0901111111",first.ReservationId));
        Assert((string)Sql(db,"SELECT CancellationReason FROM Reservations WHERE ReservationId="+first.ReservationId)=="Customer cancelled online"&&Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.Cancel' AND ActorType='Guest' AND UserId IS NULL;")==1,"Cancel exact boundary/reason/audit wrong");
        ChangePhone(db,admin,"0901111111","0909999999");Reject<InvalidOperationException>(()=>lookup.Cancel("0901111111",second.ReservationId));lookup.Cancel("0909999999",second.ReservationId);
        clock.Value=other.StartTime.AddMinutes(15).AddTicks(-1);Assert(lookup.Lookup("0902222222").Items.Count==1,"Grace ended early");
        clock.Value=other.StartTime.AddMinutes(15);Assert(lookup.Lookup("0902222222").Items.Count==0&&(string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+other.ReservationId)=="NoShow","Exact grace missing NoShow");
        var logs=Count(db,"SELECT COUNT(*) FROM AuditLog;");lookup.Lookup("0902222222");Assert(Count(db,"SELECT COUNT(*) FROM AuditLog;")==logs,"Repeated lookup duplicated NoShow");Reject<InvalidOperationException>(()=>lookup.Cancel("0902222222",other.ReservationId));
        Reset(db);var b=Book(db,1,"0901111111",13);clock.Value=At(9);
        new MySqlRoomSessionService(db,clock).CheckIn(admin,b.ReservationId);Reject<InvalidOperationException>(()=>lookup.Cancel("0901111111",b.ReservationId));
        Assert(lookup.Lookup("0901111111").Items.Count==0&&lookup.Lookup("0901111111").ActiveSession!=null,"CheckedIn listed as Confirmed");
    }

    private static void ConcurrentWriters(MySqlDatabase db,LoginSession admin,Clock clock,MySqlGuestSessionService guest,MySqlGuestReservationService lookup)
    {
        var s=Start(db,admin,clock);var observed=s.ExpectedEndTime.Value;var logs=Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Session.Extend';");
        Race(()=>guest.Extend("0901111111",s.RoomSessionId,observed,30),()=>guest.Extend("0901111111",s.RoomSessionId,observed,30));
        Assert(End(db,s.RoomSessionId)==observed.AddMinutes(30)&&Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Session.Extend';")==logs+1,"Duplicate Guest extension committed twice");
        observed=End(db,s.RoomSessionId);
        Race(()=>guest.Extend("0901111111",s.RoomSessionId,observed,30),()=>new MySqlRoomSessionService(db,clock).ExtendStaff(admin,s.RoomSessionId,observed,30));
        Assert(End(db,s.RoomSessionId)==observed.AddMinutes(30),"Guest/Staff extended twice");
        s=Start(db,admin,clock);observed=s.ExpectedEndTime.Value;
        Race(()=>guest.Extend("0901111111",s.RoomSessionId,observed,30),()=>Book(db,1,"0902222222",14));
        Assert(End(db,s.RoomSessionId)==observed||Count(db,"SELECT COUNT(*) FROM Reservations WHERE Status='Confirmed';")==0,"Room race left overlapping holds");
        s=Start(db,admin,clock);observed=s.ExpectedEndTime.Value;
        Race(()=>guest.Extend("0901111111",s.RoomSessionId,observed,30),()=>Book(db,2,"0901111111",14));
        Assert(End(db,s.RoomSessionId)==observed||Count(db,"SELECT COUNT(*) FROM Reservations WHERE Status='Confirmed';")==0,"Customer race left overlapping holds");
        Reset(db);clock.Value=At(9);var b=Book(db,1,"0901111111",13);
        Race(()=>lookup.Cancel("0901111111",b.ReservationId),()=>lookup.Cancel("0901111111",b.ReservationId));
        Assert(Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.Cancel';")==1,"Duplicate cancel audit");
        Reset(db);b=Book(db,1,"0901111111",13);
        Race(()=>lookup.Cancel("0901111111",b.ReservationId),()=>new MySqlRoomSessionService(db,clock).CheckIn(admin,b.ReservationId));
        Assert(Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action IN ('Reservation.Cancel','Session.CheckIn');")==1,"Cancel/check-in both committed");

        // Both lock orders with the real NoShow writer, using a clock gate after locks.
        Reset(db);b=Book(db,1,"0901111111",13);
        using(var gate=new GateClock{Value=At(11)})
        {
            var cancel=Task.Run(()=>new MySqlGuestReservationService(db,gate).Cancel("0901111111",b.ReservationId));
            try
            {
                Assert(gate.Entered.Wait(10000),"Cancel never acquired locks");
                var worker=Task.Run(()=>new MySqlNoShowService(db,new Clock{Value=At(13,15)}).ProcessExpired());
                Thread.Sleep(150);gate.Release.Set();cancel.GetAwaiter().GetResult();Assert(worker.GetAwaiter().GetResult()==0,"NoShow overwrote cancelled source");
            }
            finally { gate.Release.Set();cancel.GetAwaiter().GetResult(); }
        }
        Reset(db);b=Book(db,1,"0901111111",13);
        using(var gate=new GateClock{Value=At(13,15)})
        {
            var worker=Task.Run(()=>new MySqlNoShowService(db,gate).ProcessExpired());
            try
            {
                Assert(gate.Entered.Wait(10000),"NoShow never acquired locks");
                var cancel=Task.Run(()=>Attempt(()=>lookup.Cancel("0901111111",b.ReservationId)));
                Thread.Sleep(150);gate.Release.Set();Assert(worker.GetAwaiter().GetResult()==1&&!cancel.GetAwaiter().GetResult(),"Cancel overwrote NoShow source");
            }
            finally { gate.Release.Set();worker.GetAwaiter().GetResult(); }
        }
    }

    private static void SnapshotsAndWaits(MySqlDatabase db,LoginSession admin,Clock clock,MySqlGuestSessionService guest,MySqlGuestReservationService lookup)
    {
        var s=Start(db,admin,clock);Book(db,2,"0901111111",18);clock.Reads=0;
        clock.Hook=n=>{if(n==2){clock.Hook=null;ChangePhone(db,admin,"0901111111","0909999999");}};
        var all=lookup.Lookup("0901111111");
        Assert(all.ActiveSession!=null&&all.Items.Count==1,"Combined read split membership across snapshots");
        Assert(lookup.Lookup("0901111111").ActiveSession==null&&lookup.Lookup("0901111111").Items.Count==0,"Next lookup ignored committed phone edit");
        clock.Reads=0;clock.Hook=n=>{clock.Hook=null;ChangePhone(db,admin,"0909999999","0901111111");};
        Assert(guest.Lookup("0909999999")!=null&&guest.Lookup("0909999999")==null,"Session read escaped snapshot");
        var observed=s.ExpectedEndTime.Value;clock.Value=observed.AddMinutes(-1);clock.Reads=0;
        using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))using(var q=c.CreateCommand())
        {
            q.Transaction=tx;q.CommandText="SELECT RoomId FROM Rooms WHERE RoomId=1 FOR UPDATE;";q.ExecuteScalar();
            var waiting=Task.Run(()=>Attempt(()=>guest.Extend("0901111111",s.RoomSessionId,observed,30)));
            Thread.Sleep(200);Assert(!waiting.IsCompleted&&clock.Reads==0,"Guest clock taken before Room lock");clock.Value=observed.AddTicks(1);tx.Commit();
            Assert(!waiting.GetAwaiter().GetResult()&&clock.Reads==1&&End(db,s.RoomSessionId)==observed,"Guest used pre-wait end clock");
        }
        s=Start(db,admin,clock);observed=s.ExpectedEndTime.Value;clock.Reads=0;
        using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))using(var q=c.CreateCommand())
        {
            q.Transaction=tx;q.CommandText="SELECT RoomId FROM Rooms WHERE RoomId=1 FOR UPDATE;";q.ExecuteScalar();
            var waiting=Task.Run(()=>Attempt(()=>guest.Extend("0901111111",s.RoomSessionId,observed,30)));
            Thread.Sleep(200);ChangePhone(db,admin,"0901111111","0909999999");tx.Commit();
            Assert(!waiting.GetAwaiter().GetResult()&&End(db,s.RoomSessionId)==observed,"Extension trusted discovery phone after edit");
        }
        Reset(db);var b=Book(db,1,"0901111111",13);clock.Value=At(11);clock.Reads=0;
        using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))using(var q=c.CreateCommand())
        {
            q.Transaction=tx;q.CommandText="SELECT RoomId FROM Rooms WHERE RoomId=1 FOR UPDATE;";q.ExecuteScalar();
            var waiting=Task.Run(()=>Attempt(()=>lookup.Cancel("0901111111",b.ReservationId)));
            Thread.Sleep(200);Assert(!waiting.IsCompleted&&clock.Reads==0,"Cancel clock taken before Room lock");clock.Value=At(11).AddTicks(1);tx.Commit();
            Assert(!waiting.GetAwaiter().GetResult()&&clock.Reads==1&&(string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+b.ReservationId)=="Confirmed","Cancel used pre-wait cutoff");
        }
        clock.Value=At(9);
        using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))using(var q=c.CreateCommand())
        {
            q.Transaction=tx;q.CommandText="SELECT RoomId FROM Rooms WHERE RoomId=1 FOR UPDATE;";q.ExecuteScalar();
            var waiting=Task.Run(()=>Attempt(()=>lookup.Cancel("0901111111",b.ReservationId)));
            Thread.Sleep(200);ChangePhone(db,admin,"0901111111","0909999999");tx.Commit();
            Assert(!waiting.GetAwaiter().GetResult()&&(string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+b.ReservationId)=="Confirmed","Cancel trusted discovery phone after edit");
        }
    }
}
