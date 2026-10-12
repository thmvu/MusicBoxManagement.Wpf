using System;
using System.Collections.Generic;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class InvoiceSummary
    {
        public int InvoiceId { get; internal set; }
        public string InvoiceNumber { get; internal set; }
        public string RoomCode { get; internal set; }
        public DateTimeOffset PaidAt { get; internal set; }
        public string PaymentMethod { get; internal set; }
        public decimal TotalAmount { get; internal set; }
    }
    public sealed class InvoiceSearch
    {
        public IReadOnlyList<InvoiceSummary> Items { get; internal set; }
        public bool CanPrint { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
    }
    public sealed class InvoiceLine
    {
        public int OrderId { get; internal set; }
        public int ServiceId { get; internal set; }
        public string ServiceNameSnapshot { get; internal set; }
        public int Quantity { get; internal set; }
        public long UnitPrice { get; internal set; }
        public decimal Amount => (decimal)Quantity * UnitPrice;
    }
    public sealed class InvoiceDetails
    {
        public string StoreName => "Music Box";
        public Invoice Invoice { get; internal set; }
        public string RoomCodeSnapshot { get; internal set; }
        public string RoomTypeCodeSnapshot { get; internal set; }
        public string RoomTypeNameSnapshot { get; internal set; }
        public long HourlyRate { get; internal set; }
        public DateTimeOffset ActualStartTime { get; internal set; }
        public DateTimeOffset ActualEndTime { get; internal set; }
        public long UsedTicks => (ActualEndTime - ActualStartTime).Ticks;
        public decimal UsedMinutes => UsedTicks / (decimal)TimeSpan.TicksPerMinute;
        public IReadOnlyList<InvoiceLine> Items { get; internal set; }
        public bool CanPrint { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
    }
}
