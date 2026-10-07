using System;
using System.Data;
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
                var existing = ReadSession(connection, transaction, reservationId, true);
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
                var created = ReadSession(connection, transaction, reservationId, true);
                AuditService.WriteStaff(connection, transaction, actor.UserId, "Session.CheckIn", "RoomSession",
                    created.RoomSessionId.ToString(CultureInfo.InvariantCulture),
                    "Nhận phòng " + created.RoomCodeSnapshot + " từ booking " + reservationId + "; dự kiến trả " + Utc(candidateEnd) + ".", now);
                transaction.Commit();
                return created;
            }
        }

        public WalkInResult CreateWalkIn(LoginSession actor, WalkInRequest request)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = SqliteDatabase.BeginWriteTransaction(connection))
            using (var command = connection.CreateCommand())
            {
                permissions.Demand(actor, "Session.WalkIn", connection, transaction);
                if (request == null) throw new ArgumentException("Cần nhập thông tin khách trực tiếp.");
                var roomId = request.RoomId;
                var name = (request.FullName ?? "").Trim();
                if (name.Length < 1 || name.Length > 100) throw new ArgumentException("Họ tên từ 1–100 ký tự.");
                var phone = PhoneNumberNormalizer.Normalize(request.PhoneNumber);
                var now = clock.UtcNow.ToUniversalTime();
                BookingHours.GetOpenShiftEnd(now);
                NoShowService.ProcessExpiredAt(connection, transaction, now);
                command.Transaction = transaction;
                command.Parameters.AddWithValue("@phone", phone);
                command.CommandText = "SELECT CustomerId FROM Customers WHERE PhoneNumber=@phone;";
                var found = command.ExecuteScalar();
                int customerId;
                if (found != null) customerId = Convert.ToInt32(found);
                else
                {
                    command.Parameters.AddWithValue("@name", name);
                    command.CommandText = "INSERT INTO Customers(FullName,PhoneNumber) VALUES(@name,@phone); SELECT last_insert_rowid();";
                    customerId = Convert.ToInt32(command.ExecuteScalar());
                    AuditService.WriteStaff(connection, transaction, actor.UserId, "Customer.Create", "Customer",
                        customerId.ToString(CultureInfo.InvariantCulture), "Tạo khách hàng từ nhận khách trực tiếp.", now);
                }
                var check = availability.CheckWalkInAt(connection, transaction, roomId, customerId, now);
                if (!check.CanBook) throw new InvalidOperationException(check.Reason);
                command.Parameters.Clear();
                command.CommandText = @"INSERT INTO RoomSessions(CustomerId,RoomId,ActualStartTime,HourlyRate,
RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status)
SELECT @customer,r.RoomId,@now,t.PricePerHour,r.RoomCode,t.Code,t.Name,'Active'
FROM Rooms r JOIN RoomTypes t ON t.RoomTypeId=r.RoomTypeId WHERE r.RoomId=@room;
SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("@customer", customerId); command.Parameters.AddWithValue("@room", roomId);
                command.Parameters.AddWithValue("@now", Utc(now));
                var id = Convert.ToInt32(command.ExecuteScalar());
                var created = ReadSession(connection, transaction, id, false);
                var returnBy = ScheduleRules.WalkInReturnBy(connection, transaction, roomId, customerId, now, now);
                AuditService.WriteStaff(connection, transaction, actor.UserId, "Session.WalkIn", "RoomSession",
                    id.ToString(CultureInfo.InvariantCulture), "Nhận khách trực tiếp phòng " + created.RoomCodeSnapshot + "; cần trả trước " + Utc(returnBy) + ".", now);
                transaction.Commit();
                return new WalkInResult { Session = created, ReturnBy = returnBy, CheckedAt = now };
            }
        }

        public WalkInResult ReadWalkIn(LoginSession actor, int sessionId)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                permissions.Demand(actor, "Session.View", connection, transaction);
                var now = clock.UtcNow.ToUniversalTime();
                var current = ReadSession(connection, transaction, sessionId, false);
                if (current == null || current.Status != "Active" || current.ReservationId.HasValue)
                    throw new InvalidOperationException("Không tìm thấy phiên khách trực tiếp đang sử dụng.");
                return new WalkInResult { Session = current, CheckedAt = now,
                    ReturnBy = ScheduleRules.WalkInReturnBy(connection, transaction, current.RoomId, current.CustomerId, current.ActualStartTime, now) };
            }
        }

        private static RoomSession ReadSession(SQLiteConnection connection, SQLiteTransaction transaction, int id, bool byReservation)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"SELECT RoomSessionId,CustomerId,RoomId,ReservationId,ActualStartTime,ExpectedEndTime,ActualEndTime,
HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status FROM RoomSessions WHERE " + (byReservation ? "ReservationId" : "RoomSessionId") + "=@id;";
                command.Parameters.AddWithValue("@id", id);
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
