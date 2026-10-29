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
    public sealed class MySqlDashboardService : IDashboardService
    {
        private readonly MySqlDatabase database;
        private readonly IClock clock;
        private readonly MySqlPermissionService permissions;
        public MySqlDashboardService(MySqlDatabase database) : this(database, new SystemClock()) { }
        public MySqlDashboardService(MySqlDatabase database, IClock clock)
        { this.database = database ?? throw new ArgumentNullException(nameof(database)); this.clock = clock ?? throw new ArgumentNullException(nameof(clock)); permissions = new MySqlPermissionService(database); }
        public DashboardSnapshot Read(LoginSession actor)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            {
                var access = DemandDashboard(actor, connection, transaction);
                var now = clock.UtcNow.ToUniversalTime(); var day = CalendarRange.Day(now.ToOffset(BookingHours.VietnamOffset).Date);
                var rooms = ReadRooms(connection, transaction, now);
                var bookings = ReadBookings(connection, transaction, day, now);
                var sessions = ReadSessions(connection, transaction, now);
                var alerts = new List<DashboardAlert>();
                foreach (var booking in bookings.Where(b => b.Item.Status == "Confirmed" && b.Item.CheckInDeadline > now && b.Item.CheckInDeadline <= now.AddMinutes(5)))
                    alerts.Add(new DashboardAlert { Kind = "NearNoShow", RoomCode = booking.Item.RoomCode,
                        ReservationId = booking.Item.ReservationId, RelevantAt = booking.Item.CheckInDeadline });
                foreach (var active in sessions)
                {
                    var session = active.Item;
                    if (session.IsOverdue) alerts.Add(Alert("Overdue", session, session.ExpectedEnd ?? session.ReturnBy.Value));
                    var localTime = now.ToOffset(BookingHours.VietnamOffset).TimeOfDay;
                    if (localTime < TimeSpan.FromHours(9) || localTime >= TimeSpan.FromHours(23)
                        || (localTime >= TimeSpan.FromHours(12) && localTime < TimeSpan.FromHours(13)))
                        alerts.Add(Alert("OutsideHours", session, now));
                    // Same room OR same customer: a customer can have their next reservation in another room.
                    var next = bookings.Where(b => b.Item.Status == "Confirmed" && b.Item.ReservationId != active.ReservationId
                        && b.Item.Start >= session.ActualStart && (b.Item.RoomId == session.RoomId || b.CustomerId == active.CustomerId))
                        .OrderBy(b => b.Item.Start).ThenBy(b => b.Item.ReservationId).FirstOrDefault();
                    if (next != null && (session.IsWalkIn ? next.Item.Start <= session.ReturnBy.Value
                        : next.Item.Start < session.ExpectedEnd.Value || now >= next.Item.Start))
                    {
                        var warning = Alert("NextBooking", session, next.Item.Start); warning.ReservationId = next.Item.ReservationId;
                        alerts.Add(warning);
                    }
                }
                return new DashboardSnapshot { Date = now.ToOffset(BookingHours.VietnamOffset).Date, CheckedAt = now,
                    Rooms = rooms.AsReadOnly(), BookingsToday = bookings.Where(b => b.Item.Start >= day.Start && b.Item.Start < day.End).Select(b => b.Item).ToList().AsReadOnly(),
                    ActiveSessions = sessions.Select(s => s.Item).ToList().AsReadOnly(), PendingOrders = ReadPending(connection, transaction).AsReadOnly(),
                    Alerts = alerts.OrderBy(a => a.RelevantAt).ThenBy(a => a.RoomCode, StringComparer.Ordinal).ThenBy(a => a.Kind, StringComparer.Ordinal).ToList().AsReadOnly(),
                    TodayRevenue = CanReadRevenue(access) ? (decimal?)ReadRevenue(connection, transaction, day) : null };
            }
        }
        public decimal ReadRevenueToday(LoginSession actor)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            {
                var access = DemandDashboard(actor, connection, transaction);
                if (!CanReadRevenue(access)) throw new UnauthorizedAccessException("Chỉ Admin hoặc Manager có quyền báo cáo được xem doanh thu tổng hợp.");
                var now = clock.UtcNow.ToUniversalTime();
                return ReadRevenue(connection, transaction, CalendarRange.Day(now.ToOffset(BookingHours.VietnamOffset).Date));
            }
        }
        private StaffAccess DemandDashboard(LoginSession actor, MySqlConnection connection, MySqlTransaction transaction)
        {
            var access = permissions.ReadAccess(actor, connection, transaction);
            if (!access.Permissions.ContainsKey("Dashboard.View")) throw new UnauthorizedAccessException("Bạn không có quyền xem bảng điều hành.");
            return access;
        }
        private static bool CanReadRevenue(StaffAccess access) => access.Role == "Admin" || (access.Role == "Manager" && access.Permissions.ContainsKey("Report.View"));
        private static List<DashboardRoom> ReadRooms(MySqlConnection connection, MySqlTransaction transaction, DateTimeOffset now)
        {
            var result = new List<DashboardRoom>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction; command.CommandText = "SELECT RoomId,RoomCode,Name,IsActive FROM Rooms ORDER BY RoomCode;";
                using (var reader = command.ExecuteReader()) while (reader.Read()) result.Add(new DashboardRoom { RoomId = reader.GetInt32(0),
                    RoomCode = reader.GetString(1), RoomName = reader.GetString(2), Status = reader.GetInt32(3) == 1 ? "Available" : "Inactive" });
            }
            foreach (var room in result) room.Status = MySqlScheduleRules.CurrentRoomStatus(connection, transaction, room.RoomId, room.Status != "Inactive", now);
            return result;
        }
        private sealed class BookingRecord { public DashboardBooking Item; public int CustomerId; }
        private static List<BookingRecord> ReadBookings(MySqlConnection connection, MySqlTransaction transaction, CalendarRange day, DateTimeOffset now)
        {
            var result = new List<BookingRecord>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction; command.Parameters.AddWithValue("@from", Utc(day.Start)); command.Parameters.AddWithValue("@to", Utc(day.End));
                command.Parameters.AddWithValue("@cutoff", Utc(now.AddMinutes(-15)));
                command.CommandText = @"SELECT b.ReservationId,b.RoomId,r.RoomCode,b.StartTime,b.EndTime,b.Status,b.CustomerId FROM Reservations b JOIN Rooms r ON r.RoomId=b.RoomId
WHERE (b.StartTime>=@from AND b.StartTime<@to) OR (b.Status='Confirmed' AND b.StartTime>@cutoff) ORDER BY b.StartTime,b.ReservationId;";
                using (var reader = command.ExecuteReader()) while (reader.Read())
                {
                    var start = Parse(reader.GetString(3)); var status = reader.GetString(5);
                    if (status == "Confirmed" && now >= start.AddMinutes(15)) status = "NoShow";
                    result.Add(new BookingRecord { CustomerId = reader.GetInt32(6), Item = new DashboardBooking { ReservationId = reader.GetInt32(0),
                        RoomId = reader.GetInt32(1), RoomCode = reader.GetString(2), Start = start, End = Parse(reader.GetString(4)), Status = status } });
                }
            }
            return result;
        }
        private sealed class SessionRecord { public DashboardSession Item; public int CustomerId; public int? ReservationId; }
        private static List<SessionRecord> ReadSessions(MySqlConnection connection, MySqlTransaction transaction, DateTimeOffset now)
        {
            var result = new List<SessionRecord>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT RoomSessionId,RoomId,RoomCodeSnapshot,ActualStartTime,ExpectedEndTime,ReservationId,CustomerId FROM RoomSessions WHERE Status='Active' ORDER BY ActualStartTime,RoomSessionId;";
                using (var reader = command.ExecuteReader()) while (reader.Read()) result.Add(new SessionRecord { CustomerId = reader.GetInt32(6),
                    ReservationId = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5), Item = new DashboardSession { SessionId = reader.GetInt32(0), RoomId = reader.GetInt32(1),
                        RoomCode = reader.GetString(2), ActualStart = Parse(reader.GetString(3)), ExpectedEnd = reader.IsDBNull(4) ? (DateTimeOffset?)null : Parse(reader.GetString(4)), IsWalkIn = reader.IsDBNull(5) } });
            }
            foreach (var record in result)
            {
                var session = record.Item;
                if (session.IsWalkIn) session.ReturnBy = MySqlScheduleRules.WalkInReturnBy(connection, transaction, session.RoomId, record.CustomerId, session.ActualStart, now);
                session.IsOverdue = now > (session.ExpectedEnd ?? session.ReturnBy.Value);
            }
            return result;
        }
        private static List<DashboardOrder> ReadPending(MySqlConnection connection, MySqlTransaction transaction)
        {
            var result = new List<DashboardOrder>();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT o.OrderId,o.RoomSessionId,s.RoomCodeSnapshot,o.CreatedAt FROM Orders o JOIN RoomSessions s ON s.RoomSessionId=o.RoomSessionId WHERE o.Status='Pending' AND s.Status='Active' ORDER BY o.CreatedAt,o.OrderId;";
                using (var reader = command.ExecuteReader()) while (reader.Read()) result.Add(new DashboardOrder { OrderId = reader.GetInt32(0), SessionId = reader.GetInt32(1), RoomCode = reader.GetString(2), CreatedAt = Parse(reader.GetString(3)) });
            }
            return result;
        }
        private static decimal ReadRevenue(MySqlConnection connection, MySqlTransaction transaction, CalendarRange day)
        {
            decimal total = 0;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction; command.Parameters.AddWithValue("@from", Utc(day.Start)); command.Parameters.AddWithValue("@to", Utc(day.End));
                command.CommandText = "SELECT TotalAmount FROM Invoices WHERE PaidAt>=@from AND PaidAt<@to ORDER BY InvoiceId;";
                using (var reader = command.ExecuteReader()) while (reader.Read())
                {
                    try { total += reader.GetDecimal(0); }
                    catch (OverflowException) { throw new InvalidOperationException("Tổng doanh thu vượt phạm vi số tiền hỗ trợ."); }
                }
            }
            return total;
        }
        private static DashboardAlert Alert(string kind, DashboardSession session, DateTimeOffset at) => new DashboardAlert { Kind = kind, RoomCode = session.RoomCode, SessionId = session.SessionId, RelevantAt = at };
        private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        private static DateTimeOffset Parse(string value) => DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture);
    }
}
