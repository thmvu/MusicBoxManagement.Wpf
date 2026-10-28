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
    public sealed class InvoiceService : IInvoiceService
    {
        private readonly SqliteDatabase database;
        private readonly IClock clock;
        private readonly PermissionService permissions;
        public InvoiceService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public InvoiceService(SqliteDatabase database, IClock clock)
        { this.database = database ?? throw new ArgumentNullException(nameof(database)); this.clock = clock ?? throw new ArgumentNullException(nameof(clock)); permissions = new PermissionService(database); }
        public InvoiceSearch Search(LoginSession actor, string invoiceNumber = null, DateTime? fromDate = null, DateTime? toDate = null, string phoneNumber = null)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            using (var command = connection.CreateCommand())
            {
                permissions.Demand(actor, "Invoice.View", connection, transaction);
                var canPrint = permissions.ReadAccess(actor, connection, transaction).Permissions.ContainsKey("Invoice.Print");
                var number = string.IsNullOrWhiteSpace(invoiceNumber) ? null : invoiceNumber.Trim().ToUpperInvariant();
                if (number != null && number.Length > 50) throw new ArgumentException("Mã hóa đơn tối đa 50 ký tự.");
                var phone = string.IsNullOrWhiteSpace(phoneNumber) ? null : PhoneNumberNormalizer.Normalize(phoneNumber);
                if (fromDate.HasValue && toDate.HasValue && fromDate.Value.Date > toDate.Value.Date)
                    throw new ArgumentException("Ngày bắt đầu không được sau ngày kết thúc.");
                if (toDate.HasValue && toDate.Value.Date == DateTime.MaxValue.Date)
                    throw new ArgumentException("Ngày kết thúc không thể vượt giới hạn thời gian được hỗ trợ.");
                var from = fromDate.HasValue ? (DateTimeOffset?)DayStart(fromDate.Value) : null;
                var to = toDate.HasValue ? (DateTimeOffset?)DayStart(toDate.Value.Date.AddDays(1)) : null;
                command.Transaction = transaction;
                command.CommandText = @"SELECT i.InvoiceId,i.InvoiceNumber,s.RoomCodeSnapshot,i.PaidAt,i.PaymentMethod,i.TotalAmount
FROM Invoices i JOIN RoomSessions s ON s.RoomSessionId=i.RoomSessionId JOIN Customers c ON c.CustomerId=s.CustomerId
WHERE (@number IS NULL OR i.InvoiceNumber=@number) AND (@from IS NULL OR i.PaidAt>=@from)
AND (@to IS NULL OR i.PaidAt<@to) AND (@phone IS NULL OR c.PhoneNumber=@phone)
ORDER BY i.PaidAt DESC,i.InvoiceId DESC;";
                command.Parameters.AddWithValue("@number", (object)number ?? DBNull.Value);
                command.Parameters.AddWithValue("@phone", (object)phone ?? DBNull.Value);
                command.Parameters.AddWithValue("@from", from.HasValue ? (object)Utc(from.Value) : DBNull.Value);
                command.Parameters.AddWithValue("@to", to.HasValue ? (object)Utc(to.Value) : DBNull.Value);
                var items = new List<InvoiceSummary>();
                using (var reader = command.ExecuteReader()) while (reader.Read())
                    items.Add(new InvoiceSummary { InvoiceId = reader.GetInt32(0), InvoiceNumber = reader.GetString(1), RoomCode = reader.GetString(2),
                        PaidAt = ParseTime(reader.GetString(3)), PaymentMethod = reader.GetString(4), TotalAmount = decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture) });
                return new InvoiceSearch { Items = items.AsReadOnly(), CanPrint = canPrint, CheckedAt = clock.UtcNow.ToUniversalTime() };
            }
        }
        public InvoiceDetails Read(LoginSession actor, int invoiceId) => ReadCore(actor, invoiceId, false);
        // Future WPF print flow must call this at the point of preparing the document, not trust an old CanPrint flag.
        public InvoiceDetails ReadForPrint(LoginSession actor, int invoiceId) => ReadCore(actor, invoiceId, true);
        private InvoiceDetails ReadCore(LoginSession actor, int invoiceId, bool printing)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            using (var command = connection.CreateCommand())
            {
                permissions.Demand(actor, "Invoice.View", connection, transaction);
                if (printing) permissions.Demand(actor, "Invoice.Print", connection, transaction);
                var canPrint = permissions.ReadAccess(actor, connection, transaction).Permissions.ContainsKey("Invoice.Print");
                if (invoiceId <= 0) throw new ArgumentException("Mã định danh hóa đơn không hợp lệ.");
                command.Transaction = transaction;
                command.CommandText = "SELECT RoomSessionId FROM Invoices WHERE InvoiceId=@id;";
                command.Parameters.AddWithValue("@id", invoiceId);
                var found = command.ExecuteScalar();
                if (found == null) throw new InvalidOperationException("Không tìm thấy hóa đơn. Hãy tải lại danh sách.");
                var sessionId = Convert.ToInt32(found);
                var result = new InvoiceDetails { Invoice = CheckoutService.ReadInvoice(connection, transaction, sessionId), CanPrint = canPrint };
                command.Parameters.Clear(); command.Parameters.AddWithValue("@session", sessionId);
                command.CommandText = @"SELECT RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,HourlyRate,ActualStartTime,ActualEndTime,Status
FROM RoomSessions WHERE RoomSessionId=@session;";
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read() || reader.GetString(6) != "Completed" || reader.IsDBNull(5))
                        throw new InvalidOperationException("Dữ liệu phiên của hóa đơn không còn hợp lệ.");
                    result.RoomCodeSnapshot = reader.GetString(0); result.RoomTypeCodeSnapshot = reader.GetString(1); result.RoomTypeNameSnapshot = reader.GetString(2);
                    result.HourlyRate = reader.GetInt64(3); result.ActualStartTime = ParseTime(reader.GetString(4)); result.ActualEndTime = ParseTime(reader.GetString(5));
                }
                var lines = new List<InvoiceLine>();
                foreach (var order in OrderService.ReadOrders(connection, transaction, sessionId).Where(o => o.Status == "Completed"))
                    foreach (var item in order.Items) lines.Add(new InvoiceLine { OrderId = order.OrderId, ServiceId = item.ServiceId,
                        ServiceNameSnapshot = item.ServiceNameSnapshot, Quantity = item.Quantity, UnitPrice = item.UnitPrice });
                result.Items = lines.AsReadOnly();
                if (result.ActualEndTime != result.Invoice.PaidAt || result.ActualEndTime < result.ActualStartTime
                    || lines.Sum(i => i.Amount) != result.Invoice.ServiceCharge || result.Invoice.RoomCharge + result.Invoice.ServiceCharge != result.Invoice.TotalAmount)
                    throw new InvalidOperationException("Dữ liệu hóa đơn không khớp số tiền hoặc thời điểm đã chốt.");
                result.CheckedAt = clock.UtcNow.ToUniversalTime(); return result;
            }
        }
        private static DateTimeOffset DayStart(DateTime value) => new DateTimeOffset(DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified), BookingHours.VietnamOffset).ToUniversalTime();
        private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        private static DateTimeOffset ParseTime(string value) => DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture);
    }
}
