using System;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class WalkInPreview
    {
        public bool CanReceive { get; internal set; }
        public string Reason { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
        public DateTimeOffset? ReturnBy { get; internal set; }
        public long HourlyRate { get; internal set; }
    }
    public sealed class WalkInRequest
    {
        public int RoomId { get; set; }
        public string FullName { get; set; }
        public string PhoneNumber { get; set; }
    }

    public sealed class WalkInResult
    {
        public RoomSession Session { get; internal set; }
        public DateTimeOffset ReturnBy { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
        public bool IsOverdue => CheckedAt > ReturnBy;
    }
}
