using System;
using System.Collections.Generic;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class ServiceSalesReport
    {
        public DateTime FromDate { get; internal set; }
        public DateTime ToDate { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
        public bool CanExport { get; internal set; }
        public int? ServiceId { get; internal set; }
        public long TotalQuantity { get; internal set; }
        public decimal TotalRevenue { get; internal set; }
        public IReadOnlyList<ServiceSalesRow> Rows { get; internal set; }
        public IReadOnlyList<ServiceSalesRow> TopItems { get; internal set; }
        public IReadOnlyList<ServiceReportChoice> Services { get; internal set; }
    }

    public sealed class ServiceSalesRow
    {
        public int ServiceId { get; internal set; }
        public string Name { get; internal set; }
        public long Quantity { get; internal set; }
        public decimal Revenue { get; internal set; }
    }

    public sealed class ServiceReportChoice
    {
        public int? ServiceId { get; internal set; }
        public string Label { get; internal set; }
    }
}
