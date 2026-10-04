using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

public static class MusicBoxServiceChecks
{
    private const string Password = "Services-Test-2026!";
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected rejection: " + typeof(T).Name); }
    private static object Sql(SqliteDatabase db, string sql)
    { using (var c = db.OpenConnection()) using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; return cmd.ExecuteScalar(); } }
    private static long Count(SqliteDatabase db, string sql) { return Convert.ToInt64(Sql(db, sql)); }
    private static ServiceEdit Input(ServiceItem item)
    { return new ServiceEdit { Name = item.Name, Category = item.Category, Price = item.Price, Description = item.Description, IsActive = item.IsActive }; }
    private static ServiceEdit New() { return new ServiceEdit { Name = " Trà chanh ", Category = "Đồ uống", Price = 15000, Description = " Mát lạnh ", IsActive = true }; }

    public static void Run()
    {
        var file = Path.Combine(Path.GetTempPath(), "MusicBoxServices_" + Guid.NewGuid().ToString("N") + ".db");
        var db = new SqliteDatabase(file);
        try
        {
            var auth = new AuthenticationService(db);
            var admin = auth.SetupAdminAsync("admin", "Quản trị thử nghiệm", Password).GetAwaiter().GetResult();
            var catalog = new ServiceCatalogService(db);
            Assert(Count(db, "PRAGMA user_version;") == 6 && catalog.ListForManagement(admin).Count == 0, "Schema/default catalog incorrect.");
            // Reconstruct v5; a conflicting table must leave the migration version/data intact.
            Sql(db, "UPDATE RoomTypes SET PricePerHour=123000 WHERE RoomTypeId=1; DROP TABLE Services; PRAGMA user_version=5; CREATE TABLE Services(Fixture TEXT);");
            Reject<SQLiteException>(() => db.Initialize());
            Assert(Count(db, "PRAGMA user_version;") == 5 && Count(db, "SELECT PricePerHour FROM RoomTypes WHERE RoomTypeId=1;") == 123000,
                "Failed migration changed existing data/version.");
            Sql(db, "DROP TABLE Services;"); db.Initialize();
            Assert(Count(db, "PRAGMA user_version;") == 6 && Count(db, "SELECT COUNT(*) FROM AspNetUsers;") == 1 &&
                Count(db, "SELECT COUNT(*) FROM AuditLog;") > 0 && Count(db, "SELECT PricePerHour FROM RoomTypes WHERE RoomTypeId=1;") == 123000,
                "v5 upgrade lost accounts, audit or room-type data.");
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
            foreach (var category in ServiceCatalogService.Categories)
            { var edit = Input(first); edit.Category = category; edit.IsActive = false; edit.Price = 25000; edit.Description = " ";
              catalog.Save(admin, first, edit); first = catalog.ListForManagement(admin).Single();
              Assert(first.Category == category && !first.IsActive && first.Description == null && first.ServiceId == id, "Edit/category/stop-selling changed identity."); }
            var audit = Count(db, "SELECT COUNT(*) FROM AuditLog WHERE EntityName='Service';");
            catalog.Save(admin, first, Input(first));
            Assert(Count(db, "SELECT COUNT(*) FROM AuditLog WHERE EntityName='Service';") == audit, "No-op wrote audit.");
            Reject<InvalidOperationException>(() => catalog.Save(admin, new ServiceItem { ServiceId = 999 }, New()));
            Action<Action<ServiceEdit>> invalid = mutate => { var edit = New(); mutate(edit); Reject<ArgumentException>(() => catalog.Save(admin, null, edit)); };
            invalid(x => x.Name = " "); invalid(x => x.Name = new string('x', 101)); invalid(x => x.Category = "Other");
            invalid(x => x.Price = 0); invalid(x => x.Price = -1); invalid(x => x.Description = new string('x', 2001));
            Reject<ArgumentException>(() => catalog.Save(admin, null, null));
            foreach (var price in new[] { "0", "-1", "1.5", "1,000", "abc", "9223372036854775808" })
            { var vm = new ServicesViewModel(catalog, admin); vm.RefreshAsync().GetAwaiter().GetResult(); vm.Name = "Không lưu"; vm.PriceText = price;
              Assert(!vm.SaveAsync().GetAwaiter().GetResult(), "ViewModel accepted invalid VND input."); }
            foreach (var values in new[] { "'X','Đồ uống',1.5,1", "'X','Đồ uống',0,1", "'X','Other',1,1", "'X','Đồ ăn',1,2" })
                Reject<SQLiteException>(() => Sql(db, "INSERT INTO Services(Name,Category,Price,IsActive) VALUES(" + values + ");"));
            Assert(catalog.ListForManagement(admin).Count == 1, "Rejected operations inserted data.");
            var opened = catalog.ListForManagement(manager).Single();
            var revokedVm = new ServicesViewModel(catalog, manager); revokedVm.RefreshAsync().GetAwaiter().GetResult(); revokedVm.Edit(opened); revokedVm.PriceText = "35000";
            Sql(db, "DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Service.Manage');");
            Reject<UnauthorizedAccessException>(() => catalog.Save(manager, opened, New()));
            Assert(!revokedVm.SaveAsync().GetAwaiter().GetResult() && !revokedVm.CanEdit && revokedVm.Items.Count == 0, "Open form bypassed revoked permission.");
            auth.Logout(manager); Reject<UnauthorizedAccessException>(() => catalog.ListForManagement(manager));
            Sql(db, "INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Service.Manage';");
            first = catalog.ListForManagement(staff).Single(); var activate = Input(first); activate.IsActive = true;
            catalog.Save(staff, first, activate);
            first = catalog.ListForManagement(admin).Single();
            Assert(first.IsActive && Count(db, "SELECT COUNT(*) FROM AuditLog WHERE Action='Service.Update' AND ActorType='Staff' AND UserId='staff';") == 1,
                "Grant/un-stop-selling/staff audit failed.");
            audit = Count(db, "SELECT COUNT(*) FROM AuditLog;");
            Sql(db, "CREATE TRIGGER FailServiceAudit BEFORE INSERT ON AuditLog WHEN NEW.EntityName='Service' BEGIN SELECT RAISE(ABORT,'test failure'); END;");
            Reject<SQLiteException>(() => catalog.Save(admin, null, New()));
            var failing = Input(first); failing.Price = 50000; Reject<SQLiteException>(() => catalog.Save(admin, first, failing));
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
            Assert(new ServiceCatalogService(new SqliteDatabase(file)).ListForManagement(admin).Count == 1, "Reopen lost catalog.");
            Console.WriteLine("PASS: schema v6/migration, catalog, validation, live permissions, stale/concurrent edits and atomic audit on temporary SQLite.");
        }
        finally { SQLiteConnection.ClearAllPools(); if (File.Exists(file)) File.Delete(file); }
    }
}

