using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    // Staff calendar only. A future Guest calendar must project anonymous intervals.
    public sealed class CalendarService
    {
        private readonly SqliteDatabase database;
        private readonly IClock clock;
        private readonly PermissionService permissions;
        public CalendarService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public CalendarService(SqliteDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new PermissionService(database);
        }
        public IReadOnlyList<RoomSchedule> Read(LoginSession session, CalendarRange range, int? roomId = null)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                permissions.Demand(session, "Calendar.View", connection, transaction);
                if (range == null) throw new ArgumentException("Cần chọn ngày hoặc tuần.");
                var now = clock.UtcNow.ToUniversalTime();
                var rooms = new List<RoomSchedule>();
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "SELECT RoomId,RoomCode,Name,IsActive FROM Rooms WHERE @room IS NULL OR RoomId=@room ORDER BY RoomCode;";
                    command.Parameters.AddWithValue("@room", (object)roomId ?? DBNull.Value);
                    using (var reader = command.ExecuteReader()) while (reader.Read())
                        rooms.Add(new RoomSchedule { RoomId = reader.GetInt32(0), RoomCode = reader.GetString(1), RoomName = reader.GetString(2),
                            CurrentStatus = reader.GetInt32(3) == 0 ? "Inactive" : "Available", CheckedAt = now, Range = range });
                }
                if (roomId.HasValue && rooms.Count == 0) throw new ArgumentException("Không tìm thấy phòng.");
                foreach (var room in rooms) Populate(connection, transaction, room, now);
                return rooms.AsReadOnly();
            }
        }
        private static void Populate(SQLiteConnection connection, SQLiteTransaction transaction, RoomSchedule room, DateTimeOffset now)
        {
            var events = new List<RoomScheduleEvent>(); var holds = new List<ScheduleInterval>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.Parameters.AddWithValue("@room", room.RoomId);
                command.Parameters.AddWithValue("@cutoff", Utc(now.AddMinutes(-15)));
                command.Parameters.AddWithValue("@now", Utc(now));
                command.Parameters.AddWithValue("@from", Utc(room.Range.Start));
                command.Parameters.AddWithValue("@to", Utc(room.Range.End));
                if (room.CurrentStatus != "Inactive")
                {
                    command.CommandText = "SELECT COUNT(*) FROM RoomSessions WHERE RoomId=@room AND Status='Active';";
                    if (Convert.ToInt64(command.ExecuteScalar()) > 0) room.CurrentStatus = "Occupied";
                    else
                    {
                        command.CommandText = "SELECT COUNT(*) FROM Reservations WHERE RoomId=@room AND Status='Confirmed' AND StartTime>@cutoff AND StartTime<=@now AND EndTime>@now;";
                        if (Convert.ToInt64(command.ExecuteScalar()) > 0) room.CurrentStatus = "Reserved";
                    }
                }
                command.CommandText = "SELECT HoldStart,HoldEnd FROM (" + ScheduleRules.HoldsSql + ") WHERE RoomId=@room AND HoldStart<@to AND HoldEnd>@from ORDER BY HoldStart;";
                using (var reader = command.ExecuteReader()) while (reader.Read())
                    holds.Add(new ScheduleInterval { Start = Parse(reader.GetString(0)), End = Parse(reader.GetString(1)) });
                command.CommandText = @"SELECT b.ReservationId,b.StartTime,b.EndTime,c.FullName,c.PhoneNumber
FROM Reservations b JOIN Customers c ON c.CustomerId=b.CustomerId
WHERE b.RoomId=@room AND b.Status='Confirmed' AND b.StartTime>@cutoff AND b.StartTime<@to AND b.EndTime>@from;";
                using (var reader = command.ExecuteReader()) while (reader.Read())
                    events.Add(new RoomScheduleEvent { ReservationId = reader.GetInt32(0), Kind = "Confirmed", Start = Parse(reader.GetString(1)),
                        End = Parse(reader.GetString(2)), CustomerName = reader.GetString(3), PhoneNumber = reader.GetString(4) });
                // Actual usage and expected duration are distinct from future booking holds.
                command.CommandText = @"SELECT s.RoomSessionId,s.ReservationId,s.ActualStartTime,s.ExpectedEndTime,s.ActualEndTime,s.Status,c.FullName,c.PhoneNumber,s.CustomerId
FROM RoomSessions s JOIN Customers c ON c.CustomerId=s.CustomerId WHERE s.RoomId=@room
AND s.ActualStartTime<@to AND
((s.Status='Completed' AND s.ActualEndTime>s.ActualStartTime AND s.ActualEndTime>@from) OR (s.Status='Active' AND (@now>@from OR s.ExpectedEndTime>@from)));";
                var walkIns = new List<Tuple<RoomScheduleEvent, int>>();
                using (var reader = command.ExecuteReader()) while (reader.Read())
                {
                    var completed = reader.GetString(5) == "Completed";
                    var expected = reader.IsDBNull(3) ? (DateTimeOffset?)null : Parse(reader.GetString(3));
                    var end = completed ? Parse(reader.GetString(4)) : now;
                    var item = new RoomScheduleEvent { SessionId = reader.GetInt32(0), ReservationId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                        Kind = completed ? "Completed" : (reader.IsDBNull(1) ? "WalkIn" : "Active"), Start = Parse(reader.GetString(2)), End = end,
                        ExpectedEnd = expected, IsOverdue = !completed && expected.HasValue && now > expected.Value,
                        CustomerName = reader.GetString(6), PhoneNumber = reader.GetString(7) };
                    events.Add(item);
                    if (!completed && !item.ReservationId.HasValue) walkIns.Add(Tuple.Create(item, reader.GetInt32(8)));
                }
                foreach (var walkIn in walkIns)
                {
                    var local = walkIn.Item1.Start.ToOffset(BookingHours.VietnamOffset);
                    var close = new DateTimeOffset(local.Date.AddHours(local.Hour < 12 ? 12 : 23), BookingHours.VietnamOffset).ToUniversalTime();
                    command.CommandText = "SELECT MIN(StartTime) FROM Reservations WHERE Status='Confirmed' AND StartTime>@cutoff AND StartTime>=@walkStart AND (RoomId=@room OR CustomerId=@customer);";
                    command.Parameters.AddWithValue("@walkStart", Utc(walkIn.Item1.Start));
                    command.Parameters.AddWithValue("@customer", walkIn.Item2);
                    var next = command.ExecuteScalar();
                    if (next != null && next != DBNull.Value && Parse((string)next) < close) close = Parse((string)next);
                    walkIn.Item1.ReturnBy = close; walkIn.Item1.IsOverdue = now > close;
                    command.Parameters.RemoveAt("@walkStart"); command.Parameters.RemoveAt("@customer");
                }
            }
            events.Sort((left, right) => left.Start.CompareTo(right.Start));
            room.Holds = holds.AsReadOnly(); room.Events = events.AsReadOnly();
        }
        private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        private static DateTimeOffset Parse(string value) => DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture);
    }
}
