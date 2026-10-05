using System;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxAvailabilityChecks
{
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } }
    private static DateTimeOffset Time(int hour, int minute = 0) { return new DateTimeOffset(2026,10,5,hour,minute,0,TimeSpan.FromHours(7)); }
    private static string Utc(DateTimeOffset time) { return time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture); }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid hours accepted."); }
    private static object Sql(SqliteDatabase db, string sql)
    { using (var c = db.OpenConnection()) using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; return cmd.ExecuteScalar(); } }
    private static void Booking(SqliteDatabase db, int room, int customer, DateTimeOffset start, DateTimeOffset end, string status)
    { Sql(db, "INSERT INTO Reservations(RoomId,CustomerId,StartTime,EndTime,Status,CreatedAt) VALUES("+room+","+customer+",'"+Utc(start)+"','"+Utc(end)+"','"+status+"','"+Utc(Time(9))+"');"); }
    private static void Clear(SqliteDatabase db) { Sql(db, "DELETE FROM RoomSessions; DELETE FROM Reservations;"); }
    private static void Session(SqliteDatabase db, bool walkIn, DateTimeOffset expected)
    {
        if (!walkIn) Booking(db,1,1,Time(18),Time(23),"CheckedIn");
        Sql(db, "INSERT INTO RoomSessions(RoomId,CustomerId,ReservationId,ActualStartTime,ExpectedEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES(1,1,"+
            (walkIn ? "NULL" : "(SELECT ReservationId FROM Reservations LIMIT 1)")+",'"+Utc(Time(19))+"',"+(walkIn ? "NULL" : "'"+Utc(expected)+"'")+",100000,'R1','STANDARD','Standard','Active');");
    }
    public static void Run()
    {
        var now = Time(9);
        foreach (var duration in new[] {60,90,120,180})
            Assert(BookingHours.ValidateReservation(Time(9),duration,now) == Time(9).AddMinutes(duration), "Valid duration rejected.");
        Assert(BookingHours.ValidateReservation(Time(11),60,now) == Time(12) && BookingHours.ValidateReservation(Time(22),60,now) == Time(23), "Shift-end boundary rejected.");
        foreach (var start in new[] {Time(8,30),Time(12),Time(12,30),Time(23),Time(10,15),Time(10).AddSeconds(1),Time(10).AddTicks(1),Time(11,30),Time(22,30)})
            Reject(() => BookingHours.ValidateReservation(start,60,now));
        foreach (var duration in new[] {0,-60,30,150,240}) Reject(() => BookingHours.ValidateReservation(Time(13),duration,now));
        Reject(() => BookingHours.ValidateReservation(Time(11),90,now));
        Reject(() => BookingHours.ValidateReservation(Time(9),60,Time(9).AddTicks(1)));
        Assert(BookingHours.ValidateReservation(Time(9).AddDays(30),60,now) == Time(10).AddDays(30), "Inclusive day +30 rejected.");
        Reject(() => BookingHours.ValidateReservation(Time(9).AddDays(31),60,now));
        Reject(() => BookingHours.ValidateReservation(Time(9).AddDays(-1),60,now));
        Assert(BookingHours.ValidateReservation(Time(13).ToUniversalTime(),60,now) == Time(14), "UTC instant treated as local time.");
        var midnight = Time(0,30).ToUniversalTime();
        Assert(midnight.Date != Time(0,30).Date && BookingHours.ValidateReservation(Time(9),60,midnight) == Time(10), "Vietnam date depends on UTC/host date.");
        var file = Path.Combine(Path.GetTempPath(),"MusicBoxAvailability_"+Guid.NewGuid().ToString("N")+".db");
        var db = new SqliteDatabase(file); var clock = new Clock { UtcNow = now }; var service = new AvailabilityService(db,clock);
        try
        {
            db.Initialize();
            Sql(db,"INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('R1','Room 1',1,'test.png',1,'test'),('R2','Room 2',2,'test.png',1,'test'); INSERT INTO Customers(FullName,PhoneNumber) VALUES('A','0912345678'),('B','0987654321');");
            Assert(service.CheckReservation(1,1,Time(13),60).CanBook, "Empty room rejected.");
            Assert(!service.CheckReservation(999,1,Time(13),60).CanBook && !service.CheckReservation(1,999,Time(13),60).CanBook, "Missing entity accepted.");
            Sql(db,"UPDATE Rooms SET IsActive=0,InactiveReason='Test lock' WHERE RoomId=1;");
            Assert(!service.CheckReservation(1,null,Time(13),60).CanBook,"Inactive room accepted.");
            Sql(db,"UPDATE Rooms SET IsActive=1,InactiveReason=NULL WHERE RoomId=1;");
            Booking(db,1,1,Time(20),Time(22),"Confirmed");
            Assert(service.CheckReservation(1,2,Time(17),120).CanBook && service.CheckReservation(1,2,Time(19),60).CanBook && service.CheckReservation(1,2,Time(22),60).CanBook, "Adjacent intervals overlap.");
            Assert(!service.CheckReservation(1,2,Time(19),120).CanBook && !service.CheckReservation(1,2,Time(20),60).CanBook,"Overlapping room booking accepted.");
            var customerConflict = service.CheckReservation(2,1,Time(20),60);
            Assert(!customerConflict.CanBook && customerConflict.Reason.Contains("Khách"),"Customer overlap across rooms ignored.");
            Assert(service.CheckReservation(2,2,Time(20),60).CanBook && service.CheckReservation(2,null,Time(20),60).CanBook,"Unrelated customer/new customer rejected.");
            foreach (var status in new[] {"Cancelled","NoShow","Completed","CheckedIn"})
            { Sql(db,"UPDATE Reservations SET Status='"+status+"';"); Assert(service.CheckReservation(1,1,Time(20),60).CanBook,"Historical/checked-in reservation double blocked."); }
            Clear(db); Booking(db,1,1,Time(10),Time(11),"Confirmed");
            clock.UtcNow=Time(10,15).AddTicks(-1);
            Assert(!service.CheckReservation(1,2,Time(10,30),60).CanBook,"Grace ended early.");
            clock.UtcNow=Time(10,15);
            Assert(service.CheckReservation(1,1,Time(10,30),60).CanBook,"Expired Confirmed still holds room/customer.");
            Assert((string)Sql(db,"SELECT Status FROM Reservations LIMIT 1;")=="Confirmed","Preview mutated NoShow status.");
            Clear(db); clock.UtcNow=Time(20); Session(db,false,Time(21,30));
            Assert(!service.CheckReservation(1,2,Time(21),60).CanBook && !service.CheckReservation(2,1,Time(21),60).CanBook,"Reservation session hold ignored.");
            Assert(service.CheckReservation(1,1,Time(21,30),60).CanBook && service.CheckReservation(1,1,Time(22),60).CanBook,"Expected-end/old CheckedIn interval blocks future booking.");
            Clear(db); Session(db,true,Time(21));
            Assert(!service.CheckReservation(1,2,Time(20),60).CanBook && !service.CheckReservation(2,1,Time(20),60).CanBook,"Immediate booking bypassed active walk-in room/customer.");
            Assert(service.CheckReservation(1,1,Time(20,30),60).CanBook,"Walk-in blocked future booking.");
            Clear(db); Session(db,false,Time(19,30));
            Assert(!service.CheckReservation(1,2,Time(20),60).CanBook && service.CheckReservation(1,1,Time(20,30),60).CanBook,"Overdue active session interval grew indefinitely.");
            Clear(db); clock.UtcNow=Time(9);
            // This is a contract fixture, not a full authorized booking service.
            using (var gate = new ManualResetEventSlim(false))
            {
                Func<int,Task<bool>> attempt = customer => Task.Run(() => {
                    gate.Wait(); using(var c=db.OpenConnection()) using(var tx=SqliteDatabase.BeginWriteTransaction(c))
                    {
                        var result=service.CheckReservation(c,tx,1,customer,Time(20),60);
                        if(!result.CanBook)return false;
                        using(var cmd=c.CreateCommand()) { cmd.Transaction=tx; cmd.CommandText="INSERT INTO Reservations(RoomId,CustomerId,StartTime,EndTime,Status,CreatedAt) VALUES(1,"+customer+",'"+Utc(Time(20))+"','"+Utc(Time(21))+"','Confirmed','"+Utc(Time(9))+"');";cmd.ExecuteNonQuery(); }
                        tx.Commit(); return true;
                    }
                });
                var a=attempt(1);var b=attempt(2);gate.Set();Task.WaitAll(a,b);
                Assert(a.Result!=b.Result && Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM Reservations;"))==1,"Two transaction writers reserved the same room.");
            }
            using(var c=db.OpenConnection())
            { Reject(()=>service.CheckReservation(c,null,1,1,Time(13),60)); }
            Assert(Convert.ToInt64(Sql(db,"PRAGMA user_version;"))==6,"Availability changed schema.");
            Console.WriteLine("PASS Availability: Vietnam dates/shifts/slots/durations, room/customer overlap, grace tick boundaries, session/walk-in/overdue rules and transaction writers on temporary SQLite.");
        }
        finally { SQLiteConnection.ClearAllPools(); if(File.Exists(file))File.Delete(file); }
    }
}
