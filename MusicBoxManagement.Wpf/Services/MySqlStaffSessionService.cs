using System;
using System.Collections.Generic;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class MySqlStaffSessionService
    {
        private readonly MySqlDatabase database;
        private readonly IClock clock;
        private readonly MySqlPermissionService permissions;
        private readonly MySqlRoomSessionService sessions;
        public MySqlStaffSessionService(MySqlDatabase database) : this(database, new SystemClock()) { }
        public MySqlStaffSessionService(MySqlDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database)); this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new MySqlPermissionService(database); sessions = new MySqlRoomSessionService(database, clock);
        }
        public StaffSessionSearch Search(LoginSession actor, string phoneNumber = null, string status = "Active")
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            {
                var access = permissions.ReadAccess(actor, connection, transaction);
                if (!access.Permissions.ContainsKey("Session.View")) throw new UnauthorizedAccessException("Cần quyền xem phiên sử dụng.");
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
                    command.Parameters.AddWithValue("@phone", (object)phone ?? DBNull.Value); command.Parameters.AddWithValue("@status", (object)status ?? DBNull.Value);
                    using (var reader = command.ExecuteReader()) while (reader.Read())
                        items.Add(new StaffSession { Session = MySqlRoomSessionService.ReadSessionRecord(reader), CustomerName = reader.GetString(12), PhoneNumber = reader.GetString(13), CheckedAt = now });
                }
                foreach (var item in items)
                    if (item.Session.Status == "Active") item.ReturnBy = item.Session.ExpectedEndTime ?? MySqlScheduleRules.WalkInReturnBy(connection, transaction,
                        item.Session.RoomId, item.Session.CustomerId, item.Session.ActualStartTime, now);
                return new StaffSessionSearch { Items = items, CanExtend = access.Permissions.ContainsKey("Session.Extend") };
            }
        }
        public SessionExtensionCheck Preview(LoginSession actor, int sessionId, int minutes) => sessions.PreviewViewedExtension(actor, sessionId, minutes);
        public RoomSession Extend(LoginSession actor, int sessionId, DateTimeOffset observedEnd, int minutes) => sessions.ExtendViewedStaff(actor, sessionId, observedEnd, minutes);
    }
}
