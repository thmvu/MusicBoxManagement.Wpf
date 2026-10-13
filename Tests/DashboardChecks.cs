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

public static class MusicBoxDashboardChecks
{
    private sealed class Clock : IClock { public DateTimeOffset Value; public int Reads; public DateTimeOffset UtcNow { get { Reads++; return Value; } } }
    private sealed class GateClock : IClock
    {
        public DateTimeOffset Value; public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false), Release = new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow { get { Entered.Set(); if (!Release.Wait(10000)) throw new Exception("Dashboard snapshot gate timeout."); return Value; } }
    }
    private static object Sql(SqliteDatabase db, string sql) { using (var c = db.OpenConnection()) using (var command = c.CreateCommand()) { command.CommandText = sql; return command.ExecuteScalar(); } }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static DateTimeOffset At(int day, int hour, int minute) { return new DateTimeOffset(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(7)); }
    private static string Utc(DateTimeOffset value) { return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture); }
    private static void Booking(SqliteDatabase db, int id, int room, int customer, DateTimeOffset start, string status)
    { Sql(db, "INSERT INTO Reservations VALUES(" + id + "," + customer + "," + room + ",'" + Utc(start) + "','" + Utc(start.AddHours(1)) + "','" + status + "',NULL,NULL,'test');"); }
    private static void Session(SqliteDatabase db, int room, int customer, int? reservation, DateTimeOffset start, DateTimeOffset? expected, DateTimeOffset? end)
    { Sql(db, "INSERT INTO RoomSessions(CustomerId,RoomId,ReservationId,ActualStartTime,ExpectedEndTime,ActualEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES(" + customer + "," + room + "," + (reservation.HasValue ? reservation.Value.ToString() : "NULL") + ",'" + Utc(start) + "'," + (expected.HasValue ? "'" + Utc(expected.Value) + "'" : "NULL") + "," + (end.HasValue ? "'" + Utc(end.Value) + "'" : "NULL") + ",120000,'R" + room + "','STANDARD','Standard','" + (end.HasValue ? "Completed" : "Active") + "');"); }
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MusicBoxDashboard_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var db = new SqliteDatabase(Path.Combine(directory, "dashboard.db")); var auth = new AuthenticationService(db); const string password = "Dashboard-Test-2026!";
            var admin = auth.SetupAdminAsync("admin", "Admin", password).GetAwaiter().GetResult(); var clock = new Clock { Value = At(13, 13, 40) };
            Sql(db, @"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'staff','staff','STAFF',PasswordHash,'staff','Staff',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'manager','manager','MANAGER',PasswordHash,'manager','Manager',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('staff','Staff'),('manager','Manager');DELETE FROM RolePermission WHERE RoleId IN ('Staff','Manager');
INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code IN ('Dashboard.View','Report.View','Report.Export');
INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code IN ('Dashboard.View','Report.View');");
            var staff = auth.LoginAsync("staff", password).GetAwaiter().GetResult(); var manager = auth.LoginAsync("manager", password).GetAwaiter().GetResult();
            var service = new DashboardService(db, clock); clock.Reads = 0;
            new StaffReservationService(db, clock).ForDashboard(staff);
            var empty = service.Read(staff); Assert(empty.Rooms.Count == 0 && empty.TodayRevenue == null && empty.Alerts.Count == 0 && clock.Reads == 1 && empty.CheckedAt.Offset == TimeSpan.Zero, "Empty/one-clock/Staff revenue contract wrong.");
            Assert(service.Read(manager).TodayRevenue == 0 && service.ReadRevenueToday(admin) == 0, "Authorized empty revenue is not zero.");
            Reject<UnauthorizedAccessException>(() => service.Read(null)); Reject<UnauthorizedAccessException>(() => service.ReadRevenueToday(staff));
            for (var i = 1; i <= 10; i++)
            {
                Sql(db, "INSERT INTO Rooms(RoomId,RoomCode,Name,RoomTypeId,ImageUrl,IsActive,InactiveReason,CreatedAt) VALUES(" + i + ",'R" + i + "','Room " + i + "',1,'test.png'," + (i == 8 ? "0,'Locked'" : "1,NULL") + ",'test');");
                Sql(db, "INSERT INTO Customers VALUES(" + i + ",'Customer " + i + "','090000000" + (i == 10 ? "0" : i.ToString()) + "');");
            }
            Booking(db, 1, 1, 1, At(13, 13, 30), "Confirmed"); Booking(db, 2, 2, 2, At(13, 10, 0), "Confirmed");
            Booking(db, 3, 3, 3, At(13, 13, 0), "CheckedIn"); Booking(db, 4, 3, 1, At(13, 13, 30), "Confirmed");
            Booking(db, 5, 5, 4, At(13, 14, 0), "Confirmed"); Booking(db, 6, 4, 5, At(13, 15, 0), "Confirmed");
            Booking(db, 7, 2, 2, At(13, 16, 0), "Cancelled"); Booking(db, 8, 2, 2, At(13, 17, 0), "NoShow");
            Booking(db, 9, 2, 2, At(14, 9, 0), "Confirmed");
            Session(db, 3, 3, 3, At(13, 13, 0), At(13, 13, 30), null);
            Session(db, 4, 4, null, At(13, 13, 7).AddTicks(1234), null, null);
            Session(db, 6, 6, null, At(13, 11, 37), null, null);
            Session(db, 7, 7, null, At(12, 22, 0), null, null);
            Session(db, 8, 8, null, At(12, 10, 0), null, At(12, 11, 0));
            Sql(db, "INSERT INTO Orders(RoomSessionId,Status,CreatedAt) VALUES(2,'Pending','" + Utc(clock.Value) + "'),(2,'Completed','" + Utc(clock.Value) + "'),(2,'Cancelled','" + Utc(clock.Value) + "'),(5,'Pending','" + Utc(clock.Value) + "');");
            var beforeAudit = Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM AuditLog;")); clock.Reads = 0; var result = service.Read(staff);
            Assert(result.Rooms.Count == 10 && result.BookingsToday.Count == 8 && result.ActiveSessions.Count == 4 && result.PendingOrders.Count == 1 && clock.Reads == 1, "Dashboard-only operational counts or clock wrong.");
            Assert(result.BookingsToday.Single(b => b.ReservationId == 2).Status == "NoShow" && (string)Sql(db, "SELECT Status FROM Reservations WHERE ReservationId=2;") == "Confirmed", "Effective grace status wrote NoShow or showed expired Confirmed.");
            Assert(result.Rooms.Single(r => r.RoomId == 1).Status == "Reserved" && result.Rooms.Single(r => r.RoomId == 2).Status == "Available"
                && result.Rooms.Single(r => r.RoomId == 3).Status == "Occupied" && result.Rooms.Single(r => r.RoomId == 8).Status == "Inactive", "Status priority/future/grace/locked wrong.");
            var calendar = new CalendarService(db, clock).Read(admin, CalendarRange.Day(result.Date));
            Assert(result.Rooms.All(r => calendar.Single(c => c.RoomId == r.RoomId).CurrentStatus == r.Status), "Dashboard and calendar current status drifted.");
            Assert(result.Alerts.Any(a => a.Kind == "NearNoShow" && a.ReservationId == 1) && result.Alerts.Any(a => a.Kind == "Overdue" && a.SessionId == 1)
                && result.Alerts.Any(a => a.Kind == "NextBooking" && a.SessionId == 1 && a.ReservationId == 4)
                && result.Alerts.Any(a => a.Kind == "NextBooking" && a.SessionId == 2 && a.ReservationId == 5), "Required near deadline/overdue/room/customer-next warnings missing.");
            Assert(result.ActiveSessions.Single(s => s.SessionId == 2).ReturnBy == At(13, 14, 0) && result.ActiveSessions.Single(s => s.SessionId == 2).ActualStart.Ticks % TimeSpan.TicksPerSecond == 1234
                && result.ActiveSessions.Single(s => s.SessionId == 4).ReturnBy == At(12, 23, 0) && !result.Alerts.Any(a => a.Kind == "OutsideHours"), "Dynamic walk-in/customer deadline/ticks/overnight wrong.");
            Reject<UnauthorizedAccessException>(() => new CalendarService(db, clock).Read(staff, CalendarRange.Day(result.Date)));
            foreach (var type in new[] { typeof(DashboardRoom), typeof(DashboardBooking), typeof(DashboardSession), typeof(DashboardOrder), typeof(DashboardAlert) })
                Assert(!type.GetProperties().Any(p => p.Name == "CustomerId" || p.Name == "PhoneNumber" || p.Name == "CustomerName"), "Dashboard route leaked detail PII.");
            clock.Value = At(13, 13, 40).AddTicks(-1); Assert(!service.Read(staff).Alerts.Any(a => a.Kind == "NearNoShow" && a.ReservationId == 1), "More than five minutes warned early.");
            clock.Value = At(13, 13, 45); var expired = service.Read(staff); Assert(!expired.Alerts.Any(a => a.Kind == "NearNoShow" && a.ReservationId == 1) && expired.Rooms.Single(r => r.RoomId == 1).Status == "Available", "Exact grace deadline did not expire.");
            clock.Value = At(13, 13, 30); Assert(!service.Read(staff).ActiveSessions.Single(s => s.SessionId == 1).IsOverdue && service.Read(staff).Alerts.Any(a => a.Kind == "NextBooking" && a.SessionId == 1), "Exact expected end/next start boundaries wrong.");
            clock.Value = clock.Value.AddTicks(1); Assert(service.Read(staff).ActiveSessions.Single(s => s.SessionId == 1).IsOverdue, "Overdue tick lost.");
            clock.Value = At(13, 14, 0); Assert(!service.Read(staff).ActiveSessions.Single(s => s.SessionId == 2).IsOverdue, "Walk-in exact dynamic deadline marked overdue.");
            clock.Value = clock.Value.AddTicks(1); Assert(service.Read(staff).ActiveSessions.Single(s => s.SessionId == 2).IsOverdue, "Walk-in dynamic overdue tick lost.");
            clock.Value = At(13, 14, 15); Assert(service.Read(staff).ActiveSessions.Single(s => s.SessionId == 2).ReturnBy == At(13, 15, 0)
                && !service.Read(staff).ActiveSessions.Single(s => s.SessionId == 2).IsOverdue, "Expired next-customer booking did not release dynamic deadline.");
            foreach (var time in new[] { At(13, 12, 0), At(13, 12, 59), At(13, 23, 0), At(14, 0, 0), At(14, 8, 59) })
            { clock.Value = time; Assert(service.Read(staff).Alerts.Count(a => a.Kind == "OutsideHours") == 4, "Active outside-hours warning missing."); }
            foreach (var time in new[] { At(13, 9, 0), At(13, 13, 0), At(13, 22, 59) })
            { clock.Value = time; Assert(!service.Read(staff).Alerts.Any(a => a.Kind == "OutsideHours"), "Open shift marked outside-hours."); }
            Assert(Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM AuditLog;")) == beforeAudit && Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM Invoices;")) == 0, "Dashboard alerts wrote business data.");
            // Revenue comes from real paid invoices, not active estimates or Order/catalog prices.
            var sessions = new RoomSessionService(db, clock); var checkout = new CheckoutService(db, clock);
            clock.Value = At(12, 22, 0); var paidSession = sessions.CreateWalkIn(admin, new WalkInRequest { RoomId = 9, FullName = "Khách thu", PhoneNumber = "0901111111" }).Session;
            clock.Value = At(13, 0, 0); var midnight = checkout.ConfirmStaff(admin, paidSession.RoomSessionId, "Cash");
            clock.Value = At(13, 15, 0); paidSession = sessions.CreateWalkIn(admin, new WalkInRequest { RoomId = 10, FullName = "Khách lớn", PhoneNumber = "0902222222" }).Session;
            Sql(db, "UPDATE RoomSessions SET HourlyRate=9223372036854775807 WHERE RoomSessionId=" + paidSession.RoomSessionId + ";INSERT INTO Services(Name,Category,Price,IsActive) VALUES('Món lớn','Đồ uống',9223372036854775807,1);");
            new OrderService(db, clock).CreateStaff(admin, paidSession.RoomSessionId, new[] { new OrderLineRequest { ServiceId = 1, Quantity = 10 } });
            clock.Value = At(13, 17, 0); var huge = checkout.ConfirmStaff(admin, paidSession.RoomSessionId, "BankTransfer");
            var expected = midnight.TotalAmount + 12m * long.MaxValue; clock.Reads = 0;
            Assert(service.Read(manager).TodayRevenue == expected && clock.Reads == 1 && huge.TotalAmount == 12m * long.MaxValue && service.ReadRevenueToday(admin) == expected, "Revenue lost decimal precision/real paid totals or required Invoice.View.");
            Sql(db, "UPDATE RoomTypes SET PricePerHour=999000;UPDATE Services SET Price=1,IsActive=0;"); Assert(service.ReadRevenueToday(manager) == expected && service.Read(staff).TodayRevenue == null, "Revenue recomputed catalog or exposed Staff aggregate.");
            clock.Value = At(12, 23, 59); Assert(service.ReadRevenueToday(manager) == 0, "Midnight PaidAt counted on previous Vietnam day.");
            clock.Value = At(14, 0, 0); Assert(service.ReadRevenueToday(manager) == 0 && service.Read(staff).BookingsToday.Single().ReservationId == 9, "End-exclusive midnight/day booking filter wrong.");
            clock.Value = At(13, 17, 0).ToOffset(TimeSpan.FromHours(-5)); Assert(service.Read(manager).Date == new DateTime(2026, 10, 13) && service.ReadRevenueToday(manager) == expected, "Clock offset overrode Vietnam date.");
            Sql(db, "DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Report.View');");
            Assert(service.Read(manager).TodayRevenue == null, "Revoked Manager report cached."); Reject<UnauthorizedAccessException>(() => service.ReadRevenueToday(manager));
            Sql(db, "UPDATE AspNetUserRoles SET RoleId='Staff' WHERE UserId='manager';"); Assert(service.Read(manager).TodayRevenue == null, "Current role change used LoginSession's cached role.");
            Sql(db, "DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Dashboard.View');");
            clock.Reads = 0; Reject<UnauthorizedAccessException>(() => service.Read(staff)); Assert(clock.Reads == 0, "Denied dashboard still read operational clock/data.");
            Reject<UnauthorizedAccessException>(() => new StaffReservationService(db, clock).ForDashboard(staff));
            Sql(db, "INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Dashboard.View';");
            // A live read snapshot pins permissions and all operational lists while a real checkout waits.
            clock.Value = At(13, 18, 0); paidSession = sessions.CreateWalkIn(admin, new WalkInRequest { RoomId = 9, FullName = "Khách cạnh tranh", PhoneNumber = "0903333333" }).Session;
            var gate = new GateClock { Value = clock.Value }; Task<DashboardSnapshot> reading = null; Task<Invoice> writing = null;
            try
            {
                reading = Task.Run(() => new DashboardService(db, gate).Read(admin)); Assert(gate.Entered.Wait(5000), "Dashboard did not pin read snapshot.");
                clock.Value = clock.Value.AddMinutes(1); writing = Task.Run(() => checkout.ConfirmStaff(admin, paidSession.RoomSessionId, "Cash")); Thread.Sleep(100);
                Assert(!writing.IsCompleted, "Checkout did not wait for snapshot."); gate.Release.Set(); Assert(Task.WaitAll(new Task[] { reading, writing }, 15000), "Concurrent dashboard/checkout timed out.");
                Assert(reading.Result.ActiveSessions.Any(s => s.SessionId == paidSession.RoomSessionId) && reading.Result.TodayRevenue == expected
                    && !service.Read(admin).ActiveSessions.Any(s => s.SessionId == paidSession.RoomSessionId) && service.ReadRevenueToday(admin) == expected + writing.Result.TotalAmount, "Dashboard mixed before/after checkout snapshots.");
            }
            finally { gate.Release.Set(); if (reading != null) reading.GetAwaiter().GetResult(); if (writing != null) writing.GetAwaiter().GetResult(); gate.Entered.Dispose(); gate.Release.Dispose(); }
            Sql(db, "UPDATE AspNetUsers SET SecurityStamp='new' WHERE Id='staff';"); Reject<UnauthorizedAccessException>(() => service.Read(staff));
            Sql(db, "UPDATE AspNetUsers SET SecurityStamp='staff',IsActive=0 WHERE Id='staff';"); Reject<UnauthorizedAccessException>(() => service.Read(staff));
            Sql(db, "UPDATE AspNetUsers SET IsActive=1 WHERE Id='staff';"); auth.Logout(staff); Reject<UnauthorizedAccessException>(() => service.Read(staff));
            // Explicit oversized-total fixture: never silently round an aggregate or query it for Staff.
            Session(db, 8, 8, null, At(13, 18, 20), null, At(13, 18, 21));
            Sql(db, "INSERT INTO Invoices(RoomSessionId,InvoiceNumber,RoomCharge,ServiceCharge,TotalAmount,PaymentMethod,ProcessedByUserId,ProcessedByNameSnapshot,PaidAt) SELECT MAX(RoomSessionId),'OVERFLOW-FIXTURE','79228162514264337593543950335','0','79228162514264337593543950335','Cash','" + admin.UserId + "','Fixture','" + Utc(At(13, 18, 21)) + "' FROM RoomSessions;");
            Assert(service.Read(manager).TodayRevenue == null, "Staff role read unauthorized overflowing invoice aggregate.");
            Reject<InvalidOperationException>(() => service.ReadRevenueToday(admin));
            Assert(Convert.ToInt64(Sql(db, "PRAGMA user_version;")) == SqliteDatabase.CurrentSchemaVersion, "Dashboard changed schema.");
            Console.WriteLine("PASS Dashboard: live independent Dashboard.View, one read snapshot/clock, room/calendar status, today/effective grace, Active/Pending/private DTO, mandatory warnings, Admin/Manager-only PaidAt decimal revenue, revoked role/session and concurrent real checkout on temporary SQLite.");
        }
        finally
        {
            SQLiteConnection.ClearAllPools(); var target = Path.GetFullPath(directory); var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("MusicBoxDashboard_", StringComparison.Ordinal)) throw new Exception("Unsafe dashboard cleanup.");
            Directory.Delete(target, true);
        }
    }
}
