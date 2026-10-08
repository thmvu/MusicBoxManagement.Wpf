using System;
using System.Collections.Generic;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class OrderLineRequest
    {
        public int ServiceId { get; set; }
        public int Quantity { get; set; }
    }
    // Public receipt: no customer, phone, creator or session entity.
    public sealed class ServiceOrder
    {
        public int OrderId { get; internal set; }
        public string Status { get; internal set; }
        public DateTimeOffset CreatedAt { get; internal set; }
        public List<ServiceOrderItem> Items { get; internal set; }
    }
    public sealed class ServiceOrderItem
    {
        public int ServiceId { get; internal set; }
        public string ServiceNameSnapshot { get; internal set; }
        public int Quantity { get; internal set; }
        public long UnitPrice { get; internal set; }
    }
}
