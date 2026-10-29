using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using MySqlConnector;

namespace MusicBoxManagement.Wpf.Data
{
    public sealed class MigrationSnapshot
    {
        public string DirectoryPath { get; internal set; }
        public string DatabasePath => Path.Combine(DirectoryPath, "musicbox.db");
    }
    public sealed class MigrationTableResult
    {
        public string Table { get; internal set; }
        public int Rows { get; internal set; }
        public string Hash { get; internal set; }
    }
    public static class MySqlDataMigration
    {
        private static readonly string[] Tables = { "RoomTypes", "AspNetRoles", "Permission", "AspNetUsers", "AspNetUserRoles", "RolePermission", "Rooms", "Customers", "Reservations", "RoomSessions", "Services", "Orders", "OrderItems", "Invoices", "AuditLog" };
        private static readonly HashSet<string> Times = new HashSet<string> { "CreatedAt", "StartTime", "EndTime", "ActualStartTime", "ExpectedEndTime", "ActualEndTime", "PaidAt" };
        private static readonly HashSet<string> Money = new HashSet<string> { "RoomCharge", "ServiceCharge", "TotalAmount" };
        private static SQLiteConnection OpenSource(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Không tìm thấy nguồn SQLite.");
            var c = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = Path.GetFullPath(path), ReadOnly = true, Pooling = false, ForeignKeys = true }.ConnectionString);
            c.Open(); return c;
        }
        public static MigrationSnapshot Backup(string databasePath, string storageRoot, string backupParent)
        {
            if (!File.Exists(databasePath)) throw new FileNotFoundException("Không tìm thấy SQLite cần sao lưu.");
            var parent = Path.GetFullPath(backupParent); Directory.CreateDirectory(parent);
            var snapshot = new MigrationSnapshot { DirectoryPath = Path.Combine(parent, "MusicBox_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")) };
            Directory.CreateDirectory(snapshot.DirectoryPath);
            // A reserved writer lock blocks room/DB changes while a separate reader uses SQLite Backup API.
            using (var guard = new SqliteDatabase(databasePath).OpenConnection())
            using (var writeLock = SqliteDatabase.BeginWriteTransaction(guard))
            using (var source = OpenSource(databasePath))
            using (var destination = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = snapshot.DatabasePath, Pooling = false }.ConnectionString))
            {
                destination.Open(); source.BackupDatabase(destination, "main", "main", -1, null, 0);
                var content = Path.Combine(Path.GetFullPath(storageRoot), "Content");
                if (Directory.Exists(content)) CopyTree(content, Path.Combine(snapshot.DirectoryPath, "Content"));
            }
            using (var source = OpenSource(snapshot.DatabasePath))
            using (var q = source.CreateCommand())
            {
                q.CommandText = "PRAGMA user_version"; var version = Convert.ToInt32(q.ExecuteScalar());
                if (version < 1 || version > SqliteDatabase.CurrentSchemaVersion) throw new InvalidOperationException("Phiên bản SQLite không được hỗ trợ.");
                if (version == SqliteDatabase.CurrentSchemaVersion) ReadAll(source);
                else { q.CommandText = "PRAGMA integrity_check"; if ((string)q.ExecuteScalar() != "ok") throw new InvalidOperationException("Nguồn không đạt integrity_check."); }
            }
            WriteManifest(snapshot);
            return snapshot;
        }
        private static void CopyTree(string from, string to)
        {
            if ((File.GetAttributes(from) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Không sao chép thư mục liên kết trong Content.");
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Không sao chép file liên kết trong Content.");
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), false);
            }
            foreach (var dir in Directory.GetDirectories(from)) CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
        }
        private static Dictionary<string, DataTable> ReadAll(SQLiteConnection source)
        {
            using (var q = source.CreateCommand())
            {
                q.CommandText = "PRAGMA user_version";
                if (Convert.ToInt32(q.ExecuteScalar()) != SqliteDatabase.CurrentSchemaVersion) throw new InvalidOperationException("Nguồn phải là SQLite v8; không tự nâng dữ liệu trong công cụ nhập.");
                q.CommandText = "PRAGMA integrity_check";
                if ((string)q.ExecuteScalar() != "ok") throw new InvalidOperationException("SQLite không đạt integrity_check.");
                q.CommandText = "PRAGMA foreign_key_check";
                using (var r = q.ExecuteReader()) if (r.Read()) throw new InvalidOperationException("Nguồn có FK không hợp lệ.");
            }
            var result = new Dictionary<string, DataTable>();
            foreach (var table in Tables)
            {
                var data = new DataTable(table);
                using (var q = source.CreateCommand()) { q.CommandText = "SELECT * FROM [" + table + "]"; using (var r = q.ExecuteReader()) data.Load(r); }
                // All values become canonical strings for exact cross-provider comparison and insertion.
                var normalized = new DataTable(table);
                foreach (DataColumn column in data.Columns) normalized.Columns.Add(column.ColumnName, typeof(string));
                foreach (DataRow row in data.Rows)
                {
                    var values = data.Columns.Cast<DataColumn>().Select(c => Normalize(c.ColumnName, row[c])).Cast<object>().ToArray();
                    normalized.Rows.Add(values);
                }
                result.Add(table, normalized);
            }
            ValidateImages(result["Rooms"], Path.GetDirectoryName(source.FileName));
            ValidateBusiness(result);
            return result;
        }
        private static string Text(DataRow row, string column) => row[column] == DBNull.Value ? null : (string)row[column];
        private static void ValidateBusiness(Dictionary<string, DataTable> tables)
        {
            var users = tables["AspNetUsers"].Rows.Cast<DataRow>().ToArray();
            var memberships = tables["AspNetUserRoles"].Rows.Cast<DataRow>().ToArray();
            foreach (var user in users)
                if (memberships.Count(m => Text(m, "UserId") == Text(user, "Id")) != 1) throw new InvalidOperationException("Nguồn có user không thuộc đúng một role.");
            if (users.Length > 0 && !users.Any(u => Text(u, "IsActive") == "1" && memberships.Any(m => Text(m, "UserId") == Text(u, "Id") && Text(m, "RoleId") == "Admin")))
                throw new InvalidOperationException("Nguồn có tài khoản nhưng không còn Admin active.");
            foreach (DataRow role in tables["AspNetRoles"].Rows)
                if (Text(role, "Id") != Text(role, "Name") || !new[] { "Staff", "Manager", "Admin" }.Contains(Text(role, "Name"))) throw new InvalidOperationException("Nguồn có role không đúng ba vai trò cố định.");
            foreach (DataRow customer in tables["Customers"].Rows)
                if (Services.PhoneNumberNormalizer.Normalize(Text(customer, "PhoneNumber")) != Text(customer, "PhoneNumber")) throw new InvalidOperationException("SĐT nguồn chưa ở dạng chuẩn; không tự đổi/ghép CustomerId khi nhập.");
            var reservations = tables["Reservations"].Rows.Cast<DataRow>().ToDictionary(r => Text(r, "ReservationId"));
            var sessions = tables["RoomSessions"].Rows.Cast<DataRow>().ToArray();
            var invoices = tables["Invoices"].Rows.Cast<DataRow>().ToArray();
            var orders = tables["Orders"].Rows.Cast<DataRow>().ToArray();
            var items = tables["OrderItems"].Rows.Cast<DataRow>().ToArray();
            foreach (var session in sessions)
            {
                var id = Text(session, "RoomSessionId"); var completed = Text(session, "Status") == "Completed";
                var end = Text(session, "ActualEndTime"); var related = invoices.Where(i => Text(i, "RoomSessionId") == id).ToArray();
                if (completed ? (end == null || related.Length != 1) : (Text(session, "Status") != "Active" || end != null || related.Length != 0))
                    throw new InvalidOperationException("Nguồn lệch trạng thái phiên/ActualEnd/Invoice.");
                if (end != null && string.CompareOrdinal(end, Text(session, "ActualStartTime")) < 0) throw new InvalidOperationException("Nguồn có giờ trả trước giờ nhận.");
                var reservation = Text(session, "ReservationId");
                if (reservation != null && (Text(session, "ExpectedEndTime") == null || Text(reservations[reservation], "Status") != (completed ? "Completed" : "CheckedIn")))
                    throw new InvalidOperationException("Nguồn lệch phiên và booking nguồn.");
                if (reservation == null && Text(session, "ExpectedEndTime") != null) throw new InvalidOperationException("Walk-in nguồn có ExpectedEnd không hợp lệ.");
                if (!completed) continue;
                var invoice = related[0];
                if (Text(invoice, "PaidAt") != end || orders.Any(o => Text(o, "RoomSessionId") == id && Text(o, "Status") == "Pending")) throw new InvalidOperationException("Nguồn lệch PaidAt hoặc còn Pending sau checkout.");
                var room = decimal.Parse(Text(invoice, "RoomCharge"), CultureInfo.InvariantCulture); var service = decimal.Parse(Text(invoice, "ServiceCharge"), CultureInfo.InvariantCulture);
                if (room + service != decimal.Parse(Text(invoice, "TotalAmount"), CultureInfo.InvariantCulture)) throw new InvalidOperationException("Nguồn lệch tổng hóa đơn.");
                var served = new HashSet<string>(orders.Where(o => Text(o, "RoomSessionId") == id && Text(o, "Status") == "Completed").Select(o => Text(o, "OrderId")));
                var itemTotal = items.Where(i => served.Contains(Text(i, "OrderId"))).Sum(i => decimal.Parse(Text(i, "UnitPrice"), CultureInfo.InvariantCulture) * int.Parse(Text(i, "Quantity"), CultureInfo.InvariantCulture));
                if (itemTotal != service) throw new InvalidOperationException("Nguồn lệch tổng snapshot món đã phục vụ.");
            }
        }
        private static string Normalize(string column, object value)
        {
            if (value == null || value == DBNull.Value) return null;
            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (Times.Contains(column))
            {
                var offset = text.EndsWith("Z", StringComparison.OrdinalIgnoreCase) || (text.Length >= 6 && (text[text.Length - 6] == '+' || text[text.Length - 6] == '-') && text[text.Length - 3] == ':');
                DateTimeOffset time;
                if (!offset || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time))
                    throw new InvalidOperationException("Nguồn có timestamp thiếu offset hoặc không hợp lệ ở cột " + column + ".");
                return time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            }
            if (Money.Contains(column))
            {
                decimal money;
                if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out money) || money < 0 || money != decimal.Truncate(money))
                    throw new InvalidOperationException("Nguồn có tiền không hợp lệ ở cột " + column + ".");
                return money.ToString("0", CultureInfo.InvariantCulture);
            }
            return text;
        }
        private static void ValidateImages(DataTable rooms, string root)
        {
            foreach (DataRow room in rooms.Rows)
            {
                var relative = (string)room["ImageUrl"];
                const string prefix = "Content/uploads/rooms/"; Guid imageId;
                if (!relative.StartsWith(prefix, StringComparison.Ordinal) || !relative.EndsWith(".png", StringComparison.Ordinal)
                    || !Guid.TryParseExact(relative.Substring(prefix.Length, relative.Length - prefix.Length - 4), "N", out imageId))
                    throw new InvalidOperationException("Đường dẫn ảnh nguồn không hợp lệ.");
                var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                    throw new InvalidOperationException("Nguồn thiếu ảnh phòng; dừng nhập trước khi ghi MySQL.");
            }
        }
        private static string Digest(DataTable data)
        {
            var rows = new List<string>();
            foreach (DataRow row in data.Rows)
                rows.Add(string.Concat(data.Columns.Cast<DataColumn>().Select(c => row[c] == DBNull.Value || row[c] == null ? "N;" : "S" + Convert.ToString(row[c], CultureInfo.InvariantCulture).Length + ":" + Convert.ToString(row[c], CultureInfo.InvariantCulture))));
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", rows.OrderBy(r => r, StringComparer.Ordinal)))));
        }
        public static IReadOnlyList<MigrationTableResult> Import(string snapshotDirectory, MySqlDatabase target)
        {
            VerifyArchive(snapshotDirectory);
            var root = Path.GetFullPath(snapshotDirectory); Dictionary<string, DataTable> source;
            using (var c = OpenSource(Path.Combine(root, "musicbox.db"))) source = ReadAll(c);
            target.Initialize();
            using (var c = target.OpenConnection()) using (var tx = MySqlDatabase.BeginWriteTransaction(c))
            {
                foreach (var name in Tables.Except(new[] { "RoomTypes", "AspNetRoles", "Permission", "RolePermission" }))
                    using (var q = c.CreateCommand()) { q.Transaction = tx; q.CommandText = "SELECT COUNT(*) FROM `" + name + "`"; if (Convert.ToInt64(q.ExecuteScalar()) != 0) throw new InvalidOperationException("Đích đã có dữ liệu nghiệp vụ; không ghi đè hoặc tự trộn ID."); }
                foreach (var name in new[] { "RolePermission", "RoomTypes", "AspNetRoles", "Permission" })
                    using (var q = c.CreateCommand()) { q.Transaction = tx; q.CommandText = "DELETE FROM `" + name + "`"; q.ExecuteNonQuery(); }
                foreach (var name in Tables)
                {
                    var data = source[name]; var columns = data.Columns.Cast<DataColumn>().ToArray();
                    using (var q = c.CreateCommand())
                    {
                        q.Transaction = tx; q.CommandText = "INSERT INTO `" + name + "` (" + string.Join(",", columns.Select(x => "`" + x.ColumnName + "`")) + ") VALUES (" + string.Join(",", columns.Select((x, i) => "@p" + i)) + ")";
                        foreach (DataRow row in data.Rows)
                        {
                            q.Parameters.Clear(); for (var i = 0; i < columns.Length; i++) q.Parameters.AddWithValue("@p" + i, row[i] == DBNull.Value ? (object)DBNull.Value : row[i]);
                            if (q.ExecuteNonQuery() != 1) throw new InvalidOperationException("Không nhập đủ dòng.");
                        }
                    }
                }
                var verified = VerifyInside(source, c, tx); tx.Commit(); return verified;
            }
        }
        public static IReadOnlyList<MigrationTableResult> Verify(string snapshotDirectory, MySqlDatabase target)
        {
            VerifyArchive(snapshotDirectory);
            Dictionary<string, DataTable> source; using (var s = OpenSource(Path.Combine(snapshotDirectory, "musicbox.db"))) source = ReadAll(s);
            using (var c = target.OpenConnection()) using (var tx = MySqlDatabase.BeginReadTransaction(c)) return VerifyInside(source, c, tx);
        }
        private static IReadOnlyList<MigrationTableResult> VerifyInside(Dictionary<string, DataTable> source, MySqlConnection c, MySqlTransaction tx)
        {
            var results = new List<MigrationTableResult>();
            foreach (var name in Tables)
            {
                var original = source[name]; var actual = original.Clone();
                using (var q = c.CreateCommand())
                {
                    q.Transaction = tx; q.CommandText = "SELECT " + string.Join(",", original.Columns.Cast<DataColumn>().Select(x => "`" + x.ColumnName + "`")) + " FROM `" + name + "`";
                    using (var r = q.ExecuteReader()) while (r.Read()) actual.Rows.Add(original.Columns.Cast<DataColumn>().Select((x, i) => (object)Normalize(x.ColumnName, r.GetValue(i))).ToArray());
                }
                var hash = Digest(original);
                if (actual.Rows.Count != original.Rows.Count || Digest(actual) != hash) throw new InvalidOperationException("Đối chiếu không khớp bảng " + name + "; transaction chưa được commit.");
                results.Add(new MigrationTableResult { Table = name, Rows = actual.Rows.Count, Hash = hash });
            }
            return results.AsReadOnly();
        }
        private static void WriteManifest(MigrationSnapshot snapshot)
        {
            int version;
            using (var c = OpenSource(snapshot.DatabasePath)) using (var q = c.CreateCommand()) { q.CommandText = "PRAGMA user_version"; version = Convert.ToInt32(q.ExecuteScalar()); }
            using (var writer = XmlWriter.Create(Path.Combine(snapshot.DirectoryPath, "manifest.xml"), new XmlWriterSettings { Indent = true }))
            {
                writer.WriteStartElement("MusicBoxBackup"); writer.WriteAttributeString("sqliteVersion", version.ToString(CultureInfo.InvariantCulture)); writer.WriteAttributeString("createdUtc", DateTimeOffset.UtcNow.ToString("O"));
                foreach (var file in Directory.GetFiles(snapshot.DirectoryPath, "*", SearchOption.AllDirectories).Where(f => Path.GetFileName(f) != "manifest.xml").OrderBy(f => f, StringComparer.Ordinal))
                {
                    using (var stream = File.OpenRead(file)) using (var sha = SHA256.Create())
                    { writer.WriteStartElement("File"); writer.WriteAttributeString("path", file.Substring(snapshot.DirectoryPath.Length + 1)); writer.WriteAttributeString("sha256", Convert.ToBase64String(sha.ComputeHash(stream))); writer.WriteEndElement(); }
                }
                writer.WriteEndElement();
            }
        }
        public static void VerifyArchive(string directory)
        {
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(Path.Combine(root, "manifest.xml"), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) document.Load(reader);
            int version;
            if (document.DocumentElement == null || document.DocumentElement.Name != "MusicBoxBackup" || !int.TryParse(document.DocumentElement.GetAttribute("sqliteVersion"), out version) || version < 1 || version > 8
                || document.SelectNodes("/MusicBoxBackup/File[@path='musicbox.db']").Count != 1) throw new InvalidOperationException("Manifest không có database/phiên bản bắt buộc.");
            foreach (XmlElement file in document.SelectNodes("/MusicBoxBackup/File"))
            {
                var path = Path.GetFullPath(Path.Combine(root, file.GetAttribute("path")));
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) throw new InvalidOperationException("Manifest có đường dẫn không hợp lệ hoặc thiếu file.");
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Không đọc file liên kết trong backup.");
                using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                    if (Convert.ToBase64String(sha.ComputeHash(stream)) != file.GetAttribute("sha256")) throw new InvalidOperationException("Backup đã thay đổi; hash không khớp.");
            }
        }
        public static void InstallContent(string snapshotDirectory, string destinationRoot)
        {
            VerifyArchive(snapshotDirectory);
            var source = Path.Combine(Path.GetFullPath(snapshotDirectory), "Content");
            var destination = Path.Combine(Path.GetFullPath(destinationRoot), "Content");
            if (Directory.Exists(destination)) throw new InvalidOperationException("Đích Content đã tồn tại; không ghi đè ảnh đang dùng.");
            if (Directory.Exists(source)) CopyTree(source, destination);
            else Directory.CreateDirectory(destination);
        }
        public static MigrationSnapshot PrepareV8Snapshot(string archivedDirectory)
        {
            VerifyArchive(archivedDirectory);
            var archived = Path.GetFullPath(archivedDirectory);
            var result = new MigrationSnapshot { DirectoryPath = Path.Combine(Path.GetDirectoryName(archived), "MusicBox_PreparedV8_" + Guid.NewGuid().ToString("N")) };
            Directory.CreateDirectory(result.DirectoryPath);
            File.Copy(Path.Combine(archived, "musicbox.db"), result.DatabasePath, false);
            var content = Path.Combine(archived, "Content");
            if (Directory.Exists(content)) CopyTree(content, Path.Combine(result.DirectoryPath, "Content"));
            new SqliteDatabase(result.DatabasePath).Initialize();
            using (var c = OpenSource(result.DatabasePath)) ReadAll(c);
            WriteManifest(result); VerifyArchive(archived); return result;
        }
        public static string RestoreSqliteCopy(string snapshotDirectory, string destinationRoot)
        {
            VerifyArchive(snapshotDirectory);
            var root = Path.GetFullPath(destinationRoot);
            if (Directory.Exists(root) || File.Exists(root)) throw new InvalidOperationException("Đích khôi phục đã tồn tại; không ghi đè dữ liệu.");
            Directory.CreateDirectory(root);
            var database = Path.Combine(root, "musicbox.db");
            File.Copy(Path.Combine(snapshotDirectory, "musicbox.db"), database, false);
            var content = Path.Combine(snapshotDirectory, "Content");
            if (Directory.Exists(content)) CopyTree(content, Path.Combine(root, "Content"));
            VerifyArchive(snapshotDirectory); return database;
        }
    }
}
