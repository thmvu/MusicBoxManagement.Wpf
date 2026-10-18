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

public static class MusicBoxRoomUsageReportChecks
{
    private sealed class Clock : IClock { public DateTimeOffset Value; public int Reads; public DateTimeOffset UtcNow { get { Reads++; return Value; } } }
    private sealed class GateClock : IClock
    {
        public DateTimeOffset Value; public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false), Release = new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow { get { Entered.Set(); if (!Release.Wait(10000)) throw new Exception("Usage snapshot gate timeout."); return Value; } }
    }
    private static object Sql(SqliteDatabase db, string sql) { using (var c = db.OpenConnection()) using (var command = c.CreateCommand()) { command.CommandText = sql; return command.ExecuteScalar(); } }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static DateTimeOffset At(int day, int hour, int minute) { return new DateTimeOffset(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(7)); }
    private static string Utc(DateTimeOffset value) { return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture); }
    private static void Room(SqliteDatabase db, int id, DateTimeOffset created, bool active)
    { Sql(db, "INSERT INTO Rooms(RoomId,RoomCode,Name,RoomTypeId,ImageUrl,IsActive,InactiveReason,CreatedAt) VALUES(" + id + ",'R" + id + "','Phòng " + id + "',1,'test.png'," + (active ? "1,NULL" : "0,'Locked'") + ",'" + Utc(created) + "');"); }
    private static void Session(SqliteDatabase db, int id, int room, DateTimeOffset start, DateTimeOffset? end)
    {
        Sql(db, "INSERT INTO Customers VALUES(" + id + ",'PRIVATE NAME','090000" + id.ToString("0000") + "');" +
            "INSERT INTO RoomSessions(RoomSessionId,CustomerId,RoomId,ActualStartTime,ActualEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES(" + id + "," + id + "," + room + ",'" + Utc(start) + "'," + (end.HasValue ? "'" + Utc(end.Value) + "'" : "NULL") + ",120000,'R" + room + "','STANDARD','Standard','" + (end.HasValue ? "Completed" : "Active") + "');");
    }
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MusicBoxRoomUsage_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var db = new SqliteDatabase(Path.Combine(directory, "usage.db")); var auth = new AuthenticationService(db); const string password = "Usage-Test-2026!";
            var admin = auth.SetupAdminAsync("admin", "Admin", password).GetAwaiter().GetResult(); var clock = new Clock { Value = At(18, 14, 0) };
            Sql(db, @"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'staff','staff','STAFF',PasswordHash,'staff','Staff',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'manager','manager','MANAGER',PasswordHash,'manager','Manager',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('staff','Staff'),('manager','Manager');DELETE FROM RolePermission WHERE RoleId IN ('Staff','Manager');
INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Report.View';
INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.View';");
            var staff = auth.LoginAsync("staff", password).GetAwaiter().GetResult(); var manager = auth.LoginAsync("manager", password).GetAwaiter().GetResult();
            var service = new RoomUsageReportService(db, clock); var day = new DateTime(2026, 10, 18);
            clock.Reads = 0; var empty = service.Read(manager, day, day);
            Assert(empty.Rooms.Count == 0 && empty.RoomChoices.Count == 0 && empty.CompletedCount == 0 && empty.UsedMinutes == 0 && empty.AvailableMinutes == 0 && empty.UtilizationPercent == null && clock.Reads == 1 && empty.CheckedAt.Offset == TimeSpan.Zero, "Empty/one clock/nullable utilization failed.");
            Reject<UnauthorizedAccessException>(() => service.Read(null, day, day)); Reject<UnauthorizedAccessException>(() => service.Read(staff, day, day));
            Reject<ArgumentException>(() => service.Read(admin, day.AddDays(1), day)); Reject<ArgumentException>(() => service.Read(admin, DateTime.MinValue, day));
            Reject<ArgumentException>(() => service.Read(admin, day, DateTime.MaxValue)); Reject<ArgumentException>(() => service.Read(admin, day, day, 0)); Reject<ArgumentException>(() => service.Read(admin, day, day, 987));
            Room(db, 1, At(1, 0, 0), true);
            // Real service source: walk-in and checkout, not merely a hand-written Completed row.
            clock.Value = At(18, 9, 30); var session = new RoomSessionService(db, clock).CreateWalkIn(admin, new WalkInRequest { RoomId = 1, FullName = "Khách thật", PhoneNumber = "0901111111" }).Session;
            clock.Value = At(18, 10, 0); new CheckoutService(db, clock).ConfirmStaff(admin, session.RoomSessionId, "Cash");
            Room(db, 2, At(1, 0, 0), true); Session(db, 20, 2, At(18, 11, 30), null);
            Room(db, 3, At(18, 10, 30), false); Session(db, 30, 3, At(18, 9, 0), At(18, 15, 0));
            Room(db, 4, At(19, 9, 0), true);
            Room(db, 5, At(1, 0, 0), true); Session(db, 50, 5, At(18, 8, 0), At(18, 14, 30)); Session(db, 51, 5, At(18, 10, 0), At(18, 11, 0));
            Room(db, 6, At(18, 12, 30), true); Session(db, 60, 6, At(18, 13, 30), null);
            Room(db, 7, At(18, 14, 0).AddTicks(-1), true); Session(db, 70, 7, At(18, 13, 0), At(18, 14, 0));
            Room(db, 8, At(1, 0, 0), true); Session(db, 80, 8, At(17, 22, 0), null);
            Room(db, 9, At(1, 0, 0), true); Session(db, 90, 9, At(17, 22, 0), At(18, 0, 0).AddTicks(-1)); Session(db, 91, 9, At(18, 22, 0), At(19, 0, 0).AddTicks(-1)); Session(db, 92, 9, At(18, 22, 0), At(19, 0, 0));
            clock.Value = At(18, 14, 0); clock.Reads = 0;
            var result = service.Read(manager, day.AddHours(15), day.AddHours(18));
            Assert(clock.Reads == 1 && result.FromDate == day && result.ToDate == day && result.Rooms.Count == 9 && result.RoomChoices.Count == 9 && result.CompletedCount == 6, "Date normalization, source count or one read clock failed.");
            var r1 = result.Rooms.Single(r => r.RoomId == 1); Assert(r1.CompletedCount == 1 && r1.UsedMinutes == 30 && r1.AvailableMinutes == 240 && r1.UtilizationPercent == 12.5m, "Real checkout or cutoff denominator failed.");
            var r2 = result.Rooms.Single(r => r.RoomId == 2); Assert(r2.CompletedCount == 0 && r2.UsedMinutes == 90 && r2.AvailableMinutes == 240, "Active actual-now or lunch clipping failed.");
            var r3 = result.Rooms.Single(r => r.RoomId == 3); Assert(!r3.IsActive && r3.CompletedCount == 1 && r3.UsedMinutes == 150 && r3.AvailableMinutes == 150 && r3.UtilizationPercent == 100, "Inactive history/CreatedAt/now cutoff or completion count independence failed.");
            Assert(result.Rooms.Single(r => r.RoomId == 4).AvailableMinutes == 0 && result.Rooms.Single(r => r.RoomId == 4).UtilizationPercent == null, "Future CreatedAt counted denominator.");
            var r5 = result.Rooms.Single(r => r.RoomId == 5); Assert(r5.CompletedCount == 2 && r5.UsedMinutes == 240 && r5.UtilizationPercent == 100, "Overlap union/outside shift exclusion failed.");
            var r6 = result.Rooms.Single(r => r.RoomId == 6); Assert(r6.AvailableMinutes == 60 && r6.UsedMinutes == 30 && r6.UtilizationPercent == 50, "Partial CreatedAt during lunch failed.");
            var tick = 1m / TimeSpan.TicksPerMinute; var r7 = result.Rooms.Single(r => r.RoomId == 7); Assert(r7.AvailableMinutes == tick && r7.UsedMinutes == tick && r7.UtilizationPercent == 100, "100ns exact decimal occupancy lost precision.");
            Assert(result.Rooms.Single(r => r.RoomId == 9).CompletedCount == 1, "ActualEndTime [from,until) tick boundaries failed.");
            Assert(result.UsedMinutes == result.Rooms.Sum(r => r.UsedMinutes) && result.AvailableMinutes == result.Rooms.Sum(r => r.AvailableMinutes) && result.UtilizationPercent == result.UsedMinutes / result.AvailableMinutes * 100m && result.Rooms.All(r => !r.UtilizationPercent.HasValue || r.UtilizationPercent <= 100), "Weighted total/ratio invariants failed.");
            var filtered = service.Read(admin, day.AddDays(-1), day, 8); Assert(filtered.Rooms.Count == 1 && filtered.RoomChoices.Count == 9 && filtered.UsedMinutes == 300 && filtered.AvailableMinutes == 1020, "Room filter/choices or multi-day shift calculation failed.");
            var past = service.Read(admin, day.AddDays(-1), day.AddDays(-1), 1); Assert(past.AvailableMinutes == 780 && past.CompletedCount == 0, "Full standard day should equal 780 minutes.");
            var future = service.Read(admin, day.AddDays(1), day.AddDays(2)); Assert(future.AvailableMinutes == 0 && future.UsedMinutes == 0 && future.UtilizationPercent == null, "Future query invented open time.");
            // Large supported date range is constant time per interval; no arbitrary report-period cap.
            var longRange = service.Read(admin, new DateTime(2, 1, 1), new DateTime(9998, 12, 31), 1); Assert(longRange.AvailableMinutes > 780 && longRange.UsedMinutes == 30, "Long range/clipping failed.");
            Sql(db, "UPDATE Rooms SET RoomTypeId=2 WHERE RoomId=3;"); Assert(service.Read(manager, day, day, 3).UsedMinutes == 150, "Current RoomType must not reclassify usage.");
            using (var c = db.OpenConnection()) using (var command = c.CreateCommand())
            { command.CommandText = "PRAGMA ignore_check_constraints=ON;UPDATE RoomSessions SET ActualEndTime='2026-10-18T14:00:00+07:00' WHERE RoomSessionId=70;PRAGMA ignore_check_constraints=OFF;"; command.ExecuteNonQuery(); }
            Assert(service.Read(admin, day, day, 7).UsedMinutes == tick, "Legacy offset timestamp not interpreted as same instant.");
            // Disjoint sessions separated only by lunch cover every available open tick, with a fractional boundary.
            Room(db, 10, At(18, 12, 0).AddTicks(-1), true); Session(db, 100, 10, At(18, 12, 0).AddTicks(-1), At(18, 12, 0)); Session(db, 101, 10, At(18, 13, 0), At(18, 14, 0));
            var disjoint = service.Read(admin, day, day, 10); Assert(disjoint.UsedMinutes == disjoint.AvailableMinutes && disjoint.UtilizationPercent == 100, "Per-span rounding must not push utilization above 100 percent.");
            Sql(db, "DELETE FROM RoomSessions WHERE RoomId=10;DELETE FROM Rooms WHERE RoomId=10;DELETE FROM Customers WHERE CustomerId IN (100,101);");
            Assert(!typeof(RoomUsageRow).GetProperties().Any(p => p.Name.IndexOf("Customer", StringComparison.Ordinal) >= 0 || p.Name.IndexOf("Phone", StringComparison.Ordinal) >= 0 || p.GetSetMethod() != null), "Room DTO exposes PII/public setters.");
            Reject<NotSupportedException>(() => ((System.Collections.Generic.IList<RoomUsageRow>)result.Rooms).Clear()); Reject<NotSupportedException>(() => ((System.Collections.Generic.IList<RoomUsageChoice>)result.RoomChoices).Clear());
            // No silent date fabrication for damaged persisted records.
            Sql(db, "UPDATE Rooms SET CreatedAt='broken' WHERE RoomId=4;"); Reject<InvalidOperationException>(() => service.Read(admin, day, day)); Sql(db, "UPDATE Rooms SET CreatedAt='" + Utc(At(19, 9, 0)) + "' WHERE RoomId=4;");
            Sql(db, "UPDATE Rooms SET CreatedAt='2026-10-19T09:00:00' WHERE RoomId=4;"); Reject<InvalidOperationException>(() => service.Read(admin, day, day)); Sql(db, "UPDATE Rooms SET CreatedAt='" + Utc(At(19, 9, 0)) + "' WHERE RoomId=4;");
            using (var c = db.OpenConnection()) using (var command = c.CreateCommand())
            { command.CommandText = "PRAGMA ignore_check_constraints=ON;UPDATE RoomSessions SET ActualEndTime='" + Utc(At(18, 12, 0)) + "' WHERE RoomSessionId=70;PRAGMA ignore_check_constraints=OFF;"; command.ExecuteNonQuery(); }
            Reject<InvalidOperationException>(() => service.Read(admin, day, day, 7));
            Sql(db, "UPDATE RoomSessions SET ActualEndTime='" + Utc(At(18, 14, 0)) + "' WHERE RoomSessionId=70;");
            var auditCount = Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM AuditLog;"));
            foreach (var table in new[] { "Rooms", "RoomSessions", "Orders", "AuditLog", "Reservations" }) foreach (var operation in new[] { "INSERT", "UPDATE", "DELETE" })
                Sql(db, "CREATE TRIGGER UsageReadOnly_" + table + operation + " BEFORE " + operation + " ON " + table + " BEGIN SELECT RAISE(ABORT,'Usage report is read-only'); END;");
            service.Read(manager, day, day); Assert(auditCount == Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM AuditLog;")), "Read wrote audit rows.");
            var gate = new GateClock { Value = clock.Value }; Task<RoomUsageReport> reading = null; Task writing = null;
            try
            {
                reading = Task.Run(() => new RoomUsageReportService(db, gate).Read(manager, day, day)); Assert(gate.Entered.Wait(5000), "Report snapshot not established.");
                writing = Task.Run(() => Sql(db, "DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Report.View');"));
                Thread.Sleep(100); Assert(!writing.IsCompleted, "Revoke did not wait for read snapshot."); gate.Release.Set(); Assert(Task.WaitAll(new Task[] { reading, writing }, 15000), "Usage report/revoke deadlocked.");
                Assert(reading.Result.UsedMinutes == result.UsedMinutes, "Snapshot mixed permission/data.");
            }
            finally { gate.Release.Set(); if (reading != null) reading.GetAwaiter().GetResult(); if (writing != null) writing.GetAwaiter().GetResult(); gate.Entered.Dispose(); gate.Release.Dispose(); }
            clock.Reads = 0; Reject<UnauthorizedAccessException>(() => service.Read(manager, day, day)); Assert(clock.Reads == 0, "Denied report read clock/data.");
            Sql(db, "INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.View';UPDATE AspNetUsers SET SecurityStamp='new' WHERE Id='manager';"); Reject<UnauthorizedAccessException>(() => service.Read(manager, day, day));
            Sql(db, "UPDATE AspNetUsers SET SecurityStamp='manager',IsActive=0 WHERE Id='manager';"); Reject<UnauthorizedAccessException>(() => service.Read(manager, day, day));
            Sql(db, "UPDATE AspNetUsers SET IsActive=1 WHERE Id='manager';UPDATE AspNetUserRoles SET RoleId='Staff' WHERE UserId='manager';"); Reject<UnauthorizedAccessException>(() => service.Read(manager, day, day));
            auth.Logout(manager); Reject<UnauthorizedAccessException>(() => service.Read(manager, day, day));
            Assert(Convert.ToInt64(Sql(db, "PRAGMA user_version;")) == SqliteDatabase.CurrentSchemaVersion, "Report changed schema.");
            Console.WriteLine("PASS RoomUsageReport: real checkout, Admin/Manager current independent Report.View, exact ActualEndTime count, actual Completed/Active shift-clipped union, 780-minute standard day, CreatedAt/now/future/inactive history, weighted ratio, exact 100ns decimal, all room choices/filter, constant-time arbitrary periods, legacy timestamps and malformed guard, private immutable DTO, read-only one clock/snapshot and permission concurrency on temporary SQLite.");
        }
        finally
        {
            SQLiteConnection.ClearAllPools(); var target = Path.GetFullPath(directory); var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("MusicBoxRoomUsage_", StringComparison.Ordinal)) throw new Exception("Unsafe room usage cleanup.");
            Directory.Delete(target, true);
        }
    }
}
