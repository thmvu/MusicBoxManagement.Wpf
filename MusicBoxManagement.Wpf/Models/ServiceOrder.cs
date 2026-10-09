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
    public sealed class OrderMenuItem
    {
        public int ServiceId { get; internal set; }
        public string Name { get; internal set; }
        public string Category { get; internal set; }
        public long Price { get; internal set; }
        public string Description { get; internal set; }
    }
    public sealed class OrderMenu
    {
        public List<OrderMenuItem> Items { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
        public bool CanCreate { get; internal set; }
        public string Reason { get; internal set; }
    }
    // Cart estimate only; neither a saved order nor a session bill or price lock.
    public sealed class OrderPreview
    {
        public List<ServiceOrderItem> Items { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
        public decimal Amount { get; internal set; }
    }
}
