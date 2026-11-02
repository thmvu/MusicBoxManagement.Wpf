using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNet.Identity;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MySqlConnector;

public static class MusicBoxMySqlAccountBatchChecks
{
    private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Sql(MySqlConnection c, string text, MySqlTransaction tx = null)
    { using (var q = c.CreateCommand()) { q.Transaction = tx; q.CommandText = text; return q.ExecuteScalar(); } }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static LoginSession Session(MySqlDatabase db, string id, string stamp)
    { return (LoginSession)Activator.CreateInstance(typeof(LoginSession), Internal, null, new object[] { id, stamp, "mysql:" + db.DatabaseName }, null); }
    private static IReadOnlyList<UserAccount> Read(MySqlAccountManagementService service, LoginSession actor, MySqlConnection c, MySqlTransaction tx)
    {
        try { return (IReadOnlyList<UserAccount>)typeof(MySqlAccountManagementService).GetMethod("ListAt", Internal).Invoke(service, new object[] { actor, c, tx }); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }
    private static long Selects(MySqlConnection c, MySqlTransaction tx)
    { using (var q = c.CreateCommand()) { q.Transaction = tx; q.CommandText = "SHOW SESSION STATUS LIKE 'Com_select'"; using (var r = q.ExecuteReader()) { r.Read(); return long.Parse(r.GetString(1)); } } }
    // Previous List algorithm, through the actual Identity store and revision projector.
    private static List<UserAccount> Oracle(MySqlConnection c, MySqlTransaction tx)
    {
        var type = typeof(MySqlDatabase).Assembly.GetType("MusicBoxManagement.Wpf.Data.MySqlIdentityStore", true);
        using (var store = (IDisposable)Activator.CreateInstance(type, Internal, null, new object[] { c }, null))
        {
            type.GetProperty("Transaction", Internal).SetValue(store, tx, null);
            var ids = new List<string>();
            using (var q = c.CreateCommand()) { q.Transaction = tx; q.CommandText = "SELECT Id FROM AspNetUsers ORDER BY NormalizedUserName;"; using (var r = q.ExecuteReader()) while (r.Read()) ids.Add(r.GetString(0)); }
            var projector = typeof(MySqlAccountManagementService).GetMethod("Project", BindingFlags.Static | BindingFlags.NonPublic);
            return ids.Select(id => (UserAccount)projector.Invoke(null, new object[] { ((IUserStore<ApplicationUser>)store).FindByIdAsync(id).GetAwaiter().GetResult() })).ToList();
        }
    }
    private static void Same(IReadOnlyList<UserAccount> actual, IReadOnlyList<UserAccount> expected)
    {
        Assert(actual.Count == expected.Count, "Account count changed.");
        for (var i = 0; i < actual.Count; i++)
        {
            var a = actual[i]; var e = expected[i];
            Assert(a.UserId == e.UserId && a.UserName == e.UserName && a.FullName == e.FullName && a.Role == e.Role && a.IsActive == e.IsActive && a.Revision == e.Revision, "Account ordering, metadata or opaque revision changed.");
        }
    }
    public static void Run()
    {
        var name = "musicbox_test_" + Guid.NewGuid().ToString("N"); var db = new MySqlDatabase(name);
        Console.WriteLine("Account batch fixture: " + name);
        try
        {
            db.Initialize();
            var admin = Session(db, "admin", "admin-stamp"); var service = new MySqlAccountManagementService(db);
            using (var c = db.OpenConnection())
            {
                Sql(c, "INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) VALUES('admin','admin','ADMIN','admin-hash','admin-stamp','Admin',1);INSERT INTO AspNetUserRoles VALUES('admin','Admin');");
                Assert(service.List(admin).Count == 1, "Single account changed.");
                Sql(c, "INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) VALUES('z','zebra','ZEBRA','hash-z','stamp-z','Tên Z',0),('b','Beta','BETA','hash-b','stamp-b','Tên B',1),('m','middle','MIDDLE','hash-m','stamp-m','Tên M',1),('c','charlie','CHARLIE','hash-c','stamp-c','Tên C',1);INSERT INTO AspNetUserRoles VALUES('z','Staff'),('b','Manager'),('m','Staff'),('c','Admin');");
                string originalRevision;
                using (var tx = MySqlDatabase.BeginReadTransaction(c))
                {
                    var before = Selects(c, tx); var actual = Read(service, admin, c, tx); var count = Selects(c, tx) - before;
                    Console.WriteLine("Account List SELECTs: " + count + " for " + actual.Count + " accounts.");
                    Assert(count <= 2, "Account List N+1: " + count + " SELECTs; budget2 including current Admin permission.");
                    Same(actual, Oracle(c, tx));
                    Assert(string.Join(",", actual.Select(u => u.UserId)) == "admin,b,c,m,z", "Normalized username ordering changed.");
                    string expectedRevision;
                    using (var sha = SHA256.Create()) expectedRevision = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes("z\u001fzebra\u001fTên Z\u001fStaff\u001fFalse\u001fstamp-z\u001fhash-z")));
                    Assert(actual.Single(u => u.UserId == "z").Revision == expectedRevision, "Revision omitted or reordered private inputs.");
                    originalRevision = expectedRevision;
                    using (var writer = db.OpenConnection()) Sql(writer, "UPDATE AspNetUsers SET FullName='Tên mới',SecurityStamp='new-stamp',PasswordHash='new-hash',IsActive=1 WHERE Id='z';UPDATE AspNetUserRoles SET RoleId='Manager' WHERE UserId='z';UPDATE AspNetUsers SET IsActive=0 WHERE Id='admin';");
                    Same(Read(service, admin, c, tx), actual);
                    Reject<UnauthorizedAccessException>(() => service.List(admin));
                }
                Sql(c, "UPDATE AspNetUsers SET IsActive=1 WHERE Id='admin';");
                var refreshed = service.List(admin).Single(u => u.UserId == "z");
                Assert(refreshed.FullName == "Tên mới" && refreshed.Role == "Manager" && refreshed.IsActive && refreshed.Revision != originalRevision, "Fresh account metadata was cached.");
                Reject<UnauthorizedAccessException>(() => service.List(null));
                Reject<UnauthorizedAccessException>(() => service.List(Session(db, "m", "stamp-m")));
                Reject<UnauthorizedAccessException>(() => service.List(Session(db, "b", "stamp-b")));
                Reject<UnauthorizedAccessException>(() => service.List(Session(db, "admin", "wrong-stamp")));
                Sql(c, "INSERT IGNORE INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='User.Manage';");
                Reject<UnauthorizedAccessException>(() => service.List(Session(db, "m", "stamp-m")));
                Sql(c, "DELETE FROM AspNetUserRoles WHERE UserId='m';");
                Reject<InvalidOperationException>(() => service.List(admin));
                using (var tx = MySqlDatabase.BeginReadTransaction(c)) Reject<TargetInvocationException>(() => Oracle(c, tx));
                Sql(c, "INSERT INTO AspNetUserRoles VALUES('m','Staff');");
                Assert(Convert.ToInt64(Sql(c, "SELECT COUNT(*) FROM AuditLog;")) == 0, "Account reads wrote audit.");
            }
            var properties = typeof(UserAccount).GetProperties().Select(p => p.Name).OrderBy(s => s).ToArray();
            Assert(string.Join(",", properties) == "FullName,IsActive,Revision,Role,UserId,UserName", "Account DTO exposed private Identity fields.");
            Console.WriteLine("PASS Account List constant SELECT budget, old-store oracle, exact private revision inputs, ordering, missing-role failure, live Admin-only rights and RR metadata/role snapshot; GUID only.");
        }
        catch (Exception error) { Console.WriteLine("Account batch failure in owned fixture " + name + ": " + error); throw; }
        finally
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"\Amusicbox_test_[0-9a-f]{32}\z")) throw new Exception("Unsafe account batch cleanup.");
            using (var c = new MySqlDatabase("defaultdb").OpenConnection()) Sql(c, "DROP DATABASE IF EXISTS `" + name + "`");
        }
    }
}
