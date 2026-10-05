using System;
using System.Data.SQLite;
using System.Globalization;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class ReservationService
    {
        private readonly SqliteDatabase database;
        private readonly IClock clock;
        private readonly PermissionService permissions;
        private readonly AvailabilityService availability;
        public ReservationService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public ReservationService(SqliteDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database)); this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new PermissionService(database); availability = new AvailabilityService(database, clock);
        }
        public Reservation CreateGuest(ReservationRequest request) => Create(null, request, true);
        public Reservation CreateStaff(LoginSession session, ReservationRequest request) => Create(session, request, false);
        private Reservation Create(LoginSession session, ReservationRequest request, bool guest)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = SqliteDatabase.BeginWriteTransaction(connection))
            using (var command = connection.CreateCommand())
            {
                if (!guest) permissions.Demand(session, "Reservation.Create", connection, transaction);
                if (request == null) throw new ArgumentException("Cần nhập thông tin đặt phòng.");
                var room = request.RoomId; var start = request.StartTime; var duration = request.DurationMinutes;
                var name = (request.FullName ?? "").Trim();
                if (name.Length < 1 || name.Length > 100) throw new ArgumentException("Họ tên từ 1–100 ký tự.");
                var phone = PhoneNumberNormalizer.Normalize(request.PhoneNumber);
                var now = clock.UtcNow.ToUniversalTime();
                BookingHours.ValidateReservation(start, duration, now);
                NoShowService.ProcessExpiredAt(connection, transaction, now);
                command.Transaction = transaction;
                command.CommandText = "SELECT CustomerId FROM Customers WHERE PhoneNumber=@phone;";
                command.Parameters.AddWithValue("@phone", phone);
                var found = command.ExecuteScalar();
                int customer;
                if (found != null) customer = Convert.ToInt32(found);
                else
                {
                    command.CommandText = "INSERT INTO Customers(FullName,PhoneNumber) VALUES(@name,@phone); SELECT last_insert_rowid();";
                    command.Parameters.AddWithValue("@name", name); customer = Convert.ToInt32(command.ExecuteScalar());
                    var customerId = customer.ToString(CultureInfo.InvariantCulture);
                    if (guest) AuditService.WriteGuest(connection, transaction, "Customer.Create", "Customer", customerId, "Tạo khách hàng từ đặt phòng.", now);
                    else AuditService.WriteStaff(connection, transaction, session.UserId, "Customer.Create", "Customer", customerId, "Tạo khách hàng từ đặt phòng.", now);
                }
                var result = availability.CheckReservationAt(connection, transaction, room, customer, start, duration, now);
                if (!result.CanBook) throw new InvalidOperationException(result.Reason);
                command.Parameters.Clear();
                command.CommandText = @"INSERT INTO Reservations(CustomerId,RoomId,StartTime,EndTime,Status,CreatedByUserId,CreatedAt)
VALUES(@customer,@room,@start,@end,'Confirmed',@user,@now); SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("@customer", customer); command.Parameters.AddWithValue("@room", room);
                command.Parameters.AddWithValue("@start", Utc(start)); command.Parameters.AddWithValue("@end", Utc(result.EndTime.Value));
                command.Parameters.AddWithValue("@user", guest ? (object)DBNull.Value : session.UserId); command.Parameters.AddWithValue("@now", Utc(now));
                var id = Convert.ToInt32(command.ExecuteScalar());
                var idText = id.ToString(CultureInfo.InvariantCulture);
                if (guest) AuditService.WriteGuest(connection, transaction, "Reservation.Create", "Reservation", idText, "Đặt phòng " + room + ", khách " + customer + ".", now);
                else AuditService.WriteStaff(connection, transaction, session.UserId, "Reservation.Create", "Reservation", idText, "Đặt phòng " + room + ", khách " + customer + ".", now);
                transaction.Commit();
                return new Reservation { ReservationId = id, CustomerId = customer, RoomId = room, StartTime = start.ToUniversalTime(),
                    EndTime = result.EndTime.Value, Status = "Confirmed", CreatedByUserId = guest ? null : session.UserId, CreatedAt = now };
            }
        }
        private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
