using System;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class BillingService
    {
        private readonly SqliteDatabase database;
        private readonly IClock clock;
        private readonly PermissionService permissions;
        public BillingService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public BillingService(SqliteDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new PermissionService(database);
        }
        public SessionBill ReadGuest(string phoneNumber, int sessionId)
        {
            var phone = PhoneNumberNormalizer.Normalize(phoneNumber);
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
                return ReadActive(connection, transaction, sessionId, phone);
        }
        public SessionBill ReadStaff(LoginSession actor, int sessionId)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                permissions.Demand(actor, "Session.View", connection, transaction);
                return ReadActive(connection, transaction, sessionId, null);
            }
        }
        // Checkout must demand its own live permission in the SAME writer transaction before calling.
        // This reads fresh snapshots and captures the clock after the orders. It neither writes nor caches.
        internal SessionBill CalculateActive(SQLiteConnection connection, SQLiteTransaction transaction, int sessionId)
            => ReadActive(connection, transaction, sessionId, null);

        private SessionBill ReadActive(SQLiteConnection connection, SQLiteTransaction transaction, int sessionId, string phone)
        {
            if (transaction == null || transaction.Connection != connection)
                throw new ArgumentException("Cần cùng kết nối và transaction tính tiền.");
            SessionBill bill;
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"SELECT s.RoomCodeSnapshot,s.RoomTypeNameSnapshot,s.HourlyRate,s.ActualStartTime
FROM RoomSessions s JOIN Customers c ON c.CustomerId=s.CustomerId
WHERE s.RoomSessionId=@id AND s.Status='Active'" + (phone == null ? "" : " AND c.PhoneNumber=@phone") + ";";
                command.Parameters.AddWithValue("@id", sessionId);
                if (phone != null) command.Parameters.AddWithValue("@phone", phone);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) throw new InvalidOperationException("Không tìm thấy phiên đang sử dụng phù hợp" + (phone == null ? "." : " của SĐT này.") + " Hãy tải lại.");
                    bill = new SessionBill { SessionId = sessionId, RoomCode = reader.GetString(0), RoomTypeName = reader.GetString(1),
                        HourlyRate = reader.GetInt64(2), ActualStartTime = DateTimeOffset.ParseExact(reader.GetString(3), "O", CultureInfo.InvariantCulture).ToUniversalTime() };
                }
            }
            var orders = OrderService.ReadOrders(connection, transaction, sessionId);
            bill.BillingEndTime = clock.UtcNow.ToUniversalTime();
            if (bill.BillingEndTime < bill.ActualStartTime)
                throw new InvalidOperationException("Thời điểm tính tiền trước giờ nhận phòng. Hãy kiểm tra đồng hồ máy.");
            bill.UsedTicks = (bill.BillingEndTime - bill.ActualStartTime).Ticks;
            bill.RoomCharge = RoomCharge(bill.HourlyRate, bill.UsedTicks);
            bill.ServiceCharge = orders.Where(o => o.Status == "Completed").Sum(o => o.Items.Sum(i => (decimal)i.UnitPrice * i.Quantity));
            bill.CompletedOrderCount = orders.Count(o => o.Status == "Completed");
            bill.PendingOrderCount = orders.Count(o => o.Status == "Pending");
            bill.CancelledOrderCount = orders.Count(o => o.Status == "Cancelled");
            return bill;
        }
        private static decimal RoomCharge(long rate, long ticks)
        {
            // Split into exact integer parts before rounding the remaining fraction once.
            // Avoid rate*ticks overflow and preserve half-dong boundaries even for large rates.
            var wholeHours = ticks / TimeSpan.TicksPerHour;
            var remainingTicks = ticks % TimeSpan.TicksPerHour;
            var integerCharge = (decimal)rate * wholeHours + (decimal)(rate / TimeSpan.TicksPerHour) * remainingTicks;
            var fraction = (decimal)(rate % TimeSpan.TicksPerHour) * remainingTicks / TimeSpan.TicksPerHour;
            return integerCharge + decimal.Round(fraction, 0, MidpointRounding.AwayFromZero);
        }
    }
}
