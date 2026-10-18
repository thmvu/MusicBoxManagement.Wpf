using System;
using System.Collections.Generic;
using System.Globalization;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class RevenueReport
    {
        public DateTime FromDate { get; internal set; }
        public DateTime ToDate { get; internal set; }
        public string RoomTypeCode { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
        public bool CanExport { get; internal set; }
        public IReadOnlyList<RevenueReportRow> Days { get; internal set; }
        public IReadOnlyList<RevenueReportRow> Months { get; internal set; }
        public IReadOnlyList<RevenueReportRow> RoomTypes { get; internal set; }
        public int InvoiceCount { get; internal set; }
        public decimal RoomCharge { get; internal set; }
        public decimal ServiceCharge { get; internal set; }
        public decimal TotalAmount { get; internal set; }
        public string RoomChargeLabel => RoomCharge.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
        public string ServiceChargeLabel => ServiceCharge.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
        public string TotalAmountLabel => TotalAmount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
    }

    public sealed class RevenueReportRow
    {
        public string Key { get; internal set; }
        public string Label { get; internal set; }
        public int InvoiceCount { get; internal set; }
        public decimal RoomCharge { get; internal set; }
        public decimal ServiceCharge { get; internal set; }
        public decimal TotalAmount { get; internal set; }
        public string RoomChargeLabel => RoomCharge.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
        public string ServiceChargeLabel => ServiceCharge.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
        public string TotalAmountLabel => TotalAmount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
    }
}
