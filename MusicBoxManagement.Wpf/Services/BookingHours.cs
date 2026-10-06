using System;
using System.Collections.Generic;
using System.Linq;

namespace MusicBoxManagement.Wpf.Services
{
    public static class BookingHours
    {
        public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
        public static IReadOnlyList<int> InitialDurations { get; } = Array.AsReadOnly(new[] { 60, 90, 120, 180 });

        public static DateTimeOffset ValidateReservation(DateTimeOffset start, int durationMinutes, DateTimeOffset now)
        {
            var localStart = start.ToOffset(VietnamOffset);
            var today = now.ToOffset(VietnamOffset).Date;
            if (localStart.Date < today || localStart.Date > today.AddDays(30))
                throw new ArgumentException("Ngày đặt phải từ hôm nay đến 30 ngày tới, theo giờ Việt Nam.");
            if (start < now) throw new ArgumentException("Giờ bắt đầu không được ở quá khứ.");
            if (localStart.Minute % 30 != 0 || localStart.Ticks % TimeSpan.TicksPerMinute != 0)
                throw new ArgumentException("Giờ bắt đầu phải ở phút 00 hoặc 30, không có giây lẻ.");
            if (!InitialDurations.Contains(durationMinutes))
                throw new ArgumentException("Thời lượng đặt ban đầu là 60, 90, 120 hoặc 180 phút.");
            var end = localStart.AddMinutes(durationMinutes);
            ValidateSessionInterval(localStart, end);
            return end.ToUniversalTime();
        }

        // Actual arrival keeps its seconds/ticks and is not subject to booking slots.
        public static void ValidateSessionInterval(DateTimeOffset start, DateTimeOffset end)
        {
            var localStart = start.ToOffset(VietnamOffset);
            end = end.ToOffset(VietnamOffset);
            var time = localStart.TimeOfDay;
            if (end <= start || end.Date != localStart.Date || !((time >= TimeSpan.FromHours(9) && time < TimeSpan.FromHours(12) && end.TimeOfDay <= TimeSpan.FromHours(12)) ||
                (time >= TimeSpan.FromHours(13) && time < TimeSpan.FromHours(23) && end.TimeOfDay <= TimeSpan.FromHours(23))))
                throw new ArgumentException("Khoảng đặt phải nằm trong một ca 09:00–12:00 hoặc 13:00–23:00.");
        }
    }
}
