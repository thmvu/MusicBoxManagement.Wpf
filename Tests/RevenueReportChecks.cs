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

public static class MusicBoxRevenueReportChecks
{
    private sealed class Clock : IClock { public DateTimeOffset Value; public int Reads; public DateTimeOffset UtcNow { get { Reads++; return Value; } } }
    private sealed class GateClock : IClock
    {
        public DateTimeOffset Value; public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false), Release = new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow { get { Entered.Set(); if (!Release.Wait(10000)) throw new Exception("Revenue snapshot gate timeout."); return Value; } }
    }
    private static object Sql(SqliteDatabase db, string sql) { using (var c = db.OpenConnection()) using (var command = c.CreateCommand()) { command.CommandText = sql; return command.ExecuteScalar(); } }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static DateTimeOffset At(int month, int day, int hour) { return new DateTimeOffset(2026, month, day, hour, 0, 0, TimeSpan.FromHours(7)); }
    private static string Utc(DateTimeOffset value) { return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture); }
    private static void Room(SqliteDatabase db, int id, int type)
    { Sql(db, "INSERT INTO Rooms(RoomId,RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES(" + id + ",'R" + id + "','Room " + id + "'," + type + ",'test.png',1,'test');"); }
    private static void InvoiceFixture(SqliteDatabase db, LoginSession admin, int id, string paidAt, string type, decimal room, decimal service)
    {
        Room(db, id, type == "VIP" ? 2 : 1);
        Sql(db, "INSERT INTO Customers VALUES(" + id + ",'Fixture','090000" + id.ToString("0000") + "');" +
            "INSERT INTO RoomSessions(RoomSessionId,CustomerId,RoomId,ActualStartTime,ActualEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES(" + id + "," + id + "," + id + ",'" + Utc(At(9, 29, 9)) + "','" + Utc(At(9, 29, 10)) + "',120000,'R" + id + "','" + type + "','Old type name','Completed');" +
            "INSERT INTO Invoices(RoomSessionId,InvoiceNumber,RoomCharge,ServiceCharge,TotalAmount,PaymentMethod,ProcessedByUserId,ProcessedByNameSnapshot,PaidAt) VALUES(" + id + ",'FIX-" + id + "','" + room.ToString(CultureInfo.InvariantCulture) + "','" + service.ToString(CultureInfo.InvariantCulture) + "','" + (room + service).ToString(CultureInfo.InvariantCulture) + "','Cash','" + admin.UserId + "','Admin','" + paidAt + "');");
    }
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MusicBoxRevenueReports_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var db = new SqliteDatabase(Path.Combine(directory, "reports.db")); var auth = new AuthenticationService(db); const string password = "Report-Test-2026!";
            var admin = auth.SetupAdminAsync("admin", "Admin", password).GetAwaiter().GetResult(); var clock = new Clock { Value = At(10, 18, 10) };
            Sql(db, @"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'staff','staff','STAFF',PasswordHash,'staff','Staff',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'manager','manager','MANAGER',PasswordHash,'manager','Manager',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('staff','Staff'),('manager','Manager');DELETE FROM RolePermission WHERE RoleId IN ('Staff','Manager');
INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code IN ('Report.View','Report.Export');
INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.View';");
            var staff = auth.LoginAsync("staff", password).GetAwaiter().GetResult(); var manager = auth.LoginAsync("manager", password).GetAwaiter().GetResult();
            var report = new ReportService(db, clock); var from = new DateTime(2026, 10, 1); var to = new DateTime(2026, 10, 31);
            clock.Reads = 0; var empty = report.ReadRevenue(manager, from, to);
            Assert(empty.InvoiceCount == 0 && empty.TotalAmount == 0 && empty.Days.Count == 0 && empty.Months.Count == 0 && empty.RoomTypes.Count == 0 && clock.Reads == 1 && empty.CheckedAt.Offset == TimeSpan.Zero, "Empty/current clock contract wrong.");
            Reject<UnauthorizedAccessException>(() => report.ReadRevenue(null, from, to)); Reject<UnauthorizedAccessException>(() => report.ReadRevenue(staff, from, to));
            Reject<ArgumentException>(() => report.ReadRevenue(admin, to, from)); Reject<ArgumentException>(() => report.ReadRevenue(admin, DateTime.MinValue, to));
            Reject<ArgumentException>(() => report.ReadRevenue(admin, from, DateTime.MaxValue)); Reject<ArgumentException>(() => report.ReadRevenue(admin, from, to, "OTHER"));
            // Primary source evidence: real walk-in + real completed orders + real checkout, with totals beyond Int64.
            Room(db, 1, 1); Room(db, 2, 2); Room(db, 3, 1);
            var sessions = new RoomSessionService(db, clock); var checkout = new CheckoutService(db, clock);
            clock.Value = At(9, 30, 22); var first = sessions.CreateWalkIn(admin, new WalkInRequest { RoomId = 1, FullName = "Khách trả lúc nửa đêm", PhoneNumber = "0901111111" }).Session;
            clock.Value = At(10, 1, 0); var a = checkout.ConfirmStaff(admin, first.RoomSessionId, "Cash");
            clock.Value = At(10, 2, 13); var second = sessions.CreateWalkIn(admin, new WalkInRequest { RoomId = 2, FullName = "Khách VIP", PhoneNumber = "0902222222" }).Session;
            Sql(db, "UPDATE RoomSessions SET HourlyRate=9223372036854775807 WHERE RoomSessionId=" + second.RoomSessionId + ";INSERT INTO Services(Name,Category,Price,IsActive) VALUES('Món lớn','Đồ uống',9223372036854775807,1);");
            new OrderService(db, clock).CreateStaff(admin, second.RoomSessionId, new[] { new OrderLineRequest { ServiceId = 1, Quantity = 10 } });
            clock.Value = At(10, 2, 15); var b = checkout.ConfirmStaff(admin, second.RoomSessionId, "BankTransfer");
            var active = sessions.CreateWalkIn(admin, new WalkInRequest { RoomId = 3, FullName = "Chưa trả tiền", PhoneNumber = "0903333333" }).Session;
            new OrderService(db, clock).CreateStaff(admin, active.RoomSessionId, new[] { new OrderLineRequest { ServiceId = 1, Quantity = 10 } });
            var expected = a.TotalAmount + b.TotalAmount; clock.Reads = 0; var result = report.ReadRevenue(manager, from.AddHours(9), to.AddHours(23), "  ");
            Assert(result.InvoiceCount == 2 && result.TotalAmount == expected && result.RoomCharge == a.RoomCharge + b.RoomCharge && result.ServiceCharge == b.ServiceCharge && clock.Reads == 1 && result.FromDate == from && result.ToDate == to && result.RoomTypeCode == null, "Real paid revenue, split totals, date-only or independent Report.View wrong.");
            Assert(result.Days.Count == 2 && result.Days[0].Key == "2026-10-01" && result.Days[0].Label == "01/10/2026" && result.Days[1].TotalAmount == b.TotalAmount && result.Months.Single().Key == "2026-10" && result.Months.Single().TotalAmount == expected, "Day/month grouping wrong.");
            Assert(result.RoomTypes.Count == 2 && result.RoomTypes[0].Key == "STANDARD" && result.RoomTypes[1].Key == "VIP" && result.RoomTypes[1].TotalAmount == 12m * long.MaxValue && result.TotalAmountLabel == expected.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")), "Snapshot grouping/large decimal/display wrong.");
            Sql(db, "UPDATE Rooms SET RoomTypeId=1,IsActive=0,InactiveReason='Locked' WHERE RoomId=2;UPDATE RoomTypes SET Name='Changed',PricePerHour=1;UPDATE Services SET Price=1,IsActive=0;");
            var vip = report.ReadRevenue(manager, from, to, " vip "); Assert(vip.InvoiceCount == 1 && vip.RoomTypeCode == "VIP" && vip.TotalAmount == b.TotalAmount && vip.RoomTypes.Single().Label == "VIP", "Current catalog/locked room reclassified or recomputed snapshot history.");
            Assert(report.ReadRevenue(admin, new DateTime(2026, 9, 30), new DateTime(2026, 9, 30)).InvoiceCount == 0, "Used session date instead of PaidAt.");
            // Controlled fixture boundaries preserve canonical UTC seven-digit ticks; additional legacy spellings exercise exact filtering.
            InvoiceFixture(db, admin, 10, Utc(At(10, 1, 0).AddTicks(-1)), "STANDARD", 100, 1);
            InvoiceFixture(db, admin, 11, Utc(At(11, 1, 0).AddTicks(-1)), "STANDARD", 200, 2);
            InvoiceFixture(db, admin, 12, Utc(At(11, 1, 0)), "VIP", 300, 3);
            using (var c = db.OpenConnection()) using (var cmd = c.CreateCommand())
            { cmd.CommandText = "PRAGMA ignore_check_constraints=ON;INSERT INTO Customers VALUES(13,'Legacy','0900000013');INSERT INTO RoomSessions(RoomSessionId,CustomerId,RoomId,ActualStartTime,ActualEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) SELECT 13,13,1,ActualStartTime,ActualEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,'Completed' FROM RoomSessions WHERE RoomSessionId=" + first.RoomSessionId + ";INSERT INTO Invoices(RoomSessionId,InvoiceNumber,RoomCharge,ServiceCharge,TotalAmount,PaymentMethod,ProcessedByUserId,ProcessedByNameSnapshot,PaidAt) VALUES(13,'LEGACY','4','5','9','Cash','" + admin.UserId + "','Admin','2026-10-01T00:00:00+07:00');PRAGMA ignore_check_constraints=OFF;"; cmd.ExecuteNonQuery(); }
            var bounded = report.ReadRevenue(admin, from, to);
            Assert(bounded.InvoiceCount == 4 && bounded.TotalAmount == expected + 202m + 9m && bounded.Days.First().TotalAmount == a.TotalAmount + 9 && bounded.Days.Last().Key == "2026-10-31" && bounded.Months.Single().InvoiceCount == 4, "Inclusive start/exclusive next-day tick or legacy offset failed.");
            var multi = report.ReadRevenue(admin, new DateTime(2026, 9, 30), new DateTime(2026, 11, 1)); Assert(multi.Months.Count == 3 && multi.Months[0].Key == "2026-09" && multi.Months[2].Key == "2026-11", "Multi-month chronological grouping failed.");
            Assert(!typeof(RevenueReport).GetProperties().Any(p => p.Name.IndexOf("Customer", StringComparison.Ordinal) >= 0 || p.Name == "PhoneNumber") && !typeof(RevenueReportRow).GetProperties().Any(p => p.GetSetMethod() != null), "Report DTO leaks PII/public mutability.");
            Reject<NotSupportedException>(() => ((System.Collections.Generic.IList<RevenueReportRow>)bounded.Days).Clear());
            var beforeAudit = Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM AuditLog;")); var beforeInvoices = Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM Invoices;"));
            foreach (var table in new[] { "Invoices", "RoomSessions", "Orders", "AuditLog", "Reservations" })
                foreach (var operation in new[] { "INSERT", "UPDATE", "DELETE" }) Sql(db, "CREATE TRIGGER ReportReadOnly_" + table + operation + " BEFORE " + operation + " ON " + table + " BEGIN SELECT RAISE(ABORT,'Report must be read-only'); END;");
            report.ReadRevenue(manager, from, to); Assert(Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM AuditLog;")) == beforeAudit && Convert.ToInt64(Sql(db, "SELECT COUNT(*) FROM Invoices;")) == beforeInvoices, "Read wrote business/audit rows.");
            // Permission and totals share one read snapshot while a current-role permission revoke waits for it.
            var gate = new GateClock { Value = clock.Value }; Task<RevenueReport> reading = null; Task writing = null;
            try
            {
                reading = Task.Run(() => new ReportService(db, gate).ReadRevenue(manager, from, to)); Assert(gate.Entered.Wait(5000), "Report snapshot not established.");
                writing = Task.Run(() => Sql(db, "DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Report.View');"));
                Thread.Sleep(100); Assert(!writing.IsCompleted, "Revoke did not wait for read snapshot."); gate.Release.Set(); Assert(Task.WaitAll(new Task[] { reading, writing }, 15000), "Report/revoke concurrency timeout.");
                Assert(reading.Result.TotalAmount == bounded.TotalAmount, "Read mixed permission/data snapshots.");
            }
            finally { gate.Release.Set(); if (reading != null) reading.GetAwaiter().GetResult(); if (writing != null) writing.GetAwaiter().GetResult(); gate.Entered.Dispose(); gate.Release.Dispose(); }
            clock.Reads = 0; Reject<UnauthorizedAccessException>(() => report.ReadRevenue(manager, from, to)); Assert(clock.Reads == 0, "Revoked report read clock/data.");
            Sql(db, "INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.View';UPDATE AspNetUsers SET SecurityStamp='changed' WHERE Id='manager';"); Reject<UnauthorizedAccessException>(() => report.ReadRevenue(manager, from, to));
            Sql(db, "UPDATE AspNetUsers SET SecurityStamp='manager',IsActive=0 WHERE Id='manager';"); Reject<UnauthorizedAccessException>(() => report.ReadRevenue(manager, from, to));
            Sql(db, "UPDATE AspNetUsers SET IsActive=1 WHERE Id='manager';UPDATE AspNetUserRoles SET RoleId='Staff' WHERE UserId='manager';"); Reject<UnauthorizedAccessException>(() => report.ReadRevenue(manager, from, to));
            auth.Logout(manager); Reject<UnauthorizedAccessException>(() => report.ReadRevenue(manager, from, to));
            // Separate fixture period exercises decimal aggregate overflow without changing immutable paid history.
            Sql(db, "DROP TRIGGER ReportReadOnly_InvoicesINSERT;DROP TRIGGER ReportReadOnly_RoomSessionsINSERT;");
            InvoiceFixture(db, admin, 20, Utc(At(12, 1, 10)), "STANDARD", decimal.MaxValue, 0);
            InvoiceFixture(db, admin, 21, Utc(At(12, 1, 11)), "STANDARD", 1, 0);
            Reject<InvalidOperationException>(() => report.ReadRevenue(admin, new DateTime(2026, 12, 1), new DateTime(2026, 12, 1)));
            Assert(Convert.ToInt64(Sql(db, "PRAGMA user_version;")) == SqliteDatabase.CurrentSchemaVersion, "Report changed schema.");
            Console.WriteLine("PASS RevenueReport: Admin/Manager current independent Report.View, PaidAt Vietnam exact date/ticks, real paid decimal beyond Int64, day/month/snapshot RoomType, no Active/catalog revaluation, read-only one snapshot/clock, revoked/misgranted roles, immutable private DTO, invalid dates and overflow on temporary SQLite.");
        }
        finally
        {
            SQLiteConnection.ClearAllPools(); var target = Path.GetFullPath(directory); var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("MusicBoxRevenueReports_", StringComparison.Ordinal)) throw new Exception("Unsafe revenue report cleanup.");
            Directory.Delete(target, true);
        }
    }
}
