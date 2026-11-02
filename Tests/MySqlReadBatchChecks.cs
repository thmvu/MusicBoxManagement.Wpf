using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MySqlConnector;

public static class MusicBoxMySqlReadBatchChecks
{
    private sealed class Clock : IClock { public DateTimeOffset Now; public DateTimeOffset UtcNow { get { return Now; } } }
    private static object Sql(MySqlConnection c, string sql, MySqlTransaction tx = null)
    { using (var q=c.CreateCommand()) { q.Transaction=tx;q.CommandText=sql;return q.ExecuteScalar(); } }
    private static long Selects(MySqlConnection c,MySqlTransaction tx)
    {using(var q=c.CreateCommand()){q.Transaction=tx;q.CommandText="SHOW SESSION STATUS LIKE 'Com_select'";using(var r=q.ExecuteReader()){r.Read();return long.Parse(r.GetString(1));}}}
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static string Utc(DateTimeOffset t){return t.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);}
    public static void Run()
    {
        var name="musicbox_test_"+Guid.NewGuid().ToString("N");var db=new MySqlDatabase(name);
        var now=new DateTimeOffset(2026,11,2,9,0,0,TimeSpan.FromHours(7));var clock=new Clock{Now=now};
        Console.WriteLine("Read batch fixture: "+name);
        try {
            db.Initialize();
            using(var c=db.OpenConnection()) {
                Sql(c,"INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,CreatedAt) VALUES(1,'BATCH',1,'Batch','Content/uploads/rooms/11111111111111111111111111111111.png',1,'"+Utc(now)+"'); INSERT INTO Customers(CustomerId,FullName,PhoneNumber) VALUES(1,'Batch','0000000001');");
                Sql(c,"INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,CreatedAt) VALUES"+string.Join(",",Enumerable.Range(2,20).Select(id=>"("+id+",'B"+id+"',1,'Batch','Content/uploads/rooms/11111111111111111111111111111111.png',1,'"+Utc(now)+"')")));
                using(var tx=MySqlDatabase.BeginReadTransaction(c)) {
                    var before=Selects(c,tx);
                    var rooms=(System.Collections.IList)typeof(MySqlDashboardService).GetMethod("ReadRooms",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{c,tx,now});
                    var count=Selects(c,tx)-before;
                    Assert(rooms.Count==21&&count==1,"Dashboard room N+1: "+count+" SELECTs for21rooms (budget1).");
                    Console.WriteLine("Dashboard room SELECTs: "+count+" for21rooms.");
                    var schedules=Enumerable.Range(1,21).Select(id=>{var item=new RoomSchedule();typeof(RoomSchedule).GetProperty("RoomId").SetValue(item,id,null);typeof(RoomSchedule).GetProperty("Range").SetValue(item,CalendarRange.Day(now.Date),null);typeof(RoomSchedule).GetProperty("CurrentStatus").SetValue(item,"Available",null);return item;}).ToList();
                    var populate=typeof(MySqlCalendarService).GetMethod("Populate",BindingFlags.NonPublic|BindingFlags.Static);
                    before=Selects(c,tx);
                    if(populate.GetParameters()[2].ParameterType==typeof(RoomSchedule))foreach(var room in schedules)populate.Invoke(null,new object[]{c,tx,room,now});
                    else populate.Invoke(null,new object[]{c,tx,schedules,now});
                    count=Selects(c,tx)-before;
                    Assert(count<=3,"Staff calendar N+1: "+count+" SELECTs for21rooms (budget3 without walkins).");
                    Console.WriteLine("Staff calendar SELECTs: "+count+" for21rooms.");
                }
                Sql(c,"INSERT INTO Customers(CustomerId,FullName,PhoneNumber) VALUES"+string.Join(",",Enumerable.Range(2,5).Select(id=>"("+id+",'Walkin','000000000"+id+"')")));
                var starts=Enumerable.Range(2,5).ToDictionary(id=>id,id=>id==6?now.AddDays(-1):now.AddMinutes(-10));
                Sql(c,"INSERT INTO RoomSessions(RoomSessionId,CustomerId,RoomId,ActualStartTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES"+string.Join(",",Enumerable.Range(2,5).Select(id=>"("+id+","+id+","+id+",'"+Utc(starts[id])+"',120000,'BATCH','STANDARD','Standard','Active')")));
                Sql(c,"INSERT INTO Reservations(CustomerId,RoomId,StartTime,EndTime,Status,CreatedAt) VALUES(2,21,'"+Utc(now.AddHours(2))+"','"+Utc(now.AddHours(3))+"','Confirmed','"+Utc(now)+"'),(1,3,'"+Utc(now.AddHours(1))+"','"+Utc(now.AddHours(2))+"','Confirmed','"+Utc(now)+"');");
                using(var tx=MySqlDatabase.BeginReadTransaction(c)) {
                    var rules=typeof(MySqlAvailabilityService).Assembly.GetType("MusicBoxManagement.Wpf.Services.MySqlScheduleRules");
                    var single=rules.GetMethod("WalkInReturnBy",BindingFlags.NonPublic|BindingFlags.Static);
                    var batch=rules.GetMethod("WalkInReturnByMany",BindingFlags.NonPublic|BindingFlags.Static);
                    var before=Selects(c,tx);
                    var dates=batch!=null?(System.Collections.Generic.Dictionary<int,DateTimeOffset>)batch.Invoke(null,new object[]{c,tx,Enumerable.Range(2,5),now}):Enumerable.Range(2,5).ToDictionary(id=>id,id=>(DateTimeOffset)single.Invoke(null,new object[]{c,tx,id,id,starts[id],now}));
                    var count=Selects(c,tx)-before;
                    Assert(count==1,"Walk-in deadlines N+1: "+count+" SELECTs for5sessions (budget1).");
                    Console.WriteLine("Walk-in ReturnBy SELECTs: "+count+" for5sessions.");
                    foreach(var id in Enumerable.Range(2,5))Assert(dates[id]==(DateTimeOffset)single.Invoke(null,new object[]{c,tx,id,id,starts[id],now}),"Batch ReturnBy differs from room/customer/shift oracle.");
                }
                Sql(c,"DELETE FROM RoomSessions;DELETE FROM Reservations;");
                foreach(var duration in new[]{60,90,120,180}) {
                    foreach(var scenario in new[]{"empty","reservation","walkin","booking-active","grace","tick"}) {
                        Sql(c,"DELETE FROM RoomSessions;DELETE FROM Reservations;");clock.Now=scenario=="tick"?now.AddTicks(1):now;
                        if(scenario=="reservation"||scenario=="grace") {
                            var start=scenario=="grace"?now.AddMinutes(-15):now.AddHours(4);
                            Sql(c,"INSERT INTO Reservations(ReservationId,CustomerId,RoomId,StartTime,EndTime,Status,CreatedAt) VALUES(1,1,1,'"+Utc(start)+"','"+Utc(start.AddHours(1))+"','Confirmed','"+Utc(now)+"');");
                        }
                        if(scenario=="walkin"||scenario=="booking-active") {
                            if(scenario=="booking-active") Sql(c,"INSERT INTO Reservations(ReservationId,CustomerId,RoomId,StartTime,EndTime,Status,CreatedAt) VALUES(1,1,1,'"+Utc(now)+"','"+Utc(now.AddHours(2))+"','CheckedIn','"+Utc(now)+"');");
                            Sql(c,"INSERT INTO RoomSessions(CustomerId,RoomId,ReservationId,ActualStartTime,ExpectedEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES(1,1,"+(scenario=="walkin"?"NULL":"1")+",'"+Utc(now.AddMinutes(-10))+"',"+(scenario=="walkin"?"NULL":"'"+Utc(now.AddHours(2))+"'")+",120000,'BATCH','STANDARD','Standard','Active');");
                        }
                        PublicRoomDay day;
                        using(var tx=MySqlDatabase.BeginReadTransaction(c)) {
                            var before=Selects(c,tx);
                            day=(PublicRoomDay)typeof(MySqlGuestCalendarService).GetMethod("ReadAt",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{db,c,tx,1,now.Date,duration,clock.Now});
                            var count=Selects(c,tx)-before;
                            Assert(count<=3,"Guest day N+1: "+count+" SELECTs for28slots (budget3).");
                            if(duration==60&&scenario=="empty")Console.WriteLine("Guest day SELECTs: "+count+" for28slots.");
                        }
                        Assert(day.Slots.Count==28,"Slot count changed.");
                        var oracle=new MySqlAvailabilityService(db,clock);
                        using(var oracleTx=MySqlDatabase.BeginReadTransaction(c)) foreach(var slot in day.Slots) {
                            var local=slot.Start.ToOffset(BookingHours.VietnamOffset);string expected;
                            if(local.Hour==12)expected="Break";
                            else if(slot.Start<clock.Now)expected="Past";
                            else {try{BookingHours.ValidateReservation(slot.Start,duration,clock.Now);var available=(ReservationAvailability)typeof(MySqlAvailabilityService).GetMethod("CheckReservationAt",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(oracle,new object[]{c,oracleTx,1,null,slot.Start,duration,clock.Now});expected=available.CanBook?"Available":"Busy";}catch(ArgumentException){expected="Closed";}}
                            Assert(slot.State==expected,scenario+"/"+duration+" slot "+local+" mismatch: "+slot.State+" vs "+expected);
                        }
                    }
                }
                Assert(Convert.ToInt32(Sql(c,"SELECT COUNT(*) FROM AuditLog"))==0,"Calendar read wrote audit.");
            }
            Console.WriteLine("PASS Guest day constant SELECT budget,24scenario/duration combinations,28slots oracle,exactnow/tick/grace/break/walkin/future/bookingActive; GUID only.");
        } finally {using(var c=new MySqlDatabase("defaultdb").OpenConnection())Sql(c,"DROP DATABASE IF EXISTS `"+name+"`");}
    }
}
