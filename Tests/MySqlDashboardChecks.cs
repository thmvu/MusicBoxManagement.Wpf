using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxMySqlDashboardChecks
{
    private sealed class Clock : IClock
    { public DateTimeOffset Value; public int Reads; public DateTimeOffset UtcNow { get { Interlocked.Increment(ref Reads); return Value; } } }
    private sealed class GateClock : IClock, IDisposable
    {
        public DateTimeOffset Value;
        public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false), Release = new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow { get { Entered.Set(); if (!Release.Wait(12000)) throw new Exception("Dashboard gate timeout."); return Value; } }
        public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
    }
    private static object Sql(MySqlDatabase db, string sql)
    { using (var c = db.OpenConnection()) using (var q = c.CreateCommand()) { q.CommandText = sql; return q.ExecuteScalar(); } }
    private static long Count(MySqlDatabase db, string sql) { return Convert.ToInt64(Sql(db, sql)); }
    private static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static DateTimeOffset At(int day, int hour, int minute = 0)
    { return new DateTimeOffset(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(7)); }
    private static Reservation Book(MySqlDatabase db, int room, string phone, DateTimeOffset start, int duration = 60)
    {
        return new MySqlReservationService(db, new Clock { Value = At(13, 9) }).CreateGuest(
            new ReservationRequest { RoomId = room, FullName = "Khách test", PhoneNumber = phone, StartTime = start, DurationMinutes = duration });
    }
    public static void Run()
    {
        var name = "musicbox_test_" + Guid.NewGuid().ToString("N"); var db = new MySqlDatabase(name);
        Console.WriteLine("Dashboard test database: " + name);
        try
        {
            const string password = "Dashboard-MySql-2026!";
            var auth = new MySqlAuthenticationService(db);
            var admin = auth.SetupAdminAsync("admin", "Admin", password).GetAwaiter().GetResult();
            Sql(db, @"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'staff','staff','STAFF',PasswordHash,'staff','Staff',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'manager','manager','MANAGER',PasswordHash,'manager','Manager',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('staff','Staff'),('manager','Manager'); DELETE FROM RolePermission WHERE RoleId IN ('Staff','Manager');
INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code IN ('Dashboard.View','Report.View','Report.Export');
INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code IN ('Dashboard.View','Report.View');");
            var staff = auth.LoginAsync("staff", password).GetAwaiter().GetResult();
            var manager = auth.LoginAsync("manager", password).GetAwaiter().GetResult();
            var clock = new Clock { Value = At(13, 13, 40) }; var service = new MySqlDashboardService(db, clock);
            new MySqlStaffReservationService(db, clock).ForDashboard(staff);
            var empty = service.Read(staff);
            Assert(empty.Rooms.Count == 0 && empty.ActiveSessions.Count == 0 && empty.BookingsToday.Count == 0 && empty.PendingOrders.Count == 0
                && empty.Alerts.Count == 0 && empty.TodayRevenue == null && clock.Reads == 1 && empty.CheckedAt.Offset == TimeSpan.Zero, "Empty/clock/Staff contract wrong.");
            Assert(service.Read(manager).TodayRevenue == 0 && service.ReadRevenueToday(admin) == 0, "Authorized empty revenue wrong.");
            Reject<UnauthorizedAccessException>(() => service.Read(null)); Reject<UnauthorizedAccessException>(() => service.ReadRevenueToday(staff));
            Reject<UnauthorizedAccessException>(() => new MySqlCalendarService(db, clock).Read(staff, CalendarRange.Day(empty.Date)));
            for (var i = 1; i <= 10; i++) Sql(db, "INSERT INTO Rooms(RoomId,RoomCode,Name,RoomTypeId,ImageUrl,IsActive,InactiveReason,CreatedAt) VALUES("
                + i + ",'R" + i + "','Phòng test',1,'test.png'," + (i == 8 ? "0,'Khóa test'" : "1,NULL") + ",'2026-10-13T02:00:00.0000000+00:00');");
            new MySqlServiceCatalogService(db).Save(admin, null, new ServiceEdit { Name = "Trà test", Category = "Đồ uống", Price = 25000, IsActive = true });
            var booked = Book(db, 1, "0900000001", At(13, 13));
            var nextRoom = Book(db, 1, "0900000002", At(13, 14));
            var nextCustomer = Book(db, 5, "0900000003", At(13, 14, 30));
            var nextWalkRoom = Book(db, 2, "0900000004", At(13, 15));
            var tomorrow = Book(db, 6, "0900000005", At(14, 9));
            var cancelled = Book(db, 7, "0900000006", At(13, 17));
            new MySqlReservationService(db, new Clock { Value = At(13, 9) }).CancelStaff(admin, cancelled.ReservationId, "Khách đổi lịch");
            var sessions = new MySqlRoomSessionService(db, clock); var orders = new MySqlOrderService(db, clock); var checkout = new MySqlCheckoutService(db, clock);
            clock.Value = At(13, 13); var active = sessions.CheckIn(admin, booked.ReservationId);
            clock.Value = At(13, 13, 7).AddTicks(1234); var walk = sessions.CreateWalkIn(admin, new WalkInRequest { RoomId = 2, FullName = "Khách walk-in", PhoneNumber = "0900000003" }).Session;
            clock.Value = At(12, 22); var overnight = sessions.CreateWalkIn(admin, new WalkInRequest { RoomId = 3, FullName = "Khách đêm", PhoneNumber = "0900000007" }).Session;
            clock.Value = At(13, 11, 37); var morning = sessions.CreateWalkIn(admin, new WalkInRequest { RoomId = 4, FullName = "Khách sáng", PhoneNumber = "0900000008" }).Session;
            clock.Value = At(13, 13, 40);
            var cart = new[] { new OrderLineRequest { ServiceId = 1, Quantity = 1 } };
            var pending = orders.CreateGuest("0900000003", walk.RoomSessionId, cart);
            orders.CreateStaff(admin, walk.RoomSessionId, cart);
            var cancelledOrder = orders.CreateGuest("0900000003", walk.RoomSessionId, cart); orders.CancelGuest("0900000003", cancelledOrder.OrderId);
            // Create via the real writer with an earlier controlled clock, then only read: stale Confirmed must be projected, not maintained.
            var expired = Book(db, 6, "0900000009", At(13, 10));
            var near = Book(db, 7, "0900000010", At(13, 13, 30));
            var audit = Count(db, "SELECT COUNT(*) FROM AuditLog;"); clock.Reads = 0; var data = service.Read(staff);
            Assert(clock.Reads == 1 && data.CheckedAt == clock.Value && data.Rooms.Count == 10 && data.BookingsToday.Count == 7
                && data.ActiveSessions.Count == 4 && data.PendingOrders.Single().OrderId == pending.OrderId && data.TodayRevenue == null, "Dashboard lists or one clock wrong.");
            Assert(data.Rooms.Single(r => r.RoomId == 1).Status == "Occupied" && data.Rooms.Single(r => r.RoomId == 7).Status == "Reserved"
                && data.Rooms.Single(r => r.RoomId == 6).Status == "Available" && data.Rooms.Single(r => r.RoomId == 8).Status == "Inactive", "Room states wrong.");
            Assert(data.BookingsToday.Single(b => b.ReservationId == expired.ReservationId).Status == "NoShow"
                && data.BookingsToday.Single(b => b.ReservationId == cancelled.ReservationId).Status == "Cancelled"
                && data.BookingsToday.Single(b => b.ReservationId == booked.ReservationId).Status == "CheckedIn", "Booking effective/stored statuses wrong.");
            Assert((string)Sql(db, "SELECT Status FROM Reservations WHERE ReservationId=" + expired.ReservationId) == "Confirmed", "Dashboard wrote NoShow.");
            Assert(data.ActiveSessions.Single(s => s.SessionId == walk.RoomSessionId).ReturnBy == At(13, 14, 30)
                && data.ActiveSessions.Single(s => s.SessionId == walk.RoomSessionId).ActualStart.Ticks % TimeSpan.TicksPerSecond == 1234
                && data.ActiveSessions.Single(s => s.SessionId == overnight.RoomSessionId).ReturnBy == At(12, 23), "Dynamic customer/overnight/ticks wrong.");
            Assert(data.Alerts.Any(a => a.Kind == "NearNoShow" && a.ReservationId == near.ReservationId)
                && data.Alerts.Any(a => a.Kind == "Overdue" && a.SessionId == morning.RoomSessionId)
                && data.Alerts.Any(a => a.Kind == "NextBooking" && a.SessionId == walk.RoomSessionId && a.ReservationId == nextCustomer.ReservationId), "Required warnings missing.");
            var calendar = new MySqlCalendarService(db, clock).Read(admin, CalendarRange.Day(data.Date));
            Assert(data.Rooms.All(r => calendar.Single(c => c.RoomId == r.RoomId).CurrentStatus == r.Status), "Calendar/dashboard status drift.");
            foreach (var type in new[] { typeof(DashboardRoom), typeof(DashboardBooking), typeof(DashboardSession), typeof(DashboardOrder), typeof(DashboardAlert) })
                Assert(!type.GetProperties().Any(p => p.Name == "CustomerId" || p.Name == "PhoneNumber" || p.Name == "CustomerName"), "Dashboard exposed Customer PII.");
            clock.Value = At(13, 13, 40).AddTicks(-1); Assert(!service.Read(staff).Alerts.Any(a => a.Kind == "NearNoShow"), "Warned before five-minute boundary.");
            clock.Value = At(13, 13, 45); var grace = service.Read(staff);
            Assert(!grace.Alerts.Any(a => a.Kind == "NearNoShow") && grace.Rooms.Single(r => r.RoomId == 7).Status == "Available", "Exact grace wrong.");
            clock.Value = At(13, 14); var exact = service.Read(staff);
            Assert(!exact.ActiveSessions.Single(s => s.SessionId == active.RoomSessionId).IsOverdue
                && exact.Alerts.Any(a => a.Kind == "NextBooking" && a.ReservationId == nextRoom.ReservationId), "Expected-end/next start wrong.");
            clock.Value = clock.Value.AddTicks(1); Assert(service.Read(staff).ActiveSessions.Single(s => s.SessionId == active.RoomSessionId).IsOverdue, "Expected-end tick lost.");
            clock.Value = At(13, 14, 30); Assert(!service.Read(staff).ActiveSessions.Single(s => s.SessionId == walk.RoomSessionId).IsOverdue, "Exact ReturnBy overdue.");
            clock.Value = clock.Value.AddTicks(1); Assert(service.Read(staff).ActiveSessions.Single(s => s.SessionId == walk.RoomSessionId).IsOverdue, "ReturnBy tick lost.");
            clock.Value = At(13, 14, 45); Assert(service.Read(staff).ActiveSessions.Single(s => s.SessionId == walk.RoomSessionId).ReturnBy == At(13, 15), "Expired customer booking failed to release ReturnBy.");
            foreach (var time in new[] { At(13, 12), At(13, 12, 59), At(13, 23), At(14, 0), At(14, 8, 59) })
            { clock.Value = time; Assert(service.Read(staff).Alerts.Count(a => a.Kind == "OutsideHours") == 4, "Outside-hours alerts missing."); }
            foreach (var time in new[] { At(13, 9), At(13, 13), At(13, 22, 59) })
            { clock.Value = time; Assert(!service.Read(staff).Alerts.Any(a => a.Kind == "OutsideHours"), "Open hours warned."); }
            Assert(Count(db, "SELECT COUNT(*) FROM AuditLog;") == audit && Count(db, "SELECT COUNT(*) FROM Invoices;") == 0
                && Count(db, "SELECT COUNT(*) FROM RoomSessions WHERE Status='Active';") == 4, "Read changed business data.");
            Console.WriteLine("PASS Dashboard operational lists, real writers, exact warnings/ticks/grace, dynamic ReturnBy, private DTO and read-only");

            clock.Value = At(12, 22); var paidSession = sessions.CreateWalkIn(admin, new WalkInRequest { RoomId = 9, FullName = "Khách thu", PhoneNumber = "0900000011" }).Session;
            clock.Value = At(13, 0); var midnight = checkout.ConfirmStaff(admin, paidSession.RoomSessionId, "Cash");
            clock.Value = At(13, 15); var large = sessions.CreateWalkIn(admin, new WalkInRequest { RoomId = 10, FullName = "Khách lớn", PhoneNumber = "0900000012" }).Session;
            Sql(db, "UPDATE RoomSessions SET HourlyRate=9223372036854775807 WHERE RoomSessionId=" + large.RoomSessionId + "; UPDATE Services SET Price=9223372036854775807;");
            orders.CreateStaff(admin, large.RoomSessionId, new[] { new OrderLineRequest { ServiceId = 1, Quantity = 10 } });
            clock.Value = At(13, 17); var huge = checkout.ConfirmStaff(admin, large.RoomSessionId, "BankTransfer");
            var expected = midnight.TotalAmount + 12m * long.MaxValue; clock.Reads = 0;
            Assert(huge.TotalAmount == 12m * long.MaxValue && service.Read(manager).TodayRevenue == expected && clock.Reads == 1
                && service.ReadRevenueToday(admin) == expected, "Real PaidAt/decimal revenue wrong.");
            Assert(service.Read(staff).PendingOrders.Single().OrderId == pending.OrderId, "Checkout retained its Pending or included Completed orders.");
            Sql(db, "UPDATE RoomTypes SET PricePerHour=999000; UPDATE Services SET Price=1,IsActive=0;");
            Assert(service.ReadRevenueToday(manager) == expected && service.Read(staff).TodayRevenue == null, "Repriced history or Staff leak.");
            clock.Value = At(12, 23, 59); Assert(service.ReadRevenueToday(manager) == 0, "Midnight included previous day.");
            clock.Value = At(14, 0); Assert(service.ReadRevenueToday(manager) == 0 && service.Read(staff).BookingsToday.Single().ReservationId == tomorrow.ReservationId, "End-exclusive day wrong.");
            clock.Value = At(13, 17).ToOffset(TimeSpan.FromHours(-5)); Assert(service.Read(manager).Date == new DateTime(2026, 10, 13) && service.ReadRevenueToday(manager) == expected, "Clock offset changed Vietnam date.");

            clock.Value = At(13, 18); var live = sessions.CreateWalkIn(admin, new WalkInRequest { RoomId = 9, FullName = "Khách cạnh tranh", PhoneNumber = "0900000013" }).Session;
            Sql(db, "UPDATE Services SET IsActive=1;"); var livePending = orders.CreateGuest("0900000013", live.RoomSessionId, cart);
            using (var gate = new GateClock { Value = clock.Value })
            {
                var reading = Task.Run(() => new MySqlDashboardService(db, gate).Read(manager));
                try
                {
                    Assert(gate.Entered.Wait(5000), "Reader did not reach rights snapshot."); clock.Value = clock.Value.AddMinutes(1);
                    var writing = Task.Run(() => checkout.ConfirmStaff(admin, live.RoomSessionId, "Cash"));
                    Assert(writing.Wait(8000), "RR reader blocked real checkout.");
                    Sql(db, "DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Report.View'); UPDATE Rooms SET Name='Tên mới' WHERE RoomId=9;");
                    gate.Release.Set(); var old = reading.GetAwaiter().GetResult(); var fresh = service.Read(manager);
                    Assert(old.TodayRevenue == expected && old.ActiveSessions.Any(s => s.SessionId == live.RoomSessionId)
                        && old.PendingOrders.Any(o => o.OrderId == livePending.OrderId) && old.Rooms.Single(r => r.RoomId == 9).RoomName == "Phòng test"
                        && fresh.TodayRevenue == null && !fresh.ActiveSessions.Any(s => s.SessionId == live.RoomSessionId)
                        && !fresh.PendingOrders.Any(o => o.OrderId == livePending.OrderId) && fresh.Rooms.Single(r => r.RoomId == 9).RoomName == "Tên mới"
                        && service.ReadRevenueToday(admin) == expected + writing.Result.TotalAmount, "Mixed permission/metadata/operational/revenue snapshots.");
                }
                finally { gate.Release.Set(); reading.GetAwaiter().GetResult(); }
            }
            Reject<UnauthorizedAccessException>(() => service.ReadRevenueToday(manager));
            Sql(db, "INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.View'; UPDATE AspNetUserRoles SET RoleId='Staff' WHERE UserId='manager';");
            Assert(service.Read(manager).TodayRevenue == null, "Cached role exposed revenue.");
            Sql(db, "DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Dashboard.View');");
            clock.Reads = 0; Reject<UnauthorizedAccessException>(() => service.Read(staff)); Reject<UnauthorizedAccessException>(() => service.ReadRevenueToday(staff));
            Reject<UnauthorizedAccessException>(() => new MySqlStaffReservationService(db, clock).ForDashboard(staff)); Assert(clock.Reads == 0, "Denied access took clock.");
            Sql(db, "INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Dashboard.View'; UPDATE AspNetUsers SET SecurityStamp='new' WHERE Id='staff';");
            Reject<UnauthorizedAccessException>(() => service.Read(staff)); Sql(db, "UPDATE AspNetUsers SET SecurityStamp='staff',IsActive=0 WHERE Id='staff';");
            Reject<UnauthorizedAccessException>(() => service.Read(staff)); Sql(db, "UPDATE AspNetUsers SET IsActive=1 WHERE Id='staff';");
            auth.Logout(staff); Reject<UnauthorizedAccessException>(() => service.Read(staff));
            // Corruption fixture only, in this owned GUID database; preserve and restore the exact production trigger.
            var invalidTotals = "UPDATE Invoices SET RoomCharge=50000000000000000000000000000,ServiceCharge=0,TotalAmount=50000000000000000000000000000 WHERE InvoiceId IN (" + midnight.InvoiceId + "," + huge.InvoiceId + ");";
            Reject<MySqlConnector.MySqlException>(() => Sql(db, invalidTotals));
            if (!System.Text.RegularExpressions.Regex.IsMatch(db.DatabaseName, @"\Amusicbox_test_[0-9a-f]{32}\z")) throw new Exception("Unsafe corruption fixture.");
            string immutableTrigger;
            using (var c = db.OpenConnection()) using (var q = c.CreateCommand())
            {
                q.CommandText = "SHOW CREATE TRIGGER Invoices_NoUpdate;";
                using (var reader = q.ExecuteReader()) { Assert(reader.Read(), "Missing immutable trigger."); immutableTrigger = reader.GetString(reader.GetOrdinal("SQL Original Statement")); }
            }
            Sql(db, "DROP TRIGGER Invoices_NoUpdate;");
            try
            {
                Sql(db, invalidTotals);
                Assert(service.Read(manager).TodayRevenue == null, "Staff queried overflowing aggregate.");
                Reject<InvalidOperationException>(() => service.ReadRevenueToday(admin)); Reject<InvalidOperationException>(() => service.Read(admin));
            }
            finally
            {
                try
                {
                    Sql(db, "UPDATE Invoices SET RoomCharge=" + midnight.RoomCharge.ToString(CultureInfo.InvariantCulture) + ",ServiceCharge=" + midnight.ServiceCharge.ToString(CultureInfo.InvariantCulture) + ",TotalAmount=" + midnight.TotalAmount.ToString(CultureInfo.InvariantCulture) + " WHERE InvoiceId=" + midnight.InvoiceId
                        + "; UPDATE Invoices SET RoomCharge=" + huge.RoomCharge.ToString(CultureInfo.InvariantCulture) + ",ServiceCharge=" + huge.ServiceCharge.ToString(CultureInfo.InvariantCulture) + ",TotalAmount=" + huge.TotalAmount.ToString(CultureInfo.InvariantCulture) + " WHERE InvoiceId=" + huge.InvoiceId + ";");
                }
                finally { Sql(db, immutableTrigger); }
            }
            Reject<MySqlConnector.MySqlException>(() => Sql(db, invalidTotals));
            Assert(service.ReadRevenueToday(admin) > expected && Count(db, "SELECT Version FROM _MusicBoxSchemaVersion;") == MySqlDatabase.CurrentSchemaVersion, "Restore/schema wrong.");
            Console.WriteLine("PASS MySQL Dashboard: independent live rights/roles/no Staff revenue, real PaidAt invoices/decimal/overflow, RR MVCC permission-data snapshot vs real checkout, no provider/import/UI change");
        }
        catch (Exception error) { Console.WriteLine("Dashboard failure before cleanup: " + error); throw; }
        finally
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"\Amusicbox_test_[0-9a-f]{32}\z")) throw new Exception("Unsafe cleanup.");
            Sql(new MySqlDatabase("defaultdb"), "DROP DATABASE IF EXISTS `" + name + "`");
        }
    }
}
