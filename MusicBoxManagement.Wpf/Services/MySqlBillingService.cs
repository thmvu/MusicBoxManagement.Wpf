using System;
using System.Data;
using MySqlConnector;
using System.Globalization;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class MySqlBillingService : IBillingService
    {
        private readonly MySqlDatabase database;
        private readonly IClock clock;
        private readonly MySqlPermissionService permissions;
        public MySqlBillingService(MySqlDatabase database) : this(database, new SystemClock()) { }
        public MySqlBillingService(MySqlDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new MySqlPermissionService(database);
        }
        public SessionBill ReadGuest(string phoneNumber, int sessionId)
        {
            var phone = PhoneNumberNormalizer.Normalize(phoneNumber);
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
                return ReadActive(connection, transaction, sessionId, phone);
        }
        public SessionBill ReadStaff(LoginSession actor, int sessionId)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            {
                if (!permissions.ReadAccess(actor, connection, transaction).Permissions.ContainsKey("Session.View"))
                    throw new UnauthorizedAccessException("Cần quyền xem phiên sử dụng.");
                return ReadActive(connection, transaction, sessionId, null);
            }
        }
        // Checkout preview demands CheckOut in the SAME RR read snapshot; confirm demands it
        // in the SAME Serializable writer after resource locks. Neither route requires Session.View.
        // Capture the clock after the orders; this calculator never writes, commits or caches.
        internal SessionBill CalculateActive(MySqlConnection connection, MySqlTransaction transaction, int sessionId)
        {
            if (transaction == null || transaction.Connection != connection ||
                (transaction.IsolationLevel != IsolationLevel.RepeatableRead && transaction.IsolationLevel != IsolationLevel.Serializable))
                throw new ArgumentException("Cần cùng kết nối và transaction RepeatableRead hoặc Serializable để tính tiền checkout.");
            return ReadActive(connection, transaction, sessionId, null);
        }

        private SessionBill ReadActive(MySqlConnection connection, MySqlTransaction transaction, int sessionId, string phone)
        {
            if (connection == null || transaction == null || transaction.Connection != connection || connection.Database != database.DatabaseName)
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
            var orders = MySqlOrderService.ReadOrders(connection, transaction, sessionId);
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
