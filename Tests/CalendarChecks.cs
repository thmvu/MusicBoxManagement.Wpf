using System;
using System.Globalization;
using System.IO;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

public static class MusicBoxCalendarChecks
{
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static object Sql(SqliteDatabase db, string sql) { using (var c = db.OpenConnection()) using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; return cmd.ExecuteScalar(); } }
    private static DateTimeOffset At(int day, int hour, int minute) { return new DateTimeOffset(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(7)); }
    private static string Utc(DateTimeOffset value) { return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture); }
    private static void Booking(SqliteDatabase db, int id, int room, int customer, DateTimeOffset start, string status)
    { Sql(db, "INSERT INTO Reservations VALUES(" + id + "," + customer + "," + room + ",'" + Utc(start) + "','" + Utc(start.AddHours(1)) + "','" + status + "',NULL,NULL,'test');"); }
    private static void Session(SqliteDatabase db, int room, int customer, int? reservation, DateTimeOffset start, DateTimeOffset? expected, DateTimeOffset? end)
    {
        Sql(db, "INSERT INTO RoomSessions(CustomerId,RoomId,ReservationId,ActualStartTime,ExpectedEndTime,ActualEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES(" + customer + "," + room + "," + (reservation.HasValue ? reservation.Value.ToString() : "NULL") + ",'" + Utc(start) + "'," + (expected.HasValue ? "'" + Utc(expected.Value) + "'" : "NULL") + "," + (end.HasValue ? "'" + Utc(end.Value) + "'" : "NULL") + ",120000,'R" + room + "','STANDARD','Standard','" + (end.HasValue ? "Completed" : "Active") + "');");
    }
    public static void Run()
    {
        var file = Path.Combine(Path.GetTempPath(), "MusicBoxCalendar_" + Guid.NewGuid().ToString("N") + ".db");
        var db = new SqliteDatabase(file); var clock = new Clock { UtcNow = At(6, 13, 37) };
        try
        {
            var auth = new AuthenticationService(db); var admin = auth.SetupAdminAsync("admin", "Admin", "Calendar-Test!").GetAwaiter().GetResult();
            Sql(db, @"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'staff','staff','STAFF',PasswordHash,'staff','Staff',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('staff','Staff');
DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId IN(SELECT PermissionId FROM Permission WHERE Code<>'Calendar.View');");
            var staff = auth.LoginAsync("staff", "Calendar-Test!").GetAwaiter().GetResult();
            for (var i = 1; i <= 7; i++)
            {
                Sql(db, "INSERT INTO Rooms(RoomId,RoomCode,Name,RoomTypeId,ImageUrl,IsActive,InactiveReason,CreatedAt) VALUES(" + i + ",'R" + i + "','Room " + i + "',1,'test.png'," + (i == 7 ? "0,'Locked'" : "1,NULL") + ",'test');");
                Sql(db, "INSERT INTO Customers VALUES(" + i + ",'Customer " + i + "','090000000" + i + "');");
            }
            Booking(db, 1, 1, 1, At(6, 13, 30), "Confirmed");
            Booking(db, 2, 1, 2, At(6, 13, 0), "Confirmed"); // Already expired without worker maintenance.
            Booking(db, 3, 2, 2, At(7, 9, 0), "Confirmed");
            Booking(db, 4, 3, 3, At(6, 10, 30), "CheckedIn");
            Session(db, 3, 3, 4, At(6, 10, 37), At(6, 11, 37), null);
            Session(db, 4, 4, null, At(6, 13, 7), null, null);
            Booking(db, 5, 5, 4, At(6, 14, 0), "Confirmed"); // Customer's next booking at another room.
            Booking(db, 6, 4, 5, At(6, 15, 0), "Confirmed");
            Session(db, 6, 6, null, At(5, 22, 37), null, At(6, 9, 37));
            Booking(db, 7, 7, 7, At(5, 10, 0), "Completed");
            Session(db, 7, 7, 7, At(5, 10, 7), At(5, 11, 7), At(5, 11, 9));
            Booking(db, 8, 2, 5, At(6, 15, 0), "Cancelled");
            Booking(db, 9, 2, 6, At(6, 16, 0), "NoShow");
            var service = new CalendarService(db, clock); var day = CalendarRange.Day(new DateTime(2026, 10, 6));
            Assert(day.Start == At(6, 0, 0) && day.End == At(7, 0, 0), "Day is not Vietnam midnight-to-midnight.");
            var week = CalendarRange.Week(new DateTime(2026, 10, 11));
            Assert(week.Start == At(5, 0, 0) && week.End == At(12, 0, 0), "Sunday week is not Monday-to-Monday.");
            Assert(CalendarRange.Week(new DateTime(2026, 10, 5)).Start == week.Start, "Monday moved into previous week.");
            Reject<ArgumentException>(() => CalendarRange.Day(DateTime.MaxValue));
            Reject<ArgumentException>(() => service.Read(admin, null)); Reject<ArgumentException>(() => service.Read(admin, day, 999));
            Reject<UnauthorizedAccessException>(() => service.Read(null, day));
            var all = service.Read(staff, day); Assert(all.Count == 7, "Calendar-only access needs unrelated permission or lost locked rooms.");
            Assert(all[0].CurrentStatus == "Reserved" && all[0].Events.Count == 1 && all[0].Holds.Count == 1, "Reserved/grace or expired suppression failed.");
            Assert(all[1].CurrentStatus == "Available" && all[1].Events.Count == 0, "Future/cancelled/NoShow booking marks current room Reserved.");
            var active = all[2]; Assert(active.CurrentStatus == "Occupied" && active.Events.Count == 1 && active.Events[0].Kind == "Active" && active.Events[0].IsOverdue, "Active/CheckedIn double count or overdue failed.");
            Assert(active.Holds.Single().End == At(6, 11, 37) && active.Events[0].End == clock.UtcNow && active.Events[0].Start == At(6, 10, 37), "Expected hold extended to now or actual time rounded.");
            var walk = all[3].Events.Single(e => e.Kind == "WalkIn");
            Assert(walk.End == clock.UtcNow && walk.ReturnBy == At(6, 14, 0) && !walk.IsOverdue && all[3].Holds.Count == 1, "Walk-in future hold or Customer deadline failed.");
            Assert(all[5].Events.Single().Start == At(5, 22, 37) && all[5].Events.Single().End == At(6, 9, 37) && all[5].Holds.Count == 0, "Cross-midnight completed usage lost or blocks future.");
            Assert(all[6].CurrentStatus == "Inactive", "Inactive priority lost.");
            var tomorrow = service.Read(staff, CalendarRange.Day(new DateTime(2026, 10, 7)));
            Assert(tomorrow[2].Events.Count == 0 && tomorrow[2].Holds.Count == 0 && tomorrow[2].CurrentStatus == "Occupied", "Current occupied paints tomorrow busy.");
            Assert(tomorrow[3].Events.Count == 0 && tomorrow[3].Holds.Count == 0, "Walk-in paints tomorrow busy.");
            Assert(tomorrow[1].Events.Single().ReservationId == 3, "Tomorrow booking omitted.");
            var historic = service.Read(staff, week, 7).Single(); Assert(historic.Events.Single().Kind == "Completed" && historic.Holds.Count == 0, "Locked-room completed history omitted.");
            var availability = new AvailabilityService(db, clock);
            Assert(availability.CheckReservation(3, 2, At(6, 15, 0), 60).CanBook && !availability.CheckReservation(3, 2, clock.UtcNow, 60).CanBook, "Calendar diverged from future/immediate availability.");
            clock.UtcNow = At(6, 15, 0);
            Assert(!availability.CheckReservation(3, 2, clock.UtcNow, 60).CanBook && availability.CheckReservation(3, 2, At(6, 15, 30), 60).CanBook, "Overdue Active must block immediate, but not all future booking.");
            Booking(db, 10, 3, 5, At(6, 15, 0), "Confirmed");
            Assert(service.Read(staff, day, 3).Single().CurrentStatus == "Occupied", "Reserved overrode Occupied.");
            Sql(db, "UPDATE Rooms SET IsActive=0,InactiveReason='fixture' WHERE RoomId=3;");
            Assert(service.Read(staff, day, 3).Single().CurrentStatus == "Inactive", "Occupied overrode Inactive.");
            Sql(db, "UPDATE Rooms SET IsActive=1,InactiveReason=NULL WHERE RoomId=3; DELETE FROM Reservations WHERE ReservationId=10;");
            clock.UtcNow = At(6, 13, 45).AddTicks(-1); Assert(service.Read(staff, day, 1).Single().CurrentStatus == "Reserved", "Grace ended a tick early.");
            clock.UtcNow = At(6, 13, 45); Assert(service.Read(staff, day, 1).Single().CurrentStatus == "Available", "Exact grace boundary still Reserved.");
            Sql(db, "UPDATE Reservations SET Status='Cancelled' WHERE ReservationId=5;");
            Assert(service.Read(staff, day, 4).Single().Events.Single(e => e.Kind == "WalkIn").ReturnBy == At(6, 15, 0), "Walk-in deadline did not refresh after cancellation.");
            Sql(db, "UPDATE Reservations SET Status='Cancelled' WHERE ReservationId=6;");
            Assert(service.Read(staff, day, 4).Single().Events.Single().ReturnBy == At(6, 23, 0), "Walk-in shift deadline failed.");
            clock.UtcNow = At(6, 23, 1); Assert(service.Read(staff, day, 4).Single().Events.Single().IsOverdue, "Walk-in close warning missing.");
            Assert((string)Sql(db, "SELECT Status FROM Reservations WHERE ReservationId=2;") == "Confirmed" && Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.NoShow';")) == 0, "Calendar read unexpectedly wrote NoShow/audit.");
            clock.UtcNow = At(6, 13, 37);
            var vm = new CalendarDayViewModel(service, staff, clock);
            Assert(vm.IsDay && !vm.IsWeek, "Calendar no longer defaults to Day.");
            vm.LoadAsync().GetAwaiter().GetResult(); Assert(vm.Rooms.Count == 7 && vm.RoomChoices.Count == 8 && vm.CanLoad, "VM did not load all room choices/calendar.");
            vm.Select(vm.Rooms[2], vm.Rooms[2].Events[0]); Assert(vm.Details.Contains("QUÁ GIỜ") && vm.Details.Contains("10:37:00"), "VM details lost actual time/warning.");
            vm.SelectedRoom = vm.RoomChoices.Single(r => r.RoomId == 3); Assert(vm.Rooms.Count == 0 && !vm.Details.Contains("Customer"), "Changing filter left private details/old schedule.");
            vm.LoadAsync().GetAwaiter().GetResult(); Assert(vm.Rooms.Count == 1 && vm.Rooms[0].RoomId == 3, "VM room filter ignored.");
            vm.MoveAsync(1).GetAwaiter().GetResult(); Assert(vm.Date == new DateTime(2026,10,7) && vm.Rooms[0].Events.Count == 0, "VM next day used current occupied to fill tomorrow.");
            vm.TodayAsync().GetAwaiter().GetResult(); Assert(vm.Date == new DateTime(2026,10,6), "VM today not Vietnam date.");
            vm.ChangeModeAsync(true).GetAwaiter().GetResult(); Assert(vm.IsWeek && vm.Rooms[0].Range.Start == At(5,0,0) && vm.Rooms[0].Range.End == At(12,0,0) && vm.Status.Contains("05/10/2026") && vm.PreviousLabel=="Tuần trước", "VM Week is not Monday-to-Monday or labels wrong.");
            vm.MoveAsync(1).GetAwaiter().GetResult(); Assert(vm.Rooms[0].Range.Start==At(12,0,0) && vm.Rooms[0].Events.Count==0, "Week next moved one day or retained old events.");
            vm.MoveAsync(-1).GetAwaiter().GetResult(); Assert(vm.Rooms[0].Range.Start==At(5,0,0), "Week previous failed.");
            vm.Date=new DateTime(2026,10,11);vm.LoadAsync().GetAwaiter().GetResult();Assert(vm.Rooms[0].Range.Start==At(5,0,0), "Sunday selects wrong week.");
            vm.TodayAsync().GetAwaiter().GetResult(); Assert(vm.IsWeek && vm.Rooms[0].Range.Start==At(5,0,0), "Today changed Week to Day.");
            vm.ChangeModeAsync(false).GetAwaiter().GetResult();Assert(vm.IsDay && vm.Rooms[0].Range.End-vm.Rooms[0].Range.Start==TimeSpan.FromDays(1), "Return to Day still reads seven days.");
            vm.Date = null; vm.LoadAsync().GetAwaiter().GetResult(); Assert(vm.Status.Contains("Cần chọn ngày") && vm.Rooms.Count == 0, "Missing date leaves old data.");
            vm.Date = new DateTime(2026,10,6); vm.LoadAsync().GetAwaiter().GetResult(); vm.Select(vm.Rooms[0], vm.Rooms[0].Events[0]);
            vm.ChangeModeAsync(true).GetAwaiter().GetResult(); vm.Select(vm.Rooms[0],vm.Rooms[0].Events[0]);
            Sql(db, "DELETE FROM RolePermission WHERE RoleId='Staff';"); Reject<UnauthorizedAccessException>(() => service.Read(staff, day));
            vm.LoadAsync().GetAwaiter().GetResult(); Assert(vm.Rooms.Count == 0 && vm.RoomChoices.Count == 0 && !vm.Details.Contains("Customer") && vm.CanLoad && vm.Status.Contains("Không còn quyền"), "VM revocation retained private data or trapped Close/Retry.");
            auth.Logout(admin); Reject<UnauthorizedAccessException>(() => service.Read(admin, day));
            Assert(Convert.ToInt64(Sql(db, "PRAGMA user_version;")) == SqliteDatabase.CurrentSchemaVersion, "Schema changed.");
            Console.WriteLine("PASS Calendar: Vietnam Day/Week, protected live Calendar.View, current status, exact grace, holds/actual/expected/walk-in/completed/overdue, dynamic return deadline, locked history and read-only temporary SQLite.");
        }
        finally { System.Data.SQLite.SQLiteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" }) if (File.Exists(file + suffix)) File.Delete(file + suffix); }
    }
}
