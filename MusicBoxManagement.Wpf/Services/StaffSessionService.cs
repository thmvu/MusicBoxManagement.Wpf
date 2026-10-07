using System;
using System.Collections.Generic;
using System.Data;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class StaffSessionService
    {
        private readonly SqliteDatabase database;
        private readonly IClock clock;
        private readonly PermissionService permissions;
        private readonly RoomSessionService sessions;
        public StaffSessionService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public StaffSessionService(SqliteDatabase database, IClock clock)
        { this.database = database; this.clock = clock; permissions = new PermissionService(database); sessions = new RoomSessionService(database, clock); }
        public StaffSessionSearch Search(LoginSession actor, string phoneNumber = null, string status = "Active")
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                permissions.Demand(actor, "Session.View", connection, transaction);
                var access = permissions.ReadAccess(actor, connection, transaction);
                var phone = string.IsNullOrWhiteSpace(phoneNumber) ? null : PhoneNumberNormalizer.Normalize(phoneNumber);
                if (status != null && status != "Active" && status != "Completed") throw new ArgumentException("Trạng thái phiên không hợp lệ.");
                var now = clock.UtcNow.ToUniversalTime(); var items = new List<StaffSession>();
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"SELECT s.RoomSessionId,s.CustomerId,s.RoomId,s.ReservationId,s.ActualStartTime,s.ExpectedEndTime,s.ActualEndTime,
s.HourlyRate,s.RoomCodeSnapshot,s.RoomTypeCodeSnapshot,s.RoomTypeNameSnapshot,s.Status,c.FullName,c.PhoneNumber
FROM RoomSessions s JOIN Customers c ON c.CustomerId=s.CustomerId
WHERE (@phone IS NULL OR c.PhoneNumber=@phone) AND (@status IS NULL OR s.Status=@status)
ORDER BY s.ActualStartTime DESC,s.RoomSessionId DESC;";
                    command.Parameters.AddWithValue("@phone", (object)phone ?? DBNull.Value);
                    command.Parameters.AddWithValue("@status", (object)status ?? DBNull.Value);
                    using (var reader = command.ExecuteReader()) while (reader.Read())
                        items.Add(new StaffSession { Session = RoomSessionService.ReadSessionRecord(reader), CustomerName = reader.GetString(12), PhoneNumber = reader.GetString(13), CheckedAt = now });
                }
                foreach (var item in items)
                    if (item.Session.Status == "Active") item.ReturnBy = item.Session.ExpectedEndTime ?? ScheduleRules.WalkInReturnBy(connection, transaction,
                        item.Session.RoomId, item.Session.CustomerId, item.Session.ActualStartTime, now);
                return new StaffSessionSearch { Items = items, CanExtend = access.Permissions.ContainsKey("Session.Extend") };
            }
        }
        public SessionExtensionCheck Preview(LoginSession actor, int sessionId, int minutes)
        {
            return sessions.PreviewViewedExtension(actor, sessionId, minutes);
        }
        public RoomSession Extend(LoginSession actor, int sessionId, DateTimeOffset observedEnd, int minutes)
            => sessions.ExtendViewedStaff(actor, sessionId, observedEnd, minutes);
    }
}
