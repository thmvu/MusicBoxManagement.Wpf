using System;
using System.Collections.Generic;
using System.Globalization;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MySqlConnector;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class MySqlGuestReservationService
    {
        private readonly MySqlDatabase database;
        private readonly IClock clock;
        private readonly MySqlNoShowService noShows;
        public MySqlGuestReservationService(MySqlDatabase database) : this(database, new SystemClock()) { }
        public MySqlGuestReservationService(MySqlDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            noShows = new MySqlNoShowService(database, clock);
        }
        internal MySqlGuestSessionService ForSessions() => new MySqlGuestSessionService(database, clock);
        internal MySqlOrderService ForOrders() => new MySqlOrderService(database, clock);

        public GuestReservationLookup Lookup(string phoneNumber)
        {
            var phone = PhoneNumberNormalizer.Normalize(phoneNumber);
            // Match the existing Guest lookup contract: maintenance commits separately.
            noShows.ProcessExpired();
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                // Pin membership/data to the same RR snapshot before capturing the read clock.
                command.CommandText = "SELECT CustomerId FROM Customers WHERE PhoneNumber=@phone;";
                command.Parameters.AddWithValue("@phone", phone); command.ExecuteScalar();
                var now = clock.UtcNow.ToUniversalTime();
                command.CommandText = @"SELECT b.ReservationId,r.RoomCode,r.Name,b.StartTime,b.EndTime FROM Reservations b
JOIN Customers c ON c.CustomerId=b.CustomerId JOIN Rooms r ON r.RoomId=b.RoomId
WHERE c.PhoneNumber=@phone AND b.Status='Confirmed' AND b.StartTime>@cutoff
ORDER BY b.StartTime,b.ReservationId;";
                command.Parameters.AddWithValue("@cutoff", Utc(now.AddMinutes(-15)));
                var items = new List<GuestReservation>();
                using (var reader = command.ExecuteReader()) while (reader.Read())
                {
                    var start = Time(reader.GetString(3));
                    items.Add(new GuestReservation { ReservationId = reader.GetInt32(0), RoomCode = reader.GetString(1),
                        RoomName = reader.GetString(2), StartTime = start, EndTime = Time(reader.GetString(4)),
                        CanCancel = now <= start.AddHours(-2) });
                }
                return new GuestReservationLookup { Items = items, CheckedAt = now,
                    ActiveSession = MySqlGuestSessionService.ReadActiveAt(connection, transaction, phone, now) };
            }
        }

        public void Cancel(string phoneNumber, int reservationId)
        {
            var phone = PhoneNumberNormalizer.Normalize(phoneNumber);
            try
            {
                int room, customer;
                using (var discovery = database.OpenConnection())
                using (var query = discovery.CreateCommand())
                {
                    query.CommandText = @"SELECT b.RoomId,b.CustomerId FROM Reservations b
JOIN Customers c ON c.CustomerId=b.CustomerId WHERE b.ReservationId=@id AND c.PhoneNumber=@phone;";
                    query.Parameters.AddWithValue("@id", reservationId); query.Parameters.AddWithValue("@phone", phone);
                    using (var reader = query.ExecuteReader())
                    {
                        if (!reader.Read()) throw Missing();
                        room = reader.GetInt32(0); customer = reader.GetInt32(1);
                    }
                }
                using (var connection = database.OpenConnection())
                using (var transaction = MySqlDatabase.BeginWriteTransaction(connection))
                using (var command = connection.CreateCommand())
                {
                    MySqlAvailabilityService.LockResources(connection, transaction, room, customer);
                    MySqlNoShowService.LockCandidates(connection, transaction);
                    command.Transaction = transaction;
                    command.CommandText = "SELECT ReservationId FROM Reservations WHERE ReservationId=@id FOR UPDATE;";
                    command.Parameters.AddWithValue("@id", reservationId); command.ExecuteScalar();
                    command.CommandText = @"SELECT b.RoomId,b.CustomerId,b.StartTime,b.Status,
EXISTS(SELECT 1 FROM RoomSessions s WHERE s.ReservationId=b.ReservationId)
FROM Reservations b JOIN Customers c ON c.CustomerId=b.CustomerId
WHERE b.ReservationId=@id AND c.PhoneNumber=@phone;";
                    command.Parameters.AddWithValue("@phone", phone);
                    DateTimeOffset start; string status; bool hasSession;
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read() || reader.GetInt32(0) != room || reader.GetInt32(1) != customer) throw Missing();
                        start = Time(reader.GetString(2)); status = reader.GetString(3); hasSession = reader.GetInt32(4) != 0;
                    }
                    var now = clock.UtcNow.ToUniversalTime();
                    if (status != "Confirmed" || now >= start.AddMinutes(15) || hasSession)
                        throw new InvalidOperationException("Booking đã đổi trạng thái hoặc hết hạn. Hãy tra cứu lại.");
                    if (now > start.AddHours(-2))
                        throw new InvalidOperationException("Còn dưới 2 giờ đến giờ đặt. Vui lòng liên hệ cửa hàng để xử lý.");
                    command.CommandText = "UPDATE Reservations SET Status='Cancelled',CancellationReason='Customer cancelled online' WHERE ReservationId=@id AND Status='Confirmed';";
                    if (command.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("Booking đã đổi trạng thái. Hãy tra cứu lại.");
                    MySqlAuditService.WriteGuest(connection, transaction, "Reservation.Cancel", "Reservation",
                        reservationId.ToString(CultureInfo.InvariantCulture), "Customer cancelled online", now);
                    transaction.Commit();
                }
            }
            catch (MySqlException error) when (error.Number == 1213 || error.Number == 1205)
            { throw new InvalidOperationException("Booking đang được cập nhật. Hãy tra cứu lại trước khi hủy.", error); }
        }
        private static InvalidOperationException Missing()
            => new InvalidOperationException("Không tìm thấy booking của SĐT này. Hãy tra cứu lại.");
        private static DateTimeOffset Time(string value) => DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture);
        private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
