using System;
using System.Globalization;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MySqlConnector;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class MySqlRoomSessionService
    {
        private readonly MySqlDatabase database;
        private readonly IClock clock;
        private readonly MySqlPermissionService permissions;
        private readonly MySqlAvailabilityService availability;
        public MySqlRoomSessionService(MySqlDatabase database) : this(database, new SystemClock()) { }
        public MySqlRoomSessionService(MySqlDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new MySqlPermissionService(database); availability = new MySqlAvailabilityService(database, clock);
        }
        public RoomSession CheckIn(LoginSession actor, int reservationId)
        {
            try { return CheckInCore(actor, reservationId); }
            catch (MySqlException error) when (error.Number == 1062 || error.Number == 1213 || error.Number == 1205)
            { throw new InvalidOperationException("Phòng hoặc booking đang được cập nhật. Hãy kiểm tra lại trước khi nhận phòng.", error); }
        }
        private RoomSession CheckInCore(LoginSession actor, int reservationId)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginWriteTransaction(connection))
            using (var command = connection.CreateCommand())
            {
                permissions.Demand(actor, "Session.CheckIn", connection, transaction);
                int room, customer;
                // Discover resource IDs without taking a Reservation lock before Room/Customer.
                using (var discovery = database.OpenConnection())
                using (var query = discovery.CreateCommand())
                {
                    query.CommandText = "SELECT RoomId,CustomerId FROM Reservations WHERE ReservationId=@id;";
                    query.Parameters.AddWithValue("@id", reservationId);
                    using (var reader = query.ExecuteReader())
                    {
                        if (!reader.Read()) throw new InvalidOperationException("Không tìm thấy booking.");
                        room = reader.GetInt32(0); customer = reader.GetInt32(1);
                    }
                }
                MySqlAvailabilityService.LockResources(connection, transaction, room, customer);
                // Match reservation maintenance's ordered candidate locks before querying
                // shared holds, avoiding a source-row/NoShow candidate lock inversion.
                MySqlNoShowService.LockCandidates(connection, transaction);
                command.Transaction = transaction;
                command.Parameters.AddWithValue("@source", reservationId);
                command.CommandText = "SELECT ReservationId FROM Reservations WHERE ReservationId=@source FOR UPDATE;";
                if (command.ExecuteScalar() == null) throw new InvalidOperationException("Không tìm thấy booking.");
                DateTimeOffset start, end; string status;
                command.CommandText = "SELECT RoomId,CustomerId,StartTime,EndTime,Status FROM Reservations WHERE ReservationId=@source;";
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read() || reader.GetInt32(0) != room || reader.GetInt32(1) != customer)
                        throw new InvalidOperationException("Booking đã thay đổi. Hãy tải lại danh sách.");
                    start = Time(reader.GetString(2)); end = Time(reader.GetString(3)); status = reader.GetString(4);
                }
                // Replay, including Completed, still requires current permission and source locks.
                var existing = ReadByReservation(connection, transaction, reservationId);
                if (existing != null) { transaction.Commit(); return existing; }
                var now = clock.UtcNow.ToUniversalTime();
                if (status != "Confirmed") throw new InvalidOperationException("Chỉ nhận phòng từ booking Confirmed còn hiệu lực.");
                if (now >= start.AddMinutes(15)) throw new InvalidOperationException("Booking đã hết hạn nhận phòng 15 phút.");
                var expected = now.Add(end - start);
                var check = availability.CheckCheckInAt(connection, transaction, room, customer, reservationId, now, expected);
                if (!check.CanBook) throw new InvalidOperationException(check.Reason);
                command.CommandText = @"INSERT INTO RoomSessions(CustomerId,RoomId,ReservationId,ActualStartTime,ExpectedEndTime,
HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status)
SELECT @customer,r.RoomId,@source,@now,@end,t.PricePerHour,r.RoomCode,t.Code,t.Name,'Active'
FROM Rooms r JOIN RoomTypes t ON t.RoomTypeId=r.RoomTypeId WHERE r.RoomId=@room;";
                command.Parameters.AddWithValue("@customer", customer); command.Parameters.AddWithValue("@room", room);
                command.Parameters.AddWithValue("@now", Utc(now)); command.Parameters.AddWithValue("@end", Utc(expected));
                if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Không tìm thấy phòng để nhận khách.");
                command.CommandText = "UPDATE Reservations SET Status='CheckedIn' WHERE ReservationId=@source AND Status='Confirmed';";
                if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Booking đã thay đổi. Hãy tải lại danh sách.");
                var created = ReadByReservation(connection, transaction, reservationId);
                MySqlAuditService.WriteStaff(connection, transaction, actor.UserId, "Session.CheckIn", "RoomSession",
                    created.RoomSessionId.ToString(CultureInfo.InvariantCulture),
                    "Nhận phòng " + created.RoomCodeSnapshot + " từ booking " + reservationId + "; dự kiến trả " + Utc(expected) + ".", now);
                transaction.Commit(); return created;
            }
        }
        private static RoomSession ReadByReservation(MySqlConnection connection, MySqlTransaction transaction, int reservationId)
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
                    return new RoomSession { RoomSessionId = reader.GetInt32(0), CustomerId = reader.GetInt32(1), RoomId = reader.GetInt32(2),
                        ReservationId = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3), ActualStartTime = Time(reader.GetString(4)),
                        ExpectedEndTime = reader.IsDBNull(5) ? (DateTimeOffset?)null : Time(reader.GetString(5)),
                        ActualEndTime = reader.IsDBNull(6) ? (DateTimeOffset?)null : Time(reader.GetString(6)), HourlyRate = reader.GetInt64(7),
                        RoomCodeSnapshot = reader.GetString(8), RoomTypeCodeSnapshot = reader.GetString(9), RoomTypeNameSnapshot = reader.GetString(10), Status = reader.GetString(11) };
                }
            }
        }
        private static DateTimeOffset Time(string value) => DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture);
        private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
