using System;
using System.Collections.Generic;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class PublicBookingSlot
    {
        public DateTimeOffset Start { get; internal set; }
        public string State { get; internal set; }
        public bool CanChoose => State == "Available";
        public string TimeLabel => Start.ToOffset(TimeSpan.FromHours(7)).ToString("HH:mm");
        public string StateLabel => State == "Available" ? "Có thể đặt" : State == "Busy" ? "Bận" : State == "Past" ? "Đã qua" : State == "Break" ? "Giờ nghỉ" : "Không đủ ca";
    }
    // Anonymous room availability: deliberately contains no customer/source identifiers.
    public sealed class PublicRoomDay
    {
        public string RoomCode { get; internal set; }
        public string RoomName { get; internal set; }
        public DateTime Date { get; internal set; }
        public int DurationMinutes { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
        public IReadOnlyList<PublicBookingSlot> Slots { get; internal set; }
    }
}
