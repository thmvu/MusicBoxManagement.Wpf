using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class ServiceSalesReportService
    {
        private readonly SqliteDatabase database;
        private readonly IClock clock;
        private readonly PermissionService permissions;
        public ServiceSalesReportService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public ServiceSalesReportService(SqliteDatabase database, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new PermissionService(database);
        }

        public ServiceSalesReport Read(LoginSession actor, DateTime fromDate, DateTime toDate, int? serviceId = null)
            => ReadCore(actor, fromDate, toDate, serviceId, false);

        public ReportExcelFile Export(LoginSession actor, DateTime fromDate, DateTime toDate, int? serviceId = null, bool topItems = false)
            => ReportExcelWriter.CreateServices(ReadCore(actor, fromDate, toDate, serviceId, true), topItems);

        private ServiceSalesReport ReadCore(LoginSession actor, DateTime fromDate, DateTime toDate, int? serviceId, bool forExport)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            using (var command = connection.CreateCommand())
            {
                var access = permissions.ReadAccess(actor, connection, transaction);
                if ((access.Role != "Admin" && access.Role != "Manager") || !access.Permissions.ContainsKey("Report.View"))
                    throw new UnauthorizedAccessException("Chỉ Admin hoặc Manager có quyền báo cáo được xem doanh thu món.");
                if (forExport && !access.Permissions.ContainsKey("Report.Export"))
                    throw new UnauthorizedAccessException("Bạn không có quyền xuất báo cáo Excel.");
                if (fromDate.Date > toDate.Date || fromDate.Date == DateTime.MinValue.Date || toDate.Date == DateTime.MaxValue.Date)
                    throw new ArgumentException("Khoảng ngày báo cáo không hợp lệ.");
                var from = DayStart(fromDate); var until = DayStart(toDate.Date.AddDays(1));
                var result = new ServiceSalesReport { FromDate = fromDate.Date, ToDate = toDate.Date, ServiceId = serviceId,
                    CheckedAt = clock.UtcNow.ToUniversalTime(), CanExport = access.Permissions.ContainsKey("Report.Export") };
                command.Transaction = transaction;
                command.CommandText = "SELECT ServiceId,Name,IsActive FROM Services ORDER BY Name,ServiceId;";
                var choices = new List<ServiceReportChoice> { new ServiceReportChoice { Label = "Tất cả dịch vụ" } };
                using (var reader = command.ExecuteReader()) while (reader.Read())
                    choices.Add(new ServiceReportChoice { ServiceId = reader.GetInt32(0),
                        Label = reader.GetString(1) + " (#" + reader.GetInt32(0) + ")" + (reader.GetInt32(2) == 0 ? " — ngừng bán" : "") });
                if (serviceId.HasValue && !choices.Any(s => s.ServiceId == serviceId))
                    throw new ArgumentException("Dịch vụ không tồn tại. Hãy tải lại danh sách.");
                result.Services = choices.AsReadOnly();
                // Completed orders from invoiced sessions only. Historical item prices/names,
                // not current catalog values. The coarse window tolerates legacy UTC spellings.
                command.CommandText = @"SELECT i.PaidAt,it.ServiceId,it.ServiceNameSnapshot,it.Quantity,it.UnitPrice
FROM Invoices i JOIN Orders o ON o.RoomSessionId=i.RoomSessionId AND o.Status='Completed'
JOIN OrderItems it ON it.OrderId=o.OrderId
WHERE (@service IS NULL OR it.ServiceId=@service)
AND julianday(i.PaidAt)>=julianday(@from)-1.0/86400
AND julianday(i.PaidAt)<julianday(@until)+1.0/86400
ORDER BY i.InvoiceId,o.OrderId,it.OrderItemId;";
                command.Parameters.AddWithValue("@service", (object)serviceId ?? DBNull.Value);
                command.Parameters.AddWithValue("@from", from.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("@until", until.ToString("O", CultureInfo.InvariantCulture));
                var rows = new Dictionary<int, ServiceSalesRow>();
                var nameDates = new Dictionary<int, DateTimeOffset>();
                try
                {
                    using (var reader = command.ExecuteReader()) while (reader.Read())
                    {
                        var paidAt = Stamp(reader.GetString(0));
                        if (paidAt < from || paidAt >= until) continue;
                        var id = reader.GetInt32(1); var quantity = reader.GetInt32(3);
                        var revenue = (decimal)reader.GetInt64(4) * quantity;
                        ServiceSalesRow row;
                        if (!rows.TryGetValue(id, out row))
                        { row = new ServiceSalesRow { ServiceId = id }; rows.Add(id, row); }
                        if (!nameDates.ContainsKey(id) || paidAt >= nameDates[id])
                        { row.Name = reader.GetString(2); nameDates[id] = paidAt; }
                        row.Quantity = checked(row.Quantity + quantity); row.Revenue += revenue;
                        result.TotalQuantity = checked(result.TotalQuantity + quantity); result.TotalRevenue += revenue;
                    }
                }
                catch (OverflowException) { throw new InvalidOperationException("Tổng số lượng hoặc doanh thu món vượt phạm vi hỗ trợ."); }
                result.Rows = rows.Values.OrderByDescending(r => r.Revenue).ThenBy(r => r.ServiceId).ToList().AsReadOnly();
                result.TopItems = rows.Values.OrderByDescending(r => r.Quantity).ThenByDescending(r => r.Revenue)
                    .ThenBy(r => r.ServiceId).Take(10).ToList().AsReadOnly();
                return result;
            }
        }
        private static DateTimeOffset DayStart(DateTime value) => new DateTimeOffset(
            DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified), BookingHours.VietnamOffset).ToUniversalTime();
        private static DateTimeOffset Stamp(string value)
        {
            DateTimeOffset parsed;
            var hasOffset = value != null && (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
                value.Length >= 6 && (value[value.Length - 6] == '+' || value[value.Length - 6] == '-') && value[value.Length - 3] == ':');
            if (!hasOffset || value.IndexOf('T') < 0 || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed))
                throw new InvalidOperationException("Thời điểm thanh toán không hợp lệ hoặc thiếu múi giờ.");
            return parsed.ToUniversalTime();
        }
    }
}
