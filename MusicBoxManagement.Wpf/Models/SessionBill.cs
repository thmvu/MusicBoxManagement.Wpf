using System;

namespace MusicBoxManagement.Wpf.Models
{
    // Current-session estimate, not an invoice or a price guarantee. No customer PII or order history.
    public sealed class SessionBill
    {
        public int SessionId { get; internal set; }
        public string RoomCode { get; internal set; }
        public string RoomTypeName { get; internal set; }
        public long HourlyRate { get; internal set; }
        public DateTimeOffset ActualStartTime { get; internal set; }
        public DateTimeOffset BillingEndTime { get; internal set; }
        public long UsedTicks { get; internal set; }
        public decimal UsedMinutes => UsedTicks / (decimal)TimeSpan.TicksPerMinute;
        public decimal RoomCharge { get; internal set; }
        public decimal ServiceCharge { get; internal set; }
        public decimal TotalAmount => RoomCharge + ServiceCharge;
        public int CompletedOrderCount { get; internal set; }
        public int PendingOrderCount { get; internal set; }
        public int CancelledOrderCount { get; internal set; }
    }
}
