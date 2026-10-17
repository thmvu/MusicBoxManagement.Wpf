using System;
using MySqlConnector;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;


public static class MusicBoxMySqlServiceChecks
{
    private const string Password = "Services-Test-2026!";
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected rejection: " + typeof(T).Name); }
    private static object Sql(MySqlDatabase db, string sql)
    { using (var c = db.OpenConnection()) using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; return cmd.ExecuteScalar(); } }
    private static long Count(MySqlDatabase db, string sql) { return Convert.ToInt64(Sql(db, sql)); }
    private static ServiceEdit Input(ServiceItem item)
    { return new ServiceEdit { Name = item.Name, Category = item.Category, Price = item.Price, Description = item.Description, IsActive = item.IsActive }; }
    private static ServiceEdit New() { return new ServiceEdit { Name = " Trà chanh ", Category = "Đồ uống", Price = 15000, Description = " Mát lạnh ", IsActive = true }; }

    public static void Run()
    {
        var databaseName = "musicbox_test_" + Guid.NewGuid().ToString("N");
        var db = new MySqlDatabase(databaseName);
        try
        {
            var auth = new MySqlAuthenticationService(db);
            var admin = auth.SetupAdminAsync("admin", "Quản trị thử nghiệm", Password).GetAwaiter().GetResult();
            var catalog = new MySqlServiceCatalogService(db);
            Assert(Count(db, "SELECT Version FROM _MusicBoxSchemaVersion WHERE Id=1;") == MySqlDatabase.CurrentSchemaVersion && catalog.ListForManagement(admin).Count == 0, "Schema/default catalog incorrect.");
            Sql(db, @"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'manager','manager','MANAGER',PasswordHash,'manager','Manager',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('manager','Manager');
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'staff','staff','STAFF',PasswordHash,'staff','Staff',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('staff','Staff');");
            var manager = auth.LoginAsync("manager", Password).GetAwaiter().GetResult();
            var staff = auth.LoginAsync("staff", Password).GetAwaiter().GetResult();
            Reject<UnauthorizedAccessException>(() => catalog.ListForManagement(null));
            Reject<UnauthorizedAccessException>(() => catalog.ListForManagement(staff));
            Reject<UnauthorizedAccessException>(() => catalog.Save(null, null, New()));
            Reject<UnauthorizedAccessException>(() => catalog.Save(staff, null, New()));
            var id = catalog.Save(manager, null, New());
            var first = catalog.ListForManagement(admin).Single();
            Assert(first.ServiceId == id && first.Name == "Trà chanh" && first.Description == "Mát lạnh" && first.IsActive && first.Price == 15000,
                "Creation/normalization failed.");
            // Existing order rows are a fixture only; the MySQL Order writer is a later migration.
            Sql(db, @"INSERT INTO Rooms(RoomCode,RoomTypeId,Name,ImageUrl,IsActive,CreatedAt)
VALUES('FIXTURE',1,'Phòng thử','fixture.png',1,'2026-10-18T02:00:00.0000000+00:00');
INSERT INTO Customers(FullName,PhoneNumber) VALUES('Khách thử','0901234567');
INSERT INTO RoomSessions(CustomerId,RoomId,ActualStartTime,ActualEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status)
VALUES(1,1,'2026-10-18T02:00:00.0000000+00:00','2026-10-18T03:00:00.0000000+00:00',120000,'FIXTURE','STANDARD','Standard','Completed');
INSERT INTO Orders(RoomSessionId,Status,CreatedAt) VALUES(1,'Completed','2026-10-18T02:15:00.0000000+00:00');
INSERT INTO OrderItems(OrderId,ServiceId,ServiceNameSnapshot,Quantity,UnitPrice)
SELECT 1,ServiceId,Name,2,Price FROM Services;");
            foreach (var category in MySqlServiceCatalogService.Categories)
            { var edit = Input(first); edit.Category = category; edit.IsActive = false; edit.Price = 25000; edit.Description = " ";
              catalog.Save(admin, first, edit); first = catalog.ListForManagement(admin).Single();
              Assert(first.Category == category && !first.IsActive && first.Description == null && first.ServiceId == id, "Edit/category/stop-selling changed identity."); }
            Assert((string)Sql(db, "SELECT ServiceNameSnapshot FROM OrderItems;") == "Trà chanh" && Count(db, "SELECT UnitPrice FROM OrderItems;") == 15000,
                "Changing/stopping the catalog rewrote historical item snapshots.");
            var audit = Count(db, "SELECT COUNT(*) FROM AuditLog WHERE EntityName='Service';");
            catalog.Save(admin, first, Input(first));
            Assert(Count(db, "SELECT COUNT(*) FROM AuditLog WHERE EntityName='Service';") == audit, "No-op wrote audit.");
            Reject<InvalidOperationException>(() => catalog.Save(admin, new ServiceItem { ServiceId = 999 }, New()));
            Action<Action<ServiceEdit>> invalid = mutate => { var edit = New(); mutate(edit); Reject<ArgumentException>(() => catalog.Save(admin, null, edit)); };
            invalid(x => x.Name = " "); invalid(x => x.Name = new string('x', 101)); invalid(x => x.Category = "Other");
            invalid(x => x.Price = 0); invalid(x => x.Price = -1); invalid(x => x.Description = new string('x', 2001));
            Reject<ArgumentException>(() => catalog.Save(admin, null, null));
            foreach (var values in new[] { "'X','Đồ uống',0,1", "'X','Other',1,1", "'X','Đồ ăn',1,2" })
                Reject<MySqlException>(() => Sql(db, "INSERT INTO Services(Name,Category,Price,IsActive) VALUES(" + values + ");"));
            Assert(catalog.ListForManagement(admin).Count == 1, "Rejected operations inserted data.");
            var large = Input(first); large.Price = long.MaxValue;
            catalog.Save(admin, first, large);
            var exact = catalog.ListForManagement(admin).Single();
            Assert(exact.Price == long.MaxValue, "BIGINT price lost precision.");
            catalog.Save(admin, exact, Input(first));
            // A stopped item remains in the management catalog; no delete or history rewrite.
            Assert(catalog.ListForManagement(admin).Single().ServiceId == id, "Stop-selling removed the service.");
            var opened = catalog.ListForManagement(manager).Single();
            Sql(db, "DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Service.Manage');");
            Reject<UnauthorizedAccessException>(() => catalog.Save(manager, opened, New()));
            Reject<UnauthorizedAccessException>(() => catalog.ListForManagement(manager));
            auth.Logout(manager); Reject<UnauthorizedAccessException>(() => catalog.ListForManagement(manager));
            Sql(db, "INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Service.Manage';");
            first = catalog.ListForManagement(staff).Single(); var activate = Input(first); activate.IsActive = true;
            catalog.Save(staff, first, activate);
            first = catalog.ListForManagement(admin).Single();
            Assert(first.IsActive && Count(db, "SELECT COUNT(*) FROM AuditLog WHERE Action='Service.Update' AND ActorType='Staff' AND UserId='staff';") == 1,
                "Grant/un-stop-selling/staff audit failed.");
            audit = Count(db, "SELECT COUNT(*) FROM AuditLog;");
            Sql(db, "CREATE TRIGGER FailServiceAudit BEFORE INSERT ON AuditLog FOR EACH ROW BEGIN IF NEW.EntityName='Service' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='test failure'; END IF; END");
            Reject<MySqlException>(() => catalog.Save(admin, null, New()));
            var failing = Input(first); failing.Price = 50000; Reject<MySqlException>(() => catalog.Save(admin, first, failing));
            Assert(catalog.ListForManagement(admin).Single().Price == first.Price && Count(db, "SELECT COUNT(*) FROM AuditLog;") == audit, "Audit failure did not roll back data.");
            Sql(db, "DROP TRIGGER FailServiceAudit;");
            using (var gate = new ManualResetEventSlim(false))
            {
                Func<long, Task<bool>> attempt = price => Task.Run(() => { gate.Wait(); var edit = Input(first); edit.Price = price;
                    try { catalog.Save(admin, first, edit); return true; } catch (InvalidOperationException) { return false; } });
                var a = attempt(40000); var b = attempt(45000); gate.Set(); Task.WaitAll(a, b);
                Assert(a.Result != b.Result && Count(db, "SELECT COUNT(*) FROM AuditLog;") == audit + 1, "Concurrent stale edits both committed.");
            }
            Reject<InvalidOperationException>(() => catalog.Save(admin, first, Input(first)));
            Assert(new MySqlServiceCatalogService(new MySqlDatabase(databaseName)).ListForManagement(admin).Count == 1, "Reopen lost catalog.");
            Console.WriteLine("PASS MySQL Services: protected catalog, validation, stop/resume selling, live permissions, exact BIGINT, stale/concurrent edits and atomic audit on GUID database.");
        }
        finally
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(databaseName, @"\Amusicbox_test_[0-9a-f]{32}\z")) throw new Exception("Unsafe MySQL Services cleanup.");
            Sql(new MySqlDatabase("defaultdb"), "DROP DATABASE IF EXISTS `" + databaseName + "`");
        }
    }
}
