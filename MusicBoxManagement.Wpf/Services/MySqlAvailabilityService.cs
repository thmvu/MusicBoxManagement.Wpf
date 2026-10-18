using System;
using System.Data;
using MySqlConnector;
using System.Globalization;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    // Business-rule helper, not an authorization boundary. Booking writers must
    // authorize the caller and resolve the customer before using the transaction overload.
    public sealed class MySqlAvailabilityService
    {
        private readonly MySqlDatabase database;
        private readonly IClock clock;
        public MySqlAvailabilityService(MySqlDatabase database) : this(database, new SystemClock()) { }
        public MySqlAvailabilityService(MySqlDatabase database, IClock clock)
        { this.database = database ?? throw new ArgumentNullException(nameof(database)); this.clock = clock ?? throw new ArgumentNullException(nameof(clock)); }

        // Preview only. A successful result does not reserve anything.
        public ReservationAvailability CheckReservation(int roomId, int? customerId, DateTimeOffset start, int durationMinutes)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
                return CheckReservationAt(connection, transaction, roomId, customerId, start, durationMinutes, clock.UtcNow);
        }

        // Serializable writer: lock Room then Customer before reading the clock.
        // RepeatableRead preview: no locks and no promise that the result remains free.
        public ReservationAvailability CheckReservation(MySqlConnection connection, MySqlTransaction transaction,
            int roomId, int? customerId, DateTimeOffset start, int durationMinutes)
        {
            if (transaction == null || transaction.Connection != connection)
                throw new ArgumentException("Cần cùng kết nối và transaction nghiệp vụ.");
            if (transaction.IsolationLevel == IsolationLevel.Serializable)
                LockResources(connection, transaction, roomId, customerId);
            return CheckReservationAt(connection, transaction, roomId, customerId, start, durationMinutes, clock.UtcNow);
        }

        // At helpers reuse one transaction clock. Writers must LockResources first.
        internal ReservationAvailability CheckReservationAt(MySqlConnection connection, MySqlTransaction transaction,
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
                command.CommandText = @"SELECT RoomId FROM (" + MySqlScheduleRules.HoldsSql + @") holds WHERE (RoomId=@room OR CustomerId=@customer) AND @start<HoldEnd AND @end>HoldStart
ORDER BY (RoomId=@room) DESC LIMIT 1;";
                var conflict = command.ExecuteScalar();
                if (conflict != null)
                { result.Reason = Convert.ToInt32(conflict) == roomId ? "Phòng có lịch trùng khoảng đã chọn." : "Khách có lịch trùng khoảng đã chọn."; return result; }
            }
            result.CanBook = true; result.Reason = "Khoảng giờ hợp lệ; cần kiểm tra lại khi lưu đặt phòng.";
            return result;
        }
        internal ReservationAvailability CheckCheckInAt(MySqlConnection connection, MySqlTransaction transaction,
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
                command.CommandText = "SELECT RoomId FROM (" + MySqlScheduleRules.HoldsSql + @") holds WHERE ReservationId<>@source
AND (RoomId=@room OR CustomerId=@customer) AND @start<HoldEnd AND @end>HoldStart ORDER BY (RoomId=@room) DESC LIMIT 1;";
                var conflict = command.ExecuteScalar();
                if (conflict != null)
                { result.Reason = Convert.ToInt32(conflict) == roomId ? "Phòng có lịch trùng khoảng sử dụng thực tế dự kiến." : "Khách có lịch trùng khoảng sử dụng thực tế dự kiến."; return result; }
            }
            result.CanBook = true; result.Reason = "Đủ điều kiện nhận phòng.";
            return result;
        }
        internal ReservationAvailability CheckWalkInAt(MySqlConnection connection, MySqlTransaction transaction,
            int roomId, int customerId, DateTimeOffset now)
        {
            if (transaction == null || transaction.Connection != connection)
                throw new ArgumentException("Cần cùng kết nối và transaction nghiệp vụ.");
            var result = new ReservationAvailability { CheckedAt = now };
            try { BookingHours.GetOpenShiftEnd(now); }
            catch (ArgumentException error) { result.Reason = error.Message; return result; }
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.Parameters.AddWithValue("@room", roomId); command.Parameters.AddWithValue("@customer", customerId);
                command.CommandText = "SELECT IsActive FROM Rooms WHERE RoomId=@room;";
                var active = command.ExecuteScalar();
                if (active == null || Convert.ToInt32(active) != 1)
                { result.Reason = "Phòng không tồn tại hoặc đang khóa, không nhận khách trực tiếp được."; return result; }
                command.CommandText = "SELECT RoomId FROM RoomSessions WHERE Status='Active' AND (RoomId=@room OR CustomerId=@customer) ORDER BY (RoomId=@room) DESC LIMIT 1;";
                var occupied = command.ExecuteScalar();
                if (occupied != null)
                { result.Reason = Convert.ToInt32(occupied) == roomId ? "Phòng còn phiên Active, chưa thể nhận khách." : "Khách còn phiên Active, chưa thể nhận phòng."; return result; }
                command.Parameters.AddWithValue("@now", Utc(now)); command.Parameters.AddWithValue("@cutoff", Utc(now.AddMinutes(-15)));
                command.CommandText = @"SELECT RoomId FROM Reservations WHERE Status='Confirmed' AND StartTime>@cutoff AND StartTime<=@now
AND (RoomId=@room OR CustomerId=@customer) ORDER BY (RoomId=@room) DESC LIMIT 1;";
                var booking = command.ExecuteScalar();
                if (booking != null)
                { result.Reason = Convert.ToInt32(booking) == roomId ? "Phòng có booking đã tới giờ và còn hạn; hãy nhận phòng từ booking hoặc xử lý booking trước." : "Khách có booking đã tới giờ và còn hạn; hãy xử lý booking trước."; return result; }
            }
            result.CanBook = true; result.Reason = "Đủ điều kiện nhận khách trực tiếp."; return result;
        }
        internal SessionExtensionCheck CheckExtensionAt(MySqlConnection connection, MySqlTransaction transaction,
            RoomSession session, int minutes, DateTimeOffset now)
        {
            if (transaction == null || transaction.Connection != connection)
                throw new ArgumentException("Cần cùng kết nối và transaction nghiệp vụ.");
            if (minutes != 30 && minutes != 60) throw new ArgumentException("Mỗi lần gia hạn chỉ chọn 30 hoặc 60 phút.");
            if (session == null || session.Status != "Active" || !session.ReservationId.HasValue || !session.ExpectedEndTime.HasValue)
                throw new InvalidOperationException("Chỉ gia hạn phiên Active từ booking; khách trực tiếp không có gia hạn.");
            now = now.ToUniversalTime();
            var oldEnd = session.ExpectedEndTime.Value.ToUniversalTime();
            var newEnd = oldEnd.AddMinutes(minutes);
            var result = new SessionExtensionCheck { ExpectedEndTime = oldEnd, NewEndTime = newEnd,
                MaximumEndTime = oldEnd, CheckedAt = now };
            if (now > oldEnd) { result.Reason = "Phiên đã quá giờ dự kiến, không thể gia hạn. Nhân viên cần xử lý trực tiếp."; return result; }
            BookingHours.ValidateSessionInterval(session.ActualStartTime, oldEnd);
            var limit = BookingHours.GetOpenShiftEnd(session.ActualStartTime);
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.Parameters.AddWithValue("@room", session.RoomId);
                command.Parameters.AddWithValue("@customer", session.CustomerId);
                // ReservationId uniquely identifies the source session in HoldsSql.
                // CheckedIn source bookings already do not hold the original interval.
                command.Parameters.AddWithValue("@source", session.ReservationId.Value);
                command.Parameters.AddWithValue("@oldEnd", Utc(oldEnd));
                command.Parameters.AddWithValue("@shiftEnd", Utc(limit));
                command.Parameters.AddWithValue("@cutoff", Utc(now.AddMinutes(-15)));
                command.CommandText = @"SELECT MIN(HoldStart) FROM (" + MySqlScheduleRules.HoldsSql + @") holds
WHERE ReservationId<>@source AND (RoomId=@room OR CustomerId=@customer)
AND HoldEnd>@oldEnd AND HoldStart<@shiftEnd;";
                var next = command.ExecuteScalar();
                if (next != null && next != DBNull.Value)
                {
                    var start = DateTimeOffset.Parse((string)next, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                    limit = start < oldEnd ? oldEnd : start;
                }
            }
            result.MaximumEndTime = limit;
            result.CanExtend = newEnd <= limit;
            result.Reason = result.CanExtend ? "Có thể gia hạn; lịch sẽ được kiểm tra lại khi lưu."
                : "Không đủ thời gian gia hạn. Giới hạn sử dụng theo ca/lịch phòng và khách: " + limit.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy") + ".";
            return result;
        }
        // All future booking/check-in/walk-in/extension/checkout writers must use this
        // order before taking IClock and checking predicates. Preview never locks.
        public static void LockResources(MySqlConnection connection, MySqlTransaction transaction, int roomId, int? customerId)
        {
            if (transaction == null || transaction.Connection != connection || transaction.IsolationLevel != IsolationLevel.Serializable)
                throw new ArgumentException("Cần cùng kết nối và transaction ghi Serializable.");
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT RoomId FROM Rooms WHERE RoomId=@room FOR UPDATE;";
                command.Parameters.AddWithValue("@room", roomId); command.ExecuteScalar();
                if (customerId.HasValue)
                {
                    command.CommandText = "SELECT CustomerId FROM Customers WHERE CustomerId=@customer FOR UPDATE;";
                    command.Parameters.AddWithValue("@customer", customerId.Value); command.ExecuteScalar();
                }
            }
        }
        private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
