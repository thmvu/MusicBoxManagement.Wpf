using System;
using System.Collections.Generic;
using System.Data;
using MySqlConnector;
using System.Globalization;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class MySqlAuditLogService
    {
        private readonly MySqlDatabase database;
        private readonly IClock clock;
        private readonly MySqlPermissionService permissions;
        public MySqlAuditLogService(MySqlDatabase database) : this(database, new SystemClock()) { }
        public MySqlAuditLogService(MySqlDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new MySqlPermissionService(database);
        }

        public AuditLogPage Search(LoginSession actor, DateTime? fromDate = null, DateTime? toDate = null,
            string actorType = null, string action = null, string entityName = null, int? beforeId = null, int pageSize = 100)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            {
                DemandRead(actor, connection, transaction);
                if (pageSize < 1 || pageSize > 200) throw new ArgumentException("Số dòng mỗi trang phải từ 1 đến 200.");
                if (beforeId.HasValue && beforeId.Value <= 0) throw new ArgumentException("Mốc trang nhật ký không hợp lệ.");
                actorType = Filter(actorType); action = Filter(action); entityName = Filter(entityName);
                if (actorType != null && actorType != "Staff" && actorType != "Guest" && actorType != "System")
                    throw new ArgumentException("Tác nhân nhật ký không hợp lệ.");
                if (fromDate.HasValue && toDate.HasValue && fromDate.Value.Date > toDate.Value.Date)
                    throw new ArgumentException("Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.");
                var from = fromDate.HasValue ? (DateTimeOffset?)LocalDay(fromDate.Value) : null;
                DateTimeOffset? until = null;
                if (toDate.HasValue)
                {
                    if (toDate.Value.Date == DateTime.MaxValue.Date) throw new ArgumentException("Ngày kết thúc quá lớn.");
                    until = LocalDay(toDate.Value.Date.AddDays(1));
                }
                var now = clock.UtcNow.ToUniversalTime();
                var items = new List<AuditLogEntry>();
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    // MySQL schema stores fixed-width UTC O strings: exact tick bounds, no date rounding.
                    command.CommandText = Select + @"
WHERE (@before IS NULL OR a.AuditLogId < @before)
AND (@actor IS NULL OR a.ActorType = @actor)
AND (@action IS NULL OR a.Action = @action)
AND (@entity IS NULL OR a.EntityName = @entity)
AND (@from IS NULL OR a.CreatedAt >= @from)
AND (@until IS NULL OR a.CreatedAt < @until)
ORDER BY a.AuditLogId DESC LIMIT @limit;";
                    command.Parameters.AddWithValue("@limit", pageSize + 1);
                    command.Parameters.AddWithValue("@before", (object)beforeId ?? DBNull.Value);
                    command.Parameters.AddWithValue("@actor", (object)actorType ?? DBNull.Value);
                    command.Parameters.AddWithValue("@action", (object)action ?? DBNull.Value);
                    command.Parameters.AddWithValue("@entity", (object)entityName ?? DBNull.Value);
                    command.Parameters.AddWithValue("@from", from.HasValue ? (object)from.Value.ToString("O", CultureInfo.InvariantCulture) : DBNull.Value);
                    command.Parameters.AddWithValue("@until", until.HasValue ? (object)until.Value.ToString("O", CultureInfo.InvariantCulture) : DBNull.Value);
                    using (var reader = command.ExecuteReader()) while (reader.Read())
                    {
                        var item = Record(reader);
                        if ((from.HasValue && item.CreatedAt < from.Value) || (until.HasValue && item.CreatedAt >= until.Value)) continue;
                        items.Add(item);
                        if (items.Count > pageSize) break;
                    }
                }
                var hasMore = items.Count > pageSize;
                if (hasMore) items.RemoveAt(items.Count - 1);
                return new AuditLogPage { Items = items.AsReadOnly(), HasMore = hasMore,
                    NextBeforeId = hasMore ? (int?)items[items.Count - 1].AuditLogId : null, CheckedAt = now };
            }
        }

        public AuditLogEntry Read(LoginSession actor, int auditLogId)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            {
                DemandRead(actor, connection, transaction);
                if (auditLogId <= 0) throw new ArgumentException("Mã nhật ký không hợp lệ.");
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = Select + " WHERE a.AuditLogId=@id;";
                    command.Parameters.AddWithValue("@id", auditLogId);
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read()) throw new InvalidOperationException("Nhật ký không còn tồn tại. Hãy tải lại danh sách.");
                        return Record(reader);
                    }
                }
            }
        }

        private const string Select = @"SELECT a.AuditLogId,a.ActorType,a.UserId,a.Action,a.EntityName,a.EntityId,a.Description,a.CreatedAt,u.UserName,u.FullName
FROM AuditLog a LEFT JOIN AspNetUsers u ON u.Id=a.UserId";
        private void DemandRead(LoginSession actor, MySqlConnection connection, MySqlTransaction transaction)
        {
            if (!permissions.ReadAccess(actor, connection, transaction).Permissions.ContainsKey("Audit.View"))
                throw new UnauthorizedAccessException("Bạn không có quyền xem nhật ký.");
        }
        private static string Filter(string value)
        {
            value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (value != null && value.Length > 100) throw new ArgumentException("Bộ lọc nhật ký tối đa 100 ký tự.");
            return value;
        }
        private static DateTimeOffset LocalDay(DateTime day)
        { return new DateTimeOffset(DateTime.SpecifyKind(day.Date, DateTimeKind.Unspecified), BookingHours.VietnamOffset).ToUniversalTime(); }
        private static AuditLogEntry Record(MySqlDataReader r)
        {
            return new AuditLogEntry { AuditLogId = r.GetInt32(0), ActorType = r.GetString(1), UserId = Text(r, 2),
                Action = r.GetString(3), EntityName = r.GetString(4), EntityId = Text(r, 5), Description = r.GetString(6),
                CreatedAt = DateTimeOffset.Parse(r.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime(),
                UserName = Text(r, 8), FullName = Text(r, 9) };
        }
        private static string Text(MySqlDataReader r, int ordinal) => r.IsDBNull(ordinal) ? null : r.GetString(ordinal);
    }
}
