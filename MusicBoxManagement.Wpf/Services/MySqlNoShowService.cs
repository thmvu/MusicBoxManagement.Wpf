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
                    var candidates = LockCandidates(database, connection, transaction);
                    var count = ProcessExpiredAt(connection, transaction, clock.UtcNow.ToUniversalTime(), candidates);
                    transaction.Commit(); return count;
                }
            }
            catch (MySqlException error) when (error.Number == 1213 || error.Number == 1205)
            { throw new InvalidOperationException("Lịch phòng đang được cập nhật. Vui lòng thử lại sau.", error); }
        }

        internal sealed class Candidate
        {
            internal int Id;
            internal DateTimeOffset Start;
            internal string Status;
        }

        internal static IReadOnlyList<Candidate> LockCandidates(MySqlDatabase database, MySqlConnection connection, MySqlTransaction transaction, int? sourceReservationId = null)
        {
            DemandWriter(connection, transaction);
            var ids = new List<int>();
            // A Serializable range scan locks historical rows and insertion gaps even
            // when no Confirmed row matches. Discover IDs in a separate nonlocking
            // RR snapshot, then re-read each current row under its unique PK lock.
            using (var discovery = database.OpenConnection())
            using (var snapshot = MySqlDatabase.BeginReadTransaction(discovery))
            using (var read = discovery.CreateCommand())
            {
                read.Transaction = snapshot;
                read.CommandText = "SELECT ReservationId FROM Reservations WHERE Status='Confirmed' ORDER BY ReservationId;";
                using (var reader = read.ExecuteReader()) while (reader.Read()) ids.Add(reader.GetInt32(0));
            }
            // A source may have left Confirmed after another check-in committed.
            // Merge it before acquiring ANY reservation lock, so stale discovery
            // and extension/replay/cancel share one global PK order.
            if (sourceReservationId.HasValue && !ids.Contains(sourceReservationId.Value))
            {
                ids.Add(sourceReservationId.Value); ids.Sort();
            }
            var result = new List<Candidate>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                // One network request per bounded batch; each statement is a unique
                // equality lookup, never an optimizer-selected IN/range table scan.
                for (var offset = 0; offset < ids.Count; offset += 128)
                {
                    command.Parameters.Clear();
                    var statements = new List<string>();
                    for (var i = offset; i < Math.Min(offset + 128, ids.Count); i++)
                    {
                        var parameter = "@candidate" + (i - offset).ToString(CultureInfo.InvariantCulture);
                        command.Parameters.AddWithValue(parameter, ids[i]);
                        statements.Add("SELECT ReservationId,StartTime,Status FROM Reservations WHERE ReservationId=" + parameter + " FOR UPDATE");
                    }
                    command.CommandText = string.Join(";", statements) + ";";
                    using (var reader = command.ExecuteReader())
                        do
                        {
                            while (reader.Read()) result.Add(new Candidate { Id = reader.GetInt32(0),
                                Start = DateTimeOffset.ParseExact(reader.GetString(1), "O", CultureInfo.InvariantCulture), Status = reader.GetString(2) });
                        } while (reader.NextResult());
                }
            }
            return result;
        }

        internal static int ProcessExpiredAt(MySqlConnection connection, MySqlTransaction transaction, DateTimeOffset now, IReadOnlyList<Candidate> candidates)
        {
            DemandWriter(connection, transaction);
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            var cutoff = now.AddMinutes(-15).ToUniversalTime();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.Parameters.AddWithValue("@cutoff", cutoff.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("@id", 0);
                var count = 0;
                foreach (var candidate in candidates)
                {
                    if (candidate.Status != "Confirmed" || candidate.Start > cutoff) continue;
                    command.Parameters["@id"].Value = candidate.Id;
                    command.CommandText = "SELECT RoomSessionId FROM RoomSessions WHERE ReservationId=@id LIMIT 1;";
                    if (command.ExecuteScalar() != null) continue;
                    command.CommandText = "UPDATE Reservations SET Status='NoShow' WHERE ReservationId=@id AND Status='Confirmed' AND StartTime<=@cutoff;";
                    if (command.ExecuteNonQuery() != 1) continue;
                    MySqlAuditService.WriteSystem(connection, transaction, "Reservation.NoShow", "Reservation", candidate.Id.ToString(CultureInfo.InvariantCulture),
                        "Khách chưa nhận phòng trước hạn 15 phút.", now);
                    count++;
                }
                return count;
            }
        }

        private static void DemandWriter(MySqlConnection connection, MySqlTransaction transaction)
        {
            if (transaction == null || transaction.Connection != connection || transaction.IsolationLevel != System.Data.IsolationLevel.Serializable)
                throw new ArgumentException("Cần cùng kết nối và transaction ghi Serializable.");
        }
    }
}
