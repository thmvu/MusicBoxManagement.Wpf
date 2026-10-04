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

public static class MusicBoxRoomTypeChecks
{
    private const string Password = "RoomTypes-Test-2026!";
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action, string message) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception(message);
    }
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
    private static long Count(SqliteDatabase database, string sql) { return Convert.ToInt64(Sql(database, sql)); }
    private static RoomTypeEdit Changes(RoomType type) { return new RoomTypeEdit { Name = type.Name, Capacity = type.Capacity,
        PricePerHour = type.PricePerHour, Amenities = type.Amenities, Description = type.Description }; }
    private static void FixtureUser(SqliteDatabase database, string id, string role, string hash)
    {
        Sql(database, @"INSERT INTO AspNetUsers(Id, UserName, NormalizedUserName, PasswordHash, SecurityStamp, FullName, IsActive)
VALUES (@id, @id, upper(@id), @hash, @id, @id, 1);
INSERT INTO AspNetUserRoles VALUES (@id, @role);", "@id", id, "@role", role, "@hash", hash);
    }

    public static void Run()
    {
        var file = Path.Combine(Path.GetTempPath(), "MusicBoxRoomTypes_" + Guid.NewGuid().ToString("N") + ".db");
        var database = new SqliteDatabase(file);
        try
        {
            var auth = new AuthenticationService(database);
            var service = new RoomTypeService(database);
            var admin = auth.SetupAdminAsync("admin", "Quản trị viên thử nghiệm", Password).GetAwaiter().GetResult();
            Assert(Count(database, "PRAGMA user_version;") == 5, "Expected schema v5.");
            VerifyV2Migration(database, admin.UserId);
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

            var viewModel = new RoomTypeEditViewModel(service, admin, saved);
            foreach (var price in new[] { "125000.5", "120.000", "0", "-1", "abc", "999999999999999999999" })
            {
                viewModel.PriceText = price;
                Assert(!viewModel.SaveAsync().GetAwaiter().GetResult(), "Price input accepted a non-positive/non-integer/overflow value.");
            }
            viewModel.PriceText = "145000"; viewModel.CapacityText = "2.5";
            Assert(!viewModel.SaveAsync().GetAwaiter().GetResult(), "Capacity input accepted a fraction.");

            // Revoke a permission after the editor has already read the entity.
            var opened = service.GetForEdit(manager, 1);
            Sql(database, "DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='RoomType.Edit');");
            changes = Changes(opened); changes.PricePerHour = 155000;
            Reject<UnauthorizedAccessException>(() => service.Update(manager, opened, changes), "An open editor bypassed revoked permission.");
            var revoked = new RoomTypeEditViewModel(service, manager, opened) { PriceText = "155000" };
            Assert(!revoked.SaveAsync().GetAwaiter().GetResult() && revoked.Status.Contains("quyền"), "UI did not explain revoked permission.");
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
            Sql(database, "CREATE TRIGGER FailRoomTypeAudit BEFORE INSERT ON AuditLog WHEN NEW.Action='RoomType.Update' BEGIN SELECT RAISE(ABORT,'test failure'); END;");
            Reject<SQLiteException>(() => service.Update(admin, beforeFailure, failedChanges), "Injected audit failure was ignored.");
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
            Console.WriteLine("PASS RoomType editing: v2 audit migration, protected reads/writes, validation, immutable codes, persistence, permission revocation, atomic audit rollback and concurrent stale forms.");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    private static void VerifyV2Migration(SqliteDatabase database, string userId)
    {
        var existingId = Count(database, "SELECT AuditLogId FROM AuditLog LIMIT 1;");
        var existingTime = (string)Sql(database, "SELECT CreatedAt FROM AuditLog LIMIT 1;");
        // Reconstruct v2 audit storage and include Guest/System to verify all actors.
        Sql(database, @"DROP TABLE RoomSessions; DROP TABLE Reservations; DROP TABLE Customers; DROP TABLE Rooms;
ALTER TABLE AuditLog RENAME TO AuditLog_saved;
CREATE TABLE AuditLog (
AuditLogId INTEGER PRIMARY KEY, ActorType TEXT NOT NULL CHECK(ActorType IN ('User','Guest','System')),
UserId TEXT NULL REFERENCES AspNetUsers(Id), Action TEXT NOT NULL, EntityName TEXT NOT NULL,
EntityId TEXT NULL, Description TEXT NOT NULL, CreatedAt TEXT NOT NULL);
INSERT INTO AuditLog SELECT AuditLogId, 'User', UserId, Action, EntityName, EntityId, Description, CreatedAt FROM AuditLog_saved;
DROP TABLE AuditLog_saved;
INSERT INTO AuditLog VALUES(30,'Guest',NULL,'test','test',NULL,'Khách thử nghiệm','2026-10-03T00:00:00+00:00');
INSERT INTO AuditLog VALUES(31,'System',NULL,'test','test',NULL,'Hệ thống thử nghiệm','2026-10-03T00:00:00+00:00');
PRAGMA user_version=2;
CREATE TABLE AuditLog_v2(test TEXT);");
        Reject<SQLiteException>(() => database.Initialize(), "Migration conflict was ignored.");
        Assert(Count(database, "PRAGMA user_version;") == 2 && (string)Sql(database, "SELECT ActorType FROM AuditLog WHERE AuditLogId=@id;", "@id", existingId) == "User",
            "Failed audit migration changed the old schema or records.");
        Sql(database, "DROP TABLE AuditLog_v2;");
        database.Initialize();
        Assert(Count(database, "PRAGMA user_version;") == 5 && Count(database, "SELECT COUNT(*) FROM AuditLog;") == 3,
            "Audit migration lost records.");
        Assert((string)Sql(database, "SELECT ActorType FROM AuditLog WHERE AuditLogId=@id;", "@id", existingId) == "Staff" &&
            (string)Sql(database, "SELECT CreatedAt FROM AuditLog WHERE AuditLogId=@id;", "@id", existingId) == existingTime &&
            (string)Sql(database, "SELECT UserId FROM AuditLog WHERE AuditLogId=@id;", "@id", existingId) == userId,
            "Migration lost actor linkage, timestamp or log identity.");
        Assert(Count(database, "SELECT COUNT(*) FROM AuditLog WHERE ActorType IN ('Guest','System');") == 2, "Guest/System actors changed.");
    }
}
