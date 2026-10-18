using System;
using System.Collections.Generic;
using System.Globalization;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    // Internal booking reads/cancellation only. No Guest fallback or check-in stub.
    public sealed class MySqlStaffReservationService
    {
        private readonly MySqlDatabase database;
        private readonly IClock clock;
        private readonly MySqlPermissionService permissions;
        public MySqlStaffReservationService(MySqlDatabase database) : this(database, new SystemClock()) { }
        public MySqlStaffReservationService(MySqlDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new MySqlPermissionService(database);
        }

        public StaffReservationSearch Search(LoginSession actor, DateTime? fromDate = null, DateTime? toDate = null,
            string phoneNumber = null, string status = null)
        {
            permissions.Demand(actor, "Reservation.View");
            var phone = string.IsNullOrWhiteSpace(phoneNumber) ? null : PhoneNumberNormalizer.Normalize(phoneNumber);
            if (fromDate.HasValue && fromDate.Value.Date == DateTime.MinValue.Date ||
                toDate.HasValue && toDate.Value.Date == DateTime.MaxValue.Date ||
                fromDate.HasValue && toDate.HasValue && fromDate.Value.Date > toDate.Value.Date)
                throw new ArgumentException("Khoảng ngày tìm booking không hợp lệ.");
            if (!string.IsNullOrEmpty(status) && status != "Confirmed" && status != "CheckedIn" && status != "Completed" && status != "Cancelled" && status != "NoShow")
                throw new ArgumentException("Trạng thái booking không hợp lệ.");
            new MySqlNoShowService(database, clock).ProcessExpired();
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            using (var command = connection.CreateCommand())
            {
                // Check again after maintenance, inside the same snapshot as private data.
                var access = permissions.ReadAccess(actor, connection, transaction);
                if (!access.Permissions.ContainsKey("Reservation.View"))
                    throw new UnauthorizedAccessException("Bạn không có quyền xem booking.");
                var now = clock.UtcNow.ToUniversalTime();
                command.Transaction = transaction;
                command.CommandText = @"SELECT b.ReservationId,b.CustomerId,c.FullName,c.PhoneNumber,r.RoomCode,r.Name,
b.StartTime,b.EndTime,b.Status,b.CancellationReason,b.CreatedAt,
CASE WHEN b.CreatedByUserId IS NULL THEN 'Khách' ELSE u.FullName END,s.RoomSessionId,t.PricePerHour
FROM Reservations b JOIN Customers c ON c.CustomerId=b.CustomerId JOIN Rooms r ON r.RoomId=b.RoomId
JOIN RoomTypes t ON t.RoomTypeId=r.RoomTypeId
LEFT JOIN AspNetUsers u ON u.Id=b.CreatedByUserId LEFT JOIN RoomSessions s ON s.ReservationId=b.ReservationId
WHERE (@from IS NULL OR b.StartTime>=@from) AND (@to IS NULL OR b.StartTime<@to)
AND (@phone IS NULL OR c.PhoneNumber=@phone) AND (@status IS NULL OR b.Status=@status)
ORDER BY b.StartTime,b.ReservationId;";
                command.Parameters.AddWithValue("@from", fromDate.HasValue ? (object)DayUtc(fromDate.Value) : DBNull.Value);
                command.Parameters.AddWithValue("@to", toDate.HasValue ? (object)DayUtc(toDate.Value.Date.AddDays(1)) : DBNull.Value);
                command.Parameters.AddWithValue("@phone", (object)phone ?? DBNull.Value);
                command.Parameters.AddWithValue("@status", string.IsNullOrEmpty(status) ? (object)DBNull.Value : status);
                var items = new List<StaffReservation>();
                using (var reader = command.ExecuteReader()) while (reader.Read())
                {
                    var start = Parse(reader.GetString(6)); var state = reader.GetString(8);
                    items.Add(new StaffReservation {
                        ReservationId = reader.GetInt32(0), CustomerId = reader.GetInt32(1), CustomerName = reader.GetString(2),
                        PhoneNumber = reader.GetString(3), RoomCode = reader.GetString(4), RoomName = reader.GetString(5),
                        StartTime = start, EndTime = Parse(reader.GetString(7)), Status = state,
                        CancellationReason = reader.IsDBNull(9) ? null : reader.GetString(9), CreatedAt = Parse(reader.GetString(10)),
                        CreatedByName = reader.GetString(11), SessionId = reader.IsDBNull(12) ? (int?)null : reader.GetInt32(12),
                        CurrentHourlyRate = reader.GetInt64(13),
                        IsCancellable = state == "Confirmed" && now < start.AddMinutes(15) && reader.IsDBNull(12)
                    });
                }
                return new StaffReservationSearch { Items = items, CanCancel = access.Permissions.ContainsKey("Reservation.Cancel"),
                    CanCreate = access.Permissions.ContainsKey("Reservation.Create"), CanCheckIn = access.Permissions.ContainsKey("Session.CheckIn") };
            }
        }

        public void Cancel(LoginSession actor, int reservationId, string reason) =>
            new MySqlReservationService(database, clock).CancelStaff(actor, reservationId, reason);
        private static string DayUtc(DateTime value) => new DateTimeOffset(
            DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified), BookingHours.VietnamOffset)
            .ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        private static DateTimeOffset Parse(string value) => DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture).ToUniversalTime();
    }
}
