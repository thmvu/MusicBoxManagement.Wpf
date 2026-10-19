using System;
using System.Collections.Generic;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class CustomerHistory
    {
        public Customer Customer { get; internal set; }
        public IReadOnlyList<CustomerReservationHistory> Reservations { get; internal set; }
        public IReadOnlyList<RoomSession> Sessions { get; internal set; }
        public IReadOnlyList<InvoiceSummary> Invoices { get; internal set; }
        public int CompletedSessionCount { get; internal set; }
        public DateTimeOffset? LastUsedAt { get; internal set; }
        public bool CanViewInvoices { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
    }

    public sealed class CustomerReservationHistory
    {
        public int ReservationId { get; internal set; }
        public string RoomCode { get; internal set; }
        public DateTimeOffset StartTime { get; internal set; }
        public DateTimeOffset EndTime { get; internal set; }
        public string Status { get; internal set; }
        public string CancellationReason { get; internal set; }
    }
}
