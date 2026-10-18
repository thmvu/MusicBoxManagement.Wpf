using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class RoomUsageReportService
    {
        private readonly SqliteDatabase database;
        private readonly IClock clock;
        private readonly PermissionService permissions;
        public RoomUsageReportService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public RoomUsageReportService(SqliteDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new PermissionService(database);
        }

        public RoomUsageReport Read(LoginSession actor, DateTime fromDate, DateTime toDate, int? roomId = null)
            => ReadCore(actor, fromDate, toDate, roomId, false);

        public ReportExcelFile Export(LoginSession actor, DateTime fromDate, DateTime toDate, int? roomId = null)
            => ReportExcelWriter.CreateRooms(ReadCore(actor, fromDate, toDate, roomId, true));

        private RoomUsageReport ReadCore(LoginSession actor, DateTime fromDate, DateTime toDate, int? roomId, bool forExport)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            using (var command = connection.CreateCommand())
            {
                var access = permissions.ReadAccess(actor, connection, transaction);
                if ((access.Role != "Admin" && access.Role != "Manager") || !access.Permissions.ContainsKey("Report.View"))
                    throw new UnauthorizedAccessException("Chỉ Admin hoặc Manager có quyền báo cáo được xem thống kê phòng.");
                if (forExport && !access.Permissions.ContainsKey("Report.Export"))
                    throw new UnauthorizedAccessException("Bạn không có quyền xuất báo cáo Excel.");
                if (fromDate.Date > toDate.Date) throw new ArgumentException("Ngày bắt đầu không được sau ngày kết thúc.");
                if (fromDate.Date == DateTime.MinValue.Date || toDate.Date == DateTime.MaxValue.Date)
                    throw new ArgumentException("Khoảng ngày vượt giới hạn thời gian được hỗ trợ.");
                if (roomId.HasValue && roomId <= 0) throw new ArgumentException("Phòng không hợp lệ.");
                var from = DayStart(fromDate); var until = DayStart(toDate.Date.AddDays(1));
                var now = clock.UtcNow.ToUniversalTime();
                var all = new List<RoomUsageRow>();
                command.Transaction = transaction;
                command.CommandText = "SELECT RoomId,RoomCode,Name,IsActive,CreatedAt FROM Rooms ORDER BY RoomCode,RoomId;";
                using (var reader = command.ExecuteReader()) while (reader.Read())
                    all.Add(new RoomUsageRow { RoomId = reader.GetInt32(0), RoomCode = reader.GetString(1), RoomName = reader.GetString(2),
                        IsActive = reader.GetInt32(3) != 0, CreatedAt = Stamp(reader.GetString(4)) });
                if (roomId.HasValue && !all.Any(r => r.RoomId == roomId.Value)) throw new ArgumentException("Phòng không còn tồn tại. Hãy tải lại bộ lọc.");
                var rows = all.Where(r => !roomId.HasValue || r.RoomId == roomId.Value).ToList();
                var index = rows.ToDictionary(r => r.RoomId);
                var spans = rows.ToDictionary(r => r.RoomId, r => new List<Span>());
                foreach (var row in rows)
                {
                    var start = Later(from, row.CreatedAt); var end = Earlier(until, now);
                    row.AvailableMinutes = OpenTicks(start, end) / TimeSpan.TicksPerMinute;
                }
                command.CommandText = "SELECT RoomId,Status,ActualStartTime,ActualEndTime FROM RoomSessions WHERE (@room IS NULL OR RoomId=@room) ORDER BY RoomSessionId;";
                command.Parameters.AddWithValue("@room", (object)roomId ?? DBNull.Value);
                using (var reader = command.ExecuteReader()) while (reader.Read())
                {
                    RoomUsageRow row;
                    if (!index.TryGetValue(reader.GetInt32(0), out row)) throw new InvalidOperationException("Dữ liệu phiên tham chiếu phòng không tồn tại.");
                    var status = reader.GetString(1);
                    if (status != "Active" && status != "Completed") throw new InvalidOperationException("Trạng thái phiên không hợp lệ trong dữ liệu thống kê.");
                    var actualStart = Stamp(reader.GetString(2));
                    if (status == "Completed" && reader.IsDBNull(3)) throw new InvalidOperationException("Phiên hoàn tất thiếu giờ kết thúc thực tế.");
                    if (status == "Active" && !reader.IsDBNull(3)) throw new InvalidOperationException("Phiên đang sử dụng đã có giờ kết thúc thực tế không hợp lệ.");
                    var actualEnd = status == "Active" ? now : Stamp(reader.GetString(3));
                    // Count completions independently of CreatedAt, opening shifts, or the occupancy cutoff at now.
                    if (status == "Completed" && actualEnd >= from && actualEnd < until) row.CompletedCount = checked(row.CompletedCount + 1);
                    if (status == "Completed" && actualEnd < actualStart) throw new InvalidOperationException("Giờ kết thúc thực tế nằm trước giờ bắt đầu.");
                    var start = Later(Later(from, row.CreatedAt), actualStart);
                    var end = Earlier(Earlier(until, now), actualEnd);
                    if (end > start) spans[row.RoomId].Add(new Span { Start = start, End = end });
                }
                foreach (var row in rows)
                {
                    // Union legacy overlapping actual intervals instead of counting the same room-minute twice.
                    Span merged = null; decimal usedTicks = 0;
                    foreach (var span in spans[row.RoomId].OrderBy(s => s.Start))
                    {
                        if (merged == null) merged = new Span { Start = span.Start, End = span.End };
                        else if (span.Start <= merged.End) merged.End = Later(merged.End, span.End);
                        else { usedTicks += OpenTicks(merged.Start, merged.End); merged = new Span { Start = span.Start, End = span.End }; }
                    }
                    if (merged != null) usedTicks += OpenTicks(merged.Start, merged.End);
                    row.UsedMinutes = usedTicks / TimeSpan.TicksPerMinute;
                }
                return new RoomUsageReport { FromDate = fromDate.Date, ToDate = toDate.Date, RoomId = roomId, CheckedAt = now, CanExport = access.Permissions.ContainsKey("Report.Export"),
                    RoomChoices = all.Select(r => new RoomUsageChoice { RoomId = r.RoomId, Label = r.RoomCode + " — " + r.RoomName + (r.IsActive ? "" : " (đã khóa)") }).ToList().AsReadOnly(),
                    Rooms = rows.AsReadOnly(), CompletedCount = rows.Sum(r => r.CompletedCount), UsedMinutes = rows.Sum(r => r.UsedMinutes), AvailableMinutes = rows.Sum(r => r.AvailableMinutes) };
            }
        }
        private sealed class Span { public DateTimeOffset Start; public DateTimeOffset End; }
        private static DateTimeOffset Earlier(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
        private static DateTimeOffset Later(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;
        private static DateTimeOffset Stamp(string value)
        {
            DateTimeOffset result;
            var hasOffset = value != null && (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
                (value.Length >= 6 && (value[value.Length - 6] == '+' || value[value.Length - 6] == '-') && value[value.Length - 3] == ':'));
            if (!hasOffset || value.IndexOf('T') < 0 || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result))
                throw new InvalidOperationException("Dữ liệu thời gian phòng/phiên không hợp lệ; cần kiểm tra nguồn dữ liệu, không thể tự suy đoán.");
            return result.ToUniversalTime();
        }
        private static DateTimeOffset DayStart(DateTime value) => new DateTimeOffset(DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified), BookingHours.VietnamOffset).ToUniversalTime();
        private static decimal OpenTicks(DateTimeOffset start, DateTimeOffset end)
        {
            if (end <= start) return 0;
            // Constant-time prefix measure: no artificial date limit and no day-by-day loop.
            var localStart = start.ToOffset(BookingHours.VietnamOffset);
            var localEnd = end.ToOffset(BookingHours.VietnamOffset);
            var days = (localEnd.Date.Ticks - localStart.Date.Ticks) / TimeSpan.TicksPerDay;
            var ticks = days * 780m * TimeSpan.TicksPerMinute + OpenDayTicks(localEnd.TimeOfDay.Ticks) - OpenDayTicks(localStart.TimeOfDay.Ticks);
            return ticks;
        }
        private static long OpenDayTicks(long ticks)
        {
            var morning = Math.Max(0L, Math.Min(ticks, 12 * TimeSpan.TicksPerHour) - 9 * TimeSpan.TicksPerHour);
            var evening = Math.Max(0L, Math.Min(ticks, 23 * TimeSpan.TicksPerHour) - 13 * TimeSpan.TicksPerHour);
            return morning + evening;
        }
    }
}
