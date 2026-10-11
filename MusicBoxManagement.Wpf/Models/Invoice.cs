using System;
using System.Collections.Generic;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class Invoice
    {
        public int InvoiceId { get; internal set; }
        public int RoomSessionId { get; internal set; }
        public string InvoiceNumber { get; internal set; }
        public decimal RoomCharge { get; internal set; }
        public decimal ServiceCharge { get; internal set; }
        public decimal TotalAmount { get; internal set; }
        public string PaymentMethod { get; internal set; }
        public string ProcessedByUserId { get; internal set; }
        public string ProcessedByNameSnapshot { get; internal set; }
        public DateTimeOffset PaidAt { get; internal set; }
    }

    public sealed class CheckoutPreview
    {
        public SessionBill Bill { get; internal set; }
        public IReadOnlyList<ServiceOrder> CompletedOrders { get; internal set; }
    }
}
