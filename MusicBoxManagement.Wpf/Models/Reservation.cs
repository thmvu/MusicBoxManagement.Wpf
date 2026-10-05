using System;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class ReservationRequest
    {
        public int RoomId { get; set; }
        public DateTimeOffset StartTime { get; set; }
        public int DurationMinutes { get; set; }
        public string FullName { get; set; }
        public string PhoneNumber { get; set; }
    }
    public sealed class Reservation
    {
        public int ReservationId { get; internal set; }
        public int CustomerId { get; internal set; }
        public int RoomId { get; internal set; }
        public DateTimeOffset StartTime { get; internal set; }
        public DateTimeOffset EndTime { get; internal set; }
        public string Status { get; internal set; }
        public string CreatedByUserId { get; internal set; }
        public DateTimeOffset CreatedAt { get; internal set; }
    }
}
