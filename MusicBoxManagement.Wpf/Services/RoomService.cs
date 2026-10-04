using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class RoomService
    {
        private readonly SqliteDatabase database;
        private readonly PermissionService permissions;
        private readonly IClock clock;
        public RoomService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public RoomService(SqliteDatabase database, IClock clock)
        {
            this.database = database;
            permissions = new PermissionService(database);
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public List<Room> ListForManagement(LoginSession session)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            using (var command = connection.CreateCommand())
            {
                permissions.Demand(session, "Room.Manage", connection, transaction);
                command.Transaction = transaction;
                command.CommandText = @"SELECT r.RoomId, r.RoomCode, r.RoomTypeId, t.Name, r.Name,
r.ImageUrl, r.Description, r.IsActive, r.InactiveReason, r.CreatedAt
FROM Rooms r JOIN RoomTypes t ON t.RoomTypeId=r.RoomTypeId ORDER BY r.RoomCode;";
                var items = new List<Room>();
                using (var reader = command.ExecuteReader())
                    while (reader.Read()) items.Add(new Room {
                        RoomId = reader.GetInt32(0), RoomCode = reader.GetString(1), RoomTypeId = reader.GetInt32(2),
                        RoomTypeName = reader.GetString(3), Name = reader.GetString(4), ImageUrl = reader.GetString(5),
                        Description = reader.IsDBNull(6) ? null : reader.GetString(6), IsActive = reader.GetInt32(7) == 1,
                        InactiveReason = reader.IsDBNull(8) ? null : reader.GetString(8), CreatedAt = reader.GetString(9)
                    });
                return items;
            }
        }

        public int Create(LoginSession session, RoomCreate input)
        {
            permissions.Demand(session, "Room.Manage");
            if (input == null) throw new ArgumentException("Cần nhập thông tin phòng.");
            var code = (input.RoomCode ?? "").Trim();
            var name = (input.Name ?? "").Trim();
            var typeId = input.RoomTypeId;
            var description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
            if (code.Length < 1 || code.Length > 50) throw new ArgumentException("Mã phòng từ 1–50 ký tự.");
            if (name.Length < 1 || name.Length > 100) throw new ArgumentException("Tên phòng từ 1–100 ký tự.");
            if (description != null && description.Length > 2000) throw new ArgumentException("Mô tả tối đa 2000 ký tự.");
            var png = RoomImages.ReadPng(input.ImageFilePath); // No write transaction while decoding.
            var imageUrl = "Content/uploads/rooms/" + Guid.NewGuid().ToString("N") + ".png";
            var imagePath = GetImagePath(imageUrl);
            var ownsFile = false;
            var committed = false;
            try
            {
                using (var connection = database.OpenConnection())
                using (var transaction = SqliteDatabase.BeginWriteTransaction(connection))
                using (var command = connection.CreateCommand())
                {
                    permissions.Demand(session, "Room.Manage", connection, transaction);
                    command.Transaction = transaction;
                    command.CommandText = "SELECT COUNT(*) FROM RoomTypes WHERE RoomTypeId=@type;";
                    command.Parameters.AddWithValue("@type", typeId);
                    if (Convert.ToInt32(command.ExecuteScalar()) != 1) throw new ArgumentException("Cần chọn loại phòng hợp lệ.");
                    command.CommandText = "SELECT COUNT(*) FROM Rooms WHERE RoomCode=@code;";
                    command.Parameters.AddWithValue("@code", code);
                    if (Convert.ToInt32(command.ExecuteScalar()) != 0) throw new ArgumentException("Mã phòng đã tồn tại. Hãy chọn mã khác.");
                    command.CommandText = @"INSERT INTO Rooms
(RoomCode, RoomTypeId, Name, ImageUrl, Description, IsActive, InactiveReason, CreatedAt)
VALUES (@code, @type, @name, @image, @description, 1, NULL, @now);
SELECT last_insert_rowid();";
                    command.Parameters.AddWithValue("@name", name);
                    command.Parameters.AddWithValue("@image", imageUrl);
                    command.Parameters.AddWithValue("@description", (object)description ?? DBNull.Value);
                    command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                    var id = Convert.ToInt32(command.ExecuteScalar());
                    Directory.CreateDirectory(Path.GetDirectoryName(imagePath));
                    using (var stream = new FileStream(imagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        ownsFile = true;
                        stream.Write(png, 0, png.Length);
                        stream.Flush(true);
                    }
                    AuditService.WriteStaff(connection, transaction, session.UserId, "Room.Create", "Room",
                        id.ToString(CultureInfo.InvariantCulture), "Thêm phòng " + code + ".");
                    transaction.Commit();
                    committed = true;
                    return id;
                }
            }
            catch
            {
                if (ownsFile && !committed)
                {
                    try { File.Delete(imagePath); }
                    catch (IOException error) { System.Diagnostics.Trace.TraceError("Không dọn được ảnh phòng: " + error.Message); }
                    catch (UnauthorizedAccessException error) { System.Diagnostics.Trace.TraceError("Không dọn được ảnh phòng: " + error.Message); }
                }
                throw;
            }
        }

        public Room GetForEdit(LoginSession session, int roomId)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                permissions.Demand(session, "Room.Manage", connection, transaction);
                return Find(connection, transaction, roomId);
            }
        }

        public void Update(LoginSession session, Room original, RoomEdit input)
        {
            permissions.Demand(session, "Room.Manage");
            if (original == null || input == null) throw new ArgumentException("Cần chọn phòng và nhập thông tin sửa.");
            var name = (input.Name ?? "").Trim();
            var description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
            if (name.Length < 1 || name.Length > 100) throw new ArgumentException("Tên phòng từ 1–100 ký tự.");
            if (description != null && description.Length > 2000) throw new ArgumentException("Mô tả tối đa 2000 ký tự.");
            var png = string.IsNullOrWhiteSpace(input.ReplacementImageFilePath) ? null : RoomImages.ReadPng(input.ReplacementImageFilePath);
            var newUrl = png == null ? null : "Content/uploads/rooms/" + Guid.NewGuid().ToString("N") + ".png";
            var newPath = newUrl == null ? null : GetImagePath(newUrl);
            var ownsFile = false;
            var committed = false;
            using (var connection = database.OpenConnection())
            using (var transaction = SqliteDatabase.BeginWriteTransaction(connection))
            {
                try
                {
                    permissions.Demand(session, "Room.Manage", connection, transaction);
                    var current = Find(connection, transaction, original.RoomId);
                    if (current.RoomCode != original.RoomCode || current.RoomTypeId != original.RoomTypeId ||
                        current.Name != original.Name || current.Description != original.Description || current.ImageUrl != original.ImageUrl ||
                        current.IsActive != original.IsActive || current.InactiveReason != original.InactiveReason || current.CreatedAt != original.CreatedAt)
                        throw new InvalidOperationException("Phòng đã thay đổi. Hãy đóng form, làm mới danh sách rồi mở lại để sửa.");
                    var typeId = input.RoomTypeId ?? current.RoomTypeId;
                    var active = input.IsActive ?? current.IsActive;
                    var reason = input.IsActive.HasValue ? (active ? null : (input.InactiveReason ?? "").Trim()) : current.InactiveReason;
                    if (!active && string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Khóa phòng cần nhập lý do.");
                    if (reason != null && reason.Length > 1000) throw new ArgumentException("Lý do khóa tối đa 1000 ký tự.");
                    using (var check = connection.CreateCommand())
                    {
                        check.Transaction = transaction;
                        check.CommandText = "SELECT COUNT(*) FROM RoomTypes WHERE RoomTypeId=@type;";
                        check.Parameters.AddWithValue("@type", typeId);
                        if (Convert.ToInt32(check.ExecuteScalar()) != 1) throw new ArgumentException("Cần chọn loại phòng hợp lệ.");
                        check.Parameters.AddWithValue("@room", current.RoomId);
                        if (typeId != current.RoomTypeId || (current.IsActive && !active))
                        {
                            check.CommandText = "SELECT COUNT(*) FROM RoomSessions WHERE RoomId=@room AND Status='Active';";
                            if (Convert.ToInt32(check.ExecuteScalar()) > 0) throw new InvalidOperationException("Phòng còn phiên đang sử dụng; hãy checkout trước khi đổi loại hoặc khóa.");
                        }
                        if (current.IsActive && !active)
                        {
                            // Fresh time after acquiring the writer; expired Confirmed does not block even when NoShow worker is late.
                            var cutoff = clock.UtcNow.ToUniversalTime().AddMinutes(-15).ToString("O", CultureInfo.InvariantCulture);
                            check.CommandText = "SELECT COUNT(*) FROM Reservations WHERE RoomId=@room AND Status='Confirmed' AND StartTime>@cutoff;";
                            check.Parameters.AddWithValue("@cutoff", cutoff);
                            if (Convert.ToInt32(check.ExecuteScalar()) > 0) throw new InvalidOperationException("Phòng còn booking Confirmed có hiệu lực; hãy xử lý booking trước khi khóa.");
                        }
                    }
                    if (newUrl == null && name == current.Name && description == current.Description && typeId == current.RoomTypeId &&
                        active == current.IsActive && reason == current.InactiveReason) return;
                    if (png != null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(newPath));
                        using (var stream = new FileStream(newPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            ownsFile = true;
                            stream.Write(png, 0, png.Length);
                            stream.Flush(true);
                        }
                    }
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = "UPDATE Rooms SET Name=@name, Description=@description, ImageUrl=@image, RoomTypeId=@type, IsActive=@active, InactiveReason=@reason WHERE RoomId=@id;";
                        command.Parameters.AddWithValue("@type", typeId);
                        command.Parameters.AddWithValue("@active", active ? 1 : 0);
                        command.Parameters.AddWithValue("@reason", (object)reason ?? DBNull.Value);
                        command.Parameters.AddWithValue("@name", name);
                        command.Parameters.AddWithValue("@description", (object)description ?? DBNull.Value);
                        command.Parameters.AddWithValue("@image", newUrl ?? current.ImageUrl);
                        command.Parameters.AddWithValue("@id", current.RoomId);
                        command.ExecuteNonQuery();
                    }
                    var action = active != current.IsActive ? (active ? "Room.Unlock" : "Room.Lock") : "Room.Update";
                    AuditService.WriteStaff(connection, transaction, session.UserId, action, "Room",
                        current.RoomId.ToString(CultureInfo.InvariantCulture), "Cập nhật phòng " + current.RoomCode + (active ? "." : ", lý do khóa: " + reason + ".") + (png == null ? "" : " Thay ảnh."));
                    transaction.Commit();
                    committed = true;
                    // Retain the old file for readers/backups. Only ImageUrl's current image is shown.
                }
                catch
                {
                    if (ownsFile && !committed)
                    {
                        try { File.Delete(newPath); }
                        catch (IOException error) { System.Diagnostics.Trace.TraceError("Không dọn được ảnh phòng: " + error.Message); }
                        catch (UnauthorizedAccessException error) { System.Diagnostics.Trace.TraceError("Không dọn được ảnh phòng: " + error.Message); }
                    }
                    throw;
                }
            }
        }

        private static Room Find(SQLiteConnection connection, SQLiteTransaction transaction, int id)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"SELECT r.RoomId, r.RoomCode, r.RoomTypeId, t.Name, r.Name,
r.ImageUrl, r.Description, r.IsActive, r.InactiveReason, r.CreatedAt
FROM Rooms r JOIN RoomTypes t ON t.RoomTypeId=r.RoomTypeId WHERE r.RoomId=@id;";
                command.Parameters.AddWithValue("@id", id);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) throw new InvalidOperationException("Không tìm thấy phòng đã chọn.");
                    return new Room { RoomId = reader.GetInt32(0), RoomCode = reader.GetString(1), RoomTypeId = reader.GetInt32(2),
                        RoomTypeName = reader.GetString(3), Name = reader.GetString(4), ImageUrl = reader.GetString(5),
                        Description = reader.IsDBNull(6) ? null : reader.GetString(6), IsActive = reader.GetInt32(7) == 1,
                        InactiveReason = reader.IsDBNull(8) ? null : reader.GetString(8), CreatedAt = reader.GetString(9) };
                }
            }
        }

        public string GetImagePath(string imageUrl)
        {
            const string prefix = "Content/uploads/rooms/";
            Guid id;
            if (imageUrl == null || !imageUrl.StartsWith(prefix, StringComparison.Ordinal) ||
                !imageUrl.EndsWith(".png", StringComparison.Ordinal) ||
                !Guid.TryParseExact(imageUrl.Substring(prefix.Length, imageUrl.Length - prefix.Length - 4), "N", out id))
                throw new ArgumentException("Đường dẫn ảnh phòng không hợp lệ.");
            return Path.Combine(Path.GetDirectoryName(database.FilePath), imageUrl.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
