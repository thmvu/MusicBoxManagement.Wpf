using System;
using System.Data;
using System.Data.SQLite;
using System.IO;

namespace MusicBoxManagement.Wpf.Data
{
    public sealed class SqliteDatabase
    {
        public string FilePath { get; }

        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MusicBoxManagement.Wpf", "musicbox.db");

        public SqliteDatabase(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Cần đường dẫn database.", nameof(filePath));
            FilePath = Path.GetFullPath(filePath);
        }

        public SQLiteConnection OpenConnection()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var builder = new SQLiteConnectionStringBuilder
            {
                DataSource = FilePath,
                Version = 3,
                ForeignKeys = true,
                DefaultTimeout = 5,
                Pooling = false
            };
            var connection = new SQLiteConnection(builder.ConnectionString);
            try
            {
                connection.Open();
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        // System.Data.SQLite maps Serializable to BEGIN IMMEDIATE: acquire the
        // writer before reading data used to validate a business operation.
        // The caller owns disposal; disposing without Commit rolls back.
        public static SQLiteTransaction BeginWriteTransaction(SQLiteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            return connection.BeginTransaction(IsolationLevel.Serializable);
        }

        public void Initialize()
        {
            using (var connection = OpenConnection())
            using (var transaction = BeginWriteTransaction(connection))
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "PRAGMA user_version;";
                var version = Convert.ToInt32(command.ExecuteScalar());
                if (version > 2) throw new InvalidOperationException("Database thuộc phiên bản ứng dụng mới hơn.");
                if (version == 0)
                {
                    command.CommandText = @"
CREATE TABLE RoomTypes (
    RoomTypeId INTEGER PRIMARY KEY,
    Code TEXT NOT NULL UNIQUE CHECK (Code IN ('STANDARD', 'VIP')),
    Name TEXT NOT NULL CHECK (length(trim(Name)) BETWEEN 1 AND 100),
    Capacity INTEGER NOT NULL CHECK (typeof(Capacity) = 'integer' AND Capacity > 0),
    PricePerHour INTEGER NOT NULL CHECK (typeof(PricePerHour) = 'integer' AND PricePerHour > 0),
    Amenities TEXT NOT NULL CHECK (length(trim(Amenities)) BETWEEN 1 AND 1000),
    Description TEXT NULL CHECK (Description IS NULL OR length(Description) <= 2000)
);
PRAGMA user_version = 1;";
                    command.ExecuteNonQuery();
                    SeedRoomType(command, "STANDARD", "Standard", 4, 120000,
                        "TV, Điều hòa, 2 micro, Loa, Đèn LED");
                    SeedRoomType(command, "VIP", "VIP", 6, 200000,
                        "TV lớn, Điều hòa, 4 micro, Loa cao cấp, Đèn LED, Sofa");
                }
                if (version < 2) AuthenticationSchema.Create(command);
                transaction.Commit();
            }
        }

        private static void SeedRoomType(SQLiteCommand command, string code, string name,
            int capacity, long price, string amenities)
        {
            command.CommandText = @"INSERT INTO RoomTypes
(Code, Name, Capacity, PricePerHour, Amenities) VALUES (@code, @name, @capacity, @price, @amenities);";
            command.Parameters.Clear();
            command.Parameters.Add("@code", DbType.String).Value = code;
            command.Parameters.Add("@name", DbType.String).Value = name;
            command.Parameters.Add("@capacity", DbType.Int32).Value = capacity;
            command.Parameters.Add("@price", DbType.Int64).Value = price;
            command.Parameters.Add("@amenities", DbType.String).Value = amenities;
            command.ExecuteNonQuery();
        }
    }
}
