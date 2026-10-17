using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MySqlConnector;

public static class MusicBoxMySqlAuthenticationChecks
{
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); }
    private static object Sql(MySqlConnection c, string sql, MySqlTransaction t = null)
    {
        using (var command = c.CreateCommand())
        { command.Transaction = t; command.CommandText = sql; return command.ExecuteScalar(); }
    }
    private static void Denied(Action action, string message)
    {
        try { action(); } catch (UnauthorizedAccessException) { return; }
        throw new Exception(message);
    }
    private static void Fails(Action action, string message)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        catch (MySqlException ex) { if (ex.Number == 1644 || ex.Number == 1213 || ex.Number == 1205) return; throw; }
        throw new Exception(message);
    }
    private static void Invalid(Action action, string message)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception(message);
    }
    public static void RunOffline()
    {
        // These security/validation failures must be rejected before any network operation.
        var db = new MySqlDatabase("musicbox_test_offline");
        var auth = new MySqlAuthenticationService(db);
        var permissions = new MySqlPermissionService(db);
        Invalid(() => new MySqlDatabase("musicbox;DROP DATABASE musicbox"), "Unsafe database name accepted.");
        Invalid(() => auth.SetupAdminAsync("a", "Admin", "Test-Password-123").GetAwaiter().GetResult(), "Short username accepted.");
        Invalid(() => auth.SetupAdminAsync("admin", " ", "Test-Password-123").GetAwaiter().GetResult(), "Empty full name accepted.");
        Invalid(() => auth.SetupAdminAsync("admin", "Admin", "short").GetAwaiter().GetResult(), "Short password accepted.");
        Invalid(() => auth.SetupAdminAsync("admin", "Admin", new string('x',129)).GetAwaiter().GetResult(), "Oversized password accepted.");
        Denied(() => auth.LoginAsync("", "test").GetAwaiter().GetResult(), "Empty login accepted.");
        Denied(() => auth.LoginAsync("admin", null).GetAwaiter().GetResult(), "Null password accepted.");
        Check(!permissions.HasPermission(null,"Session.View"), "Guest authorized.");
        var constructor = typeof(LoginSession).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Single();
        var unscoped = (LoginSession)constructor.Invoke(new object[] { "same_id","same_stamp",null });
        var differentDatabase = (LoginSession)constructor.Invoke(new object[] { "same_id","same_stamp","mysql:other_database" });
        Check(!permissions.HasPermission(unscoped,"Session.View"), "SQLite session reached MySQL.");
        Check(!permissions.HasPermission(differentDatabase,"Session.View"), "Different database session reached MySQL.");
        Check(!permissions.HasPermission(unscoped,"Unknown.Permission"), "Unknown permission accepted.");
        var mysqlSession = (LoginSession)constructor.Invoke(new object[] { "same_id","same_stamp","mysql:musicbox_test_offline" });
        string sqliteFile = Path.Combine(Path.GetTempPath(),"MusicBoxScope_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            Denied(() => new PermissionService(new SqliteDatabase(sqliteFile)).GetStaffAccess(mysqlSession),
                "MySQL session authorized on SQLite.");
        }
        finally { if (File.Exists(sqliteFile)) File.Delete(sqliteFile); }
        Console.WriteLine("PASS MySQL offline preflight: input validation and Guest/provider/database session boundaries; no cloud integration claimed.");
    }
    public static void Run()
    {
        RunOffline();
        string name = "musicbox_test_" + Guid.NewGuid().ToString("N");
        var db = new MySqlDatabase(name);
        try
        {
            db.Initialize();
            var auth = new MySqlAuthenticationService(db);
            var permissions = new MySqlPermissionService(db);
            Check(auth.NeedsSetup(), "Fresh database must need bootstrap.");
            using (var c = db.OpenConnection())
            {
                Sql(c, "CREATE TRIGGER TestAuditFailure BEFORE INSERT ON AuditLog FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Injected audit failure'");
                Fails(() => auth.SetupAdminAsync("admin", "Admin thử", "Test-Password-123").GetAwaiter().GetResult(), "Bootstrap audit failure accepted.");
                Check(Convert.ToInt32(Sql(c,"SELECT COUNT(*) FROM AspNetUsers")) == 0, "User not rolled back.");
                Check(Convert.ToInt32(Sql(c,"SELECT COUNT(*) FROM AspNetUserRoles")) == 0, "Membership not rolled back.");
                Sql(c, "DROP TRIGGER TestAuditFailure");
            }
            // Two actual connections, competing for the initially empty user range.
            var start = new ManualResetEventSlim(false);
            Func<string, Task<LoginSession>> bootstrap = user => Task.Run(() => {
                start.Wait();
                try { return auth.SetupAdminAsync(user, "Admin thử", "Test-Password-123").GetAwaiter().GetResult(); }
                catch (InvalidOperationException) { return null; }
                catch (MySqlException ex) { if (ex.Number == 1213 || ex.Number == 1205) return null; throw; }
            });
            var first = bootstrap("admin_one"); var second = bootstrap("admin_two"); start.Set();
            Task.WaitAll(first, second); start.Dispose();
            Check((first.Result == null) != (second.Result == null), "Concurrent bootstrap must have exactly one winner.");
            var admin = first.Result ?? second.Result;
            Check(!auth.NeedsSetup(), "Bootstrap not persisted.");
            Check(permissions.GetStaffAccess(admin).Permissions.Count == 27, "Admin must have fixed full access.");
            Fails(() => auth.SetupAdminAsync("another", "Admin", "Test-Password-123").GetAwaiter().GetResult(), "Second bootstrap allowed.");
            using (var c = db.OpenConnection())
            {
                Check(Convert.ToInt32(Sql(c,"SELECT COUNT(*) FROM AspNetUsers")) == 1, "Duplicate bootstrap user.");
                Check(Convert.ToInt32(Sql(c,"SELECT COUNT(*) FROM AuditLog WHERE Action='User.Bootstrap'")) == 1, "Duplicate bootstrap audit.");
                var userName = (string)Sql(c,"SELECT UserName FROM AspNetUsers LIMIT 1");
                Check(((string)Sql(c,"SELECT PasswordHash FROM AspNetUsers LIMIT 1")).StartsWith("PBKDF2-SHA256$1$600000$"), "Identity password format changed.");
                Denied(() => auth.LoginAsync(userName,"Wrong-password").GetAwaiter().GetResult(), "Wrong password accepted.");
                var login = auth.LoginAsync("  " + userName.ToUpperInvariant() + "  ","Test-Password-123").GetAwaiter().GetResult();
                Check(permissions.HasPermission(login,"User.Manage"), "Normalized login failed.");
                auth.Logout(login);
                Check(!permissions.HasPermission(login,"User.Manage"), "Logout session reused.");
                Sql(c, "CREATE TRIGGER TestAuditFailure BEFORE INSERT ON AuditLog FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Injected audit failure'");
                long auditCount = Convert.ToInt64(Sql(c,"SELECT COUNT(*) FROM AuditLog"));
                Fails(() => auth.LoginAsync(userName,"Test-Password-123").GetAwaiter().GetResult(), "Login returned a session despite audit failure.");
                Check(Convert.ToInt64(Sql(c,"SELECT COUNT(*) FROM AuditLog")) == auditCount, "Failed login wrote audit.");
                Sql(c, "DROP TRIGGER TestAuditFailure");
                // Fixture accounts are confined to this GUID database. No production admin API bypass.
                Sql(c, @"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'staff','staff_test','STAFF_TEST',PasswordHash,'staff_stamp','Nhân viên thử',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles(UserId,RoleId) VALUES('staff','Staff');
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'manager','manager_test','MANAGER_TEST',PasswordHash,'manager_stamp','Quản lý thử',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles(UserId,RoleId) VALUES('manager','Manager');");
                var staff = auth.LoginAsync("staff_test","Test-Password-123").GetAwaiter().GetResult();
                var manager = auth.LoginAsync("manager_test","Test-Password-123").GetAwaiter().GetResult();
                Check(permissions.HasPermission(staff,"Reservation.Create"), "Staff defaults missing.");
                Check(permissions.HasPermission(manager,"Report.Export"), "Manager defaults missing.");
                Sql(c, "INSERT IGNORE INTO RolePermission(RoleId,PermissionId) SELECT 'Staff',PermissionId FROM Permission");
                Sql(c, "INSERT IGNORE INTO RolePermission(RoleId,PermissionId) SELECT 'Manager',PermissionId FROM Permission");
                Check(!permissions.HasPermission(staff,"Report.View") && !permissions.HasPermission(staff,"Report.Export"), "Staff report invariant bypassed.");
                Check(!permissions.HasPermission(staff,"User.Manage") && !permissions.HasPermission(manager,"Permission.Manage") && !permissions.HasPermission(manager,"Audit.View"), "Admin-only invariant bypassed.");
                Sql(c, "DELETE rp FROM RolePermission rp JOIN Permission p ON p.PermissionId=rp.PermissionId WHERE rp.RoleId='Manager' AND p.Code IN ('Report.View','Invoice.View')");
                Check(!permissions.HasPermission(manager,"Report.Export") && !permissions.HasPermission(manager,"Invoice.Print"), "Permission dependency bypassed.");
                Sql(c, "DELETE rp FROM RolePermission rp JOIN Permission p ON p.PermissionId=rp.PermissionId WHERE rp.RoleId='Staff' AND p.Code='Reservation.Create'");
                Check(!permissions.HasPermission(staff,"Reservation.Create"), "Revoked permission cached.");
                Check(permissions.HasPermission(manager,"Reservation.Create"), "Manager dynamically inherited Staff changes.");
                db.Initialize();
                Check(!permissions.HasPermission(staff,"Reservation.Create"), "Initialize restored revoked permission.");
                using (var tx = MySqlDatabase.BeginWriteTransaction(c))
                {
                    permissions.Demand(admin,"User.Manage",c,tx);
                    Denied(() => permissions.Demand(staff,"User.Manage",c,tx), "Writer bypassed current permission.");
                    Sql(c,"UPDATE AspNetUsers SET FullName='Rollback' WHERE Id='staff'",tx);
                    tx.Rollback();
                }
                Check((string)Sql(c,"SELECT FullName FROM AspNetUsers WHERE Id='staff'") == "Nhân viên thử", "Writer rollback failed.");
                using (var tx = MySqlDatabase.BeginWriteTransaction(c))
                    Sql(c,"UPDATE AspNetUsers SET FullName='Dispose rollback' WHERE Id='staff'",tx);
                Check((string)Sql(c,"SELECT FullName FROM AspNetUsers WHERE Id='staff'") == "Nhân viên thử", "Dispose did not roll back.");
                using (var other = db.OpenConnection())
                {
                    using (var read = MySqlDatabase.BeginReadTransaction(c))
                    {
                        Check(Convert.ToInt32(Sql(c,"SELECT Capacity FROM RoomTypes WHERE Code='STANDARD'",read)) == 4, "Initial snapshot incorrect.");
                        Sql(other,"UPDATE RoomTypes SET Capacity=5 WHERE Code='STANDARD'");
                        Check(Convert.ToInt32(Sql(c,"SELECT Capacity FROM RoomTypes WHERE Code='STANDARD'",read)) == 4, "Read transaction mixed snapshots.");
                        bool readOnly = false;
                        try { Sql(c,"UPDATE RoomTypes SET Capacity=6 WHERE Code='STANDARD'",read); }
                        catch (MySqlException ex) { if (ex.Number == 1792) readOnly = true; else throw; }
                        Check(readOnly,"Read-only transaction allowed writes.");
                    }
                    Check(Convert.ToInt32(Sql(c,"SELECT Capacity FROM RoomTypes WHERE Code='STANDARD'")) == 5, "Fresh read did not see commit.");
                    // Demand's Serializable read must retain protection until the business writer ends.
                    using (var tx = MySqlDatabase.BeginWriteTransaction(c))
                    {
                        permissions.Demand(staff,"Session.View",c,tx);
                        Sql(other,"SET SESSION innodb_lock_wait_timeout=1");
                        bool blocked = false;
                        try { Sql(other,"UPDATE AspNetUsers SET IsActive=0 WHERE Id='staff'"); }
                        catch (MySqlException ex) { if (ex.Number == 1205) blocked = true; else throw; }
                        Check(blocked,"Concurrent account lock slipped between Demand and commit.");
                        tx.Commit();
                    }
                    Sql(other,"UPDATE AspNetUsers SET IsActive=0 WHERE Id='staff'");
                }
                Check(!permissions.HasPermission(staff,"Session.View"), "Locked account still authorized.");
                Denied(() => auth.LoginAsync("staff_test","Test-Password-123").GetAwaiter().GetResult(), "Locked user logged in.");
                Sql(c,"UPDATE AspNetUsers SET IsActive=1,SecurityStamp='new_stamp' WHERE Id='staff'");
                Check(!permissions.HasPermission(staff,"Session.View"), "Old stamp revived after unlock.");
                var fresh = auth.LoginAsync("staff_test","Test-Password-123").GetAwaiter().GetResult();
                Check(permissions.HasPermission(fresh,"Session.View"), "Fresh login failed after stamp change.");
                Sql(c,"UPDATE AspNetUserRoles SET RoleId='Manager' WHERE UserId='staff'; UPDATE AspNetUsers SET SecurityStamp='role_stamp' WHERE Id='staff'");
                Check(!permissions.HasPermission(fresh,"Session.View"), "Role change did not invalidate previous session.");
                var newRole = auth.LoginAsync("staff_test","Test-Password-123").GetAwaiter().GetResult();
                Check(permissions.GetStaffAccess(newRole).Role == "Manager", "New login has stale role.");
                Check(!new MySqlPermissionService(new MySqlDatabase("defaultdb")).HasPermission(admin,"User.Manage"), "Session crossed database boundary.");
                var constructor = typeof(LoginSession).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Single();
                var unscoped = (LoginSession)constructor.Invoke(new object[] { admin.UserId,admin.SecurityStamp,null });
                Check(!permissions.HasPermission(unscoped,"User.Manage"), "Unscoped SQLite session authorized on MySQL.");
                Check(!permissions.HasPermission(null,"Session.View") && !permissions.HasPermission(admin,"Unknown.Permission"), "Invalid authorization accepted.");
                Console.WriteLine("MySQL authentication: bootstrap/concurrency/audit rollback/login/RBAC/current permissions/stamp/snapshot/transaction/scope passed.");
            }
        }
        finally
        {
            // Only this locally generated test database is eligible for deletion.
            if (!System.Text.RegularExpressions.Regex.IsMatch(name,"\\Amusicbox_test_[0-9a-f]{32}\\z")) throw new Exception("Unsafe cleanup name.");
            using (var c = new MySqlDatabase("defaultdb").OpenConnection())
                Sql(c,"DROP DATABASE IF EXISTS `" + name + "`");
        }
    }
}
