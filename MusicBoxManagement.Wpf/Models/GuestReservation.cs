using System;
using System.Collections.Generic;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class GuestReservation
    {
        public int ReservationId { get; internal set; }
        public string RoomCode { get; internal set; }
        public string RoomName { get; internal set; }
        public DateTimeOffset StartTime { get; internal set; }
        public DateTimeOffset EndTime { get; internal set; }
        public bool CanCancel { get; internal set; }
        public string StartLabel => StartTime.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm dd/MM/yyyy");
        public string EndLabel => EndTime.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm dd/MM/yyyy");
        public string CancelNotice => CanCancel ? "Có thể hủy trước hoặc đúng " + StartTime.AddHours(-2).ToOffset(BookingHours.VietnamOffset).ToString("HH:mm dd/MM/yyyy")
            : "Còn dưới 2 giờ đến giờ đặt. Vui lòng liên hệ cửa hàng nếu cần hủy.";
    }
    public sealed class GuestReservationLookup
    {
        public List<GuestReservation> Items { get; internal set; }
        public GuestSession ActiveSession { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
    }
}
