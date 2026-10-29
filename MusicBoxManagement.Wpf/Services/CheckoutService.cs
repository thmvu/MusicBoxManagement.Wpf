using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class CheckoutService : ICheckoutService
    {
        private readonly SqliteDatabase database;
        private readonly PermissionService permissions;
        private readonly BillingService billing;
        public CheckoutService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public CheckoutService(SqliteDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            permissions = new PermissionService(database); billing = new BillingService(database, clock);
        }
        public CheckoutPreview PreviewStaff(LoginSession actor, int sessionId)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                permissions.Demand(actor, "Session.CheckOut", connection, transaction);
                var bill = billing.CalculateActive(connection, transaction, sessionId);
                return new CheckoutPreview { Bill = bill,
                    CompletedOrders = OrderService.ReadOrders(connection, transaction, sessionId).Where(o => o.Status == "Completed").ToList().AsReadOnly() };
            }
        }
        public IReadOnlyList<CheckoutSessionChoice> ListActiveStaff(LoginSession actor)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            using (var command = connection.CreateCommand())
            {
                permissions.Demand(actor, "Session.CheckOut", connection, transaction);
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
            using (var connection = database.OpenConnection())
            using (var transaction = SqliteDatabase.BeginWriteTransaction(connection))
            {
                permissions.Demand(actor, "Session.CheckOut", connection, transaction);
                var existing = ReadInvoice(connection, transaction, sessionId);
                if (existing != null) { transaction.Commit(); return existing; }
                if (paymentMethod != "Cash" && paymentMethod != "BankTransfer")
                    throw new ArgumentException("Hình thức thanh toán chỉ là Cash hoặc BankTransfer.");
                var bill = billing.CalculateActive(connection, transaction, sessionId);
                var name = permissions.ReadAccess(actor, connection, transaction).FullName;
                var now = bill.BillingEndTime.ToUniversalTime();
                var pending = OrderService.ReadOrders(connection, transaction, sessionId).Where(o => o.Status == "Pending").ToList();
                var number = "MB-" + now.ToOffset(BookingHours.VietnamOffset).ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                    + "-" + sessionId.ToString("D6", CultureInfo.InvariantCulture);
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.Parameters.AddWithValue("@session", sessionId);
                    command.Parameters.AddWithValue("@now", now.ToString("O", CultureInfo.InvariantCulture));
                    command.CommandText = "UPDATE Orders SET Status='Cancelled' WHERE RoomSessionId=@session AND Status='Pending';";
                    command.ExecuteNonQuery();
                    command.CommandText = "UPDATE Reservations SET Status='Completed' WHERE ReservationId=(SELECT ReservationId FROM RoomSessions WHERE RoomSessionId=@session) AND Status='CheckedIn';";
                    var changedSource = command.ExecuteNonQuery();
                    command.CommandText = "SELECT ReservationId FROM RoomSessions WHERE RoomSessionId=@session;";
                    var source = command.ExecuteScalar();
                    if (source != null && source != DBNull.Value && changedSource != 1)
                        throw new InvalidOperationException("Booking nguồn không còn CheckedIn; chưa thể hoàn tất phiên.");
                    command.CommandText = "UPDATE RoomSessions SET Status='Completed',ActualEndTime=@now WHERE RoomSessionId=@session AND Status='Active';";
                    if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Phiên không còn Active; hãy tải lại.");
                    command.CommandText = @"INSERT INTO Invoices(RoomSessionId,InvoiceNumber,RoomCharge,ServiceCharge,TotalAmount,PaymentMethod,ProcessedByUserId,ProcessedByNameSnapshot,PaidAt)
VALUES(@session,@number,@room,@service,@total,@method,@user,@name,@now);";
                    command.Parameters.AddWithValue("@number", number);
                    command.Parameters.AddWithValue("@room", bill.RoomCharge.ToString("0", CultureInfo.InvariantCulture));
                    command.Parameters.AddWithValue("@service", bill.ServiceCharge.ToString("0", CultureInfo.InvariantCulture));
                    command.Parameters.AddWithValue("@total", bill.TotalAmount.ToString("0", CultureInfo.InvariantCulture));
                    command.Parameters.AddWithValue("@method", paymentMethod); command.Parameters.AddWithValue("@user", actor.UserId);
                    command.Parameters.AddWithValue("@name", name); command.ExecuteNonQuery();
                }
                foreach (var order in pending)
                    AuditService.WriteStaff(connection, transaction, actor.UserId, "Order.Cancel", "Order",
                        order.OrderId.ToString(CultureInfo.InvariantCulture), "Hủy Pending khi trả phòng " + number + ".", now);
                var invoice = ReadInvoice(connection, transaction, sessionId);
                AuditService.WriteStaff(connection, transaction, actor.UserId, "Session.CheckOut", "RoomSession",
                    sessionId.ToString(CultureInfo.InvariantCulture), "Trả phòng, hóa đơn " + number + "; đã nhận "
                    + invoice.TotalAmount.ToString("0", CultureInfo.InvariantCulture) + " đ qua " + paymentMethod + ".", now);
                AuditService.WriteStaff(connection, transaction, actor.UserId, "Invoice.Create", "Invoice",
                    invoice.InvoiceId.ToString(CultureInfo.InvariantCulture), "Tạo hóa đơn đã thanh toán " + number + ".", now);
                transaction.Commit(); return invoice;
            }
        }
        // Snapshot reader: callers demand their own live permission in this transaction.
        internal static Invoice ReadInvoice(SQLiteConnection connection, SQLiteTransaction transaction, int sessionId)
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
                        RoomCharge = decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                        ServiceCharge = decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                        TotalAmount = decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture), PaymentMethod = reader.GetString(6),
                        ProcessedByUserId = reader.GetString(7), ProcessedByNameSnapshot = reader.GetString(8),
                        PaidAt = DateTimeOffset.ParseExact(reader.GetString(9), "O", CultureInfo.InvariantCulture) };
                }
            }
        }
    }
}
