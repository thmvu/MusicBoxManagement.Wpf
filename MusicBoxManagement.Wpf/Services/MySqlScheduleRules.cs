using System;
using System.Collections.Generic;
using System.Linq;
using MySqlConnector;
using System.Globalization;

namespace MusicBoxManagement.Wpf.Services
{
    internal static class MySqlScheduleRules
    {
        // Embed in a Rooms r SELECT; the caller supplies the same snapshot clock and grace cutoff.
        internal const string RoomStatusSql = @"CASE WHEN r.IsActive=0 THEN 'Inactive'
WHEN EXISTS(SELECT 1 FROM RoomSessions s WHERE s.RoomId=r.RoomId AND s.Status='Active') THEN 'Occupied'
WHEN EXISTS(SELECT 1 FROM Reservations b WHERE b.RoomId=r.RoomId AND b.Status='Confirmed'
 AND b.StartTime>@cutoff AND b.StartTime<=@now AND b.EndTime>@now) THEN 'Reserved'
ELSE 'Available' END";
        // Shared with booking availability. @cutoff is now minus the 15-minute grace.
        internal const string HoldsSql = @"SELECT RoomId,CustomerId,StartTime AS HoldStart,EndTime AS HoldEnd,ReservationId FROM Reservations
 WHERE Status='Confirmed' AND StartTime>@cutoff
UNION ALL
SELECT RoomId,CustomerId,ActualStartTime,ExpectedEndTime,ReservationId FROM RoomSessions
 WHERE Status='Active' AND ReservationId IS NOT NULL";

        internal static string CurrentRoomStatus(MySqlConnection connection, MySqlTransaction transaction,
            int roomId, bool isActive, DateTimeOffset now)
        {
            if (!isActive) return "Inactive";
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction; command.Parameters.AddWithValue("@room", roomId);
                command.CommandText = "SELECT COUNT(*) FROM RoomSessions WHERE RoomId=@room AND Status='Active';";
                if (Convert.ToInt64(command.ExecuteScalar()) > 0) return "Occupied";
                command.Parameters.AddWithValue("@cutoff", Utc(now.AddMinutes(-15)));
                command.Parameters.AddWithValue("@now", Utc(now));
                command.CommandText = "SELECT COUNT(*) FROM Reservations WHERE RoomId=@room AND Status='Confirmed' AND StartTime>@cutoff AND StartTime<=@now AND EndTime>@now;";
                return Convert.ToInt64(command.ExecuteScalar()) > 0 ? "Reserved" : "Available";
            }
        }

        // Warning only: never stored as ExpectedEndTime or used as a future hold.
        internal static Dictionary<int, DateTimeOffset> WalkInReturnByMany(MySqlConnection connection, MySqlTransaction transaction,
            IEnumerable<int> sessionIds, DateTimeOffset now)
        {
            if (transaction == null || transaction.Connection != connection || transaction.IsolationLevel != System.Data.IsolationLevel.RepeatableRead)
                throw new ArgumentException("Cần cùng snapshot đọc phiên sử dụng.");
            var result = new Dictionary<int, DateTimeOffset>();
            var ids = sessionIds.Distinct().OrderBy(id => id).ToArray();
            if (ids.Length == 0) return result;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                var names = ids.Select((id, i) => "@id" + i).ToArray();
                for (var i = 0; i < ids.Length; i++) command.Parameters.AddWithValue(names[i], ids[i]);
                command.Parameters.AddWithValue("@cutoff", Utc(now.AddMinutes(-15)));
                command.CommandText = @"SELECT s.RoomSessionId,s.ActualStartTime,
(SELECT MIN(b.StartTime) FROM Reservations b WHERE b.Status='Confirmed' AND b.StartTime>@cutoff
 AND b.StartTime>=s.ActualStartTime AND (b.RoomId=s.RoomId OR b.CustomerId=s.CustomerId))
FROM RoomSessions s WHERE s.Status='Active' AND s.ReservationId IS NULL AND s.RoomSessionId IN (" + string.Join(",", names) + ");";
                using (var reader = command.ExecuteReader()) while (reader.Read())
                {
                    var actual = DateTimeOffset.ParseExact(reader.GetString(1), "O", CultureInfo.InvariantCulture);
                    var close = WalkInShiftEnd(actual);
                    if (!reader.IsDBNull(2))
                    {
                        var next = DateTimeOffset.ParseExact(reader.GetString(2), "O", CultureInfo.InvariantCulture);
                        if (next < close) close = next;
                    }
                    result.Add(reader.GetInt32(0), close);
                }
            }
            return result;
        }

        internal static DateTimeOffset WalkInReturnBy(MySqlConnection connection, MySqlTransaction transaction,
            int roomId, int customerId, DateTimeOffset actualStart, DateTimeOffset now)
        {
            if (transaction == null || transaction.Connection != connection)
                throw new ArgumentException("Cần cùng kết nối và transaction nghiệp vụ.");
            var close = WalkInShiftEnd(actualStart);
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"SELECT MIN(StartTime) FROM Reservations WHERE Status='Confirmed' AND StartTime>@cutoff
AND StartTime>=@actualStart AND (RoomId=@room OR CustomerId=@customer);";
                command.Parameters.AddWithValue("@cutoff", Utc(now.AddMinutes(-15)));
                command.Parameters.AddWithValue("@actualStart", Utc(actualStart));
                command.Parameters.AddWithValue("@room", roomId); command.Parameters.AddWithValue("@customer", customerId);
                var next = command.ExecuteScalar();
                if (next != null && next != DBNull.Value)
                {
                    var start = DateTimeOffset.ParseExact((string)next, "O", CultureInfo.InvariantCulture);
                    if (start < close) close = start;
                }
            }
            return close;
        }
        private static DateTimeOffset WalkInShiftEnd(DateTimeOffset actualStart)
        {
            var local = actualStart.ToOffset(BookingHours.VietnamOffset);
            return new DateTimeOffset(local.Date.AddHours(local.Hour < 12 ? 12 : 23), BookingHours.VietnamOffset).ToUniversalTime();
        }
        private static string Utc(DateTimeOffset time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
