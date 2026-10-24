using System;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using SkiaSharp;

// Real service chains on one temporary database; never manufacture Completed/Invoice fixtures.
public static class MusicBoxWorkflowChecks
{
    private sealed class Clock : IClock { public DateTimeOffset Now; public DateTimeOffset UtcNow { get { return Now; } } }
    private static DateTimeOffset At(int hour, int minute = 0, int second = 0)
    { return new DateTimeOffset(2026, 10, 26, hour, minute, second, TimeSpan.FromHours(7)); }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static object Sql(SqliteDatabase db, string sql)
    { using (var c = db.OpenConnection()) using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; return cmd.ExecuteScalar(); } }
    private static long Count(SqliteDatabase db, string sql) { return Convert.ToInt64(Sql(db, sql)); }
    private static OrderLineRequest[] Cart(int id, int quantity)
    { return new[] { new OrderLineRequest { ServiceId = id, Quantity = quantity } }; }
    private static ReservationRequest Booking(int room, string phone, DateTimeOffset start)
    { return new ReservationRequest { RoomId = room, FullName = "Khách đặt", PhoneNumber = phone, StartTime = start, DurationMinutes = 60 }; }

    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "MusicBoxWorkflow_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var db = new SqliteDatabase(Path.Combine(root, "workflow.db"));
            var clock = new Clock { Now = At(9) };
            var auth = new AuthenticationService(db);
            const string password = "Workflow-Test-2026!";
            var admin = auth.SetupAdminAsync("admin", "Quản trị", password).GetAwaiter().GetResult();
            var accounts = new AccountManagementService(db, clock);
            accounts.CreateAsync(admin, new AccountEdit { UserName = "staff", FullName = "Nhân viên", Role = "Staff" }, password).GetAwaiter().GetResult();
            accounts.CreateAsync(admin, new AccountEdit { UserName = "manager", FullName = "Quản lý", Role = "Manager" }, password).GetAwaiter().GetResult();
            var staff = auth.LoginAsync("staff", password).GetAwaiter().GetResult();
            var manager = auth.LoginAsync("manager", password).GetAwaiter().GetResult();
            var image = Path.Combine(root, "input.png");
            using (var bitmap = new SKBitmap(2, 2)) using (var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100))
                File.WriteAllBytes(image, encoded.ToArray());
            var rooms = new RoomService(db, clock);
            var types = new RoomTypeService(db);
            var standard = types.List().Single(x => x.Code == "STANDARD");
            var vip = types.List().Single(x => x.Code == "VIP");
            var firstRoom = rooms.Create(manager, new RoomCreate { RoomCode = "WF01", Name = "Phòng 1", RoomTypeId = standard.RoomTypeId, ImageFilePath = image });
            var secondRoom = rooms.Create(manager, new RoomCreate { RoomCode = "WF02", Name = "Phòng 2", RoomTypeId = vip.RoomTypeId, ImageFilePath = image });
            var catalog = new ServiceCatalogService(db);
            var drink = catalog.Save(manager, null, new ServiceEdit { Name = "Trà", Category = "Đồ uống", Price = 25000, IsActive = true });
            var guest = new GuestBookingService(db, clock);
            var reservations = new ReservationService(db, clock);
            var sessions = new RoomSessionService(db, clock);
            var lookup = new GuestReservationService(db, clock);
            var guestSessions = new GuestSessionService(db, clock);
            var orders = new OrderService(db, clock);
            var billing = new BillingService(db, clock);
            var checkout = new CheckoutService(db, clock);
            var invoices = new InvoiceService(db, clock);
            var reports = new ReportService(db, clock);
            var dashboard = new DashboardService(db, clock);
            var history = new CustomerHistoryService(db, clock);
            var calendar = new CalendarService(db, clock);

            // A: Guest booking -> calendar -> check-in -> extension/orders -> phone/catalog changes -> payment -> reports/history.
            Assert(guest.ListRooms().Count == 2, "Public room catalog missing.");
            var request = Booking(firstRoom, "+84 901.111-111", At(13));
            Assert(guest.Preview(request).CanBook, "Valid booking preview denied.");
            var source = guest.Create(request);
            Assert(lookup.Lookup("0901111111").Items.Single().ReservationId == source.ReservationId, "Created booking missing from Guest lookup.");
            Assert(calendar.Read(staff, CalendarRange.Day(At(13).Date)).Single(r => r.RoomId == firstRoom).Events.Any(e => e.ReservationId == source.ReservationId), "Booking missing from Day calendar.");
            Assert(calendar.Read(staff, CalendarRange.Week(At(13).Date)).Single(r => r.RoomId == firstRoom).Holds.Count == 1, "Week hold differs from Day.");
            clock.Now = At(13, 7, 12);
            var active = sessions.CheckIn(staff, source.ReservationId);
            Assert(active.ActualStartTime == clock.Now && active.ExpectedEndTime == clock.Now.AddHours(1), "Check-in shortened/rounded duration.");
            Assert(sessions.CheckIn(staff, source.ReservationId).RoomSessionId == active.RoomSessionId, "Check-in retry duplicated session.");
            var extension = guestSessions.PreviewExtension("0901111111", active.RoomSessionId, 30);
            Assert(extension.CanExtend, "Guest extension denied.");
            var extended = guestSessions.Extend("0901111111", active.RoomSessionId, active.ExpectedEndTime.Value, 30);
            Assert(extended.ExpectedEndTime == active.ExpectedEndTime.Value.AddMinutes(30), "Extension changed wrong end.");
            Assert((string)Sql(db, "SELECT EndTime FROM Reservations WHERE ReservationId=" + source.ReservationId) == source.EndTime.ToUniversalTime().ToString("O"), "Extension rewrote original reservation.");
            var served = orders.CreateGuest("0901111111", active.RoomSessionId, Cart(drink, 2));
            orders.ConfirmStaff(staff, served.OrderId);
            orders.CreateStaff(staff, active.RoomSessionId, Cart(drink, 1));
            var cancelled = orders.CreateGuest("0901111111", active.RoomSessionId, Cart(drink, 1));
            orders.CancelGuest("0901111111", cancelled.OrderId);
            var pending = orders.CreateGuest("0901111111", active.RoomSessionId, Cart(drink, 1));
            clock.Now = At(13, 8, 42);
            var bill = billing.ReadGuest("0901111111", active.RoomSessionId);
            Assert(bill.RoomCharge == 3000 && bill.ServiceCharge == 75000 && bill.PendingOrderCount == 1, "Actual seconds/Completed-only bill incorrect.");
            var customers = new CustomerService(db);
            var customer = customers.Search(staff, null, "0901111111").Items.Single();
            customers.Save(staff, customer, new CustomerEdit { FullName = "Khách đổi số", PhoneNumber = "0902222222" });
            Assert(lookup.Lookup("0901111111").ActiveSession == null && lookup.Lookup("0902222222").ActiveSession.SessionId == active.RoomSessionId, "Guest membership did not follow current phone.");
            Reject<InvalidOperationException>(() => orders.CreateGuest("0901111111", active.RoomSessionId, Cart(drink, 1)));
            var oldDrink = catalog.ListForManagement(manager).Single();
            catalog.Save(manager, oldDrink, new ServiceEdit { Name = "Trà đổi tên", Category = oldDrink.Category, Price = 99000, IsActive = false });
            var originalType = types.GetForEdit(manager, standard.RoomTypeId);
            types.Update(manager, originalType, new RoomTypeEdit { Name = "Standard mới", Capacity = originalType.Capacity, PricePerHour = 200000, Amenities = originalType.Amenities });
            clock.Now = At(13, 9, 12);
            var preview = checkout.PreviewStaff(staff, active.RoomSessionId);
            Assert(preview.Bill.TotalAmount == 79000 && dashboard.Read(manager).TodayRevenue == 0, "Preview counted Pending or became revenue.");
            clock.Now = At(13, 10, 12);
            var logsBefore = Count(db, "SELECT COUNT(*) FROM AuditLog");
            Sql(db, "CREATE TRIGGER FailWorkflowInvoice BEFORE INSERT ON AuditLog WHEN NEW.Action='Invoice.Create' BEGIN SELECT RAISE(ABORT,'workflow rollback'); END;");
            Reject<SQLiteException>(() => checkout.ConfirmStaff(staff, active.RoomSessionId, "Cash"));
            Assert((string)Sql(db, "SELECT Status FROM RoomSessions WHERE RoomSessionId=" + active.RoomSessionId) == "Active"
                && (string)Sql(db, "SELECT Status FROM Reservations WHERE ReservationId=" + source.ReservationId) == "CheckedIn"
                && (string)Sql(db, "SELECT Status FROM Orders WHERE OrderId=" + pending.OrderId) == "Pending"
                && Count(db, "SELECT COUNT(*) FROM Invoices") == 0 && Count(db, "SELECT COUNT(*) FROM AuditLog") == logsBefore, "Failed checkout partially committed.");
            Sql(db, "DROP TRIGGER FailWorkflowInvoice;");
            var paid = checkout.ConfirmStaff(staff, active.RoomSessionId, "Cash");
            Assert(paid.TotalAmount == 81000 && paid.RoomCharge == 6000 && paid.ServiceCharge == 75000 && paid.PaidAt == clock.Now, "Confirm trusted old preview/catalog instead of fresh time/snapshots.");
            Assert(checkout.ConfirmStaff(staff, active.RoomSessionId, "BankTransfer").InvoiceId == paid.InvoiceId, "Checkout retry duplicated invoice.");
            var print = invoices.ReadForPrint(staff, paid.InvoiceId);
            Assert(print.HourlyRate == 120000 && print.RoomTypeNameSnapshot == originalType.Name && print.Items.Sum(i => i.Quantity) == 3
                && print.Items.All(i => i.UnitPrice == 25000 && i.ServiceNameSnapshot == "Trà"), "Print used mutable catalog or Pending/Cancelled items.");
            Assert(lookup.Lookup("0902222222").ActiveSession == null && lookup.Lookup("0902222222").Items.Count == 0, "Checkout left Guest active/source visible.");
            Assert(dashboard.Read(staff).TodayRevenue == null && dashboard.Read(manager).TodayRevenue == paid.TotalAmount, "Dashboard role/revenue mismatch.");
            Assert(reports.ReadRevenue(manager, At(9).Date, At(9).Date).TotalAmount == paid.TotalAmount, "Paid invoice not reflected in report.");
            Reject<UnauthorizedAccessException>(() => reports.ReadRevenue(staff, At(9).Date, At(9).Date));
            Assert(reports.ExportRevenue(manager, At(9).Date, At(9).Date).GetBytes().Length > 500, "Paid report did not export.");
            var customerHistory = history.Read(staff, customer.CustomerId);
            Assert(customerHistory.CompletedSessionCount == 1 && customerHistory.Invoices.Single().InvoiceId == paid.InvoiceId
                && customerHistory.Reservations.Single().Status == "Completed" && customerHistory.Sessions.Single().HourlyRate == 120000, "History did not follow checkout/CustomerId snapshots.");
            Assert(calendar.Read(staff, CalendarRange.Day(At(9).Date)).Single(r => r.RoomId == firstRoom).CurrentStatus == "Available", "Checkout failed to release room.");

            // B: Walk-in -> future Room/Customer bookings -> dynamic deadline -> checkout -> next real check-in.
            clock.Now = At(16);
            var walk = sessions.CreateWalkIn(staff, new WalkInRequest { RoomId = secondRoom, FullName = "Khách trực tiếp", PhoneNumber = "0903333333" });
            Assert(walk.Session.ReservationId == null && walk.Session.ExpectedEndTime == null, "Walk-in manufactured reservation/duration.");
            var future = reservations.CreateGuest(Booking(secondRoom, "0904444444", At(17)));
            var next = reservations.CreateStaff(staff, Booking(firstRoom, "0903333333", At(16, 30)));
            Assert(sessions.ReadWalkIn(staff, walk.Session.RoomSessionId).ReturnBy == At(16, 30), "Walk-in deadline ignored next Customer booking.");
            clock.Now = At(16, 30);
            Reject<InvalidOperationException>(() => sessions.CheckIn(staff, next.ReservationId));
            Reject<InvalidOperationException>(() => guestSessions.Extend("0903333333", walk.Session.RoomSessionId, clock.Now, 30));
            clock.Now = At(16, 31); checkout.ConfirmStaff(staff, walk.Session.RoomSessionId, "BankTransfer");
            var nextActive = sessions.CheckIn(staff, next.ReservationId);
            Assert(nextActive.CustomerId == walk.Session.CustomerId && nextActive.RoomId == firstRoom, "Checkout failed to release Customer for next room.");
            clock.Now = At(16, 32); checkout.ConfirmStaff(staff, nextActive.RoomSessionId, "Cash");
            Reject<InvalidOperationException>(() => lookup.Cancel("0904444444", future.ReservationId));
            Reject<ArgumentException>(() => reservations.CancelStaff(staff, future.ReservationId, ""));
            reservations.CancelStaff(staff, future.ReservationId, "Khách đổi kế hoạch");

            // C: Exact Guest cancellation boundary, effective NoShow before worker, then new walk-in.
            clock.Now = At(9).AddDays(1);
            var cancel = reservations.CreateGuest(Booking(firstRoom, "0905555555", At(11).AddDays(1)));
            lookup.Cancel("0905555555", cancel.ReservationId);
            var noShow = reservations.CreateGuest(Booking(firstRoom, "0906666666", At(14).AddDays(1)));
            clock.Now = At(14, 15).AddDays(1);
            Reject<InvalidOperationException>(() => sessions.CheckIn(staff, noShow.ReservationId));
            Assert(calendar.Read(staff, CalendarRange.Day(clock.Now.Date)).Single(r => r.RoomId == firstRoom).Holds.Count == 0, "Expired grace still holds room before worker.");
            var worker = new NoShowService(db, clock);
            Assert(worker.ProcessExpired() == 1 && worker.ProcessExpired() == 0, "NoShow was not idempotent at grace boundary.");
            var fallback = sessions.CreateWalkIn(staff, new WalkInRequest { RoomId = firstRoom, FullName = "Đến sau grace", PhoneNumber = "0906666666" });
            var matrix = new PermissionManagementService(db, clock);
            var original = matrix.Read(admin, "Staff");
            matrix.Update(admin, original, original.PermissionCodes.Where(code => code != "Session.CheckOut"));
            Reject<UnauthorizedAccessException>(() => checkout.ConfirmStaff(staff, fallback.Session.RoomSessionId, "Cash"));
            matrix.Update(admin, matrix.Read(admin, "Staff"), original.PermissionCodes);
            clock.Now = clock.Now.AddMinutes(1); checkout.ConfirmStaff(staff, fallback.Session.RoomSessionId, "Cash");
            Assert(Count(db, "SELECT COUNT(*) FROM RoomSessions s LEFT JOIN Invoices i ON i.RoomSessionId=s.RoomSessionId WHERE (s.Status='Completed' AND i.InvoiceId IS NULL) OR (s.Status='Active' AND i.InvoiceId IS NOT NULL)") == 0, "Session/Invoice lifecycle invariant broken.");
            Assert(Count(db, "SELECT COUNT(*) FROM (SELECT RoomId FROM RoomSessions WHERE Status='Active' GROUP BY RoomId HAVING COUNT(*)>1)") == 0
                && Count(db, "SELECT COUNT(*) FROM (SELECT CustomerId FROM RoomSessions WHERE Status='Active' GROUP BY CustomerId HAVING COUNT(*)>1)") == 0, "Duplicate Active Room/Customer.");
            Assert(Sql(db, "PRAGMA foreign_key_check;") == null, "Workflow left broken foreign keys.");
            Console.WriteLine("PASS end-to-end workflows: real booking/calendar/check-in/Guest extension/orders/current phone/snapshots/bill/rollback/payment/replay/invoice/print/report/Excel/history; walk-in/future holds/next Customer check-in; cancellation/NoShow/current RBAC. No fabricated Completed or Invoice states.");
        }
        finally
        {
            SQLiteConnection.ClearAllPools();
            var resolved = Path.GetFullPath(root);
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(resolved), temp, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith("MusicBoxWorkflow_", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing cleanup outside workflow temporary directory.");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }
}
