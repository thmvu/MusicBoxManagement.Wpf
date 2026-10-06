using System;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using MusicBoxManagement.Wpf.Data;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class ReservationAvailability
    {
        public bool CanBook { get; internal set; }
        public string Reason { get; internal set; }
        public DateTimeOffset? EndTime { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
    }

    // Business-rule helper, not an authorization boundary. Booking writers must
    // authorize the caller and resolve the customer before using the transaction overload.
    public sealed class AvailabilityService
    {
        private readonly SqliteDatabase database;
        private readonly IClock clock;
        public AvailabilityService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public AvailabilityService(SqliteDatabase database, IClock clock)
        { this.database = database ?? throw new ArgumentNullException(nameof(database)); this.clock = clock ?? throw new ArgumentNullException(nameof(clock)); }

        // Preview only. A successful result does not reserve anything.
        public ReservationAvailability CheckReservation(int roomId, int? customerId, DateTimeOffset start, int durationMinutes)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
                return CheckReservation(connection, transaction, roomId, customerId, start, durationMinutes);
        }

        // Reuse inside the caller's BeginWriteTransaction, before inserting a booking.
        public ReservationAvailability CheckReservation(SQLiteConnection connection, SQLiteTransaction transaction,
            int roomId, int? customerId, DateTimeOffset start, int durationMinutes)
        { return CheckReservationAt(connection, transaction, roomId, customerId, start, durationMinutes, clock.UtcNow); }

        internal ReservationAvailability CheckReservationAt(SQLiteConnection connection, SQLiteTransaction transaction,
            int roomId, int? customerId, DateTimeOffset start, int durationMinutes, DateTimeOffset now)
        {
            if (transaction == null || transaction.Connection != connection)
                throw new ArgumentException("Cần cùng kết nối và transaction nghiệp vụ.");
            now = now.ToUniversalTime();
            var result = new ReservationAvailability { CheckedAt = now };
            try { result.EndTime = BookingHours.ValidateReservation(start, durationMinutes, now); }
            catch (ArgumentException error) { result.Reason = error.Message; return result; }
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.Parameters.AddWithValue("@room", roomId);
                command.CommandText = "SELECT IsActive FROM Rooms WHERE RoomId=@room;";
                var active = command.ExecuteScalar();
                if (active == null) { result.Reason = "Không tìm thấy phòng."; return result; }
                if (Convert.ToInt32(active) != 1) { result.Reason = "Phòng đang khóa, không nhận đặt phòng."; return result; }
                command.Parameters.AddWithValue("@customer", (object)customerId ?? DBNull.Value);
                if (customerId.HasValue)
                {
                    command.CommandText = "SELECT COUNT(*) FROM Customers WHERE CustomerId=@customer;";
                    if (Convert.ToInt64(command.ExecuteScalar()) == 0) { result.Reason = "Không tìm thấy khách hàng."; return result; }
                }
                // At exactly now, even walk-ins and overdue sessions block immediate booking.
                if (start == now)
                {
                    command.CommandText = "SELECT RoomId FROM RoomSessions WHERE Status='Active' AND (RoomId=@room OR CustomerId=@customer) ORDER BY (RoomId=@room) DESC LIMIT 1;";
                    var occupiedRoom = command.ExecuteScalar();
                    if (occupiedRoom != null)
                    { result.Reason = Convert.ToInt32(occupiedRoom) == roomId ? "Phòng còn phiên Active, không đặt bắt đầu ngay được." : "Khách còn phiên Active, không đặt bắt đầu ngay được."; return result; }
                }
                command.Parameters.AddWithValue("@start", Utc(start));
                command.Parameters.AddWithValue("@end", Utc(result.EndTime.Value));
                command.Parameters.AddWithValue("@cutoff", Utc(now.AddMinutes(-15)));
                command.CommandText = @"SELECT RoomId FROM (" + ScheduleRules.HoldsSql + @") WHERE (RoomId=@room OR CustomerId=@customer) AND @start<HoldEnd AND @end>HoldStart
ORDER BY (RoomId=@room) DESC LIMIT 1;";
                var conflict = command.ExecuteScalar();
                if (conflict != null)
                { result.Reason = Convert.ToInt32(conflict) == roomId ? "Phòng có lịch trùng khoảng đã chọn." : "Khách có lịch trùng khoảng đã chọn."; return result; }
            }
            result.CanBook = true; result.Reason = "Khoảng giờ hợp lệ; cần kiểm tra lại khi lưu đặt phòng.";
            return result;
        }
        internal ReservationAvailability CheckCheckInAt(SQLiteConnection connection, SQLiteTransaction transaction,
            int roomId, int customerId, int reservationId, DateTimeOffset now, DateTimeOffset end)
        {
            if (transaction == null || transaction.Connection != connection)
                throw new ArgumentException("Cần cùng kết nối và transaction nghiệp vụ.");
            var result = new ReservationAvailability { CheckedAt = now, EndTime = end };
            try { BookingHours.ValidateSessionInterval(now, end); }
            catch (ArgumentException error) { result.Reason = error.Message; return result; }
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.Parameters.AddWithValue("@room", roomId);
                command.Parameters.AddWithValue("@customer", customerId);
                command.CommandText = "SELECT IsActive FROM Rooms WHERE RoomId=@room;";
                var active = command.ExecuteScalar();
                if (active == null || Convert.ToInt32(active) != 1)
                { result.Reason = "Phòng không tồn tại hoặc đang khóa, không nhận phòng được."; return result; }
                command.CommandText = "SELECT RoomId FROM RoomSessions WHERE Status='Active' AND (RoomId=@room OR CustomerId=@customer) ORDER BY (RoomId=@room) DESC LIMIT 1;";
                var occupied = command.ExecuteScalar();
                if (occupied != null)
                { result.Reason = Convert.ToInt32(occupied) == roomId ? "Phòng còn phiên Active, chưa thể nhận khách." : "Khách còn phiên Active, chưa thể nhận phòng."; return result; }
                command.Parameters.AddWithValue("@source", reservationId);
                command.Parameters.AddWithValue("@start", Utc(now));
                command.Parameters.AddWithValue("@end", Utc(end));
                command.Parameters.AddWithValue("@cutoff", Utc(now.AddMinutes(-15)));
                command.CommandText = "SELECT RoomId FROM (" + ScheduleRules.HoldsSql + @") WHERE ReservationId<>@source
AND (RoomId=@room OR CustomerId=@customer) AND @start<HoldEnd AND @end>HoldStart ORDER BY (RoomId=@room) DESC LIMIT 1;";
                var conflict = command.ExecuteScalar();
                if (conflict != null)
                { result.Reason = Convert.ToInt32(conflict) == roomId ? "Phòng có lịch trùng khoảng sử dụng thực tế dự kiến." : "Khách có lịch trùng khoảng sử dụng thực tế dự kiến."; return result; }
            }
            result.CanBook = true; result.Reason = "Đủ điều kiện nhận phòng.";
            return result;
        }
        private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
