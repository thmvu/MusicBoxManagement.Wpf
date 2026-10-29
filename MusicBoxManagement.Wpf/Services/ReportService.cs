using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class ReportService : IReportService
    {
        private readonly SqliteDatabase database;
        private readonly IClock clock;
        private readonly PermissionService permissions;
        public ReportService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public ReportService(SqliteDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new PermissionService(database);
        }

        public RevenueReport ReadRevenue(LoginSession actor, DateTime fromDate, DateTime toDate, string roomTypeCode = null)
            => ReadRevenueCore(actor, fromDate, toDate, roomTypeCode, false);

        public ReportExcelFile ExportRevenue(LoginSession actor, DateTime fromDate, DateTime toDate,
            string roomTypeCode = null, string mode = "Day")
        {
            var result = ReadRevenueCore(actor, fromDate, toDate, roomTypeCode, true);
            if (mode != "Day" && mode != "Month" && mode != "RoomType")
                throw new ArgumentException("Cách nhóm báo cáo không hợp lệ.");
            return ReportExcelWriter.CreateRevenue(result, mode);
        }

        private RevenueReport ReadRevenueCore(LoginSession actor, DateTime fromDate, DateTime toDate, string roomTypeCode, bool forExport)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            using (var command = connection.CreateCommand())
            {
                var access = permissions.ReadAccess(actor, connection, transaction);
                if ((access.Role != "Admin" && access.Role != "Manager") || !access.Permissions.ContainsKey("Report.View"))
                    throw new UnauthorizedAccessException("Chỉ Admin hoặc Manager có quyền báo cáo được xem doanh thu tổng hợp.");
                if (forExport && !access.Permissions.ContainsKey("Report.Export"))
                    throw new UnauthorizedAccessException("Bạn không có quyền xuất báo cáo Excel.");
                if (fromDate.Date > toDate.Date) throw new ArgumentException("Ngày bắt đầu không được sau ngày kết thúc.");
                if (toDate.Date == DateTime.MaxValue.Date || fromDate.Date == DateTime.MinValue.Date)
                    throw new ArgumentException("Khoảng ngày vượt giới hạn thời gian được hỗ trợ.");
                var type = string.IsNullOrWhiteSpace(roomTypeCode) ? null : roomTypeCode.Trim().ToUpperInvariant();
                if (type != null && type != "STANDARD" && type != "VIP") throw new ArgumentException("Loại phòng phải là Standard hoặc VIP.");
                var from = DayStart(fromDate); var until = DayStart(toDate.Date.AddDays(1));
                var result = new RevenueReport { FromDate = fromDate.Date, ToDate = toDate.Date, RoomTypeCode = type,
                    CheckedAt = clock.UtcNow.ToUniversalTime(), CanExport = access.Permissions.ContainsKey("Report.Export") };
                var days = new SortedDictionary<string, RevenueReportRow>(StringComparer.Ordinal);
                var months = new SortedDictionary<string, RevenueReportRow>(StringComparer.Ordinal);
                var types = new SortedDictionary<string, RevenueReportRow>(StringComparer.Ordinal);
                command.Transaction = transaction;
                // Coarse date restriction tolerates legacy UTC offset/fraction spellings. Exact inclusion uses .NET ticks below.
                command.CommandText = @"SELECT i.PaidAt,s.RoomTypeCodeSnapshot,i.RoomCharge,i.ServiceCharge,i.TotalAmount
FROM Invoices i JOIN RoomSessions s ON s.RoomSessionId=i.RoomSessionId
WHERE (@type IS NULL OR s.RoomTypeCodeSnapshot=@type)
AND julianday(i.PaidAt)>=julianday(@from)-1.0/86400
AND julianday(i.PaidAt)<julianday(@until)+1.0/86400 ORDER BY i.InvoiceId;";
                command.Parameters.AddWithValue("@type", (object)type ?? DBNull.Value);
                command.Parameters.AddWithValue("@from", from.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("@until", until.ToString("O", CultureInfo.InvariantCulture));
                try
                {
                    using (var reader = command.ExecuteReader()) while (reader.Read())
                    {
                        var paidAt = DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
                        if (paidAt < from || paidAt >= until) continue;
                        var local = paidAt.ToOffset(BookingHours.VietnamOffset); var snapshot = reader.GetString(1);
                        var room = decimal.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
                        var service = decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture);
                        var total = decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture);
                        result.InvoiceCount = checked(result.InvoiceCount + 1);
                        result.RoomCharge += room; result.ServiceCharge += service; result.TotalAmount += total;
                        Add(days, local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), room, service, total);
                        Add(months, local.ToString("yyyy-MM", CultureInfo.InvariantCulture), local.ToString("MM/yyyy", CultureInfo.InvariantCulture), room, service, total);
                        Add(types, snapshot, snapshot == "STANDARD" ? "Standard" : "VIP", room, service, total);
                    }
                }
                catch (OverflowException) { throw new InvalidOperationException("Tổng doanh thu vượt phạm vi số tiền hỗ trợ."); }
                result.Days = days.Values.ToList().AsReadOnly(); result.Months = months.Values.ToList().AsReadOnly(); result.RoomTypes = types.Values.ToList().AsReadOnly();
                return result;
            }
        }
        private static DateTimeOffset DayStart(DateTime value) => new DateTimeOffset(DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified), BookingHours.VietnamOffset).ToUniversalTime();
        private static void Add(IDictionary<string, RevenueReportRow> rows, string key, string label, decimal room, decimal service, decimal total)
        {
            RevenueReportRow row;
            if (!rows.TryGetValue(key, out row)) { row = new RevenueReportRow { Key = key, Label = label }; rows.Add(key, row); }
            row.InvoiceCount = checked(row.InvoiceCount + 1); row.RoomCharge += room; row.ServiceCharge += service; row.TotalAmount += total;
        }
    }
}
