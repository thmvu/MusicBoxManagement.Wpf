using System;
using System.Collections.Generic;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class StaffReservation
    {
        public int ReservationId { get; internal set; }
        public int CustomerId { get; internal set; }
        public string CustomerName { get; internal set; }
        public string PhoneNumber { get; internal set; }
        public string RoomCode { get; internal set; }
        public string RoomName { get; internal set; }
        public DateTimeOffset StartTime { get; internal set; }
        public DateTimeOffset EndTime { get; internal set; }
        public DateTimeOffset CreatedAt { get; internal set; }
        public string CreatedByName { get; internal set; }
        public string Status { get; internal set; }
        public string CancellationReason { get; internal set; }
        public int? SessionId { get; internal set; }
        public bool IsCancellable { get; internal set; }
        public string StartLabel => Local(StartTime);
        public string EndLabel => Local(EndTime);
        public string Details => "Booking #" + ReservationId + " — " + Status + "\n\n"
            + "Phòng: " + RoomCode + " — " + RoomName + "\n"
            + "Khách: " + CustomerName + "\nSĐT: " + PhoneNumber + "\n\n"
            + "Bắt đầu: " + StartLabel + "\nKết thúc: " + EndLabel + "\n"
            + "Hạn nhận: trước " + Local(StartTime.AddMinutes(15)) + "\n\n"
            + "Người đặt: " + CreatedByName + "\nTạo lúc: " + Local(CreatedAt)
            + (SessionId.HasValue ? "\nPhiên nguồn: #" + SessionId : "")
            + (string.IsNullOrEmpty(CancellationReason) ? "" : "\n\nLý do hủy: " + CancellationReason);
        private static string Local(DateTimeOffset value) => value.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm dd/MM/yyyy");
    }

    public sealed class StaffReservationSearch
    {
        public List<StaffReservation> Items { get; internal set; }
        public bool CanCancel { get; internal set; }
    }
}
