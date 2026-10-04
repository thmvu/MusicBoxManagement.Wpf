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
            Sql(database, @"DROP TABLE Rooms; PRAGMA user_version=3;
CREATE TRIGGER Rooms_ImmutableCode BEFORE UPDATE ON RoomTypes BEGIN SELECT RAISE(ABORT,'fixture'); END;");
            Reject<SQLiteException>(() => database.Initialize(), "Migration conflict ignored.");
            Assert(Count(database, "PRAGMA user_version;") == 3 &&
                Count(database, "SELECT COUNT(*) FROM sqlite_master WHERE name='Rooms';") == 0, "Migration did not roll back schema/version.");
            Sql(database, "DROP TRIGGER Rooms_ImmutableCode;");
            database.Initialize();
            Assert(Count(database, "PRAGMA user_version;") == 4 && service.ListForManagement(admin).Count == 0, "Migration/empty list incorrect.");
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
            Console.WriteLine("PASS Rooms: v3 migration/rollback, preservation, RBAC, mandatory images, PNG/JPEG/WebP decode, corrupt/oversize rejection, immutable code/FK/reason constraints, audit/filesystem rollback and concurrent uniqueness.");
        }
        finally
        {
            // This entire directory belongs to this fixture, never the live app.
            Directory.Delete(root, true);
        }
    }
}
