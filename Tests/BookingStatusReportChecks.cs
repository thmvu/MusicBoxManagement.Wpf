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

public static class MusicBoxBookingStatusReportChecks
{
    private sealed class Clock : IClock { public DateTimeOffset Value; public int Reads; public DateTimeOffset UtcNow { get { Reads++; return Value; } } }
    private sealed class GateClock : IClock
    {
        public DateTimeOffset Value; public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false), Release = new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow { get { Entered.Set(); if (!Release.Wait(10000)) throw new Exception("Booking report snapshot timeout."); return Value; } }
    }
    private static object Sql(SqliteDatabase db, string sql) { using (var c = db.OpenConnection()) using (var command = c.CreateCommand()) { command.CommandText = sql; return command.ExecuteScalar(); } }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static DateTimeOffset At(int day, int hour, int minute) { return new DateTimeOffset(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(7)); }
    private static string Utc(DateTimeOffset value) { return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture); }
    private static int Count(BookingStatusReport report, string status) { return report.Rows.Single(r => r.Status == status).Count; }
    private static void Fixture(SqliteDatabase db, int id, DateTimeOffset start, string status)
    {
        Sql(db, "INSERT INTO Customers VALUES(" + id + ",'PRIVATE NAME','090000" + id.ToString("0000") + "');INSERT INTO Reservations(ReservationId,CustomerId,RoomId,StartTime,EndTime,Status,CreatedAt) VALUES(" + id + "," + id + ",1,'" + Utc(start) + "','" + Utc(start.AddHours(1)) + "','" + status + "','created is intentionally unrelated');");
    }
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MusicBoxBookingReport_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var db = new SqliteDatabase(Path.Combine(directory, "booking-report.db")); var auth = new AuthenticationService(db); const string password = "Booking-Report-2026!";
            var admin = auth.SetupAdminAsync("admin", "Admin", password).GetAwaiter().GetResult(); var clock = new Clock { Value = At(17, 9, 0) };
            Sql(db, @"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'staff','staff','STAFF',PasswordHash,'staff','Staff',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'manager','manager','MANAGER',PasswordHash,'manager','Manager',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('staff','Staff'),('manager','Manager');DELETE FROM RolePermission WHERE RoleId IN ('Staff','Manager');
INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Report.View';INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.View';");
            var staff = auth.LoginAsync("staff", password).GetAwaiter().GetResult(); var manager = auth.LoginAsync("manager", password).GetAwaiter().GetResult();
            var service = new BookingStatusReportService(db, clock); var day = new DateTime(2026, 10, 18);
            clock.Reads = 0; var empty = service.Read(manager, day, day);
            Assert(empty.TotalCount == 0 && empty.Rows.Count == 5 && empty.Rows.All(r => r.Count == 0) && clock.Reads == 1 && empty.CheckedAt.Offset == TimeSpan.Zero, "Empty fixed statuses/clock failed.");
            Reject<UnauthorizedAccessException>(() => service.Read(null, day, day)); Reject<UnauthorizedAccessException>(() => service.Read(staff, day, day));
            Reject<ArgumentException>(() => service.Read(admin, day.AddDays(1), day)); Reject<ArgumentException>(() => service.Read(admin, DateTime.MinValue, day)); Reject<ArgumentException>(() => service.Read(admin, day, DateTime.MaxValue));
            for (var id = 1; id <= 5; id++) Sql(db, "INSERT INTO Rooms(RoomId,RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES(" + id + ",'R" + id + "','Phòng',1,'test.png',1,'fixture');");
            var reservations = new ReservationService(db, clock); var ids = new int[5];
            for (var i = 0; i < 5; i++)
            {
                var request = new ReservationRequest { RoomId = i + 1, FullName = "Khách thật", PhoneNumber = "090111111" + i, StartTime = At(18, i == 0 ? 15 : 9, 0), DurationMinutes = 60 };
                ids[i] = (i % 2 == 0 ? reservations.CreateGuest(request) : reservations.CreateStaff(admin, request)).ReservationId;
            }
            reservations.CancelStaff(admin, ids[3], "Đổi lịch"); clock.Value = At(18, 9, 0);
            var sessions = new RoomSessionService(db, clock); var checkedIn = sessions.CheckIn(admin, ids[1]); var completed = sessions.CheckIn(admin, ids[2]);
            clock.Value = At(18, 10, 0); new CheckoutService(db, clock).ConfirmStaff(admin, completed.RoomSessionId, "Cash");
            Assert(new NoShowService(db, clock).ProcessExpired() == 1, "Real worker source should expire one booking.");
            clock.Reads = 0; var actual = service.Read(manager, day.AddHours(12), day.AddHours(23));
            Assert(actual.TotalCount == 5 && actual.Rows.All(r => r.Count == 1) && actual.FromDate == day && actual.ToDate == day && clock.Reads == 1, "Real Guest/Staff/cancel/check-in/checkout/NoShow source counts failed.");
            Assert(service.Read(admin, day.AddDays(-1), day.AddDays(-1)).TotalCount == 0, "Used CreatedAt instead of StartTime.");
            Fixture(db, 20, At(18, 10, 0), "Confirmed");
            clock.Value = At(18, 10, 15).AddTicks(-1); Assert(Count(service.Read(manager, day, day), "Confirmed") == 2, "Grace expired one tick early.");
            clock.Value = clock.Value.AddTicks(1); var audit = Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM AuditLog;"));
            var expired = service.Read(manager, day, day); Assert(Count(expired, "NoShow") == 2 && Count(expired, "Confirmed") == 1, "Exact grace effective NoShow failed.");
            Assert((string)Sql(db, "SELECT Status FROM Reservations WHERE ReservationId=20;") == "Confirmed" && audit == Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM AuditLog;")), "Report persisted worker lag status/audit.");
            Fixture(db, 21, At(18, 0, 0).AddTicks(-1), "Cancelled"); Fixture(db, 22, At(18, 0, 0), "Cancelled");
            Fixture(db, 23, At(19, 0, 0).AddTicks(-1), "Cancelled"); Fixture(db, 24, At(19, 0, 0), "Cancelled");
            Fixture(db, 25, At(18, 20, 0), "Completed"); Fixture(db, 26, At(18, 21, 0), "CheckedIn");
            using (var c = db.OpenConnection()) using (var command = c.CreateCommand())
            { command.CommandText = "PRAGMA ignore_check_constraints=ON;UPDATE Reservations SET StartTime='2026-10-18T00:00:00+07:00' WHERE ReservationId=22;UPDATE Reservations SET StartTime='2026-10-18T16:59:59.9999999Z' WHERE ReservationId=23;PRAGMA ignore_check_constraints=OFF;"; command.ExecuteNonQuery(); }
            var bounded = service.Read(manager, day, day);
            Assert(bounded.TotalCount == 10 && Count(bounded, "Cancelled") == 3 && Count(bounded, "Completed") == 2 && Count(bounded, "CheckedIn") == 2, "Exact date ticks, legacy offsets/Z or unchanged terminal status failed.");
            Assert(service.Read(admin, new DateTime(2, 1, 1), new DateTime(9998, 12, 31)).TotalCount == 12, "Arbitrary supported date range failed.");
            // A legacy anomaly must follow explicit report status/grace policy, without changing the source session.
            Sql(db, "UPDATE Reservations SET Status='Confirmed' WHERE ReservationId=" + ids[1] + ";");
            Assert(Count(service.Read(manager, day, day), "NoShow") == 3 && (string)Sql(db, "SELECT Status FROM RoomSessions WHERE RoomSessionId=" + checkedIn.RoomSessionId) == "Active", "Source anomaly changed status policy or mutated session.");
            Sql(db, "UPDATE Reservations SET Status='CheckedIn' WHERE ReservationId=" + ids[1] + ";");
            Assert(!typeof(BookingStatusReport).GetProperties().Concat(typeof(BookingStatusRow).GetProperties()).Any(p => p.GetSetMethod() != null || p.Name.IndexOf("Customer", StringComparison.Ordinal) >= 0 || p.Name.IndexOf("Phone", StringComparison.Ordinal) >= 0 || p.Name.EndsWith("Id", StringComparison.Ordinal)), "DTO exposes PII/IDs/public setters.");
            Reject<NotSupportedException>(() => ((System.Collections.Generic.IList<BookingStatusRow>)bounded.Rows).Clear());
            using (var c = db.OpenConnection()) using (var command = c.CreateCommand())
            { command.CommandText = "PRAGMA ignore_check_constraints=ON;UPDATE Reservations SET StartTime='2026-10-18T00:00:00' WHERE ReservationId=22;PRAGMA ignore_check_constraints=OFF;"; command.ExecuteNonQuery(); }
            Reject<InvalidOperationException>(() => service.Read(admin, day, day));
            using (var c = db.OpenConnection()) using (var command = c.CreateCommand())
            { command.CommandText = "PRAGMA ignore_check_constraints=ON;UPDATE Reservations SET StartTime='broken' WHERE ReservationId=22;PRAGMA ignore_check_constraints=OFF;"; command.ExecuteNonQuery(); }
            Reject<InvalidOperationException>(() => service.Read(admin, day, day)); Sql(db, "UPDATE Reservations SET StartTime='" + Utc(At(18, 0, 0)) + "' WHERE ReservationId=22;");
            foreach (var table in new[] { "Reservations", "RoomSessions", "Invoices", "AuditLog" }) foreach (var operation in new[] { "INSERT", "UPDATE", "DELETE" })
                Sql(db, "CREATE TRIGGER BookingReportReadOnly_" + table + operation + " BEFORE " + operation + " ON " + table + " BEGIN SELECT RAISE(ABORT,'Booking report is read-only'); END;");
            service.Read(manager, day, day); Assert(audit == Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM AuditLog;")), "Report wrote audit.");
            var gate = new GateClock { Value = clock.Value }; Task<BookingStatusReport> reading = null; Task writing = null;
            try
            {
                reading = Task.Run(() => new BookingStatusReportService(db, gate).Read(manager, day, day)); Assert(gate.Entered.Wait(5000), "Read snapshot gate absent.");
                writing = Task.Run(() => Sql(db, "DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Report.View');"));
                Thread.Sleep(100); Assert(!writing.IsCompleted, "Revoke did not wait for existing snapshot."); gate.Release.Set(); Assert(Task.WaitAll(new Task[] { reading, writing }, 15000), "Report/revoke deadlocked."); Assert(reading.Result.TotalCount == bounded.TotalCount, "Snapshot mixed data or permissions.");
            }
            finally { gate.Release.Set(); if (reading != null) reading.GetAwaiter().GetResult(); if (writing != null) writing.GetAwaiter().GetResult(); gate.Entered.Dispose(); gate.Release.Dispose(); }
            clock.Reads = 0; Reject<UnauthorizedAccessException>(() => service.Read(manager, day, day)); Assert(clock.Reads == 0, "Denied read accessed data clock.");
            Sql(db, "INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.View';UPDATE AspNetUsers SET SecurityStamp='new' WHERE Id='manager';"); Reject<UnauthorizedAccessException>(() => service.Read(manager, day, day));
            Sql(db, "UPDATE AspNetUsers SET SecurityStamp='manager',IsActive=0 WHERE Id='manager';"); Reject<UnauthorizedAccessException>(() => service.Read(manager, day, day));
            Sql(db, "UPDATE AspNetUsers SET IsActive=1 WHERE Id='manager';UPDATE AspNetUserRoles SET RoleId='Staff' WHERE UserId='manager';"); Reject<UnauthorizedAccessException>(() => service.Read(manager, day, day));
            auth.Logout(manager); Reject<UnauthorizedAccessException>(() => service.Read(manager, day, day));
            Assert(Convert.ToInt64(Sql(db, "PRAGMA user_version;")) == SqliteDatabase.CurrentSchemaVersion, "Report changed schema.");
            Console.WriteLine("PASS BookingStatusReport: real Guest/Staff creation, cancel/check-in/checkout/NoShow, fixed five statuses, StartTime Vietnam exact ticks/legacy offsets, effective grace worker lag/anomaly readonly, current independent Report.View Admin/Manager only, private immutable DTO, one clock/read snapshot/revoke concurrency, temporary SQLite only.");
        }
        finally
        {
            SQLiteConnection.ClearAllPools(); var target = Path.GetFullPath(directory); var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("MusicBoxBookingReport_", StringComparison.Ordinal)) throw new Exception("Unsafe booking report cleanup.");
            Directory.Delete(target, true);
        }
    }
}
