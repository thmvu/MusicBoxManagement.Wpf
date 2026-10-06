using System;
using System.IO;
using System.Linq;
using System.Data.SQLite;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

public static class MusicBoxGuestCalendarChecks
{
    private sealed class Clock:IClock { public DateTimeOffset UtcNow {get;set;} }
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject<T>(Action action) where T:Exception {try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static object Sql(SqliteDatabase db,string sql){using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=sql;return cmd.ExecuteScalar();}}
    private static string State(PublicRoomDay day,string time){return day.Slots.Single(s=>s.TimeLabel==time).State;}
    public static void Run()
    {
        var file=Path.Combine(Path.GetTempPath(),"MusicBoxGuestCalendar_"+Guid.NewGuid().ToString("N")+".db");
        var db=new SqliteDatabase(file);db.Initialize();var clock=new Clock{UtcNow=new DateTimeOffset(2026,10,6,6,0,0,TimeSpan.Zero)};var date=new DateTime(2026,10,6);
        try
        {
            Sql(db,@"INSERT INTO Rooms(RoomId,RoomCode,Name,RoomTypeId,ImageUrl,IsActive,InactiveReason,CreatedAt) VALUES(1,'R1','Room',1,'test.png',1,NULL,'test'),(2,'R2','Walk-in',1,'test.png',1,NULL,'test'),(3,'LOCK','Locked',1,'test.png',0,'Private reason','test');
INSERT INTO Customers VALUES(1,'Secret customer','0912345678'),(2,'Walk-in secret','0987654321');
INSERT INTO Reservations VALUES(1,1,1,'2026-10-06T08:00:00.0000000+00:00','2026-10-06T09:00:00.0000000+00:00','Confirmed',NULL,NULL,'test'),(2,1,1,'2026-10-06T03:30:00.0000000+00:00','2026-10-06T04:30:00.0000000+00:00','CheckedIn',NULL,NULL,'test'),(3,1,1,'2026-10-06T04:00:00.0000000+00:00','2026-10-06T05:00:00.0000000+00:00','Confirmed',NULL,NULL,'test');
INSERT INTO RoomSessions(CustomerId,RoomId,ReservationId,ActualStartTime,ExpectedEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES(1,1,2,'2026-10-06T03:37:00.0000000+00:00','2026-10-06T04:37:00.0000000+00:00',120000,'R1','STANDARD','Standard','Active'),(2,2,NULL,'2026-10-06T06:00:00.0000000+00:00',NULL,120000,'R2','STANDARD','Standard','Active');");
            var route=new GuestBookingService(db,clock);var day=route.ReadDay(1,date,60);
            Assert(day.Slots.Count==28 && State(day,"09:00")=="Past" && State(day,"12:00")=="Break" && State(day,"12:30")=="Break", "Day/past/rest slots wrong.");
            Assert(State(day,"13:00")=="Busy" && State(day,"13:30")=="Available" && State(day,"14:00")=="Available" && State(day,"14:30")=="Busy" && State(day,"15:00")=="Busy" && State(day,"16:00")=="Available", "Immediate Active/overdue/future holds/adjacency wrong.");
            Assert(State(day,"22:00")=="Available" && State(day,"22:30")=="Closed", "End-of-shift validation wrong.");
            Assert(State(route.ReadDay(1,date,180),"20:00")=="Available" && State(route.ReadDay(1,date,180),"20:30")=="Closed", "Duration not applied.");
            Assert(State(route.ReadDay(2,date,60),"13:00")=="Busy" && State(route.ReadDay(2,date,60),"13:30")=="Available", "Walk-in blocks all future slots.");
            Assert(State(route.ReadDay(1,date.AddDays(1),60),"09:00")=="Available", "Current occupied filled tomorrow.");
            Sql(db,"INSERT INTO Rooms(RoomId,RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES(4,'GRACE','Grace',1,'test.png',1,'test'); INSERT INTO Reservations VALUES(4,1,4,'2026-10-06T06:00:00.0000000+00:00','2026-10-06T07:00:00.0000000+00:00','Confirmed',NULL,NULL,'test');");
            clock.UtcNow=new DateTimeOffset(2026,10,6,6,15,0,TimeSpan.Zero).AddTicks(-1);Assert(State(route.ReadDay(4,date,60),"13:30")=="Busy","Grace ended early.");
            clock.UtcNow=clock.UtcNow.AddTicks(1);Assert(State(route.ReadDay(4,date,60),"13:30")=="Available" && (string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId=4;")=="Confirmed","Exact +15 boundary retained hold or wrote NoShow.");clock.UtcNow=new DateTimeOffset(2026,10,6,6,0,0,TimeSpan.Zero);
            Assert(route.ReadDay(1,date.AddDays(30),60).Slots.Count==28,"+30 day rejected.");
            Reject<ArgumentException>(()=>route.ReadDay(1,date.AddDays(-1),60));Reject<ArgumentException>(()=>route.ReadDay(1,date.AddDays(31),60));Reject<ArgumentException>(()=>route.ReadDay(1,date,45));
            Reject<InvalidOperationException>(()=>route.ReadDay(3,date,60));Reject<InvalidOperationException>(()=>route.ReadDay(999,date,60));
            Assert(typeof(PublicRoomDay).GetProperties().All(p=>new[]{"RoomCode","RoomName","Date","DurationMinutes","CheckedAt","Slots"}.Contains(p.Name)) && typeof(PublicBookingSlot).GetProperties().All(p=>new[]{"Start","State","CanChoose","TimeLabel","StateLabel"}.Contains(p.Name)), "Anonymous DTO contains unexpected private/source data.");
            Assert((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId=3;")=="Confirmed" && Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog;"))==0 && Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AspNetUsers;"))==0, "Public schedule wrote NoShow/audit or required login.");
            var parent=new GuestBookingViewModel(route,clock);parent.RefreshAsync().GetAwaiter().GetResult();parent.SelectedRoom=parent.Rooms.Single(r=>r.RoomId==1);parent.SelectedDate=date;
            var vm=parent.PrepareCalendarAsync().GetAwaiter().GetResult();Assert(vm!=null && string.IsNullOrEmpty(parent.FullName) && string.IsNullOrEmpty(parent.PhoneNumber), "Calendar requires name/phone.");
            vm.Selected=vm.Slots.Single(s=>s.TimeLabel=="15:00");Assert(!vm.CanChoose,"Busy slot selectable.");vm.Selected=vm.Slots.Single(s=>s.TimeLabel=="13:30");Assert(vm.CanChoose,"Available slot not selectable.");
            route.Create(new ReservationRequest{RoomId=1,StartTime=new DateTimeOffset(2026,10,6,13,30,0,TimeSpan.FromHours(7)),DurationMinutes=60,FullName="New guest",PhoneNumber="0900000009"});
            vm.RefreshAsync().GetAwaiter().GetResult();Assert(!vm.CanChoose && vm.Selected==null && vm.Slots.Single(s=>s.TimeLabel=="13:30").State=="Busy","Refresh trusted stale slot.");
            parent.StartTimeText="13:30";parent.FullName="Another";parent.PhoneNumber="0900000008";Assert(!parent.SubmitAsync().GetAwaiter().GetResult() && Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM Customers;"))==3,"Stale anonymous schedule bypassed writer/left new customer.");
            Sql(db,"UPDATE Rooms SET IsActive=0,InactiveReason='Private lock' WHERE RoomId=1;");vm.RefreshAsync().GetAwaiter().GetResult();Assert(vm.Slots.Count==0 && !vm.CanChoose && vm.CanClose && !vm.Status.Contains("Private lock"),"Locked room retained old schedule/leaked reason.");
            var auth=new AuthenticationService(db);var admin=auth.SetupAdminAsync("admin","Admin","GuestCalendar-Test!").GetAwaiter().GetResult();var staffRoute=new StaffBookingService(db,admin,clock);
            Assert(staffRoute.ReadDay(2,date,60).Slots.Count==28,"Staff anonymous calendar route failed.");auth.Logout(admin);Reject<UnauthorizedAccessException>(()=>staffRoute.ReadDay(2,date,60));
            Assert(Convert.ToInt64(Sql(db,"PRAGMA user_version;"))==6,"Schema changed.");
            Console.WriteLine("PASS Guest calendar: anonymous DTO/no login, 28 slots/Vietnam/date/duration/shifts, holds/grace/walk-in/overdue/immediate/tomorrow, locked rooms, read-only, stale writer rollback and Staff session guard on temporary SQLite.");
        }
        finally{SQLiteConnection.ClearAllPools();foreach(var suffix in new[]{"","-wal","-shm","-journal"})if(File.Exists(file+suffix))File.Delete(file+suffix);}
    }
}
