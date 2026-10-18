using System;
using System.Data;
using System.Globalization;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class BookingStatusReportService
    {
        private readonly SqliteDatabase database;
        private readonly IClock clock;
        private readonly PermissionService permissions;
        public BookingStatusReportService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public BookingStatusReportService(SqliteDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new PermissionService(database);
        }

        public BookingStatusReport Read(LoginSession actor, DateTime fromDate, DateTime toDate)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            using (var command = connection.CreateCommand())
            {
                var access = permissions.ReadAccess(actor, connection, transaction);
                if ((access.Role != "Admin" && access.Role != "Manager") || !access.Permissions.ContainsKey("Report.View"))
                    throw new UnauthorizedAccessException("Chỉ Admin hoặc Manager có quyền báo cáo được xem thống kê booking.");
                if (fromDate.Date > toDate.Date) throw new ArgumentException("Ngày bắt đầu không được sau ngày kết thúc.");
                if (fromDate.Date == DateTime.MinValue.Date || toDate.Date == DateTime.MaxValue.Date)
                    throw new ArgumentException("Khoảng ngày vượt giới hạn thời gian được hỗ trợ.");
                var from = DayStart(fromDate); var until = DayStart(toDate.Date.AddDays(1));
                var now = clock.UtcNow.ToUniversalTime();
                var rows = new[] {
                    new BookingStatusRow { Status = "Confirmed", Label = "Đã xác nhận" },
                    new BookingStatusRow { Status = "CheckedIn", Label = "Đã nhận phòng" },
                    new BookingStatusRow { Status = "Completed", Label = "Đã hoàn tất" },
                    new BookingStatusRow { Status = "Cancelled", Label = "Đã hủy" },
                    new BookingStatusRow { Status = "NoShow", Label = "Không đến" }
                };
                var byStatus = rows.ToDictionary(r => r.Status, StringComparer.Ordinal);
                command.Transaction = transaction;
                // Coarse SQL filter only; exact .NET ticks decide inclusion, including legacy offset/fractions.
                // Malformed dates are retained so the parser reports bad data instead of hiding it.
                command.CommandText = @"SELECT StartTime,Status FROM Reservations
WHERE julianday(StartTime) IS NULL OR
(julianday(StartTime)>=julianday(@from)-1.0/86400 AND julianday(StartTime)<julianday(@until)+1.0/86400)
ORDER BY ReservationId;";
                command.Parameters.AddWithValue("@from", from.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("@until", until.ToString("O", CultureInfo.InvariantCulture));
                var total = 0;
                using (var reader = command.ExecuteReader()) while (reader.Read())
                {
                    var start = Stamp(reader.GetString(0));
                    if (start < from || start >= until) continue;
                    var status = reader.GetString(1);
                    // This report follows the status rule even if a legacy Confirmed row has a source session.
                    if (status == "Confirmed" && now - start >= TimeSpan.FromMinutes(15)) status = "NoShow";
                    BookingStatusRow row;
                    if (!byStatus.TryGetValue(status, out row)) throw new InvalidOperationException("Trạng thái booking không hợp lệ trong dữ liệu thống kê.");
                    row.Count = checked(row.Count + 1); total = checked(total + 1);
                }
                return new BookingStatusReport { FromDate = fromDate.Date, ToDate = toDate.Date, CheckedAt = now,
                    TotalCount = total, Rows = Array.AsReadOnly(rows) };
            }
        }
        private static DateTimeOffset DayStart(DateTime value) => new DateTimeOffset(DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified), BookingHours.VietnamOffset).ToUniversalTime();
        private static DateTimeOffset Stamp(string value)
        {
            DateTimeOffset result;
            var hasOffset = value != null && (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
                (value.Length >= 6 && (value[value.Length - 6] == '+' || value[value.Length - 6] == '-') && value[value.Length - 3] == ':'));
            if (!hasOffset || value.IndexOf('T') < 0 || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result))
                throw new InvalidOperationException("Dữ liệu giờ bắt đầu booking không hợp lệ; cần kiểm tra nguồn dữ liệu, không thể tự suy đoán.");
            return result.ToUniversalTime();
        }
    }
}
