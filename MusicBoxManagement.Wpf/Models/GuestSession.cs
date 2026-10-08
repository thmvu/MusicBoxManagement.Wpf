using System;

namespace MusicBoxManagement.Wpf.Models
{
    // Only the active session belonging to the submitted phone; no customer or source IDs.
    public sealed class GuestSession
    {
        public int SessionId { get; internal set; }
        public string RoomCode { get; internal set; }
        public string RoomTypeName { get; internal set; }
        public long HourlyRate { get; internal set; }
        public DateTimeOffset ActualStartTime { get; internal set; }
        public DateTimeOffset? ExpectedEndTime { get; internal set; }
        public DateTimeOffset ReturnBy { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
        public bool FromBooking { get; internal set; }
        public bool CanExtend => FromBooking && ExpectedEndTime.HasValue && CheckedAt <= ExpectedEndTime.Value;
    }
}
