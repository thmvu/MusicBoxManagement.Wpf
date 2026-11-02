using System;
using System.Collections.Generic;
using System.Data;
using MySqlConnector;
using System.Globalization;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    // Staff calendar with private customer details. Guest uses MySqlGuestCalendarService.
    public sealed class MySqlCalendarService : ICalendarService
    {
        private readonly MySqlDatabase database;
        private readonly IClock clock;
        private readonly MySqlPermissionService permissions;
        public MySqlCalendarService(MySqlDatabase database) : this(database, new SystemClock()) { }
        public MySqlCalendarService(MySqlDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new MySqlPermissionService(database);
        }
        public IReadOnlyList<RoomSchedule> Read(LoginSession session, CalendarRange range, int? roomId = null)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            {
                var access = permissions.ReadAccess(session, connection, transaction);
                if (!access.Permissions.ContainsKey("Calendar.View")) throw new UnauthorizedAccessException("Cần quyền xem lịch phòng.");
                if (range == null) throw new ArgumentException("Cần chọn ngày hoặc tuần.");
                var now = clock.UtcNow.ToUniversalTime();
                var rooms = new List<RoomSchedule>();
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "SELECT RoomId,RoomCode,Name," + MySqlScheduleRules.RoomStatusSql + " FROM Rooms r WHERE @room IS NULL OR RoomId=@room ORDER BY RoomCode;";
                    command.Parameters.AddWithValue("@now", Utc(now));
                    command.Parameters.AddWithValue("@cutoff", Utc(now.AddMinutes(-15)));
                    command.Parameters.AddWithValue("@room", (object)roomId ?? DBNull.Value);
                    using (var reader = command.ExecuteReader()) while (reader.Read())
                        rooms.Add(new RoomSchedule { RoomId = reader.GetInt32(0), RoomCode = reader.GetString(1), RoomName = reader.GetString(2),
                            CurrentStatus = reader.GetString(3), CheckedAt = now, Range = range });
                }
                if (roomId.HasValue && rooms.Count == 0) throw new ArgumentException("Không tìm thấy phòng.");
                if (rooms.Count > 0) Populate(connection, transaction, rooms, now);
                return rooms.AsReadOnly();
            }
        }
        private static void Populate(MySqlConnection connection, MySqlTransaction transaction, List<RoomSchedule> rooms, DateTimeOffset now)
        {
            var events = rooms.ToDictionary(r => r.RoomId, r => new List<RoomScheduleEvent>());
            var holds = rooms.ToDictionary(r => r.RoomId, r => new List<ScheduleInterval>());
            var range = rooms[0].Range;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                var roomParameters = rooms.Select((room, index) => "@room" + index).ToArray();
                for (var i = 0; i < rooms.Count; i++) command.Parameters.AddWithValue(roomParameters[i], rooms[i].RoomId);
                var selected = " IN (" + string.Join(",", roomParameters) + ")";
                command.Parameters.AddWithValue("@cutoff", Utc(now.AddMinutes(-15)));
                command.Parameters.AddWithValue("@now", Utc(now));
                command.Parameters.AddWithValue("@from", Utc(range.Start));
                command.Parameters.AddWithValue("@to", Utc(range.End));
                command.CommandText = "SELECT HoldStart,HoldEnd,RoomId FROM (" + MySqlScheduleRules.HoldsSql + ") AS holds WHERE RoomId" + selected + " AND HoldStart<@to AND HoldEnd>@from ORDER BY HoldStart;";
                using (var reader = command.ExecuteReader()) while (reader.Read())
                    holds[reader.GetInt32(2)].Add(new ScheduleInterval { Start = Parse(reader.GetString(0)), End = Parse(reader.GetString(1)) });
                command.CommandText = @"SELECT b.ReservationId,b.StartTime,b.EndTime,c.FullName,c.PhoneNumber,b.RoomId
FROM Reservations b JOIN Customers c ON c.CustomerId=b.CustomerId
WHERE b.RoomId" + selected + @" AND b.Status='Confirmed' AND b.StartTime>@cutoff AND b.StartTime<@to AND b.EndTime>@from;";
                using (var reader = command.ExecuteReader()) while (reader.Read())
                    events[reader.GetInt32(5)].Add(new RoomScheduleEvent { ReservationId = reader.GetInt32(0), Kind = "Confirmed", Start = Parse(reader.GetString(1)),
                        End = Parse(reader.GetString(2)), CustomerName = reader.GetString(3), PhoneNumber = reader.GetString(4) });
                // Actual usage and expected duration are distinct from future booking holds.
                command.CommandText = @"SELECT s.RoomSessionId,s.ReservationId,s.ActualStartTime,s.ExpectedEndTime,s.ActualEndTime,s.Status,c.FullName,c.PhoneNumber,s.CustomerId,s.RoomId
FROM RoomSessions s JOIN Customers c ON c.CustomerId=s.CustomerId WHERE s.RoomId" + selected + @"
AND s.ActualStartTime<@to AND
((s.Status='Completed' AND s.ActualEndTime>s.ActualStartTime AND s.ActualEndTime>@from) OR (s.Status='Active' AND (@now>@from OR s.ExpectedEndTime>@from)));";
                var walkIns = new List<RoomScheduleEvent>();
                using (var reader = command.ExecuteReader()) while (reader.Read())
                {
                    var completed = reader.GetString(5) == "Completed";
                    var expected = reader.IsDBNull(3) ? (DateTimeOffset?)null : Parse(reader.GetString(3));
                    var end = completed ? Parse(reader.GetString(4)) : now;
                    var item = new RoomScheduleEvent { SessionId = reader.GetInt32(0), ReservationId = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                        Kind = completed ? "Completed" : (reader.IsDBNull(1) ? "WalkIn" : "Active"), Start = Parse(reader.GetString(2)), End = end,
                        ExpectedEnd = expected, IsOverdue = !completed && expected.HasValue && now > expected.Value,
                        CustomerName = reader.GetString(6), PhoneNumber = reader.GetString(7) };
                    events[reader.GetInt32(9)].Add(item);
                    if (!completed && !item.ReservationId.HasValue) walkIns.Add(item);
                }
                var returnBy = MySqlScheduleRules.WalkInReturnByMany(connection, transaction, walkIns.Select(w => w.SessionId.Value), now);
                foreach (var walkIn in walkIns)
                {
                    var close = returnBy[walkIn.SessionId.Value];
                    walkIn.ReturnBy = close; walkIn.IsOverdue = now > close;
                }
            }
            foreach (var room in rooms)
            {
                events[room.RoomId].Sort((left, right) => left.Start.CompareTo(right.Start));
                room.Holds = holds[room.RoomId].AsReadOnly(); room.Events = events[room.RoomId].AsReadOnly();
            }
        }
        private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        private static DateTimeOffset Parse(string value) => DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture);
    }
}

