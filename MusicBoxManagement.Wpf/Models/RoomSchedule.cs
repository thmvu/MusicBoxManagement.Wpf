using System;
using System.Collections.Generic;
using MusicBoxManagement.Wpf.Services;

namespace MusicBoxManagement.Wpf.Models
{
    public sealed class CalendarRange
    {
        public DateTimeOffset Start { get; private set; }
        public DateTimeOffset End { get; private set; }
        private CalendarRange(DateTime date, int days)
        {
            if (date.Year < 2 || date.Year > 9998) throw new ArgumentException("Ngày lịch không hợp lệ.");
            Start = new DateTimeOffset(DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified), BookingHours.VietnamOffset).ToUniversalTime();
            End = Start.AddDays(days);
        }
        public static CalendarRange Day(DateTime date) => new CalendarRange(date, 1);
        public static CalendarRange Week(DateTime date) => new CalendarRange(date.Date.AddDays(-((int)date.DayOfWeek + 6) % 7), 7);
    }
    public sealed class ScheduleInterval
    {
        public DateTimeOffset Start { get; internal set; }
        public DateTimeOffset End { get; internal set; }
    }
    public sealed class RoomScheduleEvent
    {
        public int? ReservationId { get; internal set; }
        public int? SessionId { get; internal set; }
        public string Kind { get; internal set; }
        public string CustomerName { get; internal set; }
        public string PhoneNumber { get; internal set; }
        // Raw times are preserved, even when they extend past the selected range.
        public DateTimeOffset Start { get; internal set; }
        public DateTimeOffset End { get; internal set; }
        public DateTimeOffset? ExpectedEnd { get; internal set; }
        public DateTimeOffset? ReturnBy { get; internal set; }
        public bool IsOverdue { get; internal set; }
    }
    public sealed class RoomSchedule
    {
        public int RoomId { get; internal set; }
        public string RoomCode { get; internal set; }
        public string RoomName { get; internal set; }
        public string CurrentStatus { get; internal set; }
        public DateTimeOffset CheckedAt { get; internal set; }
        public CalendarRange Range { get; internal set; }
        public IReadOnlyList<ScheduleInterval> Holds { get; internal set; }
        public IReadOnlyList<RoomScheduleEvent> Events { get; internal set; }
    }
}
