using System;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class GuestSessionService
    {
        private readonly SqliteDatabase database;
        private readonly IClock clock;
        private readonly AvailabilityService availability;

        public GuestSessionService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public GuestSessionService(SqliteDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            availability = new AvailabilityService(database, clock);
        }

        public GuestSession Lookup(string phoneNumber)
        {
            var phone = PhoneNumberNormalizer.Normalize(phoneNumber);
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var now = clock.UtcNow.ToUniversalTime();
                var current = ReadActive(connection, transaction, phone, null);
                return current == null ? null : PublicSession(connection, transaction, current, now);
            }
        }

        public SessionExtensionCheck PreviewExtension(string phoneNumber, int sessionId, int minutes)
        {
            var phone = PhoneNumberNormalizer.Normalize(phoneNumber);
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                var current = DemandActive(connection, transaction, phone, sessionId);
                return availability.CheckExtensionAt(connection, transaction, current, minutes, clock.UtcNow);
            }
        }

        public GuestSession Extend(string phoneNumber, int sessionId, DateTimeOffset observedEnd, int minutes)
        {
            var phone = PhoneNumberNormalizer.Normalize(phoneNumber);
            using (var connection = database.OpenConnection())
            using (var transaction = SqliteDatabase.BeginWriteTransaction(connection))
            {
                var current = DemandActive(connection, transaction, phone, sessionId);
                if (!current.ReservationId.HasValue || !current.ExpectedEndTime.HasValue)
                    throw new InvalidOperationException("Khách trực tiếp không có gia hạn.");
                if (current.ExpectedEndTime.Value != observedEnd)
                    throw new InvalidOperationException("Giờ trả dự kiến đã thay đổi. Hãy tra cứu lại trước khi gia hạn.");
                // Capture time after acquiring the writer, including any wait for another writer.
                var now = clock.UtcNow.ToUniversalTime();
                var check = availability.CheckExtensionAt(connection, transaction, current, minutes, now);
                if (!check.CanExtend) throw new SessionExtensionException(check.Reason, check.MaximumEndTime);
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "UPDATE RoomSessions SET ExpectedEndTime=@end WHERE RoomSessionId=@id;";
                    command.Parameters.AddWithValue("@end", Utc(check.NewEndTime));
                    command.Parameters.AddWithValue("@id", sessionId);
                    command.ExecuteNonQuery();
                }
                AuditService.WriteGuest(connection, transaction, "Session.Extend", "RoomSession",
                    sessionId.ToString(CultureInfo.InvariantCulture), "Gia hạn " + minutes + " phút từ " +
                    Utc(current.ExpectedEndTime.Value) + " đến " + Utc(check.NewEndTime) + ".", now);
                current.ExpectedEndTime = check.NewEndTime;
                var result = PublicSession(connection, transaction, current, now);
                transaction.Commit();
                return result;
            }
        }

        private static RoomSession DemandActive(SQLiteConnection connection, SQLiteTransaction transaction, string phone, int id)
        {
            var current = ReadActive(connection, transaction, phone, id);
            if (current == null) throw new InvalidOperationException("Không tìm thấy phiên đang sử dụng của SĐT này. Hãy tra cứu lại.");
            return current;
        }

        private static RoomSession ReadActive(SQLiteConnection connection, SQLiteTransaction transaction, string phone, int? id)
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
                    return reader.Read() ? RoomSessionService.ReadSessionRecord(reader) : null;
            }
        }

        private static GuestSession PublicSession(SQLiteConnection connection, SQLiteTransaction transaction, RoomSession current, DateTimeOffset now)
        {
            return new GuestSession
            {
                SessionId = current.RoomSessionId, RoomCode = current.RoomCodeSnapshot,
                RoomTypeName = current.RoomTypeNameSnapshot, HourlyRate = current.HourlyRate,
                ActualStartTime = current.ActualStartTime, ExpectedEndTime = current.ExpectedEndTime,
                FromBooking = current.ReservationId.HasValue, CheckedAt = now,
                ReturnBy = current.ExpectedEndTime ?? ScheduleRules.WalkInReturnBy(connection, transaction,
                    current.RoomId, current.CustomerId, current.ActualStartTime, now)
            };
        }
        private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
