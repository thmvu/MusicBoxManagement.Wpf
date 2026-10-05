using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using MusicBoxManagement.Wpf.Data;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class NoShowService
    {
        private readonly SqliteDatabase database;
        private readonly IClock clock;
        public NoShowService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public NoShowService(SqliteDatabase database, IClock clock)
        { this.database = database ?? throw new ArgumentNullException(nameof(database)); this.clock = clock ?? throw new ArgumentNullException(nameof(clock)); }
        public int ProcessExpired()
        {
            using (var connection = database.OpenConnection())
            using (var transaction = SqliteDatabase.BeginWriteTransaction(connection))
            {
                var count = ProcessExpiredAt(connection, transaction, clock.UtcNow.ToUniversalTime());
                transaction.Commit(); return count;
            }
        }
        internal static int ProcessExpiredAt(SQLiteConnection connection, SQLiteTransaction transaction, DateTimeOffset now)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"SELECT ReservationId FROM Reservations WHERE Status='Confirmed' AND StartTime<=@cutoff
AND NOT EXISTS(SELECT 1 FROM RoomSessions WHERE RoomSessions.ReservationId=Reservations.ReservationId) ORDER BY ReservationId;";
                command.Parameters.AddWithValue("@cutoff", now.AddMinutes(-15).ToString("O", CultureInfo.InvariantCulture));
                var ids = new List<int>();
                using (var reader = command.ExecuteReader()) while (reader.Read()) ids.Add(reader.GetInt32(0));
                command.CommandText = "UPDATE Reservations SET Status='NoShow' WHERE ReservationId=@id AND Status='Confirmed';";
                command.Parameters.AddWithValue("@id", 0);
                foreach (var id in ids)
                {
                    command.Parameters["@id"].Value = id; command.ExecuteNonQuery();
                    AuditService.WriteSystem(connection, transaction, "Reservation.NoShow", "Reservation", id.ToString(CultureInfo.InvariantCulture),
                        "Khách chưa nhận phòng trước hạn 15 phút.", now);
                }
                return ids.Count;
            }
        }
    }
}
