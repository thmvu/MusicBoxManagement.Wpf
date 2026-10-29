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
    public sealed class MySqlOrderService : IOrderService
    {
        private readonly MySqlDatabase database;
        private readonly IClock clock;
        private readonly MySqlPermissionService permissions;
        public MySqlOrderService(MySqlDatabase database) : this(database, new SystemClock()) { }
        public MySqlOrderService(MySqlDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new MySqlPermissionService(database);
        }

        public OrderWorkspace ReadWorkspaceStaff(LoginSession actor)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            {
                var access = permissions.ReadAccess(actor, connection, transaction).Permissions;
                var result = new OrderWorkspace { Sessions = new List<OrderSessionChoice>(),
                    CanView = access.ContainsKey("Order.View"), CanCreate = access.ContainsKey("Order.Create"),
                    CanConfirm = access.ContainsKey("Order.Confirm"), CanCancel = access.ContainsKey("Order.Cancel") };
                if (!result.CanView && !result.CanCreate)
                    throw new UnauthorizedAccessException("Cần quyền xem hoặc tạo đơn món.");
                result.CheckedAt = clock.UtcNow.ToUniversalTime();
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "SELECT RoomSessionId,RoomCodeSnapshot,Status,ActualStartTime FROM RoomSessions" +
                        (result.CanView ? "" : " WHERE Status='Active'") + " ORDER BY CASE Status WHEN 'Active' THEN 0 ELSE 1 END, RoomSessionId DESC;";
                    using (var reader = command.ExecuteReader()) while (reader.Read())
                        result.Sessions.Add(new OrderSessionChoice { SessionId = reader.GetInt32(0), RoomCode = reader.GetString(1),
                            Status = reader.GetString(2), ActualStartTime = DateTimeOffset.ParseExact(reader.GetString(3), "O", CultureInfo.InvariantCulture) });
                }
                return result;
            }
        }

        public ServiceOrder CreateGuest(string phoneNumber, int sessionId, IEnumerable<OrderLineRequest> items)
        { return Create(null, PhoneNumberNormalizer.Normalize(phoneNumber), sessionId, items); }
        // Staff uses this only for items already served; Order.Create is independent of View/Confirm.
        public ServiceOrder CreateStaff(LoginSession actor, int sessionId, IEnumerable<OrderLineRequest> items)
        { return Create(actor, null, sessionId, items); }
        private ServiceOrder Create(LoginSession actor, string phone, int sessionId, IEnumerable<OrderLineRequest> items)
        {
            try { return CreateCore(actor, phone, sessionId, items); }
            catch (MySqlException error) when (error.Number == 1213 || error.Number == 1205)
            { throw new InvalidOperationException("Phiên hoặc menu đang được cập nhật. Hãy kiểm tra lại trước khi gửi món.", error); }
        }
        private ServiceOrder CreateCore(LoginSession actor, string phone, int sessionId, IEnumerable<OrderLineRequest> items)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginWriteTransaction(connection))
            {
                if (phone == null) permissions.Demand(actor, "Order.Create", connection, transaction);
                LockActiveSession(connection, transaction, sessionId, phone, false);
                // Serializable cart reads lock Services in ID order against catalog edits.
                // Take the business clock after these waits as well as the session locks.
                var snapshots = ReadCart(connection, transaction, items);
                var now = clock.UtcNow.ToUniversalTime();
                DemandOrderHours(now);
                int id;
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "INSERT INTO Orders(RoomSessionId,CreatedByUserId,Status,CreatedAt) VALUES(@session,@user,@status,@now); SELECT LAST_INSERT_ID();";
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

        public OrderMenu ReadMenuGuest(string phoneNumber, int sessionId)
        { return ReadMenu(null, PhoneNumberNormalizer.Normalize(phoneNumber), sessionId); }
        public OrderMenu ReadMenuStaff(LoginSession actor, int sessionId)
        { return ReadMenu(actor, null, sessionId); }
        private OrderMenu ReadMenu(LoginSession actor, string phone, int sessionId)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            {
                if (phone == null) DemandRead(actor, "Order.Create", connection, transaction);
                DemandSession(connection, transaction, sessionId, phone, true);
                var now = clock.UtcNow.ToUniversalTime();
                var result = new OrderMenu { Items = new List<OrderMenuItem>(), CheckedAt = now, CanCreate = true,
                    Reason = "Có thể chọn món; giá và tình trạng bán sẽ được kiểm tra lại khi gửi." };
                try { DemandOrderHours(now); }
                catch (ArgumentException error) { result.CanCreate = false; result.Reason = error.Message; }
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "SELECT ServiceId,Name,Category,Price,Description FROM Services WHERE IsActive=1 ORDER BY Category,ServiceId;";
                    using (var reader = command.ExecuteReader()) while (reader.Read())
                        result.Items.Add(new OrderMenuItem { ServiceId = reader.GetInt32(0), Name = reader.GetString(1),
                            Category = reader.GetString(2), Price = reader.GetInt64(3), Description = reader.IsDBNull(4) ? null : reader.GetString(4) });
                }
                return result;
            }
        }
        public OrderPreview PreviewGuest(string phoneNumber, int sessionId, IEnumerable<OrderLineRequest> items)
        { return Preview(null, PhoneNumberNormalizer.Normalize(phoneNumber), sessionId, items); }
        public OrderPreview PreviewStaff(LoginSession actor, int sessionId, IEnumerable<OrderLineRequest> items)
        { return Preview(actor, null, sessionId, items); }
        private OrderPreview Preview(LoginSession actor, string phone, int sessionId, IEnumerable<OrderLineRequest> items)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            {
                if (phone == null) DemandRead(actor, "Order.Create", connection, transaction);
                DemandSession(connection, transaction, sessionId, phone, true);
                var now = clock.UtcNow.ToUniversalTime(); DemandOrderHours(now);
                var snapshots = ReadCart(connection, transaction, items);
                return new OrderPreview { Items = snapshots, CheckedAt = now,
                    Amount = snapshots.Sum(i => (decimal)i.UnitPrice * i.Quantity) };
            }
        }
        private static void DemandOrderHours(DateTimeOffset now)
        {
            try { BookingHours.GetOpenShiftEnd(now); }
            catch (ArgumentException) { throw new ArgumentException("Chỉ nhận món mới trong ca 09:00–12:00 hoặc 13:00–23:00."); }
        }
        private static List<ServiceOrderItem> ReadCart(MySqlConnection connection, MySqlTransaction transaction, IEnumerable<OrderLineRequest> items)
        {
            if (items == null) throw new ArgumentException("Cần ít nhất một món.");
            var lines = items.Select(i => i == null ? null : new OrderLineRequest { ServiceId = i.ServiceId, Quantity = i.Quantity }).ToList();
            if (lines.Count == 0 || lines.Any(i => i == null || i.ServiceId <= 0 || i.Quantity < 1 || i.Quantity > 10))
                throw new ArgumentException("Cần ít nhất một món; số lượng mỗi món từ 1 đến 10.");
            var snapshots = new List<ServiceOrderItem>();
            foreach (var group in lines.GroupBy(i => i.ServiceId).OrderBy(g => g.Key))
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
            return snapshots;
        }

        public ServiceOrder ConfirmStaff(LoginSession actor, int orderId)
        { return ChangeState(actor, null, orderId, "Completed"); }
        public ServiceOrder CancelStaff(LoginSession actor, int orderId)
        { return ChangeState(actor, null, orderId, "Cancelled"); }
        public ServiceOrder CancelGuest(string phoneNumber, int orderId)
        { return ChangeState(null, PhoneNumberNormalizer.Normalize(phoneNumber), orderId, "Cancelled"); }
        private ServiceOrder ChangeState(LoginSession actor, string phone, int orderId, string status)
        {
            try { return ChangeStateCore(actor, phone, orderId, status); }
            catch (MySqlException error) when (error.Number == 1213 || error.Number == 1205)
            { throw new InvalidOperationException("Đơn món hoặc phiên đang được cập nhật. Hãy tải lại trước khi xử lý.", error); }
        }
        private ServiceOrder ChangeStateCore(LoginSession actor, string phone, int orderId, string status)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginWriteTransaction(connection))
            {
                var action = status == "Completed" ? "Order.Confirm" : "Order.Cancel";
                if (phone == null) permissions.Demand(actor, action, connection, transaction);
                var sessionId = LockActiveSession(connection, transaction, orderId, phone, true);
                string current;
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "SELECT OrderId FROM Orders WHERE OrderId=@id FOR UPDATE;";
                    command.Parameters.AddWithValue("@id", orderId); command.ExecuteScalar();
                    command.CommandText = @"SELECT o.RoomSessionId,o.Status FROM Orders o
JOIN RoomSessions s ON s.RoomSessionId=o.RoomSessionId JOIN Customers c ON c.CustomerId=s.CustomerId
WHERE o.OrderId=@id" + (phone == null ? "" : " AND c.PhoneNumber=@phone") + ";";
                    if (phone != null) command.Parameters.AddWithValue("@phone", phone);
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read() || reader.GetInt32(0) != sessionId)
                            throw new InvalidOperationException("Không tìm thấy đơn món hợp lệ. Hãy tải lại.");
                        current = reader.GetString(1);
                    }
                }
                if (current != "Pending") throw new InvalidOperationException("Đơn món đã đổi trạng thái. Chỉ xử lý đơn Pending.");
                var now = clock.UtcNow.ToUniversalTime();
                // Existing Pending items may be served/cancelled during breaks or after closing.
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "UPDATE Orders SET Status=@status WHERE OrderId=@id AND RoomSessionId=@session AND Status='Pending';";
                    command.Parameters.AddWithValue("@status", status); command.Parameters.AddWithValue("@id", orderId);
                    command.Parameters.AddWithValue("@session", sessionId);
                    if (command.ExecuteNonQuery() != 1)
                        throw new InvalidOperationException("Đơn món đã đổi trạng thái. Chỉ xử lý đơn Pending.");
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
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            {
                if (phone == null) DemandRead(actor, "Order.View", connection, transaction);
                DemandSession(connection, transaction, sessionId, phone, phone != null);
                return ReadOrders(connection, transaction, sessionId);
            }
        }
        internal static List<ServiceOrder> ReadOrders(MySqlConnection connection, MySqlTransaction transaction, int sessionId)
        {
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
        private void DemandRead(LoginSession actor, string permission, MySqlConnection connection, MySqlTransaction transaction)
        {
            if (!permissions.ReadAccess(actor, connection, transaction).Permissions.ContainsKey(permission))
                throw new UnauthorizedAccessException("Bạn không có quyền thực hiện thao tác này.");
        }
        private int LockActiveSession(MySqlConnection connection, MySqlTransaction transaction, int id, string phone, bool byOrder)
        {
            int room, customer, sessionId; int? source;
            // Never acquire an Order/Session lock before Room and Customer.
            using (var discovery = database.OpenConnection())
            using (var query = discovery.CreateCommand())
            {
                query.CommandText = @"SELECT s.RoomId,s.CustomerId,s.ReservationId,s.RoomSessionId FROM RoomSessions s
JOIN Customers c ON c.CustomerId=s.CustomerId " + (byOrder ? "JOIN Orders o ON o.RoomSessionId=s.RoomSessionId " : "") +
                    "WHERE " + (byOrder ? "o.OrderId" : "s.RoomSessionId") + "=@id" + (phone == null ? "" : " AND c.PhoneNumber=@phone") + ";";
                query.Parameters.AddWithValue("@id", id);
                if (phone != null) query.Parameters.AddWithValue("@phone", phone);
                using (var reader = query.ExecuteReader())
                {
                    if (!reader.Read()) throw MissingSession(byOrder, phone);
                    room = reader.GetInt32(0); customer = reader.GetInt32(1);
                    source = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2); sessionId = reader.GetInt32(3);
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
                command.CommandText = "SELECT RoomSessionId FROM RoomSessions WHERE RoomSessionId=@session FOR UPDATE;";
                command.Parameters.AddWithValue("@session", sessionId); command.ExecuteScalar();
                command.CommandText = @"SELECT s.RoomId,s.CustomerId,s.ReservationId,s.Status FROM RoomSessions s
JOIN Customers c ON c.CustomerId=s.CustomerId WHERE s.RoomSessionId=@session" + (phone == null ? "" : " AND c.PhoneNumber=@phone") + ";";
                if (phone != null) command.Parameters.AddWithValue("@phone", phone);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read() || reader.GetInt32(0) != room || reader.GetInt32(1) != customer ||
                        (reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2)) != source || reader.GetString(3) != "Active")
                        throw MissingSession(byOrder, phone);
                }
            }
            // Orders do not read schedule candidates or run NoShow maintenance.
            return sessionId;
        }
        private static InvalidOperationException MissingSession(bool byOrder, string phone)
            => new InvalidOperationException(byOrder ? "Không tìm thấy đơn món hợp lệ trong phiên Active. Hãy tải lại."
                : "Không tìm thấy phiên phù hợp" + (phone == null ? "." : " của SĐT này.") + " Hãy tải lại.");
        private static void DemandSession(MySqlConnection connection, MySqlTransaction transaction, int id, string phone, bool active)
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
        private static ServiceOrder ReadOrder(MySqlConnection connection, MySqlTransaction transaction, int id)
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
        private static void WriteAudit(MySqlConnection connection, MySqlTransaction transaction, LoginSession actor, string phone,
            string action, int id, string description, DateTimeOffset now)
        {
            if (phone == null) MySqlAuditService.WriteStaff(connection, transaction, actor.UserId, action, "Order", id.ToString(CultureInfo.InvariantCulture), description, now);
            else MySqlAuditService.WriteGuest(connection, transaction, action, "Order", id.ToString(CultureInfo.InvariantCulture), description, now);
        }
        private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }
}
