using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.Identity;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxAuthenticationChecks
{
    // Synthetic credentials only, used with randomly named test databases.
    private const string Password = "MusicBox-Test-2026!";

    private static void Assert(bool value, string message)
    { if (!value) throw new Exception(message); }

    private static void Reject<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception(message);
    }

    private static string NewFile()
    { return Path.Combine(Path.GetTempPath(), "MusicBoxAuth_" + Guid.NewGuid().ToString("N") + ".db"); }

    private static object Sql(SqliteDatabase database, string sql, params object[] values)
    {
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = sql;
            for (var i = 0; i < values.Length; i += 2) command.Parameters.AddWithValue((string)values[i], values[i + 1]);
            return command.ExecuteScalar();
        }
    }

    private static long Count(SqliteDatabase database, string sql)
    { return Convert.ToInt64(Sql(database, sql)); }

    private static void AddPermission(SqliteDatabase database, string role, string code)
    {
        Sql(database, @"INSERT OR IGNORE INTO RolePermission(RoleId, PermissionId)
SELECT @role, PermissionId FROM Permission WHERE Code = @code;", "@role", role, "@code", code);
    }

    private static void FixtureUser(SqliteDatabase database, string id, string role, string hash)
    {
        Sql(database, @"INSERT INTO AspNetUsers(Id, UserName, NormalizedUserName, PasswordHash, SecurityStamp, FullName, IsActive)
VALUES (@id, @id, upper(@id), @hash, @id, @id, 1);
INSERT INTO AspNetUserRoles(UserId, RoleId) VALUES (@id, @role);", "@id", id, "@role", role, "@hash", hash);
    }

    public static void Run()
    {
        MigrationAndAuthorization();
        BootstrapRollback();
        ConcurrentBootstrap();
        Console.WriteLine("PASS authentication: v1 migration, seed preservation, bootstrap, password/login/logout, live RBAC, session revocation, rollback and concurrent bootstrap.");
    }

    private static void MigrationAndAuthorization()
    {
        var file = NewFile();
        var database = new SqliteDatabase(file);
        try
        {
            // Reproduce the actual v1 table independently of the v2 migration.
            Sql(database, @"CREATE TABLE RoomTypes (
RoomTypeId INTEGER PRIMARY KEY,
Code TEXT NOT NULL UNIQUE CHECK(Code IN ('STANDARD','VIP')),
Name TEXT NOT NULL CHECK(length(trim(Name)) BETWEEN 1 AND 100),
Capacity INTEGER NOT NULL CHECK(typeof(Capacity)='integer' AND Capacity>0),
PricePerHour INTEGER NOT NULL CHECK(typeof(PricePerHour)='integer' AND PricePerHour>0),
Amenities TEXT NOT NULL CHECK(length(trim(Amenities)) BETWEEN 1 AND 1000),
Description TEXT NULL CHECK(Description IS NULL OR length(Description)<=2000));
INSERT INTO RoomTypes VALUES (1,'STANDARD','Standard đã chỉnh',4,135000,'Điều hòa',NULL);
INSERT INTO RoomTypes VALUES (2,'VIP','VIP',6,200000,'TV lớn',NULL);
PRAGMA user_version=1;");
            // Force failure after the migration has already created its first
            // table; schema and version must both return to their v1 state.
            Sql(database, "CREATE TABLE AspNetRoles(Id TEXT);");
            Reject<SQLiteException>(() => database.Initialize(), "Injected migration conflict was ignored.");
            Assert(Count(database, "PRAGMA user_version;") == 1 &&
                Count(database, "SELECT COUNT(*) FROM sqlite_master WHERE name='AspNetUsers';") == 0,
                "Failed migration left a partial v2 schema/version.");
            Sql(database, "DROP TABLE AspNetRoles;");
            var auth = new AuthenticationService(database);
            var permissions = new PermissionService(database);
            Assert(auth.NeedsSetup(), "A migrated database unexpectedly has a default account.");
            Assert(Count(database, "PRAGMA user_version;") == 4, "Schema v4 migration failed.");
            Assert((string)Sql(database, "SELECT Name FROM RoomTypes WHERE RoomTypeId=1;") == "Standard đã chỉnh", "Migration overwrote v1 data.");
            Assert(Count(database, "SELECT PricePerHour FROM RoomTypes WHERE RoomTypeId=1;") == 135000, "Migration changed the existing price.");
            Assert(Count(database, "SELECT COUNT(*) FROM AspNetRoles;") == 3, "Expected three Identity roles.");
            Assert(Count(database, "SELECT COUNT(*) FROM Permission;") == 27, "Missing permissions.");
            Assert(Count(database, "SELECT COUNT(*) FROM RolePermission WHERE RoleId='Staff';") == 19, "Incorrect Staff seed.");
            Assert(Count(database, "SELECT COUNT(*) FROM RolePermission WHERE RoleId='Manager';") == 24, "Incorrect Manager seed.");
            Reject<UnauthorizedAccessException>(() => permissions.GetStaffAccess(null), "Guest entered staff service.");
            Assert(!permissions.HasPermission(null, "User.Manage"), "Guest has an internal permission.");
            Reject<ArgumentException>(() => auth.SetupAdminAsync("bad name", "Admin", Password).GetAwaiter().GetResult(), "Invalid username accepted.");
            Reject<ArgumentException>(() => auth.SetupAdminAsync("admin", "Admin", "short").GetAwaiter().GetResult(), "Short password accepted.");

            var admin = auth.SetupAdminAsync("  Admin  ", "Quản trị viên", Password).GetAwaiter().GetResult();
            Assert(!auth.NeedsSetup(), "Setup remains available after bootstrap.");
            Assert(permissions.GetStaffAccess(admin).Permissions.Count == 27, "Admin lost fixed permissions.");
            Assert(!permissions.HasPermission(admin, "MadeUp.Permission"), "Admin can use an undefined permission.");
            var hash = (string)Sql(database, "SELECT PasswordHash FROM AspNetUsers WHERE Id=@id;", "@id", admin.UserId);
            Assert(hash.StartsWith("PBKDF2-SHA256$1$600000$") && !hash.Contains(Password), "Password is not stored in the specified hash format.");
            var hasher = new Pbkdf2PasswordHasher();
            var otherHash = hasher.HashPassword(Password);
            Assert(hash != otherHash, "Random password salts were reused.");
            Assert(hasher.VerifyHashedPassword("PBKDF2-SHA256$1$600000$bad$bad", Password) == PasswordVerificationResult.Failed, "Malformed hash accepted.");
            Assert(hasher.VerifyHashedPassword(hash.Replace("600000", "1"), Password) == PasswordVerificationResult.Failed, "Weak work factor accepted.");
            Assert(Count(database, "SELECT COUNT(*) FROM AuditLog WHERE Action='User.Bootstrap';") == 1, "Bootstrap audit was not saved.");
            Reject<InvalidOperationException>(() => auth.SetupAdminAsync("another", "Admin 2", Password).GetAwaiter().GetResult(), "Second bootstrap was accepted.");
            Assert(Count(database, "SELECT COUNT(*) FROM AspNetUsers;") == 1, "Repeated setup saved another account.");
            Reject<UnauthorizedAccessException>(() => auth.LoginAsync("Admin", "incorrect").GetAwaiter().GetResult(), "Wrong password accepted.");
            var login = auth.LoginAsync("aDmIn", Password).GetAwaiter().GetResult();
            Assert(permissions.GetStaffAccess(login).FullName == "Quản trị viên", "Normalized username or Vietnamese name failed.");
            auth.Logout(login);
            Reject<UnauthorizedAccessException>(() => permissions.GetStaffAccess(login), "Logged-out session remained usable.");

            FixtureUser(database, "staff", "Staff", hash);
            FixtureUser(database, "manager", "Manager", hash);
            var staff = auth.LoginAsync("staff", Password).GetAwaiter().GetResult();
            var manager = auth.LoginAsync("manager", Password).GetAwaiter().GetResult();
            Assert(permissions.GetStaffAccess(staff).Permissions.Count == 19, "Staff defaults are incorrect.");
            Assert(permissions.GetStaffAccess(manager).Permissions.Count == 24, "Manager defaults are incorrect.");
            foreach (var code in new[] { "User.Manage", "Permission.Manage", "Audit.View", "Report.View", "Report.Export", "Room.Manage" })
                AddPermission(database, "Staff", code);
            Assert(!permissions.HasPermission(staff, "Report.View") && !permissions.HasPermission(staff, "Report.Export"), "Staff bypassed the report restriction.");
            Assert(!permissions.HasPermission(staff, "User.Manage") && !permissions.HasPermission(staff, "Audit.View") &&
                !permissions.HasPermission(staff, "Permission.Manage"), "Staff bypassed administrative restrictions.");
            Assert(permissions.HasPermission(staff, "Room.Manage"), "Staff could not receive an adjustable catalog permission.");
            foreach (var code in new[] { "User.Manage", "Permission.Manage", "Audit.View" }) AddPermission(database, "Manager", code);
            Assert(!permissions.HasPermission(manager, "User.Manage") && !permissions.HasPermission(manager, "Audit.View") &&
                !permissions.HasPermission(manager, "Permission.Manage"), "Manager bypassed administrative restrictions.");

            Sql(database, "DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Report.View');");
            Assert(!permissions.HasPermission(manager, "Report.Export"), "Export did not require report viewing.");
            Sql(database, "DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Invoice.View');");
            Assert(!permissions.HasPermission(staff, "Invoice.Print"), "Print did not require invoice viewing.");
            Sql(database, "DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Reservation.Create');");
            Assert(!permissions.HasPermission(staff, "Reservation.Create"), "Permission changes were cached across calls.");
            Assert(permissions.HasPermission(manager, "Reservation.Create"), "Manager dynamically inherited changes to Staff.");
            Reject<UnauthorizedAccessException>(() => permissions.Demand(staff, "Reservation.Create"), "Direct service call bypassed permission validation.");
            using (var connection = database.OpenConnection())
            using (var transaction = SqliteDatabase.BeginWriteTransaction(connection))
            {
                permissions.Demand(admin, "User.Manage", connection, transaction);
                Reject<UnauthorizedAccessException>(() => permissions.Demand(staff, "Report.View", connection, transaction), "Write-transaction permission check was bypassed.");
            }
            database.Initialize();
            Assert(!permissions.HasPermission(staff, "Reservation.Create"), "Initialization overwrote edited permissions.");

            Sql(database, "UPDATE AspNetUsers SET IsActive=0 WHERE Id='staff';");
            Assert(!permissions.HasPermission(staff, "Calendar.View"), "Disabled user retained access.");
            Reject<UnauthorizedAccessException>(() => auth.LoginAsync("staff", Password).GetAwaiter().GetResult(), "Disabled user logged in.");
            Sql(database, "UPDATE AspNetUsers SET IsActive=1, SecurityStamp='new-staff-stamp' WHERE Id='staff';");
            Assert(!permissions.HasPermission(staff, "Calendar.View"), "Security stamp change did not invalidate old session.");
            Sql(database, "UPDATE AspNetUserRoles SET RoleId='Staff' WHERE UserId='manager'; UPDATE AspNetUsers SET SecurityStamp='role-change' WHERE Id='manager';");
            Assert(!permissions.HasPermission(manager, "Calendar.View"), "Role change retained old login.");
            var changed = auth.LoginAsync("manager", Password).GetAwaiter().GetResult();
            Assert(permissions.GetStaffAccess(changed).Role == "Staff", "Re-login used the old role.");
            Sql(database, "UPDATE AspNetUsers SET PasswordHash=@hash, SecurityStamp='password-reset' WHERE Id='manager';", "@hash", hasher.HashPassword("Changed-Test-2026!"));
            Assert(!permissions.HasPermission(changed, "Calendar.View"), "Password reset retained old login.");
            Reject<UnauthorizedAccessException>(() => auth.LoginAsync("manager", Password).GetAwaiter().GetResult(), "Reset password still accepted the old password.");
            Assert(auth.LoginAsync("manager", "Changed-Test-2026!").GetAwaiter().GetResult() != null, "Reset password was not usable.");
            Reject<SQLiteException>(() => Sql(database, "INSERT INTO AspNetUserRoles VALUES ('staff','Manager');"), "Multiple roles were accepted.");
            Reject<SQLiteException>(() => Sql(database, "INSERT INTO AspNetRoles VALUES ('Other','Other');"), "A fourth role was accepted.");
            Reject<SQLiteException>(() => Sql(database, "INSERT INTO RolePermission VALUES ('Unknown',1);"), "RolePermission foreign key was not enforced.");
            Reject<SQLiteException>(() => FixtureUser(database, "ADMIN", "Staff", hash), "Normalized duplicate username accepted.");
            Assert(Count(database, "SELECT COUNT(*) FROM AspNetUsers;") == 3, "A rejected operation left a partial user.");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    private static void BootstrapRollback()
    {
        var file = NewFile();
        var database = new SqliteDatabase(file);
        try
        {
            var auth = new AuthenticationService(database);
            Assert(auth.NeedsSetup(), "Expected empty test database.");
            Sql(database, "CREATE TRIGGER FailAudit BEFORE INSERT ON AuditLog BEGIN SELECT RAISE(ABORT,'test failure'); END;");
            Reject<SQLiteException>(() => auth.SetupAdminAsync("admin", "Admin", Password).GetAwaiter().GetResult(), "Injected audit error did not fail setup.");
            Assert(Count(database, "SELECT COUNT(*) FROM AspNetUsers;") == 0 && Count(database, "SELECT COUNT(*) FROM AspNetUserRoles;") == 0,
                "Audit failure did not roll back user and membership together.");
            Sql(database, "DROP TRIGGER FailAudit;");
            Assert(auth.SetupAdminAsync("admin", "Admin", Password).GetAwaiter().GetResult() != null, "Setup could not recover after rollback.");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    private static void ConcurrentBootstrap()
    {
        var file = NewFile();
        var database = new SqliteDatabase(file);
        try
        {
            database.Initialize();
            var gate = new System.Threading.ManualResetEventSlim(false);
            Func<string, Task<bool>> attempt = name => Task.Run(() =>
            {
                gate.Wait();
                try { new AuthenticationService(database).SetupAdminAsync(name, name, Password).GetAwaiter().GetResult(); return true; }
                catch (InvalidOperationException) { return false; }
            });
            var first = attempt("first");
            var second = attempt("second");
            gate.Set();
            Task.WaitAll(first, second);
            gate.Dispose();
            Assert(first.Result != second.Result, "Concurrent setup did not have exactly one successful attempt.");
            Assert(Count(database, "SELECT COUNT(*) FROM AspNetUsers;") == 1 &&
                Count(database, "SELECT COUNT(*) FROM AspNetUserRoles WHERE RoleId='Admin';") == 1 &&
                Count(database, "SELECT COUNT(*) FROM AuditLog WHERE Action='User.Bootstrap';") == 1,
                "Concurrent setup saved multiple/partial Admins or audits.");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }
}
