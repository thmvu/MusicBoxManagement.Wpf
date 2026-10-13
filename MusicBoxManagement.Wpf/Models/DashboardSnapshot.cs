using System;
using System.Collections.Generic;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class DashboardSnapshot
    {
        public DateTime Date { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
        public IReadOnlyList<DashboardRoom> Rooms { get; internal set; }
        public IReadOnlyList<DashboardBooking> BookingsToday { get; internal set; }
        public IReadOnlyList<DashboardSession> ActiveSessions { get; internal set; }
        public IReadOnlyList<DashboardOrder> PendingOrders { get; internal set; }
        public IReadOnlyList<DashboardAlert> Alerts { get; internal set; }
        // Null means this caller may not read revenue; zero means authorized with no receipts today.
        public decimal? TodayRevenue { get; internal set; }
    }
    public sealed class DashboardRoom
    {
        public int RoomId { get; internal set; }
        public string RoomCode { get; internal set; }
        public string RoomName { get; internal set; }
        public string Status { get; internal set; }
    }
    public sealed class DashboardBooking
    {
        public int ReservationId { get; internal set; }
        public int RoomId { get; internal set; }
        public string RoomCode { get; internal set; }
        public DateTimeOffset Start { get; internal set; }
        public DateTimeOffset End { get; internal set; }
        public string Status { get; internal set; }
        public DateTimeOffset CheckInDeadline => Start.AddMinutes(15);
    }
    public sealed class DashboardSession
    {
        public int SessionId { get; internal set; }
        public int RoomId { get; internal set; }
        public string RoomCode { get; internal set; }
        public DateTimeOffset ActualStart { get; internal set; }
        public DateTimeOffset? ExpectedEnd { get; internal set; }
        public DateTimeOffset? ReturnBy { get; internal set; }
        public bool IsWalkIn { get; internal set; }
        public bool IsOverdue { get; internal set; }
    }
    public sealed class DashboardOrder
    {
        public int OrderId { get; internal set; }
        public int SessionId { get; internal set; }
        public string RoomCode { get; internal set; }
        public DateTimeOffset CreatedAt { get; internal set; }
    }
    public sealed class DashboardAlert
    {
        // NearNoShow, Overdue, NextBooking, OutsideHours. UI translates these explicit kinds.
        public string Kind { get; internal set; }
        public string RoomCode { get; internal set; }
        public int? SessionId { get; internal set; }
        public int? ReservationId { get; internal set; }
        public DateTimeOffset RelevantAt { get; internal set; }
    }
}
