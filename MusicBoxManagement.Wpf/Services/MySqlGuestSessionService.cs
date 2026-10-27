using System;
using System.Globalization;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MySqlConnector;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class MySqlGuestSessionService
    {
        private readonly MySqlDatabase database;
        private readonly IClock clock;
        private readonly MySqlAvailabilityService availability;

        public MySqlGuestSessionService(MySqlDatabase database) : this(database, new SystemClock()) { }
        public MySqlGuestSessionService(MySqlDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            availability = new MySqlAvailabilityService(database, clock);
        }

        public GuestSession Lookup(string phoneNumber)
        {
            var phone = PhoneNumberNormalizer.Normalize(phoneNumber);
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            {
                var current = ReadActive(connection, transaction, phone, null);
                var now = clock.UtcNow.ToUniversalTime();
                return current == null ? null : PublicSession(connection, transaction, current, now);
            }
        }

        public SessionExtensionCheck PreviewExtension(string phoneNumber, int sessionId, int minutes)
        {
            var phone = PhoneNumberNormalizer.Normalize(phoneNumber);
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            {
                var current = DemandActive(connection, transaction, phone, sessionId);
                return availability.CheckExtensionAt(connection, transaction, current, minutes, clock.UtcNow);
            }
        }

        public GuestSession Extend(string phoneNumber, int sessionId, DateTimeOffset observedEnd, int minutes)
        {
            var phone = PhoneNumberNormalizer.Normalize(phoneNumber);
            try
            {
                // Discover only immutable resource IDs, without taking a session lock first.
                int room, customer; int? source;
                using (var discovery = database.OpenConnection())
                using (var query = discovery.CreateCommand())
                {
                    query.CommandText = @"SELECT s.RoomId,s.CustomerId,s.ReservationId FROM RoomSessions s
JOIN Customers c ON c.CustomerId=s.CustomerId
WHERE s.RoomSessionId=@id AND c.PhoneNumber=@phone AND s.Status='Active';";
                    query.Parameters.AddWithValue("@id", sessionId); query.Parameters.AddWithValue("@phone", phone);
                    using (var reader = query.ExecuteReader())
                    {
                        if (!reader.Read()) throw Missing();
                        room = reader.GetInt32(0); customer = reader.GetInt32(1);
                        source = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                    }
                }
                using (var connection = database.OpenConnection())
                using (var transaction = MySqlDatabase.BeginWriteTransaction(connection))
                using (var command = connection.CreateCommand())
                {
                    MySqlAvailabilityService.LockResources(connection, transaction, room, customer);
                    MySqlNoShowService.LockCandidates(connection, transaction);
                    command.Transaction = transaction;
                    if (source.HasValue)
                    {
                        command.CommandText = "SELECT ReservationId FROM Reservations WHERE ReservationId=@source FOR UPDATE;";
                        command.Parameters.AddWithValue("@source", source.Value); command.ExecuteScalar();
                    }
                    command.CommandText = "SELECT RoomSessionId FROM RoomSessions WHERE RoomSessionId=@id FOR UPDATE;";
                    command.Parameters.AddWithValue("@id", sessionId); command.ExecuteScalar();
                    // Recheck membership after the Customer lock, including a phone edit while waiting.
                    var current = DemandActive(connection, transaction, phone, sessionId);
                    if (current.RoomId != room || current.CustomerId != customer || current.ReservationId != source) throw Missing();
                    if (!current.ReservationId.HasValue || !current.ExpectedEndTime.HasValue)
                        throw new InvalidOperationException("Khách trực tiếp không có gia hạn.");
                    if (current.ExpectedEndTime.Value != observedEnd)
                        throw new InvalidOperationException("Giờ trả dự kiến đã thay đổi. Hãy tra cứu lại trước khi gia hạn.");
                    var now = clock.UtcNow.ToUniversalTime();
                    var check = availability.CheckExtensionAt(connection, transaction, current, minutes, now);
                    if (!check.CanExtend) throw new SessionExtensionException(check.Reason, check.MaximumEndTime);
                    command.CommandText = "UPDATE RoomSessions SET ExpectedEndTime=@end WHERE RoomSessionId=@id AND Status='Active' AND ExpectedEndTime=@old;";
                    command.Parameters.AddWithValue("@end", Utc(check.NewEndTime));
                    command.Parameters.AddWithValue("@old", Utc(current.ExpectedEndTime.Value));
                    if (command.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("Giờ trả dự kiến đã thay đổi. Hãy tra cứu lại trước khi gia hạn.");
                    MySqlAuditService.WriteGuest(connection, transaction, "Session.Extend", "RoomSession",
                        sessionId.ToString(CultureInfo.InvariantCulture), "Gia hạn " + minutes + " phút từ " +
                        Utc(current.ExpectedEndTime.Value) + " đến " + Utc(check.NewEndTime) + ".", now);
                    current.ExpectedEndTime = check.NewEndTime;
                    var result = PublicSession(connection, transaction, current, now);
                    transaction.Commit(); return result;
                }
            }
            catch (MySqlException error) when (error.Number == 1213 || error.Number == 1205)
            { throw new InvalidOperationException("Phiên hoặc lịch đang được cập nhật. Hãy tra cứu lại trước khi gia hạn.", error); }
        }

        internal static GuestSession ReadActiveAt(MySqlConnection connection, MySqlTransaction transaction, string phone, DateTimeOffset now)
        {
            var current = ReadActive(connection, transaction, phone, null);
            return current == null ? null : PublicSession(connection, transaction, current, now);
        }
        private static InvalidOperationException Missing()
            => new InvalidOperationException("Không tìm thấy phiên đang sử dụng của SĐT này. Hãy tra cứu lại.");
        private static RoomSession DemandActive(MySqlConnection connection, MySqlTransaction transaction, string phone, int id)
        {
            var current = ReadActive(connection, transaction, phone, id);
            if (current == null) throw Missing();
            return current;
        }
        private static RoomSession ReadActive(MySqlConnection connection, MySqlTransaction transaction, string phone, int? id)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"SELECT s.RoomSessionId,s.CustomerId,s.RoomId,s.ReservationId,s.ActualStartTime,
s.ExpectedEndTime,s.ActualEndTime,s.HourlyRate,s.RoomCodeSnapshot,s.RoomTypeCodeSnapshot,s.RoomTypeNameSnapshot,s.Status
FROM RoomSessions s JOIN Customers c ON c.CustomerId=s.CustomerId
WHERE c.PhoneNumber=@phone AND s.Status='Active'" + (id.HasValue ? " AND s.RoomSessionId=@id" : "") + ";";
                command.Parameters.AddWithValue("@phone", phone);
                if (id.HasValue) command.Parameters.AddWithValue("@id", id.Value);
                using (var reader = command.ExecuteReader())
                    return reader.Read() ? MySqlRoomSessionService.ReadSessionRecord(reader) : null;
            }
        }
        private static GuestSession PublicSession(MySqlConnection connection, MySqlTransaction transaction, RoomSession current, DateTimeOffset now)
        {
            return new GuestSession
            {
                SessionId = current.RoomSessionId, RoomCode = current.RoomCodeSnapshot,
                RoomTypeName = current.RoomTypeNameSnapshot, HourlyRate = current.HourlyRate,
                ActualStartTime = current.ActualStartTime, ExpectedEndTime = current.ExpectedEndTime,
                FromBooking = current.ReservationId.HasValue, CheckedAt = now,
                ReturnBy = current.ExpectedEndTime ?? MySqlScheduleRules.WalkInReturnBy(connection, transaction,
                    current.RoomId, current.CustomerId, current.ActualStartTime, now)
            };
        }
        private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
