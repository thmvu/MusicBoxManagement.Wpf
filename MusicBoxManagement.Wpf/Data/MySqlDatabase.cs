using System;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;
using MusicBoxManagement.Wpf.Services;
using MySqlConnector;

namespace MusicBoxManagement.Wpf.Data
{
    // MySQL version 1 mirrors the business schema of SQLite v8.
    // This foundation is not yet wired to the application's business services.
    public sealed class MySqlDatabase
    {
        public const int CurrentSchemaVersion = 1;
        public const string DefaultDatabaseName = "musicbox";
        public string DatabaseName { get; }

        public MySqlDatabase(string databaseName)
        {
            if (databaseName == null || !Regex.IsMatch(databaseName, "\\A[a-z][a-z0-9_]{0,47}\\z"))
                throw new ArgumentException("Tên database chỉ dùng chữ thường, số và dấu gạch dưới.", nameof(databaseName));
            DatabaseName = databaseName;
        }

        public MySqlConnection OpenConnection()
        {
            var connection = AivenDatabase.CreateConnection(DatabaseName);
            try
            {
                connection.Open();
                Configure(connection);
                return connection;
            }
            catch { connection.Dispose(); throw; }
        }

        // InnoDB protects each writer's rows/ranges; no application-wide write lock.
        // Read entities and take IClock only after beginning this transaction.
        public static MySqlTransaction BeginWriteTransaction(MySqlConnection connection)
        {
            return connection.BeginTransaction(IsolationLevel.Serializable);
        }

        // MySQL READ COMMITTED gives a new snapshot per SELECT, not per transaction.
        public static MySqlTransaction BeginReadTransaction(MySqlConnection connection)
        {
            return connection.BeginTransaction(IsolationLevel.RepeatableRead, true);
        }

        private static void Configure(MySqlConnection connection)
        {
            // Reject truncation/coercion; never silently weaken constraints.
            Execute(connection, "SET SESSION sql_mode='STRICT_ALL_TABLES,NO_ZERO_DATE,NO_ZERO_IN_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION'; SET SESSION time_zone='+00:00'; SET SESSION innodb_lock_wait_timeout=5;");
        }

        public void Initialize()
        {
            using (var connection = AivenDatabase.CreateConnection("defaultdb"))
            {
                connection.Open();
                Configure(connection);
                Execute(connection, "CREATE DATABASE IF NOT EXISTS `" + DatabaseName + "` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_bin;");
                connection.ChangeDatabase(DatabaseName);
                var lockName = "musicbox-schema:" + DatabaseName;
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT GET_LOCK(@name, 15);";
                    command.Parameters.AddWithValue("@name", lockName);
                    if (Convert.ToInt32(command.ExecuteScalar()) != 1)
                        throw new InvalidOperationException("Chưa lấy được khóa khởi tạo schema MySQL.");
                }
                try { InitializeLocked(connection); }
                finally
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT RELEASE_LOCK(@name);";
                        command.Parameters.AddWithValue("@name", lockName);
                        command.ExecuteScalar();
                    }
                }
            }
        }

        private static void InitializeLocked(MySqlConnection connection)
        {
            var hasVersion = Convert.ToInt32(Scalar(connection,
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='_MusicBoxSchemaVersion';")) != 0;
            if (!hasVersion && Convert.ToInt32(Scalar(connection,
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE();")) != 0)
                throw new InvalidOperationException("Database có bảng chưa được quản lý bởi Music Box; không tự thay schema.");

            Execute(connection, "CREATE TABLE IF NOT EXISTS _MusicBoxSchemaVersion (Id INT PRIMARY KEY CHECK(Id=1), Version INT NOT NULL CHECK(Version>=0)) ENGINE=InnoDB; INSERT IGNORE INTO _MusicBoxSchemaVersion(Id,Version) VALUES(1,0);");
            var version = Convert.ToInt32(Scalar(connection, "SELECT Version FROM _MusicBoxSchemaVersion WHERE Id=1;"));
            if (version > CurrentSchemaVersion)
                throw new InvalidOperationException("Database MySQL thuộc phiên bản ứng dụng mới hơn.");
            if (version == CurrentSchemaVersion) return;

            // MySQL DDL auto-commits. Version remains 0 until all DDL and seed succeed;
            // the schema lock and IF NOT EXISTS allow recovery after interrupted setup.
            using (var stream = typeof(MySqlDatabase).Assembly.GetManifestResourceStream(
                "MusicBoxManagement.Wpf.Data.MySqlSchema.sql"))
            {
                if (stream == null) throw new InvalidOperationException("Không tìm thấy tài nguyên schema MySQL.");
                using (var reader = new StreamReader(stream))
                    foreach (var statement in Regex.Split(reader.ReadToEnd(), @"(?m)^-- statement\s*$"))
                        if (!string.IsNullOrWhiteSpace(statement)) Execute(connection, statement);
            }
            CreateTrigger(connection, "Rooms_ImmutableCode", @"CREATE TRIGGER Rooms_ImmutableCode BEFORE UPDATE ON Rooms FOR EACH ROW
BEGIN IF BINARY NEW.RoomCode <> BINARY OLD.RoomCode THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='RoomCode is immutable'; END IF; END");
            CreateTrigger(connection, "Invoices_NoUpdate", "CREATE TRIGGER Invoices_NoUpdate BEFORE UPDATE ON Invoices FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Invoice is immutable'");
            CreateTrigger(connection, "Invoices_NoDelete", "CREATE TRIGGER Invoices_NoDelete BEFORE DELETE ON Invoices FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Invoice is immutable'");

            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"INSERT INTO RoomTypes(Code,Name,Capacity,PricePerHour,Amenities) VALUES
('STANDARD','Standard',4,120000,'TV, Điều hòa, 2 micro, Loa, Đèn LED'),
('VIP','VIP',6,200000,'TV lớn, Điều hòa, 4 micro, Loa cao cấp, Đèn LED, Sofa');
INSERT INTO AspNetRoles(Id,Name) VALUES ('Staff','Staff'),('Manager','Manager'),('Admin','Admin');";
                command.ExecuteNonQuery();
                foreach (var permission in PermissionCatalog.All)
                {
                    command.Parameters.Clear();
                    command.CommandText = "INSERT INTO Permission(Code,Name) VALUES(@code,@name);";
                    command.Parameters.AddWithValue("@code", permission.Key);
                    command.Parameters.AddWithValue("@name", permission.Value);
                    command.ExecuteNonQuery();
                }
                foreach (var role in new[] { "Staff", "Manager" })
                    foreach (var code in PermissionCatalog.Defaults(role))
                    {
                        command.Parameters.Clear();
                        command.CommandText = "INSERT INTO RolePermission(RoleId,PermissionId) SELECT @role,PermissionId FROM Permission WHERE Code=@code;";
                        command.Parameters.AddWithValue("@role", role);
                        command.Parameters.AddWithValue("@code", code);
                        command.ExecuteNonQuery();
                    }
                command.Parameters.Clear();
                command.CommandText = "UPDATE _MusicBoxSchemaVersion SET Version=1 WHERE Id=1;";
                command.ExecuteNonQuery();
                transaction.Commit();
            }
        }

        private static void CreateTrigger(MySqlConnection connection, string name, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM information_schema.triggers WHERE trigger_schema=DATABASE() AND trigger_name=@name;";
                command.Parameters.AddWithValue("@name", name);
                if (Convert.ToInt32(command.ExecuteScalar()) == 0) Execute(connection, sql);
            }
        }

        private static object Scalar(MySqlConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            { command.CommandText = sql; return command.ExecuteScalar(); }
        }

        private static void Execute(MySqlConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            { command.CommandText = sql; command.CommandTimeout = 30; command.ExecuteNonQuery(); }
        }
    }
}
