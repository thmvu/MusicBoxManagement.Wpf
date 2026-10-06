using System;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class RoomSession
    {
        public int RoomSessionId { get; internal set; }
        public int CustomerId { get; internal set; }
        public int RoomId { get; internal set; }
        public int? ReservationId { get; internal set; }
        public DateTimeOffset ActualStartTime { get; internal set; }
        public DateTimeOffset? ExpectedEndTime { get; internal set; }
        public DateTimeOffset? ActualEndTime { get; internal set; }
        public long HourlyRate { get; internal set; }
        public string RoomCodeSnapshot { get; internal set; }
        public string RoomTypeCodeSnapshot { get; internal set; }
        public string RoomTypeNameSnapshot { get; internal set; }
        public string Status { get; internal set; }
    }
}
