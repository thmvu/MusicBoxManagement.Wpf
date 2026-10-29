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
    public sealed class MySqlCheckoutService : ICheckoutService
    {
        private readonly MySqlDatabase database;
        private readonly MySqlPermissionService permissions;
        private readonly MySqlBillingService billing;
        public MySqlCheckoutService(MySqlDatabase database) : this(database, new SystemClock()) { }
        public MySqlCheckoutService(MySqlDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            permissions = new MySqlPermissionService(database); billing = new MySqlBillingService(database, clock);
        }
        public CheckoutPreview PreviewStaff(LoginSession actor, int sessionId)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            {
                DemandRead(actor, connection, transaction);
                var bill = billing.CalculateActive(connection, transaction, sessionId);
                return new CheckoutPreview { Bill = bill,
                    CompletedOrders = MySqlOrderService.ReadOrders(connection, transaction, sessionId).Where(o => o.Status == "Completed").ToList().AsReadOnly() };
            }
        }
        public IReadOnlyList<CheckoutSessionChoice> ListActiveStaff(LoginSession actor)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            using (var command = connection.CreateCommand())
            {
                DemandRead(actor, connection, transaction);
                command.Transaction = transaction;
                command.CommandText = "SELECT RoomSessionId,RoomCodeSnapshot,ActualStartTime FROM RoomSessions WHERE Status='Active' ORDER BY RoomSessionId DESC;";
                var result = new List<CheckoutSessionChoice>();
                using (var reader = command.ExecuteReader()) while (reader.Read())
                    result.Add(new CheckoutSessionChoice { SessionId = reader.GetInt32(0), RoomCode = reader.GetString(1),
                        ActualStartTime = DateTimeOffset.ParseExact(reader.GetString(2), "O", CultureInfo.InvariantCulture) });
                return result.AsReadOnly();
            }
        }
        public Invoice ConfirmStaff(LoginSession actor, int sessionId, string paymentMethod)
        {
            try { return ConfirmCore(actor, sessionId, paymentMethod); }
            catch (MySqlException error) when (error.Number == 1062 || error.Number == 1213 || error.Number == 1205)
            { throw new InvalidOperationException("Phiên hoặc hóa đơn đang được cập nhật. Hãy kiểm tra hóa đơn trước khi xác nhận lại.", error); }
        }
        private Invoice ConfirmCore(LoginSession actor, int sessionId, string paymentMethod)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginWriteTransaction(connection))
            {
                permissions.Demand(actor, "Session.CheckOut", connection, transaction);
                var current = LockSession(connection, transaction, sessionId);
                // Recheck the actor after resource waits, including an in-memory logout.
                var access = DemandRead(actor, connection, transaction);
                var existing = ReadInvoice(connection, transaction, sessionId);
                if (existing != null) { transaction.Commit(); return existing; }
                if (current.Status != "Active") throw new InvalidOperationException("Phiên không còn Active; hãy tải lại.");
                if (paymentMethod != "Cash" && paymentMethod != "BankTransfer")
                    throw new ArgumentException("Hình thức thanh toán chỉ là Cash hoặc BankTransfer.");
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "SELECT OrderId FROM Orders WHERE RoomSessionId=@session ORDER BY OrderId FOR UPDATE;";
                    command.Parameters.AddWithValue("@session", sessionId);
                    using (var reader = command.ExecuteReader()) while (reader.Read()) { }
                }
                var bill = billing.CalculateActive(connection, transaction, sessionId);
                var name = access.FullName;
                var now = bill.BillingEndTime.ToUniversalTime();
                var pending = MySqlOrderService.ReadOrders(connection, transaction, sessionId).Where(o => o.Status == "Pending").ToList();
                var number = "MB-" + now.ToOffset(BookingHours.VietnamOffset).ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                    + "-" + sessionId.ToString("D6", CultureInfo.InvariantCulture);
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.Parameters.AddWithValue("@session", sessionId);
                    command.Parameters.AddWithValue("@now", now.ToString("O", CultureInfo.InvariantCulture));
                    command.CommandText = "UPDATE Orders SET Status='Cancelled' WHERE RoomSessionId=@session AND Status='Pending';";
                    if (command.ExecuteNonQuery() != pending.Count)
                        throw new InvalidOperationException("Đơn món đã đổi trạng thái; chưa thể hoàn tất phiên.");
                    if (current.ReservationId.HasValue)
                    {
                        command.CommandText = "UPDATE Reservations SET Status='Completed' WHERE ReservationId=@source AND RoomId=@sourceRoom AND CustomerId=@customer AND Status='CheckedIn';";
                        command.Parameters.AddWithValue("@source", current.ReservationId.Value);
                        command.Parameters.AddWithValue("@sourceRoom", current.RoomId); command.Parameters.AddWithValue("@customer", current.CustomerId);
                        if (command.ExecuteNonQuery() != 1)
                            throw new InvalidOperationException("Booking nguồn không còn CheckedIn phù hợp; chưa thể hoàn tất phiên.");
                    }
                    command.CommandText = "UPDATE RoomSessions SET Status='Completed',ActualEndTime=@now WHERE RoomSessionId=@session AND Status='Active';";
                    if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Phiên không còn Active; hãy tải lại.");
                    command.CommandText = @"INSERT INTO Invoices(RoomSessionId,InvoiceNumber,RoomCharge,ServiceCharge,TotalAmount,PaymentMethod,ProcessedByUserId,ProcessedByNameSnapshot,PaidAt)
VALUES(@session,@number,@room,@service,@total,@method,@user,@name,@now);";
                    command.Parameters.AddWithValue("@number", number);
                    command.Parameters.AddWithValue("@room", bill.RoomCharge);
                    command.Parameters.AddWithValue("@service", bill.ServiceCharge);
                    command.Parameters.AddWithValue("@total", bill.TotalAmount);
                    command.Parameters.AddWithValue("@method", paymentMethod); command.Parameters.AddWithValue("@user", actor.UserId);
                    command.Parameters.AddWithValue("@name", name); command.ExecuteNonQuery();
                }
                foreach (var order in pending)
                    MySqlAuditService.WriteStaff(connection, transaction, actor.UserId, "Order.Cancel", "Order",
                        order.OrderId.ToString(CultureInfo.InvariantCulture), "Hủy Pending khi trả phòng " + number + ".", now);
                var invoice = ReadInvoice(connection, transaction, sessionId);
                MySqlAuditService.WriteStaff(connection, transaction, actor.UserId, "Session.CheckOut", "RoomSession",
                    sessionId.ToString(CultureInfo.InvariantCulture), "Trả phòng, hóa đơn " + number + "; đã nhận "
                    + invoice.TotalAmount.ToString("0", CultureInfo.InvariantCulture) + " đ qua " + paymentMethod + ".", now);
                MySqlAuditService.WriteStaff(connection, transaction, actor.UserId, "Invoice.Create", "Invoice",
                    invoice.InvoiceId.ToString(CultureInfo.InvariantCulture), "Tạo hóa đơn đã thanh toán " + number + ".", now);
                transaction.Commit(); return invoice;
            }
        }
        private StaffAccess DemandRead(LoginSession actor, MySqlConnection connection, MySqlTransaction transaction)
        {
            var access = permissions.ReadAccess(actor, connection, transaction);
            if (!access.Permissions.ContainsKey("Session.CheckOut"))
                throw new UnauthorizedAccessException("Cần quyền trả phòng và thanh toán.");
            return access;
        }
        private RoomSession LockSession(MySqlConnection connection, MySqlTransaction transaction, int id)
        {
            int room, customer; int? source;
            // Discover IDs without taking Session/Reservation locks ahead of Room/Customer.
            using (var discovery = database.OpenConnection())
            using (var query = discovery.CreateCommand())
            {
                query.CommandText = "SELECT RoomId,CustomerId,ReservationId FROM RoomSessions WHERE RoomSessionId=@id;";
                query.Parameters.AddWithValue("@id", id);
                using (var reader = query.ExecuteReader())
                {
                    if (!reader.Read()) throw new InvalidOperationException("Không tìm thấy phiên sử dụng. Hãy tải lại.");
                    room = reader.GetInt32(0); customer = reader.GetInt32(1); source = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                }
            }
            MySqlAvailabilityService.LockResources(connection, transaction, room, customer);
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                if (source.HasValue)
                {
                    command.CommandText = "SELECT ReservationId FROM Reservations WHERE ReservationId=@source FOR UPDATE;";
                    command.Parameters.AddWithValue("@source", source.Value); command.ExecuteScalar();
                }
                command.CommandText = "SELECT RoomSessionId FROM RoomSessions WHERE RoomSessionId=@id FOR UPDATE;";
                command.Parameters.AddWithValue("@id", id); command.ExecuteScalar();
                command.CommandText = @"SELECT RoomSessionId,CustomerId,RoomId,ReservationId,ActualStartTime,ExpectedEndTime,ActualEndTime,
HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status FROM RoomSessions WHERE RoomSessionId=@id;";
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) throw new InvalidOperationException("Không tìm thấy phiên sử dụng. Hãy tải lại.");
                    var current = MySqlRoomSessionService.ReadSessionRecord(reader);
                    if (current.RoomId != room || current.CustomerId != customer || current.ReservationId != source)
                        throw new InvalidOperationException("Phiên đã thay đổi. Hãy tải lại trước khi thanh toán.");
                    return current;
                }
            }
        }
        // Snapshot reader: callers demand their own live permission in this transaction.
        internal static Invoice ReadInvoice(MySqlConnection connection, MySqlTransaction transaction, int sessionId)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"SELECT InvoiceId,RoomSessionId,InvoiceNumber,RoomCharge,ServiceCharge,TotalAmount,PaymentMethod,ProcessedByUserId,ProcessedByNameSnapshot,PaidAt
FROM Invoices WHERE RoomSessionId=@session;";
                command.Parameters.AddWithValue("@session", sessionId);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) return null;
                    return new Invoice { InvoiceId = reader.GetInt32(0), RoomSessionId = reader.GetInt32(1), InvoiceNumber = reader.GetString(2),
                        RoomCharge = reader.GetDecimal(3),
                        ServiceCharge = reader.GetDecimal(4),
                        TotalAmount = reader.GetDecimal(5), PaymentMethod = reader.GetString(6),
                        ProcessedByUserId = reader.GetString(7), ProcessedByNameSnapshot = reader.GetString(8),
                        PaidAt = DateTimeOffset.ParseExact(reader.GetString(9), "O", CultureInfo.InvariantCulture) };
                }
            }
        }
    }
}
