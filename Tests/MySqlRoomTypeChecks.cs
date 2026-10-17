using System;
using MySqlConnector;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;


public static class MusicBoxMySqlRoomTypeChecks
{
    private const string Password = "RoomTypes-Test-2026!";
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action, string message) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception(message);
    }
    private static object Sql(MySqlDatabase database, string sql, params object[] values)
    {
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = sql;
            for (var i = 0; i < values.Length; i += 2) command.Parameters.AddWithValue((string)values[i], values[i + 1]);
            return command.ExecuteScalar();
        }
    }
    private static long Count(MySqlDatabase database, string sql) { return Convert.ToInt64(Sql(database, sql)); }
    private static RoomTypeEdit Changes(RoomType type) { return new RoomTypeEdit { Name = type.Name, Capacity = type.Capacity,
        PricePerHour = type.PricePerHour, Amenities = type.Amenities, Description = type.Description }; }
    private static void FixtureUser(MySqlDatabase database, string id, string role, string hash)
    {
        Sql(database, @"INSERT INTO AspNetUsers(Id, UserName, NormalizedUserName, PasswordHash, SecurityStamp, FullName, IsActive)
VALUES (@id, @id, upper(@id), @hash, @id, @id, 1);
INSERT INTO AspNetUserRoles VALUES (@id, @role);", "@id", id, "@role", role, "@hash", hash);
    }

    public static void Run()
    {
        var databaseName = "musicbox_test_" + Guid.NewGuid().ToString("N");
        var database = new MySqlDatabase(databaseName);
        try
        {
            var auth = new MySqlAuthenticationService(database);
            var service = new MySqlRoomTypeService(database);
            var admin = auth.SetupAdminAsync("admin", "Quản trị viên thử nghiệm", Password).GetAwaiter().GetResult();
            Assert(Count(database, "SELECT Version FROM _MusicBoxSchemaVersion WHERE Id=1;") == MySqlDatabase.CurrentSchemaVersion, "Expected current MySQL schema.");

            var hash = (string)Sql(database, "SELECT PasswordHash FROM AspNetUsers WHERE Id=@id;", "@id", admin.UserId);
            FixtureUser(database, "manager", "Manager", hash);
            FixtureUser(database, "staff", "Staff", hash);
            var manager = auth.LoginAsync("manager", Password).GetAwaiter().GetResult();
            var staff = auth.LoginAsync("staff", Password).GetAwaiter().GetResult();
            var standard = service.GetForEdit(manager, 1);
            Reject<UnauthorizedAccessException>(() => service.GetForEdit(null, 1), "Guest opened protected editing data.");
            Reject<UnauthorizedAccessException>(() => service.Update(staff, standard, Changes(standard)), "Default Staff edited a room type.");
            Reject<UnauthorizedAccessException>(() => service.Update(null, standard, Changes(standard)), "Guest updated a room type.");
            Reject<InvalidOperationException>(() => service.GetForEdit(admin, 999), "Missing RoomType accepted.");

            var changes = Changes(standard);
            changes.Name = "  Standard mới  "; changes.Capacity = 5; changes.PricePerHour = 145000;
            changes.Amenities = " TV, Điều hòa, Micro "; changes.Description = " Phòng thử nghiệm ";
            service.Update(manager, standard, changes);
            var saved = service.GetForEdit(admin, 1);
            Assert(saved.Name == "Standard mới" && saved.Capacity == 5 && saved.PricePerHour == 145000 &&
                saved.Amenities == "TV, Điều hòa, Micro" && saved.Description == "Phòng thử nghiệm" && saved.Code == "STANDARD",
                "Editable fields, normalization or immutable Code are incorrect.");
            Assert(Count(database, "SELECT COUNT(*) FROM AuditLog WHERE Action='RoomType.Update' AND ActorType='Staff' AND EntityId='1';") == 1,
                "Update and its Staff audit were not saved together.");
            Assert(service.List().Count == 2 && service.List()[0].PricePerHour == 145000, "Reopening changed catalog data or count.");
            service.Update(admin, saved, Changes(saved));
            Assert(Count(database, "SELECT COUNT(*) FROM AuditLog WHERE Action='RoomType.Update';") == 1, "An unchanged save created an unnecessary audit.");
            Reject<InvalidOperationException>(() => service.Update(admin, standard, changes), "An old form silently overwrote newer data.");
            Reject<InvalidOperationException>(() => service.Update(admin, new RoomType { RoomTypeId = 999 }, changes), "Update inserted an unknown RoomType.");

            Action<Action<RoomTypeEdit>> invalid = mutate =>
            {
                var invalidChanges = Changes(saved); mutate(invalidChanges);
                Reject<ArgumentException>(() => service.Update(admin, saved, invalidChanges), "Invalid input bypassed service validation.");
            };
            invalid(x => x.Name = " "); invalid(x => x.Name = new string('x', 101));
            invalid(x => x.Capacity = 0); invalid(x => x.Capacity = -1);
            invalid(x => x.PricePerHour = 0); invalid(x => x.PricePerHour = -1);
            invalid(x => x.Amenities = " "); invalid(x => x.Amenities = new string('x', 1001));
            invalid(x => x.Description = new string('x', 2001));
            Assert(service.List()[0].PricePerHour == 145000 && Count(database, "SELECT COUNT(*) FROM AuditLog WHERE Action='RoomType.Update';") == 1,
                "Rejected edits changed data or audit count.");

            var large = Changes(saved); large.PricePerHour = long.MaxValue;
            service.Update(admin, saved, large);
            var exact = service.GetForEdit(admin, 1);
            Assert(exact.PricePerHour == long.MaxValue, "BIGINT price lost precision.");
            service.Update(admin, exact, Changes(saved));
            var forgedCode = new RoomType { RoomTypeId=saved.RoomTypeId,Code="VIP",Name=saved.Name,
                Capacity=saved.Capacity,PricePerHour=saved.PricePerHour,Amenities=saved.Amenities,Description=saved.Description };
            Reject<InvalidOperationException>(() => service.Update(admin,forgedCode,Changes(saved)), "A forged Code bypassed original-record validation.");

            // Revoke a permission after the editor has already read the entity.
            var opened = service.GetForEdit(manager, 1);
            Sql(database, "DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='RoomType.Edit');");
            changes = Changes(opened); changes.PricePerHour = 155000;
            Reject<UnauthorizedAccessException>(() => service.Update(manager, opened, changes), "An open editor bypassed revoked permission.");
            auth.Logout(manager);
            Reject<UnauthorizedAccessException>(() => service.Update(manager, opened, changes), "A logged-out session saved a form.");

            // Staff can receive this ordinary permission without receiving reports.
            Sql(database, "INSERT INTO RolePermission SELECT 'Staff', PermissionId FROM Permission WHERE Code='RoomType.Edit';");
            var staffOriginal = service.GetForEdit(staff, 1);
            var staffChanges = Changes(staffOriginal); staffChanges.Description = " ";
            service.Update(staff, staffOriginal, staffChanges);
            Assert(service.List()[0].Description == null, "Optional blank description was not stored as NULL.");
            var auditCount = Count(database, "SELECT COUNT(*) FROM AuditLog;");
            var beforeFailure = service.GetForEdit(admin, 1);
            var failedChanges = Changes(beforeFailure); failedChanges.PricePerHour = 175000;
            Sql(database, "CREATE TRIGGER FailRoomTypeAudit BEFORE INSERT ON AuditLog FOR EACH ROW BEGIN IF NEW.Action='RoomType.Update' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='test failure'; END IF; END");
            Reject<MySqlException>(() => service.Update(admin, beforeFailure, failedChanges), "Injected audit failure was ignored.");
            Assert(service.List()[0].PricePerHour == beforeFailure.PricePerHour && Count(database, "SELECT COUNT(*) FROM AuditLog;") == auditCount,
                "Audit failure did not roll back the room-type edit.");
            Sql(database, "DROP TRIGGER FailRoomTypeAudit;");

            var firstOriginal = service.GetForEdit(admin, 2);
            var secondOriginal = service.GetForEdit(admin, 2);
            using (var gate = new ManualResetEventSlim(false))
            {
                Func<RoomType, long, Task<bool>> attempt = (original, price) => Task.Run(() =>
                {
                    var edit = Changes(original); edit.PricePerHour = price; gate.Wait();
                    try { service.Update(admin, original, edit); return true; }
                    catch (InvalidOperationException) { return false; }
                });
                var first = attempt(firstOriginal, 210000);
                var second = attempt(secondOriginal, 220000);
                gate.Set(); Task.WaitAll(first, second);
                Assert(first.Result != second.Result, "Concurrent old forms did not have exactly one successful save.");
            }
            Assert(service.List().Count == 2 && service.List()[1].Code == "VIP", "Concurrent editing changed type identities.");
            Assert(Count(database, "SELECT COUNT(*) FROM AuditLog WHERE Action='RoomType.Update' AND EntityId='2';") == 1,
                "Concurrent edits created duplicate audit entries.");
            Console.WriteLine("PASS MySQL RoomType editing: protected reads/writes, validation, immutable codes, persistence, permission revocation, atomic audit rollback and concurrent stale forms on a GUID database.");
        }
        finally
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(databaseName,@"\Amusicbox_test_[0-9a-f]{32}\z")) throw new Exception("Unsafe MySQL RoomType cleanup.");
            Sql(new MySqlDatabase("defaultdb"),"DROP DATABASE IF EXISTS `"+databaseName+"`");
        }
    }
}
