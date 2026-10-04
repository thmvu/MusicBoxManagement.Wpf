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
using MusicBoxManagement.Wpf.ViewModels;
using SkiaSharp;

public static class MusicBoxRoomChecks
{
    private const string Password = "Rooms-Test-2026!";
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action, string message) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception(message); }
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
    private static RoomCreate Input(string code, string image) { return new RoomCreate { RoomCode = code,
        Name = "Phòng thử nghiệm", RoomTypeId = 1, Description = "Tiếng Việt", ImageFilePath = image }; }
    private static void FixtureUser(SqliteDatabase database, string id, string role, string hash)
    {
        Sql(database, @"INSERT INTO AspNetUsers VALUES (@id,@id,upper(@id),@hash,@id,@id,1);
INSERT INTO AspNetUserRoles VALUES (@id,@role);", "@id", id, "@role", role, "@hash", hash);
    }
    private static void Image(string path, SKEncodedImageFormat format)
    {
        using (var bitmap = new SKBitmap(32, 24))
        {
            bitmap.Erase(new SKColor(130, 150, 140));
            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(format, 90)) File.WriteAllBytes(path, data.ToArray());
        }
    }

    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "MusicBoxRooms_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var database = new SqliteDatabase(Path.Combine(root, "test.db"));
        try
        {
            var auth = new AuthenticationService(database);
            var admin = auth.SetupAdminAsync("admin", "Quản trị thử nghiệm", Password).GetAwaiter().GetResult();
            var service = new RoomService(database);
            var hash = (string)Sql(database, "SELECT PasswordHash FROM AspNetUsers LIMIT 1;");
            FixtureUser(database, "manager", "Manager", hash);
            FixtureUser(database, "staff", "Staff", hash);
            var manager = auth.LoginAsync("manager", Password).GetAwaiter().GetResult();
            var staff = auth.LoginAsync("staff", Password).GetAwaiter().GetResult();
            Sql(database, "UPDATE RoomTypes SET Name='Standard giữ lại', PricePerHour=135000 WHERE RoomTypeId=1;");
            var users = Count(database, "SELECT COUNT(*) FROM AspNetUsers;");
            var audits = Count(database, "SELECT COUNT(*) FROM AuditLog;");
            Sql(database, @"DROP TABLE RoomSessions; DROP TABLE Reservations; DROP TABLE Customers; DROP TABLE Rooms; PRAGMA user_version=3;
CREATE TRIGGER Rooms_ImmutableCode BEFORE UPDATE ON RoomTypes BEGIN SELECT RAISE(ABORT,'fixture'); END;");
            Reject<SQLiteException>(() => database.Initialize(), "Migration conflict ignored.");
            Assert(Count(database, "PRAGMA user_version;") == 3 &&
                Count(database, "SELECT COUNT(*) FROM sqlite_master WHERE name='Rooms';") == 0, "Migration did not roll back schema/version.");
            Sql(database, "DROP TRIGGER Rooms_ImmutableCode;");
            database.Initialize();
            Assert(Count(database, "PRAGMA user_version;") == 5 && service.ListForManagement(admin).Count == 0, "Migration/empty list incorrect.");
            Assert(Count(database, "SELECT COUNT(*) FROM AspNetUsers;") == users &&
                Count(database, "SELECT COUNT(*) FROM AuditLog;") == audits &&
                Count(database, "SELECT PricePerHour FROM RoomTypes WHERE RoomTypeId=1;") == 135000, "Migration changed existing data.");
            Reject<UnauthorizedAccessException>(() => service.ListForManagement(null), "Guest read management data.");
            Reject<UnauthorizedAccessException>(() => service.ListForManagement(staff), "Default Staff read management data.");

            var png = Path.Combine(root, "input.png"); Image(png, SKEncodedImageFormat.Png);
            var input = Input("  P01  ", png); input.Name = "  Phòng tiếng Việt  "; input.Description = "  Mô tả phòng  ";
            var id = service.Create(manager, input);
            var saved = service.ListForManagement(admin).Single();
            Assert(saved.RoomId == id && saved.RoomCode == "P01" && saved.Name == "Phòng tiếng Việt" &&
                saved.RoomTypeName == "Standard giữ lại" && saved.Description == "Mô tả phòng" && saved.IsActive && saved.InactiveReason == null,
                "Room data/normalization incorrect.");
            Assert(DateTimeOffset.Parse(saved.CreatedAt, CultureInfo.InvariantCulture).Offset == TimeSpan.Zero, "CreatedAt is not UTC.");
            Assert(File.Exists(service.GetImagePath(saved.ImageUrl)) && Path.GetFileName(service.GetImagePath(saved.ImageUrl)) != "input.png",
                "Image not stored under a generated name.");
            Assert(Count(database, "SELECT COUNT(*) FROM AuditLog WHERE Action='Room.Create' AND ActorType='Staff' AND EntityId='" + id + "';") == 1,
                "Room creation audit missing.");
            Reject<ArgumentException>(() => service.GetImagePath("Content/uploads/rooms/../../outside.png"), "Unsafe image path accepted.");
            Reject<UnauthorizedAccessException>(() => service.Create(null, input), "Guest created a room.");
            Reject<UnauthorizedAccessException>(() => service.Create(staff, input), "Default Staff created a room.");
            Reject<ArgumentException>(() => service.Create(admin, Input("p01", png)), "Duplicate case variant accepted.");

            Action<Action<RoomCreate>> invalid = mutate => {
                var change = Input("INVALID", png); mutate(change);
                Reject<ArgumentException>(() => service.Create(admin, change), "Invalid room accepted.");
            };
            invalid(x => x.RoomCode = " "); invalid(x => x.RoomCode = new string('x', 51));
            invalid(x => x.Name = " "); invalid(x => x.Name = new string('x', 101));
            invalid(x => x.RoomTypeId = 999); invalid(x => x.Description = new string('x', 2001));
            invalid(x => x.ImageFilePath = null);
            var fake = Path.Combine(root, "fake.jpg"); File.WriteAllText(fake, "not an image");
            invalid(x => x.ImageFilePath = fake);
            var gif = Path.Combine(root, "input.gif");
            File.WriteAllBytes(gif, Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7"));
            invalid(x => x.ImageFilePath = gif);
            var truncated = Path.Combine(root, "truncated.png");
            var bytes = File.ReadAllBytes(png); File.WriteAllBytes(truncated, bytes.Take(bytes.Length / 2).ToArray());
            invalid(x => x.ImageFilePath = truncated);
            var large = Path.Combine(root, "large.png"); File.WriteAllBytes(large, new byte[5 * 1024 * 1024 + 1]);
            invalid(x => x.ImageFilePath = large);

            foreach (var format in new[] { SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Webp })
            {
                var path = Path.Combine(root, "input." + format); Image(path, format);
                var addedId = service.Create(admin, Input(format.ToString(), path));
                var room = service.ListForManagement(admin).Single(x => x.RoomId == addedId);
                using (var data = SKData.CreateCopy(File.ReadAllBytes(service.GetImagePath(room.ImageUrl))))
                using (var codec = SKCodec.Create(data)) Assert(codec.EncodedFormat == SKEncodedImageFormat.Png, "Image was not normalized for WPF.");
            }
            var folder = Path.GetDirectoryName(service.GetImagePath(saved.ImageUrl));
            var fileCount = Directory.GetFiles(folder).Length;
            var roomCount = service.ListForManagement(admin).Count;
            Sql(database, "CREATE TRIGGER FailRoomAudit BEFORE INSERT ON AuditLog WHEN NEW.Action='Room.Create' BEGIN SELECT RAISE(ABORT,'test'); END;");
            Reject<SQLiteException>(() => service.Create(admin, Input("ROLLBACK", png)), "Audit failure ignored.");
            Assert(service.ListForManagement(admin).Count == roomCount && Directory.GetFiles(folder).Length == fileCount,
                "Failed audit left a room or orphan image.");
            Sql(database, "DROP TRIGGER FailRoomAudit;");
            Reject<SQLiteException>(() => Sql(database, "UPDATE Rooms SET RoomCode='CHANGED' WHERE RoomId=@id;", "@id", id), "Immutable code changed.");
            Reject<SQLiteException>(() => Sql(database, "UPDATE Rooms SET RoomTypeId=999 WHERE RoomId=@id;", "@id", id), "Foreign key bypassed.");
            Reject<SQLiteException>(() => Sql(database, "UPDATE Rooms SET IsActive=0 WHERE RoomId=@id;", "@id", id), "Inactive room accepted without reason.");
            Reject<SQLiteException>(() => Sql(database, "UPDATE Rooms SET ImageUrl='' WHERE RoomId=@id;", "@id", id), "Empty image path accepted.");

            var editor = new RoomsViewModel(service, manager, new RoomTypeService(database).List()) {
                RoomCode = "REVOKED", Name = "Không được lưu", SelectedRoomType = new RoomTypeService(database).List()[0], ImageFilePath = png };
            Sql(database, "DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Room.Manage');");
            Assert(!editor.CreateAsync().GetAwaiter().GetResult() && editor.Status.Contains("quyền"), "Editor bypassed revoked permission.");
            editor.RefreshAsync().GetAwaiter().GetResult();
            Assert(editor.Rooms.Count == 0 && !editor.CanAddRoom, "Revoked permission exposed list or enabled creation.");
            database.Initialize();
            Assert(!new PermissionService(database).HasPermission(manager, "Room.Manage"), "Initialize restored revoked permission.");
            Sql(database, "INSERT INTO RolePermission SELECT 'Staff', PermissionId FROM Permission WHERE Code='Room.Manage';");
            service.Create(staff, Input("STAFF", png));
            auth.Logout(staff);
            Reject<UnauthorizedAccessException>(() => service.Create(staff, Input("LOGOUT", png)), "Logged-out user created room.");

            using (var gate = new ManualResetEventSlim(false))
            {
                Func<Task<bool>> attempt = () => Task.Run(() => {
                    gate.Wait();
                    try { service.Create(admin, Input("RACE", png)); return true; }
                    catch (ArgumentException) { return false; }
                });
                var first = attempt(); var second = attempt(); gate.Set(); Task.WaitAll(first, second);
                Assert(first.Result != second.Result, "Concurrent duplicate creation did not have one success.");
            }
            Assert(Count(database, "SELECT COUNT(*) FROM Rooms WHERE RoomCode='RACE';") == 1 &&
                Count(database, "SELECT COUNT(*) FROM AuditLog WHERE Action='Room.Create' AND Description='Thêm phòng RACE.';") == 1,
                "Concurrent duplicate room/audit persisted.");
            Assert(Directory.GetFiles(folder).Length == service.ListForManagement(admin).Count, "Rejected creations left orphan images.");
            Assert(new RoomService(new SqliteDatabase(database.FilePath)).ListForManagement(admin).Count == roomCount + 2,
                "Reopening lost rooms.");
            // A filesystem failure must also roll back the already inserted room.
            var blockedDirectory = Path.Combine(root, "blocked"); Directory.CreateDirectory(blockedDirectory);
            var blockedDb = new SqliteDatabase(Path.Combine(blockedDirectory, "blocked.db"));
            var blockedAdmin = new AuthenticationService(blockedDb).SetupAdminAsync("admin", "Thử lỗi file", Password).GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(blockedDirectory, "Content"), "fixture blocking image directory");
            Reject<IOException>(() => new RoomService(blockedDb).Create(blockedAdmin, Input("DISKFAIL", png)), "Image write failure ignored.");
            Assert(Count(blockedDb, "SELECT COUNT(*) FROM Rooms;") == 0 &&
                Count(blockedDb, "SELECT COUNT(*) FROM AuditLog WHERE Action='Room.Create';") == 0, "Filesystem failure did not roll back creation.");
            Editing(database, service, auth, admin, manager, png, fake, large);
            UsageGuards(database, admin, png);
            Console.WriteLine("PASS Rooms: migration/preservation, creation/editing/images/RBAC, rollback/concurrency, active-session guards, confirmed/grace-boundary locks, unlock and schema constraints.");
        }
        finally
        {
            // This entire directory belongs to this fixture, never the live app.
            Directory.Delete(root, true);
        }
    }

    private static void Editing(SqliteDatabase database, RoomService service, AuthenticationService auth,
        LoginSession admin, LoginSession manager, string png, string fake, string large)
    {
        var room = service.ListForManagement(admin).First();
        var id = room.RoomId;
        var oldUrl = room.ImageUrl;
        var originalTime = room.CreatedAt;
        var imageFolder = Path.GetDirectoryName(service.GetImagePath(oldUrl));
        Reject<UnauthorizedAccessException>(() => service.GetForEdit(null, id), "Guest opened room editor.");
        Reject<UnauthorizedAccessException>(() => service.Update(null, room, new RoomEdit { Name = "Guest" }), "Guest saved room editor.");
        Reject<InvalidOperationException>(() => service.GetForEdit(admin, 999), "Missing room opened.");
        Reject<InvalidOperationException>(() => service.Update(admin, new Room { RoomId = 999 }, new RoomEdit { Name = "Missing" }), "Missing room saved.");
        Sql(database, "INSERT INTO RolePermission SELECT 'Manager', PermissionId FROM Permission WHERE Code='Room.Manage';");
        var original = service.GetForEdit(manager, id);
        service.Update(manager, original, new RoomEdit { Name = "  Phòng đã sửa  ", Description = "  Mô tả mới  " });
        var saved = service.GetForEdit(admin, id);
        Assert(saved.Name == "Phòng đã sửa" && saved.Description == "Mô tả mới" && saved.ImageUrl == oldUrl && saved.RoomCode == room.RoomCode &&
            saved.RoomTypeId == room.RoomTypeId && saved.CreatedAt == originalTime && saved.IsActive == room.IsActive,
            "Edit did not normalize/preserve immutable fields/current image.");
        var auditCount = Count(database, "SELECT COUNT(*) FROM AuditLog WHERE Action='Room.Update';");
        service.Update(admin, saved, new RoomEdit { Name = saved.Name, Description = saved.Description });
        Assert(Count(database, "SELECT COUNT(*) FROM AuditLog WHERE Action='Room.Update';") == auditCount, "No-op save created audit.");
        Reject<InvalidOperationException>(() => service.Update(admin, original, new RoomEdit { Name = "Stale", ReplacementImageFilePath = png }), "Stale edit overwrote room.");
        foreach (var edit in new[] { new RoomEdit { Name = " " }, new RoomEdit { Name = new string('x', 101) },
            new RoomEdit { Name = saved.Name, Description = new string('x', 2001) },
            new RoomEdit { Name = saved.Name, ReplacementImageFilePath = fake }, new RoomEdit { Name = saved.Name, ReplacementImageFilePath = large } })
            Reject<ArgumentException>(() => service.Update(admin, saved, edit), "Invalid edit accepted.");

        foreach (var format in new[] { SKEncodedImageFormat.Png, SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Webp })
        {
            var path = Path.Combine(Path.GetDirectoryName(database.FilePath), "replacement." + format); Image(path, format);
            var before = service.GetForEdit(admin, id);
            service.Update(admin, before, new RoomEdit { Name = before.Name, Description = " ", ReplacementImageFilePath = path });
            saved = service.GetForEdit(admin, id);
            Assert(saved.ImageUrl != before.ImageUrl && saved.Description == null && File.Exists(service.GetImagePath(before.ImageUrl)) &&
                File.Exists(service.GetImagePath(saved.ImageUrl)), "Image replacement lost current/old file or blank description was not NULL.");
        }
        var beforeFailure = service.GetForEdit(admin, id);
        var fileCount = Directory.GetFiles(imageFolder).Length;
        auditCount = Count(database, "SELECT COUNT(*) FROM AuditLog WHERE Action='Room.Update';");
        Sql(database, "CREATE TRIGGER FailUpdateAudit BEFORE INSERT ON AuditLog WHEN NEW.Action='Room.Update' BEGIN SELECT RAISE(ABORT,'fixture'); END;");
        Reject<SQLiteException>(() => service.Update(admin, beforeFailure, new RoomEdit { Name = "Rollback", ReplacementImageFilePath = png }), "Update audit failure ignored.");
        saved = service.GetForEdit(admin, id);
        Assert(saved.Name == beforeFailure.Name && saved.ImageUrl == beforeFailure.ImageUrl && Directory.GetFiles(imageFolder).Length == fileCount &&
            Count(database, "SELECT COUNT(*) FROM AuditLog WHERE Action='Room.Update';") == auditCount, "Update rollback left data/new image/audit.");
        Sql(database, "DROP TRIGGER FailUpdateAudit;");
        var vm = new RoomEditViewModel(service, manager, saved) { Name = "Revoked" };
        Sql(database, "DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Room.Manage');");
        Assert(!vm.SaveAsync().GetAwaiter().GetResult() && vm.Status.Contains("quyền"), "Open editor bypassed revoked permission.");
        Reject<UnauthorizedAccessException>(() => service.GetForEdit(manager, id), "Revoked user opened room edit.");
        var staff = auth.LoginAsync("staff", Password).GetAwaiter().GetResult();
        var staffOriginal = service.GetForEdit(staff, id);
        service.Update(staff, staffOriginal, new RoomEdit { Name = "Staff được cấp quyền" });
        var logoutOriginal = service.GetForEdit(staff, id); auth.Logout(staff);
        Reject<UnauthorizedAccessException>(() => service.Update(staff, logoutOriginal, new RoomEdit { Name = "Logout" }), "Logout session edited room.");
        var firstOriginal = service.GetForEdit(admin, id);
        var secondOriginal = service.GetForEdit(admin, id);
        fileCount = Directory.GetFiles(imageFolder).Length;
        auditCount = Count(database, "SELECT COUNT(*) FROM AuditLog WHERE Action='Room.Update';");
        using (var gate = new ManualResetEventSlim(false))
        {
            Func<Room, string, Task<bool>> attempt = (snapshot, name) => Task.Run(() => {
                gate.Wait();
                try { service.Update(admin, snapshot, new RoomEdit { Name = name, ReplacementImageFilePath = png }); return true; }
                catch (InvalidOperationException) { return false; }
            });
            var first = attempt(firstOriginal, "Concurrent A"); var second = attempt(secondOriginal, "Concurrent B");
            gate.Set(); Task.WaitAll(first, second);
            Assert(first.Result != second.Result, "Concurrent stale edits did not have one success.");
        }
        Assert(Directory.GetFiles(imageFolder).Length == fileCount + 1 &&
            Count(database, "SELECT COUNT(*) FROM AuditLog WHERE Action='Room.Update';") == auditCount + 1, "Concurrent edits left extra image/audit.");
        var beforeLock = service.GetForEdit(admin, id);
        Sql(database, "UPDATE Rooms SET IsActive=0, InactiveReason='Fixture' WHERE RoomId=@id;", "@id", id);
        Reject<InvalidOperationException>(() => service.Update(admin, beforeLock, new RoomEdit { Name = "Stale state" }), "Changed room state bypassed stale check.");
        var inactive = service.GetForEdit(admin, id);
        service.Update(admin, inactive, new RoomEdit { Name = "Sửa tên phòng đã khóa" });
        var final = new RoomService(new SqliteDatabase(database.FilePath)).GetForEdit(admin, id);
        Assert(!final.IsActive && final.InactiveReason == "Fixture" && final.RoomCode == room.RoomCode && final.RoomTypeId == room.RoomTypeId &&
            final.CreatedAt == originalTime && final.Name == "Sửa tên phòng đã khóa", "Editing changed protected fields or persistence failed.");
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset Now;
        public DateTimeOffset UtcNow { get { return Now; } }
    }
    private static string Utc(DateTimeOffset time) { return time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture); }
    private static void UsageGuards(SqliteDatabase database, LoginSession admin, string png)
    {
        var clock = new FixedClock { Now = new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero) };
        var service = new RoomService(database, clock);
        var original = service.ListForManagement(admin).First(x => x.IsActive);
        var roomId = original.RoomId;
        var roomsBefore = Count(database, "SELECT COUNT(*) FROM Rooms;");
        var auditBefore = Count(database, "SELECT COUNT(*) FROM AuditLog;");
        // Reconstruct v4 and inject a failure after v5 has already created Customers.
        Sql(database, "DROP TABLE RoomSessions; DROP TABLE Reservations; DROP TABLE Customers; PRAGMA user_version=4; CREATE TABLE Reservations(Fixture TEXT);");
        Reject<SQLiteException>(() => database.Initialize(), "v5 migration conflict ignored.");
        Assert(Count(database, "PRAGMA user_version;") == 4 && Count(database, "SELECT COUNT(*) FROM sqlite_master WHERE name='Customers';") == 0,
            "Failed v5 migration left partial schema/version.");
        Sql(database, "DROP TABLE Reservations;"); database.Initialize();
        Assert(Count(database, "PRAGMA user_version;") == 5 && Count(database, "SELECT COUNT(*) FROM Rooms;") == roomsBefore &&
            Count(database, "SELECT COUNT(*) FROM AuditLog;") == auditBefore && service.GetForEdit(admin, roomId).ImageUrl == original.ImageUrl,
            "v5 migration lost rooms/audit/images.");
        Sql(database, "INSERT INTO Customers VALUES(1,'Khách thử','0901234567'); INSERT INTO Customers VALUES(2,'Khách hai','0901234568');");
        Reject<SQLiteException>(() => Sql(database, "INSERT INTO Customers VALUES(3,'Trùng','0901234567');"), "Duplicate phone accepted.");
        Reject<SQLiteException>(() => Sql(database, "INSERT INTO Customers VALUES(3,'Sai','+84901234567');"), "Unnormalized phone accepted.");
        Action<int,int> session = (targetRoom, customer) => Sql(database, @"INSERT INTO RoomSessions
(CustomerId,RoomId,ActualStartTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status)
VALUES(@customer,@room,@start,120000,'Fixture','STANDARD','Standard snapshot','Active');", "@customer", customer, "@room", targetRoom, "@start", Utc(clock.Now.AddMinutes(-5)));
        session(roomId, 1);
        Reject<SQLiteException>(() => session(roomId, 2), "Two Active sessions in one room accepted.");
        var otherId = service.ListForManagement(admin).First(x => x.RoomId != roomId).RoomId;
        Reject<SQLiteException>(() => session(otherId, 1), "Two Active sessions for one customer accepted.");
        Action lockRoom = () => {
            var current = service.GetForEdit(admin, roomId);
            service.Update(admin, current, new RoomEdit { Name = current.Name, Description = current.Description, IsActive = false, InactiveReason = "Thử khóa" });
        };
        Reject<InvalidOperationException>(lockRoom, "Active session did not block lock.");
        Reject<InvalidOperationException>(() => service.Update(admin, original, new RoomEdit { Name = original.Name, RoomTypeId = 2 }), "Active session did not block type change.");
        service.Update(admin, original, new RoomEdit { Name = "Tên mới khi đang dùng" });
        Assert((string)Sql(database, "SELECT RoomTypeNameSnapshot FROM RoomSessions;") == "Standard snapshot" &&
            Count(database, "SELECT HourlyRate FROM RoomSessions;") == 120000, "Catalog change modified session snapshot.");
        Sql(database, "UPDATE RoomSessions SET Status='Completed', ActualEndTime=@end;", "@end", Utc(clock.Now));
        var currentRoom = service.GetForEdit(admin, roomId);
        service.Update(admin, currentRoom, new RoomEdit { Name = currentRoom.Name, RoomTypeId = 2 });
        Assert(service.GetForEdit(admin, roomId).RoomTypeId == 2, "Completed session blocked type change.");
        Action<DateTimeOffset,string> booking = (start, status) => Sql(database, @"INSERT INTO Reservations
(CustomerId,RoomId,StartTime,EndTime,Status,CreatedAt) VALUES(1,@room,@start,@end,@status,@now);",
            "@room", roomId, "@start", Utc(start), "@end", Utc(start.AddHours(1)), "@status", status, "@now", Utc(clock.Now));
        booking(clock.Now.AddDays(1), "Confirmed");
        Reject<InvalidOperationException>(lockRoom, "Future booking did not block lock.");
        Sql(database, "DELETE FROM Reservations;");
        booking(clock.Now.AddMinutes(-15).AddTicks(1), "Confirmed");
        Reject<InvalidOperationException>(lockRoom, "Booking one tick before grace expiry did not block lock.");
        Sql(database, "DELETE FROM Reservations;"); booking(clock.Now.AddMinutes(-15), "Confirmed");
        var room = service.GetForEdit(admin, roomId);
        Reject<ArgumentException>(() => service.Update(admin, room, new RoomEdit { Name = room.Name, IsActive = false, InactiveReason = " " }), "Missing lock reason accepted.");
        Reject<ArgumentException>(() => service.Update(admin, room, new RoomEdit { Name = room.Name, IsActive = false, InactiveReason = new string('x',1001) }), "Oversize lock reason accepted.");
        Reject<ArgumentException>(() => service.Update(admin, room, new RoomEdit { Name = room.Name, RoomTypeId = 999 }), "Unknown type accepted.");
        lockRoom();
        Assert(!service.GetForEdit(admin, roomId).IsActive && Count(database, "SELECT COUNT(*) FROM AuditLog WHERE Action='Room.Lock';") == 1,
            "Exact grace expiry did not permit lock/audit.");
        room = service.GetForEdit(admin, roomId);
        service.Update(admin, room, new RoomEdit { Name = room.Name, IsActive = true, InactiveReason = "Ignored" });
        Assert(service.GetForEdit(admin, roomId).IsActive && service.GetForEdit(admin, roomId).InactiveReason == null, "Unlock did not clear reason.");
        Sql(database, "DELETE FROM Reservations;");
        foreach (var status in new[] { "Cancelled", "NoShow", "Completed", "CheckedIn" }) booking(clock.Now.AddDays(1), status);
        lockRoom(); // Non-Confirmed history does not hold the lock.
        room = service.GetForEdit(admin, roomId); service.Update(admin, room, new RoomEdit { Name = room.Name, IsActive = true });
        var files = Directory.GetFiles(Path.GetDirectoryName(service.GetImagePath(room.ImageUrl))).Length;
        Sql(database, "CREATE TRIGGER FailLockAudit BEFORE INSERT ON AuditLog WHEN NEW.Action='Room.Lock' BEGIN SELECT RAISE(ABORT,'fixture'); END;");
        room = service.GetForEdit(admin, roomId);
        Reject<SQLiteException>(() => service.Update(admin, room, new RoomEdit { Name = room.Name, IsActive = false, InactiveReason = "Rollback", ReplacementImageFilePath = png }), "Lock audit failure ignored.");
        Assert(service.GetForEdit(admin, roomId).IsActive && Directory.GetFiles(Path.GetDirectoryName(service.GetImagePath(room.ImageUrl))).Length == files,
            "Lock audit failure left inactive room/new image.");
        Sql(database, "DROP TRIGGER FailLockAudit;");
        Sql(database, "DELETE FROM Reservations;");
        var raceOriginal = service.GetForEdit(admin, roomId);
        using (var gate = new ManualResetEventSlim(false))
        {
            // Contract fixture for the later booking service: it must check IsActive inside the writer transaction.
            var bookingAttempt = Task.Run(() => {
                gate.Wait();
                using (var connection = database.OpenConnection())
                using (var transaction = SqliteDatabase.BeginWriteTransaction(connection))
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "SELECT IsActive FROM Rooms WHERE RoomId=@room;";
                    command.Parameters.AddWithValue("@room", roomId);
                    if (Convert.ToInt32(command.ExecuteScalar()) != 1) return false;
                    command.CommandText = "INSERT INTO Reservations(CustomerId,RoomId,StartTime,EndTime,Status,CreatedAt) VALUES(1,@room,@start,@end,'Confirmed',@now);";
                    command.Parameters.AddWithValue("@start", Utc(clock.Now.AddDays(1)));
                    command.Parameters.AddWithValue("@end", Utc(clock.Now.AddDays(1).AddHours(1)));
                    command.Parameters.AddWithValue("@now", Utc(clock.Now));
                    command.ExecuteNonQuery(); transaction.Commit(); return true;
                }
            });
            var lockAttempt = Task.Run(() => {
                gate.Wait();
                try { service.Update(admin, raceOriginal, new RoomEdit { Name = raceOriginal.Name, IsActive = false, InactiveReason = "Race" }); return true; }
                catch (InvalidOperationException) { return false; }
            });
            gate.Set(); Task.WaitAll(bookingAttempt, lockAttempt);
            Assert(bookingAttempt.Result != lockAttempt.Result, "Booking writer contract and lock both succeeded/failed.");
        }
    }
}
