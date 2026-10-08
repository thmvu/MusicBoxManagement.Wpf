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

public static class MusicBoxCustomerChecks
{
    private const string Password = "Customers-Test-2026!";
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected rejection: " + typeof(T).Name); }
    private static object Sql(SqliteDatabase db, string sql)
    { using (var c = db.OpenConnection()) using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; return cmd.ExecuteScalar(); } }
    private static long Count(SqliteDatabase db, string sql) { return Convert.ToInt64(Sql(db, sql)); }
    private static CustomerEdit Input(Customer item) { return new CustomerEdit { FullName = item.FullName, PhoneNumber = item.PhoneNumber }; }
    private static CustomerEdit New(string phone) { return new CustomerEdit { FullName = " Nguyễn An ", PhoneNumber = phone }; }
    public static void Run()
    {
        foreach (var phone in new[] { "0912345678", " +84 912.345-678 ", "84\t912 345 678", "0 912.345-678" })
            Assert(PhoneNumberNormalizer.Normalize(phone) == "0912345678", "Equivalent phones differ.");
        foreach (var phone in new[] { null, "", "1234567890", "091234567", "+840912345678", "849123456789", "(091)2345678", "0912/345678", "０９１２３４５６７８", "+850912345678" })
            Reject<ArgumentException>(() => PhoneNumberNormalizer.Normalize(phone));
        // The plan deliberately does not validate carrier prefixes.
        Assert(PhoneNumberNormalizer.Normalize("0000000000") == "0000000000", "Introduced a carrier/prefix rule absent from the plan.");
        var file = Path.Combine(Path.GetTempPath(), "MusicBoxCustomers_" + Guid.NewGuid().ToString("N") + ".db");
        var db = new SqliteDatabase(file);
        try
        {
            var auth = new AuthenticationService(db); var catalog = new CustomerService(db);
            var admin = auth.SetupAdminAsync("admin", "Quản trị thử nghiệm", Password).GetAwaiter().GetResult();
            Sql(db, @"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'staff','staff','STAFF',PasswordHash,'staff','Staff',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('staff','Staff');
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'manager','manager','MANAGER',PasswordHash,'manager','Manager',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('manager','Manager');");
            var staff = auth.LoginAsync("staff", Password).GetAwaiter().GetResult();
            var manager = auth.LoginAsync("manager", Password).GetAwaiter().GetResult();
            Assert(catalog.Search(staff).CanCreate && catalog.Search(manager).CanEdit, "Default staff/manager permissions missing.");
            Reject<UnauthorizedAccessException>(() => catalog.Search(null));
            Reject<UnauthorizedAccessException>(() => catalog.Save(null, null, New("0912345678")));
            var first = catalog.Save(staff, null, New("+84 912.345-678"));
            Assert(first.FullName == "Nguyễn An" && first.PhoneNumber == "0912345678", "Normalized save incorrect.");
            Reject<ArgumentException>(() => catalog.Save(admin, null, new CustomerEdit { FullName = "Tên khác", PhoneNumber = "84 912 345 678" }));
            Assert(catalog.Search(admin, "NGUYỄN", "84 912 345 678").Items.Single().FullName == "Nguyễn An", "Search/name preserved incorrectly.");
            Assert(catalog.Search(admin, "Không có").Items.Count == 0, "Name filter ignored.");
            Reject<ArgumentException>(() => catalog.Search(admin, null, "abc"));
            foreach (var name in new[] { null, " ", new string('x', 101) })
                Reject<ArgumentException>(() => catalog.Save(admin, null, new CustomerEdit { FullName = name, PhoneNumber = "0987654321" }));
            Reject<ArgumentException>(() => catalog.Save(admin, null, null));
            Reject<InvalidOperationException>(() => catalog.Save(admin, new Customer { CustomerId = 999 }, New("0987654321")));
            var second = catalog.Save(manager, null, New("0987654321"));
            var duplicate = Input(first); duplicate.PhoneNumber = second.PhoneNumber;
            Reject<ArgumentException>(() => catalog.Save(admin, first, duplicate));
            // Existing usage keeps its CustomerId when the customer's name/phone changes.
            Sql(db, "INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('TEST','Test',1,'Content/uploads/rooms/test.png',1,'2026-10-05');");
            Sql(db, "INSERT INTO Reservations(CustomerId,RoomId,StartTime,EndTime,Status,CreatedAt) VALUES(" + first.CustomerId + ",1,'2026-10-05T10:00:00.0000000+00:00','2026-10-05T11:00:00.0000000+00:00','Completed','2026-10-05');");
            Sql(db, "INSERT INTO RoomSessions(CustomerId,RoomId,ActualStartTime,ActualEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES(" + first.CustomerId + ",1,'2026-10-05T10:00:00.0000000+00:00','2026-10-05T11:00:00.0000000+00:00',100000,'TEST','STANDARD','Standard','Completed');");
            var edit = Input(first); edit.FullName = " Nguyễn Bình "; edit.PhoneNumber = "+84 901 234 567";
            var saved = catalog.Save(staff, first, edit);
            Assert(saved.CustomerId == first.CustomerId && saved.PhoneNumber == "0901234567" && catalog.Search(admin, null, first.PhoneNumber).Items.Count == 0 &&
                catalog.Search(admin, null, saved.PhoneNumber).Items.Single().CustomerId == first.CustomerId &&
                Count(db, "SELECT CustomerId FROM Reservations LIMIT 1;") == first.CustomerId && Count(db, "SELECT CustomerId FROM RoomSessions LIMIT 1;") == first.CustomerId,
                "Phone change lost lookup or history links.");
            Reject<InvalidOperationException>(() => catalog.Save(admin, first, edit));
            var audit = Count(db, "SELECT COUNT(*) FROM AuditLog;");
            catalog.Save(admin, saved, Input(saved)); Assert(Count(db, "SELECT COUNT(*) FROM AuditLog;") == audit, "No-op wrote audit.");
            Sql(db, "CREATE TRIGGER FailCustomerAudit BEFORE INSERT ON AuditLog WHEN NEW.EntityName='Customer' BEGIN SELECT RAISE(ABORT,'test failure'); END;");
            Reject<SQLiteException>(() => catalog.Save(admin, null, New("0900000000")));
            edit = Input(saved); edit.FullName = "Lỗi audit"; Reject<SQLiteException>(() => catalog.Save(admin, saved, edit));
            Assert(catalog.Search(admin, null, saved.PhoneNumber).Items.Single().FullName == saved.FullName && Count(db, "SELECT COUNT(*) FROM AuditLog;") == audit && Count(db, "SELECT COUNT(*) FROM Customers;") == 2, "Audit rollback failed.");
            Sql(db, "DROP TRIGGER FailCustomerAudit;");
            using (var gate = new ManualResetEventSlim(false))
            {
                Func<string, Task<bool>> attempt = name => Task.Run(() => { gate.Wait(); var change = Input(saved); change.FullName = name;
                    try { catalog.Save(admin, saved, change); return true; } catch (InvalidOperationException) { return false; } });
                var a = attempt("Tên A"); var b = attempt("Tên B"); gate.Set(); Task.WaitAll(a, b);
                Assert(a.Result != b.Result && Count(db, "SELECT COUNT(*) FROM AuditLog;") == audit + 1, "Concurrent stale edits both committed.");
            }
            using (var gate = new ManualResetEventSlim(false))
            {
                Func<string, Task<bool>> attempt = phone => Task.Run(() => { gate.Wait(); try { catalog.Save(admin, null, New(phone)); return true; } catch (ArgumentException) { return false; } });
                var a = attempt("0900000000"); var b = attempt("+84 900 000 000"); gate.Set(); Task.WaitAll(a, b);
                Assert(a.Result != b.Result && Count(db, "SELECT COUNT(*) FROM Customers;") == 3, "Concurrent equivalent phones duplicated.");
            }
            var vm = new CustomersViewModel(catalog, staff); vm.SearchAsync().GetAwaiter().GetResult(); vm.Edit(second); vm.FullName = "Không được lưu";
            Sql(db, "DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Customer.Edit');");
            Reject<UnauthorizedAccessException>(() => catalog.Save(staff, second, Input(second)));
            Assert(!vm.SaveAsync().GetAwaiter().GetResult() && !vm.CanSave && vm.Items.Count == 0, "Open editor bypassed revocation.");
            vm.SearchAsync().GetAwaiter().GetResult(); vm.Edit(second);
            Assert(!vm.CanSave && vm.CanCreate && vm.Items.Count == 3, "View/create/edit UI permissions coupled incorrectly.");
            Sql(db, "DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Customer.Create');");
            Reject<UnauthorizedAccessException>(() => catalog.Save(staff, null, New("0909999999")));
            vm.SearchAsync().GetAwaiter().GetResult(); Assert(!vm.CanCreate && vm.CanSelect, "View-only UI opened creation.");
            Sql(db, "DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Customer.View'); INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Customer.Create';");
            Reject<UnauthorizedAccessException>(() => catalog.Search(staff));
            Reject<UnauthorizedAccessException>(() => catalog.Save(staff, null, New("0909999999")));
            auth.Logout(manager); Reject<UnauthorizedAccessException>(() => catalog.Search(manager));
            Assert(new CustomerService(new SqliteDatabase(file)).Search(admin).Items.Count == 3 && Count(db, "PRAGMA user_version;") == SqliteDatabase.CurrentSchemaVersion &&
                Count(db, "SELECT COUNT(*) FROM AuditLog WHERE EntityName='Customer' AND ActorType<>'Staff';") == 0, "Persistence/schema/audit actor incorrect.");
            Console.WriteLine("PASS Customers: phone normalization, search, separate live permissions, unique/concurrent phones, stale edits, history IDs, audit rollback and persistence on temporary SQLite.");
        }
        finally { SQLiteConnection.ClearAllPools(); if (File.Exists(file)) File.Delete(file); }
    }
}
