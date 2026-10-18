using System;
using MySqlConnector;
using System.Globalization;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class MySqlReservationService
    {
        private readonly MySqlDatabase database;
        private readonly IClock clock;
        private readonly MySqlPermissionService permissions;
        private readonly MySqlAvailabilityService availability;
        public MySqlReservationService(MySqlDatabase database) : this(database, new SystemClock()) { }
        public MySqlReservationService(MySqlDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database)); this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new MySqlPermissionService(database); availability = new MySqlAvailabilityService(database, clock);
        }
        public Reservation CreateGuest(ReservationRequest request) => Create(null, request, true);
        public Reservation CreateStaff(LoginSession session, ReservationRequest request) => Create(session, request, false);
        private Reservation Create(LoginSession session, ReservationRequest request, bool guest)
        {
            try { return CreateCore(session, request, guest); }
            catch (MySqlException error) when (error.Number == 1062 || error.Number == 1213 || error.Number == 1205)
            { throw new InvalidOperationException("Lịch hoặc SĐT vừa được cập nhật. Hãy kiểm tra lại trước khi đặt phòng.", error); }
        }
        private Reservation CreateCore(LoginSession session, ReservationRequest request, bool guest)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginWriteTransaction(connection))
            using (var command = connection.CreateCommand())
            {
                if (!guest) permissions.Demand(session, "Reservation.Create", connection, transaction);
                if (request == null) throw new ArgumentException("Cần nhập thông tin đặt phòng.");
                var room = request.RoomId; var start = request.StartTime; var duration = request.DurationMinutes;
                var name = (request.FullName ?? "").Trim();
                if (name.Length < 1 || name.Length > 100) throw new ArgumentException("Họ tên từ 1–100 ký tự.");
                var phone = PhoneNumberNormalizer.Normalize(request.PhoneNumber);
                MySqlAvailabilityService.LockResources(connection, transaction, room, null);
                command.Transaction = transaction;
                command.CommandText = "SELECT CustomerId FROM Customers WHERE PhoneNumber=@phone FOR UPDATE;";
                command.Parameters.AddWithValue("@phone", phone);
                var found = command.ExecuteScalar();
                int customer; var newCustomer = false;
                if (found != null) customer = Convert.ToInt32(found);
                else
                {
                    command.CommandText = "INSERT INTO Customers(FullName,PhoneNumber) VALUES(@name,@phone); SELECT LAST_INSERT_ID();";
                    command.Parameters.AddWithValue("@name", name); customer = Convert.ToInt32(command.ExecuteScalar());
                    newCustomer = true;
                }
                // Room -> canonical Customer -> one business clock, shared with availability.
                MySqlAvailabilityService.LockResources(connection, transaction, room, customer);
                MySqlNoShowService.LockCandidates(connection, transaction);
                var now = clock.UtcNow.ToUniversalTime();
                BookingHours.ValidateReservation(start, duration, now);
                MySqlNoShowService.ProcessExpiredAt(connection, transaction, now);
                if (newCustomer)
                {
                    var customerId = customer.ToString(CultureInfo.InvariantCulture);
                    if (guest) MySqlAuditService.WriteGuest(connection, transaction, "Customer.Create", "Customer", customerId, "Tạo khách hàng từ đặt phòng.", now);
                    else MySqlAuditService.WriteStaff(connection, transaction, session.UserId, "Customer.Create", "Customer", customerId, "Tạo khách hàng từ đặt phòng.", now);
                }
                var result = availability.CheckReservationAt(connection, transaction, room, customer, start, duration, now);
                if (!result.CanBook) throw new InvalidOperationException(result.Reason);
                command.Parameters.Clear();
                command.CommandText = @"INSERT INTO Reservations(CustomerId,RoomId,StartTime,EndTime,Status,CreatedByUserId,CreatedAt)
VALUES(@customer,@room,@start,@end,'Confirmed',@user,@now); SELECT LAST_INSERT_ID();";
                command.Parameters.AddWithValue("@customer", customer); command.Parameters.AddWithValue("@room", room);
                command.Parameters.AddWithValue("@start", Utc(start)); command.Parameters.AddWithValue("@end", Utc(result.EndTime.Value));
                command.Parameters.AddWithValue("@user", guest ? (object)DBNull.Value : session.UserId); command.Parameters.AddWithValue("@now", Utc(now));
                var id = Convert.ToInt32(command.ExecuteScalar());
                var idText = id.ToString(CultureInfo.InvariantCulture);
                if (guest) MySqlAuditService.WriteGuest(connection, transaction, "Reservation.Create", "Reservation", idText, "Đặt phòng " + room + ", khách " + customer + ".", now);
                else MySqlAuditService.WriteStaff(connection, transaction, session.UserId, "Reservation.Create", "Reservation", idText, "Đặt phòng " + room + ", khách " + customer + ".", now);
                transaction.Commit();
                return new Reservation { ReservationId = id, CustomerId = customer, RoomId = room, StartTime = start.ToUniversalTime(),
                    EndTime = result.EndTime.Value, Status = "Confirmed", CreatedByUserId = guest ? null : session.UserId, CreatedAt = now };
            }
        }
        public void CancelStaff(LoginSession session, int reservationId, string reason)
        {
            try { CancelStaffCore(session, reservationId, reason); }
            catch (MySqlException error) when (error.Number == 1213 || error.Number == 1205)
            { throw new InvalidOperationException("Booking đang được cập nhật. Hãy tải lại danh sách.", error); }
        }
        private void CancelStaffCore(LoginSession session, int reservationId, string reason)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginWriteTransaction(connection))
            using (var command = connection.CreateCommand())
            {
                permissions.Demand(session, "Reservation.Cancel", connection, transaction);
                var text = (reason ?? "").Trim();
                if (text.Length < 1 || text.Length > 500) throw new ArgumentException("Lý do hủy bắt buộc, từ 1–500 ký tự.");
                command.Transaction = transaction;
                // Discover immutable resource IDs without a Serializable shared lock first;
                // the authoritative row is locked and checked again below.
                int room, customer;
                using (var discovery = database.OpenConnection())
                using (var query = discovery.CreateCommand())
                {
                    query.CommandText = "SELECT RoomId,CustomerId FROM Reservations WHERE ReservationId=@id;";
                    query.Parameters.AddWithValue("@id", reservationId);
                    using (var reader = query.ExecuteReader())
                    {
                        if (!reader.Read()) throw new InvalidOperationException("Không tìm thấy booking. Hãy tải lại danh sách.");
                        room = reader.GetInt32(0); customer = reader.GetInt32(1);
                    }
                }
                MySqlAvailabilityService.LockResources(connection, transaction, room, customer);
                command.CommandText = "SELECT ReservationId FROM Reservations WHERE ReservationId=@id FOR UPDATE;";
                command.Parameters.AddWithValue("@id", reservationId);
                command.ExecuteScalar();
                var now = clock.UtcNow.ToUniversalTime();
                command.CommandText = @"SELECT StartTime,Status,EXISTS(SELECT 1 FROM RoomSessions s WHERE s.ReservationId=b.ReservationId),RoomId,CustomerId
FROM Reservations b WHERE ReservationId=@id;";
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) throw new InvalidOperationException("Không tìm thấy booking. Hãy tải lại danh sách.");
                    var start = DateTimeOffset.ParseExact(reader.GetString(0), "O", CultureInfo.InvariantCulture);
                    if (reader.GetInt32(3) != room || reader.GetInt32(4) != customer || reader.GetString(1) != "Confirmed" || now >= start.AddMinutes(15) || reader.GetInt32(2) != 0)
                        throw new InvalidOperationException("Booking đã đổi trạng thái hoặc hết hạn nhận phòng. Hãy tải lại danh sách.");
                }
                command.CommandText = "UPDATE Reservations SET Status='Cancelled',CancellationReason=@reason WHERE ReservationId=@id AND Status='Confirmed';";
                command.Parameters.AddWithValue("@reason", text);
                if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Booking đã thay đổi. Hãy tải lại danh sách.");
                MySqlAuditService.WriteStaff(connection, transaction, session.UserId, "Reservation.Cancel", "Reservation",
                    reservationId.ToString(CultureInfo.InvariantCulture), text, now);
                transaction.Commit();
            }
        }
        private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
