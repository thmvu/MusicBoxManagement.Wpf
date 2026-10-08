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
    public sealed class OrderService
    {
        private readonly SqliteDatabase database;
        private readonly IClock clock;
        private readonly PermissionService permissions;
        public OrderService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public OrderService(SqliteDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new PermissionService(database);
        }

        public ServiceOrder CreateGuest(string phoneNumber, int sessionId, IEnumerable<OrderLineRequest> items)
        { return Create(null, PhoneNumberNormalizer.Normalize(phoneNumber), sessionId, items); }
        // Staff uses this only for items already served; Order.Create is independent of View/Confirm.
        public ServiceOrder CreateStaff(LoginSession actor, int sessionId, IEnumerable<OrderLineRequest> items)
        { return Create(actor, null, sessionId, items); }
        private ServiceOrder Create(LoginSession actor, string phone, int sessionId, IEnumerable<OrderLineRequest> items)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = SqliteDatabase.BeginWriteTransaction(connection))
            {
                if (phone == null) permissions.Demand(actor, "Order.Create", connection, transaction);
                DemandSession(connection, transaction, sessionId, phone, true);
                var now = clock.UtcNow.ToUniversalTime();
                try { BookingHours.GetOpenShiftEnd(now); }
                catch (ArgumentException) { throw new ArgumentException("Chỉ nhận món mới trong ca 09:00–12:00 hoặc 13:00–23:00."); }
                if (items == null) throw new ArgumentException("Cần ít nhất một món.");
                var lines = items.Select(i => i == null ? null : new OrderLineRequest { ServiceId = i.ServiceId, Quantity = i.Quantity }).ToList();
                if (lines.Count == 0 || lines.Any(i => i == null || i.ServiceId <= 0 || i.Quantity < 1 || i.Quantity > 10))
                    throw new ArgumentException("Cần ít nhất một món; số lượng mỗi món từ 1 đến 10.");
                var snapshots = new List<ServiceOrderItem>();
                foreach (var group in lines.GroupBy(i => i.ServiceId))
                {
                    var quantity = group.Sum(i => (long)i.Quantity);
                    if (quantity > 10) throw new ArgumentException("Tổng số lượng mỗi món từ 1 đến 10.");
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = "SELECT Name,Price FROM Services WHERE ServiceId=@id AND IsActive=1;";
                        command.Parameters.AddWithValue("@id", group.Key);
                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read()) throw new InvalidOperationException("Món đã ngừng bán hoặc không còn trong danh mục. Hãy tải lại menu.");
                            snapshots.Add(new ServiceOrderItem { ServiceId = group.Key, Quantity = (int)quantity,
                                ServiceNameSnapshot = reader.GetString(0), UnitPrice = reader.GetInt64(1) });
                        }
                    }
                }
                int id;
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "INSERT INTO Orders(RoomSessionId,CreatedByUserId,Status,CreatedAt) VALUES(@session,@user,@status,@now); SELECT last_insert_rowid();";
                    command.Parameters.AddWithValue("@session", sessionId);
                    command.Parameters.AddWithValue("@user", phone == null ? (object)actor.UserId : DBNull.Value);
                    command.Parameters.AddWithValue("@status", phone == null ? "Completed" : "Pending");
                    command.Parameters.AddWithValue("@now", Utc(now));
                    id = Convert.ToInt32(command.ExecuteScalar());
                }
                foreach (var item in snapshots)
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = "INSERT INTO OrderItems(OrderId,ServiceId,ServiceNameSnapshot,Quantity,UnitPrice) VALUES(@order,@service,@name,@quantity,@price);";
                        command.Parameters.AddWithValue("@order", id); command.Parameters.AddWithValue("@service", item.ServiceId);
                        command.Parameters.AddWithValue("@name", item.ServiceNameSnapshot); command.Parameters.AddWithValue("@quantity", item.Quantity);
                        command.Parameters.AddWithValue("@price", item.UnitPrice); command.ExecuteNonQuery();
                    }
                WriteAudit(connection, transaction, actor, phone, "Order.Create", id, "Tạo đơn món " + (phone == null ? "Completed (đã phục vụ)." : "Pending."), now);
                var result = ReadOrder(connection, transaction, id);
                transaction.Commit(); return result;
            }
        }

        public ServiceOrder ConfirmStaff(LoginSession actor, int orderId)
        { return ChangeState(actor, null, orderId, "Completed"); }
        public ServiceOrder CancelStaff(LoginSession actor, int orderId)
        { return ChangeState(actor, null, orderId, "Cancelled"); }
        public ServiceOrder CancelGuest(string phoneNumber, int orderId)
        { return ChangeState(null, PhoneNumberNormalizer.Normalize(phoneNumber), orderId, "Cancelled"); }
        private ServiceOrder ChangeState(LoginSession actor, string phone, int orderId, string status)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = SqliteDatabase.BeginWriteTransaction(connection))
            {
                var action = status == "Completed" ? "Order.Confirm" : "Order.Cancel";
                if (phone == null) permissions.Demand(actor, action, connection, transaction);
                int sessionId; string current;
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"SELECT o.RoomSessionId,o.Status FROM Orders o
JOIN RoomSessions s ON s.RoomSessionId=o.RoomSessionId JOIN Customers c ON c.CustomerId=s.CustomerId
WHERE o.OrderId=@id" + (phone == null ? "" : " AND c.PhoneNumber=@phone") + ";";
                    command.Parameters.AddWithValue("@id", orderId);
                    if (phone != null) command.Parameters.AddWithValue("@phone", phone);
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read()) throw new InvalidOperationException("Không tìm thấy đơn món hợp lệ. Hãy tải lại.");
                        sessionId = reader.GetInt32(0); current = reader.GetString(1);
                    }
                }
                DemandSession(connection, transaction, sessionId, phone, true);
                if (current != "Pending") throw new InvalidOperationException("Đơn món đã đổi trạng thái. Chỉ xử lý đơn Pending.");
                var now = clock.UtcNow.ToUniversalTime();
                // Existing Pending items may be served/cancelled during breaks or after closing.
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "UPDATE Orders SET Status=@status WHERE OrderId=@id;";
                    command.Parameters.AddWithValue("@status", status); command.Parameters.AddWithValue("@id", orderId); command.ExecuteNonQuery();
                }
                WriteAudit(connection, transaction, actor, phone, action, orderId,
                    status == "Completed" ? "Xác nhận món đã phục vụ." : "Hủy đơn món Pending.", now);
                var result = ReadOrder(connection, transaction, orderId);
                transaction.Commit(); return result;
            }
        }

        public List<ServiceOrder> ListGuest(string phoneNumber, int sessionId)
        { return List(null, PhoneNumberNormalizer.Normalize(phoneNumber), sessionId); }
        public List<ServiceOrder> ListStaff(LoginSession actor, int sessionId)
        { return List(actor, null, sessionId); }
        private List<ServiceOrder> List(LoginSession actor, string phone, int sessionId)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                if (phone == null) permissions.Demand(actor, "Order.View", connection, transaction);
                DemandSession(connection, transaction, sessionId, phone, phone != null);
                var ids = new List<int>();
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "SELECT OrderId FROM Orders WHERE RoomSessionId=@session ORDER BY OrderId;";
                    command.Parameters.AddWithValue("@session", sessionId);
                    using (var reader = command.ExecuteReader()) while (reader.Read()) ids.Add(reader.GetInt32(0));
                }
                return ids.Select(id => ReadOrder(connection, transaction, id)).ToList();
            }
        }
        private static void DemandSession(SQLiteConnection connection, SQLiteTransaction transaction, int id, string phone, bool active)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"SELECT COUNT(*) FROM RoomSessions s JOIN Customers c ON c.CustomerId=s.CustomerId
WHERE s.RoomSessionId=@id" + (active ? " AND s.Status='Active'" : "") + (phone == null ? "" : " AND c.PhoneNumber=@phone") + ";";
                command.Parameters.AddWithValue("@id", id); if (phone != null) command.Parameters.AddWithValue("@phone", phone);
                if (Convert.ToInt32(command.ExecuteScalar()) != 1)
                    throw new InvalidOperationException("Không tìm thấy phiên phù hợp" + (phone == null ? "." : " của SĐT này.") + " Hãy tải lại.");
            }
        }
        private static ServiceOrder ReadOrder(SQLiteConnection connection, SQLiteTransaction transaction, int id)
        {
            var result = new ServiceOrder { OrderId = id, Items = new List<ServiceOrderItem>() };
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT Status,CreatedAt FROM Orders WHERE OrderId=@id;"; command.Parameters.AddWithValue("@id", id);
                using (var reader = command.ExecuteReader())
                { reader.Read(); result.Status = reader.GetString(0); result.CreatedAt = DateTimeOffset.ParseExact(reader.GetString(1), "O", CultureInfo.InvariantCulture); }
                command.CommandText = "SELECT ServiceId,ServiceNameSnapshot,Quantity,UnitPrice FROM OrderItems WHERE OrderId=@id ORDER BY OrderItemId;";
                using (var reader = command.ExecuteReader()) while (reader.Read())
                    result.Items.Add(new ServiceOrderItem { ServiceId = reader.GetInt32(0), ServiceNameSnapshot = reader.GetString(1), Quantity = reader.GetInt32(2), UnitPrice = reader.GetInt64(3) });
            }
            return result;
        }
        private static void WriteAudit(SQLiteConnection connection, SQLiteTransaction transaction, LoginSession actor, string phone,
            string action, int id, string description, DateTimeOffset now)
        {
            if (phone == null) AuditService.WriteStaff(connection, transaction, actor.UserId, action, "Order", id.ToString(CultureInfo.InvariantCulture), description, now);
            else AuditService.WriteGuest(connection, transaction, action, "Order", id.ToString(CultureInfo.InvariantCulture), description, now);
        }
        private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
