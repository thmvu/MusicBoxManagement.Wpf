using System;
using System.Data.SQLite;
using System.Globalization;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class RoomSessionService
    {
        private readonly SqliteDatabase database;
        private readonly IClock clock;
        private readonly PermissionService permissions;
        private readonly AvailabilityService availability;
        public RoomSessionService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public RoomSessionService(SqliteDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new PermissionService(database);
            availability = new AvailabilityService(database, clock);
        }

        public RoomSession CheckIn(LoginSession actor, int reservationId)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = SqliteDatabase.BeginWriteTransaction(connection))
            {
                permissions.Demand(actor, "Session.CheckIn", connection, transaction);
                // A replay still needs current authorization, but never changes the old snapshot.
                var existing = ReadSource(connection, transaction, reservationId);
                if (existing != null) { transaction.Commit(); return existing; }
                var now = clock.UtcNow.ToUniversalTime();
                int roomId, customerId;
                DateTimeOffset start, end;
                string status;
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "SELECT RoomId,CustomerId,StartTime,EndTime,Status FROM Reservations WHERE ReservationId=@id;";
                    command.Parameters.AddWithValue("@id", reservationId);
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read()) throw new InvalidOperationException("Không tìm thấy booking.");
                        roomId = reader.GetInt32(0); customerId = reader.GetInt32(1);
                        start = Parse(reader.GetString(2)); end = Parse(reader.GetString(3)); status = reader.GetString(4);
                    }
                }
                if (status != "Confirmed") throw new InvalidOperationException("Chỉ nhận phòng từ booking Confirmed còn hiệu lực.");
                if (now >= start.AddMinutes(15)) throw new InvalidOperationException("Booking đã hết hạn nhận phòng 15 phút.");
                var candidateEnd = now.Add(end - start);
                var check = availability.CheckCheckInAt(connection, transaction, roomId, customerId, reservationId, now, candidateEnd);
                if (!check.CanBook) throw new InvalidOperationException(check.Reason);
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"INSERT INTO RoomSessions(CustomerId,RoomId,ReservationId,ActualStartTime,ExpectedEndTime,
HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status)
SELECT @customer,r.RoomId,@source,@now,@end,t.PricePerHour,r.RoomCode,t.Code,t.Name,'Active'
FROM Rooms r JOIN RoomTypes t ON t.RoomTypeId=r.RoomTypeId WHERE r.RoomId=@room;
UPDATE Reservations SET Status='CheckedIn' WHERE ReservationId=@source AND Status='Confirmed';";
                    command.Parameters.AddWithValue("@customer", customerId);
                    command.Parameters.AddWithValue("@room", roomId);
                    command.Parameters.AddWithValue("@source", reservationId);
                    command.Parameters.AddWithValue("@now", Utc(now));
                    command.Parameters.AddWithValue("@end", Utc(candidateEnd));
                    command.ExecuteNonQuery();
                }
                var created = ReadSource(connection, transaction, reservationId);
                AuditService.WriteStaff(connection, transaction, actor.UserId, "Session.CheckIn", "RoomSession",
                    created.RoomSessionId.ToString(CultureInfo.InvariantCulture),
                    "Nhận phòng " + created.RoomCodeSnapshot + " từ booking " + reservationId + "; dự kiến trả " + Utc(candidateEnd) + ".", now);
                transaction.Commit();
                return created;
            }
        }

        private static RoomSession ReadSource(SQLiteConnection connection, SQLiteTransaction transaction, int reservationId)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"SELECT RoomSessionId,CustomerId,RoomId,ReservationId,ActualStartTime,ExpectedEndTime,ActualEndTime,
HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status FROM RoomSessions WHERE ReservationId=@source;";
                command.Parameters.AddWithValue("@source", reservationId);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    return new RoomSession
                    {
                        RoomSessionId = reader.GetInt32(0), CustomerId = reader.GetInt32(1), RoomId = reader.GetInt32(2),
                        ReservationId = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3), ActualStartTime = Parse(reader.GetString(4)),
                        ExpectedEndTime = reader.IsDBNull(5) ? (DateTimeOffset?)null : Parse(reader.GetString(5)),
                        ActualEndTime = reader.IsDBNull(6) ? (DateTimeOffset?)null : Parse(reader.GetString(6)),
                        HourlyRate = reader.GetInt64(7), RoomCodeSnapshot = reader.GetString(8), RoomTypeCodeSnapshot = reader.GetString(9),
                        RoomTypeNameSnapshot = reader.GetString(10), Status = reader.GetString(11)
                    };
                }
            }
        }
        private static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
