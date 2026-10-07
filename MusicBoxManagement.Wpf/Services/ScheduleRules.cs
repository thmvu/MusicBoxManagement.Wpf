using System;
using System.Data.SQLite;
using System.Globalization;

namespace MusicBoxManagement.Wpf.Services
{
    internal static class ScheduleRules
    {
        // Shared with booking availability. @cutoff is now minus the 15-minute grace.
        internal const string HoldsSql = @"SELECT RoomId,CustomerId,StartTime AS HoldStart,EndTime AS HoldEnd,ReservationId FROM Reservations
 WHERE Status='Confirmed' AND StartTime>@cutoff
UNION ALL
SELECT RoomId,CustomerId,ActualStartTime,ExpectedEndTime,ReservationId FROM RoomSessions
 WHERE Status='Active' AND ReservationId IS NOT NULL";

        // Warning only: never stored as ExpectedEndTime or used as a future hold.
        internal static DateTimeOffset WalkInReturnBy(SQLiteConnection connection, SQLiteTransaction transaction,
            int roomId, int customerId, DateTimeOffset actualStart, DateTimeOffset now)
        {
            if (transaction == null || transaction.Connection != connection)
                throw new ArgumentException("Cần cùng kết nối và transaction nghiệp vụ.");
            var local = actualStart.ToOffset(BookingHours.VietnamOffset);
            var close = new DateTimeOffset(local.Date.AddHours(local.Hour < 12 ? 12 : 23), BookingHours.VietnamOffset).ToUniversalTime();
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
        private static string Utc(DateTimeOffset time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
