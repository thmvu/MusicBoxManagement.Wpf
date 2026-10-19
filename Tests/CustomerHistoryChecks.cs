using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxCustomerHistoryChecks
{
    private const string Password = "History-Test-2026!";
    private sealed class Clock : IClock
    {
        public int Reads;
        public DateTimeOffset UtcNow { get { Reads++; return DateTimeOffset.Parse("2026-10-19T08:00:00+00:00"); } }
    }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static object Sql(SqliteDatabase db, string sql)
    { using (var c = db.OpenConnection()) using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; return cmd.ExecuteScalar(); } }
    public static void Run()
    {
        var file = Path.Combine(Path.GetTempPath(), "MusicBoxHistory_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var db = new SqliteDatabase(file); var auth = new AuthenticationService(db);
            var admin = auth.SetupAdminAsync("admin", "Admin", Password).GetAwaiter().GetResult();
            Sql(db, @"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'staff','staff','STAFF',PasswordHash,'staff','Staff',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('staff','Staff');
DELETE FROM RolePermission WHERE RoleId='Staff';
INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Customer.View';
INSERT INTO Customers VALUES(1,'Khách cũ','0912345678');
INSERT INTO Customers VALUES(2,'Khách khác','0987654321');
INSERT INTO Customers VALUES(3,'Chưa sử dụng','0900000000');
INSERT INTO Rooms(RoomId,RoomCode,Name,RoomTypeId,ImageUrl,IsActive,InactiveReason,CreatedAt)
VALUES(1,'CURRENT','Phòng khóa',1,'Content/uploads/rooms/test.png',0,'Bảo trì','2026-10-01');
INSERT INTO Reservations(ReservationId,CustomerId,RoomId,StartTime,EndTime,Status,CreatedAt) VALUES
(1,1,1,'2026-10-18T03:00:00.0000000+00:00','2026-10-18T04:00:00.0000000+00:00','Completed','2026-10-01'),
(2,1,1,'2026-10-20T03:00:00.0000000+00:00','2026-10-20T04:00:00.0000000+00:00','Confirmed','2026-10-01'),
(3,2,1,'2026-10-20T04:00:00.0000000+00:00','2026-10-20T05:00:00.0000000+00:00','Cancelled','2026-10-01'),
(4,1,1,'2026-10-17T03:00:00.0000000+00:00','2026-10-17T04:00:00.0000000+00:00','Confirmed','2026-10-01');
INSERT INTO RoomSessions(RoomSessionId,CustomerId,RoomId,ReservationId,ActualStartTime,ExpectedEndTime,ActualEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES
(1,1,1,1,'2026-10-18T03:00:00.0000000+00:00','2026-10-18T04:00:00.0000000+00:00','2026-10-18T04:00:00.0000000+00:00',60000,'OLD','STANDARD','Tên cũ','Completed'),
(2,1,1,NULL,'2026-10-19T03:00:00.1234567+00:00',NULL,NULL,90000,'WALK','VIP','VIP cũ','Active'),
(3,2,1,NULL,'2026-10-16T03:00:00.0000000+00:00',NULL,'2026-10-16T04:00:00.0000000+00:00',60000,'OTHER','STANDARD','Standard','Completed');");
            // Read-only historical fixture; checkout itself is covered by Verify-Checkout.
            using (var c = db.OpenConnection()) using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO Invoices(RoomSessionId,InvoiceNumber,RoomCharge,ServiceCharge,TotalAmount,PaymentMethod,ProcessedByUserId,ProcessedByNameSnapshot,PaidAt)
VALUES(1,'HD-OLD','60000','0','60000','Cash',@actor,'Admin cũ','2026-10-18T04:00:00.0000000+00:00');";
                cmd.Parameters.AddWithValue("@actor", admin.UserId); cmd.ExecuteNonQuery();
            }
            var staff = auth.LoginAsync("staff", Password).GetAwaiter().GetResult();
            var clock = new Clock(); var history = new CustomerHistoryService(db, clock);
            var audit = Sql(db, "SELECT COUNT(*) FROM AuditLog;");
            var result = history.Read(staff, 1);
            Assert(clock.Reads == 1 && result.CompletedSessionCount == 1 && result.Sessions.Count == 2 && result.Reservations.Count == 3, "Count/single clock/customer isolation failed.");
            Assert(result.LastUsedAt == result.Sessions[0].ActualStartTime && result.Sessions[0].RoomCodeSnapshot == "WALK" && result.Sessions[0].ExpectedEndTime == null, "Actual/walk-in/snapshot lost.");
            Assert(result.Reservations[0].ReservationId == 2 && result.Reservations.Last().Status == "Confirmed", "History order or stored grace state changed.");
            Assert(!result.CanViewInvoices && result.Invoices.Count == 0, "Customer.View leaked invoices.");
            var full = history.Read(admin, 1);
            Assert(full.CanViewInvoices && full.Invoices.Single().InvoiceNumber == "HD-OLD" && full.Invoices[0].RoomCode == "OLD" && full.Invoices[0].TotalAmount == 60000m, "Invoice snapshot/amount missing.");
            Sql(db, "UPDATE Customers SET FullName='Tên mới',PhoneNumber='0901234567' WHERE CustomerId=1; UPDATE Customers SET PhoneNumber='0912345678' WHERE CustomerId=2;");
            var renamed = history.Read(admin, 1); var reused = history.Read(admin, 2);
            Assert(renamed.Customer.PhoneNumber == "0901234567" && renamed.Customer.FullName == "Tên mới" && renamed.Sessions.Count == 2 && renamed.Invoices.Count == 1 && reused.Sessions.Count == 1 && reused.Invoices.Count == 0, "Phone reuse moved history between customers.");
            var empty = history.Read(staff, 3);
            Assert(empty.LastUsedAt == null && empty.CompletedSessionCount == 0 && empty.Sessions.Count == 0 && empty.Reservations.Count == 0, "Empty customer fabricated history.");
            Reject<ArgumentException>(() => history.Read(admin, 0));
            Reject<InvalidOperationException>(() => history.Read(admin, 999));
            Reject<UnauthorizedAccessException>(() => history.Read(null, 1));
            Sql(db, "INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Invoice.View';");
            Assert(history.Read(staff, 1).Invoices.Count == 1, "Current invoice grant ignored.");
            Sql(db, "DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Invoice.View');");
            Assert(history.Read(staff, 1).Invoices.Count == 0, "Revoked invoice access remained.");
            Sql(db, "DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Customer.View');");
            Reject<UnauthorizedAccessException>(() => history.Read(staff, 1));
            Sql(db, "INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Customer.View'; UPDATE AspNetUsers SET SecurityStamp='changed' WHERE Id='staff';");
            Reject<UnauthorizedAccessException>(() => history.Read(staff, 1));
            Assert(Equals(audit, Sql(db, "SELECT COUNT(*) FROM AuditLog;")) && Convert.ToInt32(Sql(db, "PRAGMA user_version;")) == SqliteDatabase.CurrentSchemaVersion, "History wrote audit/schema.");
            Assert((string)Sql(db, "SELECT Status FROM Reservations WHERE ReservationId=4;") == "Confirmed", "Read ran NoShow maintenance.");
            Console.WriteLine("PASS Customer history: ID links/phone reuse, snapshots, completed count/last actual start, empty data, locked room, current independent permissions, stamp, one clock, no writes.");
        }
        finally { SQLiteConnection.ClearAllPools(); if (File.Exists(file)) File.Delete(file); }
    }
}
