using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class CustomerHistoryService : ICustomerHistoryService
    {
        private readonly SqliteDatabase database;
        private readonly PermissionService permissions;
        private readonly IClock clock;
        public CustomerHistoryService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public CustomerHistoryService(SqliteDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new PermissionService(database);
        }

        public CustomerHistory Read(LoginSession actor, int customerId)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            using (var command = connection.CreateCommand())
            {
                var access = permissions.ReadAccess(actor, connection, transaction);
                if (!access.Permissions.ContainsKey("Customer.View"))
                    throw new UnauthorizedAccessException("Cần quyền xem khách hàng.");
                if (customerId <= 0) throw new ArgumentException("Mã khách hàng không hợp lệ.");
                var result = new CustomerHistory { CheckedAt = clock.UtcNow.ToUniversalTime(),
                    CanViewInvoices = access.Permissions.ContainsKey("Invoice.View") };
                command.Transaction = transaction;
                command.Parameters.AddWithValue("@id", customerId);
                command.CommandText = "SELECT FullName,PhoneNumber FROM Customers WHERE CustomerId=@id;";
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) throw new InvalidOperationException("Không tìm thấy khách hàng. Hãy tải lại danh sách.");
                    result.Customer = new Customer { CustomerId = customerId, FullName = reader.GetString(0), PhoneNumber = reader.GetString(1) };
                }
                var reservations = new List<CustomerReservationHistory>();
                command.CommandText = @"SELECT b.ReservationId,r.RoomCode,b.StartTime,b.EndTime,b.Status,b.CancellationReason
FROM Reservations b JOIN Rooms r ON r.RoomId=b.RoomId WHERE b.CustomerId=@id;";
                using (var reader = command.ExecuteReader()) while (reader.Read())
                    reservations.Add(new CustomerReservationHistory { ReservationId = reader.GetInt32(0), RoomCode = reader.GetString(1),
                        StartTime = Time(reader.GetString(2)), EndTime = Time(reader.GetString(3)), Status = reader.GetString(4),
                        CancellationReason = reader.IsDBNull(5) ? null : reader.GetString(5) });
                // History shows persisted states; it never runs NoShow maintenance or writes audit.
                result.Reservations = reservations.OrderByDescending(r => r.StartTime).ThenByDescending(r => r.ReservationId).ToList().AsReadOnly();
                var sessions = new List<RoomSession>();
                command.CommandText = @"SELECT RoomSessionId,CustomerId,RoomId,ReservationId,ActualStartTime,ExpectedEndTime,ActualEndTime,
HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status FROM RoomSessions WHERE CustomerId=@id;";
                using (var reader = command.ExecuteReader()) while (reader.Read()) sessions.Add(RoomSessionService.ReadSessionRecord(reader));
                result.Sessions = sessions.OrderByDescending(s => s.ActualStartTime).ThenByDescending(s => s.RoomSessionId).ToList().AsReadOnly();
                result.CompletedSessionCount = sessions.Count(s => s.Status == "Completed");
                result.LastUsedAt = result.Sessions.Count == 0 ? (DateTimeOffset?)null : result.Sessions[0].ActualStartTime;
                var invoices = new List<InvoiceSummary>();
                if (result.CanViewInvoices)
                {
                    command.CommandText = @"SELECT i.InvoiceId,i.InvoiceNumber,s.RoomCodeSnapshot,i.PaidAt,i.PaymentMethod,i.TotalAmount
FROM Invoices i JOIN RoomSessions s ON s.RoomSessionId=i.RoomSessionId WHERE s.CustomerId=@id;";
                    using (var reader = command.ExecuteReader()) while (reader.Read())
                        invoices.Add(new InvoiceSummary { InvoiceId = reader.GetInt32(0), InvoiceNumber = reader.GetString(1), RoomCode = reader.GetString(2),
                            PaidAt = Time(reader.GetString(3)), PaymentMethod = reader.GetString(4), TotalAmount = decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture) });
                }
                result.Invoices = invoices.OrderByDescending(i => i.PaidAt).ThenByDescending(i => i.InvoiceId).ToList().AsReadOnly();
                return result;
            }
        }
        private static DateTimeOffset Time(string value) => DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture);
    }
}
