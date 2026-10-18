using System;
using System.Collections.Generic;
using System.Globalization;
using MySqlConnector;
using MusicBoxManagement.Wpf.Data;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class MySqlNoShowService
    {
        private readonly MySqlDatabase database;
        private readonly IClock clock;
        public MySqlNoShowService(MySqlDatabase database) : this(database, new SystemClock()) { }
        public MySqlNoShowService(MySqlDatabase database, IClock clock)
        { this.database = database ?? throw new ArgumentNullException(nameof(database)); this.clock = clock ?? throw new ArgumentNullException(nameof(clock)); }
        public int ProcessExpired()
        {
            try
            {
                using (var connection = database.OpenConnection())
                using (var transaction = MySqlDatabase.BeginWriteTransaction(connection))
                {
                    // No Room/Customer lock follows these locks. Take the clock only after
                    // any earlier state writer has released the Confirmed rows.
                    LockCandidates(connection, transaction);
                    var count = ProcessExpiredAt(connection, transaction, clock.UtcNow.ToUniversalTime());
                    transaction.Commit(); return count;
                }
            }
            catch (MySqlException error) when (error.Number == 1213 || error.Number == 1205)
            { throw new InvalidOperationException("Lịch đang được cập nhật. Hãy chạy lại xử lý NoShow.", error); }
        }
        internal static void LockCandidates(MySqlConnection connection, MySqlTransaction transaction)
        {
            if (transaction == null || transaction.Connection != connection || transaction.IsolationLevel != System.Data.IsolationLevel.Serializable)
                throw new ArgumentException("Cần cùng kết nối và transaction ghi Serializable.");
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT ReservationId FROM Reservations WHERE Status='Confirmed' ORDER BY ReservationId FOR UPDATE;";
                using (var reader = command.ExecuteReader()) while (reader.Read()) { }
            }
        }
        internal static int ProcessExpiredAt(MySqlConnection connection, MySqlTransaction transaction, DateTimeOffset now)
        {
            if (transaction == null || transaction.Connection != connection || transaction.IsolationLevel != System.Data.IsolationLevel.Serializable)
                throw new ArgumentException("Cần cùng kết nối và transaction ghi Serializable.");
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT ReservationId FROM Reservations WHERE Status='Confirmed' AND StartTime<=@cutoff ORDER BY ReservationId FOR UPDATE;";
                command.Parameters.AddWithValue("@cutoff", now.AddMinutes(-15).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                var ids = new List<int>();
                using (var reader = command.ExecuteReader()) while (reader.Read()) ids.Add(reader.GetInt32(0));
                command.Parameters.AddWithValue("@id", 0);
                var count = 0;
                foreach (var id in ids)
                {
                    command.Parameters["@id"].Value = id;
                    // A source session is checked after the reservation lock. Future check-in
                    // writers must take that same reservation lock before creating a session.
                    command.CommandText = "SELECT RoomSessionId FROM RoomSessions WHERE ReservationId=@id LIMIT 1;";
                    if (command.ExecuteScalar() != null) continue;
                    command.CommandText = "UPDATE Reservations SET Status='NoShow' WHERE ReservationId=@id AND Status='Confirmed' AND StartTime<=@cutoff;";
                    if (command.ExecuteNonQuery() != 1) continue;
                    MySqlAuditService.WriteSystem(connection, transaction, "Reservation.NoShow", "Reservation", id.ToString(CultureInfo.InvariantCulture),
                        "Khách chưa nhận phòng trước hạn 15 phút.", now);
                    count++;
                }
                return count;
            }
        }
    }
}
