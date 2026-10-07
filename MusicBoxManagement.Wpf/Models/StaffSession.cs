using System;
using System.Collections.Generic;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class StaffSession
    {
        public RoomSession Session { get; internal set; }
        public string CustomerName { get; internal set; }
        public string PhoneNumber { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
        public DateTimeOffset? ReturnBy { get; internal set; }
        public bool IsExtendable => Session.Status == "Active" && Session.ReservationId.HasValue && Session.ExpectedEndTime.HasValue && CheckedAt <= Session.ExpectedEndTime.Value;
        public string SourceLabel => Session.ReservationId.HasValue ? "Booking #" + Session.ReservationId : "Khách trực tiếp";
        public string StartLabel => Local(Session.ActualStartTime);
        public string EndLabel => Session.Status == "Completed" ? Local(Session.ActualEndTime.Value) : Local(ReturnBy.Value);
        public string Details => "Phiên #" + Session.RoomSessionId + " — " + Session.Status + "\nNguồn: " + SourceLabel
            + "\nPhòng: " + Session.RoomCodeSnapshot + " — " + Session.RoomTypeNameSnapshot
            + "\nKhách: " + CustomerName + "\nSĐT: " + PhoneNumber
            + "\n\nNhận thực tế: " + StartLabel
            + (Session.ExpectedEndTime.HasValue ? "\nDự kiến trả: " + Local(Session.ExpectedEndTime.Value) : "\nWalk-in không có thời lượng cam kết hoặc gia hạn.")
            + (Session.Status == "Completed" ? "\nTrả thực tế: " + EndLabel : "\nGiờ cần trả: " + EndLabel
                + (CheckedAt > ReturnBy.Value ? " · ĐÃ QUÁ GIỜ" : ""))
            + "\nGiá đã chốt: " + Session.HourlyRate.ToString("N0") + " đ/giờ"
            + "\nCập nhật lúc: " + Local(CheckedAt);
        internal static string Local(DateTimeOffset value) => value.ToOffset(BookingHours.VietnamOffset).ToString("HH:mm:ss dd/MM/yyyy");
    }
    public sealed class StaffSessionSearch
    {
        public List<StaffSession> Items { get; internal set; }
        public bool CanExtend { get; internal set; }
    }
}
