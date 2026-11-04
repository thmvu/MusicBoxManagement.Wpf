using System;
using System.Collections.Generic;
using System.Linq;

namespace MusicBoxManagement.Wpf.Services
{
    public static class BookingHours
    {
        public static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
        public static IReadOnlyList<int> InitialDurations { get; } = Array.AsReadOnly(new[] { 60, 90, 120, 180 });
        private static bool CanUseVietnamTime(DateTimeOffset value) => value.UtcDateTime.Ticks <= DateTime.MaxValue.Ticks - VietnamOffset.Ticks;

        public static bool TryValidateReservation(DateTimeOffset start, int durationMinutes, DateTimeOffset now, out DateTimeOffset end, out string reason)
        {
            end = default(DateTimeOffset); reason = null;
            if (!CanUseVietnamTime(start) || !CanUseVietnamTime(now)) { reason = "Thời gian đặt nằm ngoài phạm vi hỗ trợ."; return false; }
            var localStart = start.ToOffset(VietnamOffset);
            var days = (localStart.Date - now.ToOffset(VietnamOffset).Date).Days;
            if (days < 0 || days > 30) { reason = "Ngày đặt phải từ hôm nay đến 30 ngày tới, theo giờ Việt Nam."; return false; }
            if (start < now) { reason = "Giờ bắt đầu không được ở quá khứ."; return false; }
            if (localStart.Minute % 30 != 0 || localStart.Ticks % TimeSpan.TicksPerMinute != 0)
            { reason = "Giờ bắt đầu phải ở phút 00 hoặc 30, không có giây lẻ."; return false; }
            if (!InitialDurations.Contains(durationMinutes)) { reason = "Thời lượng đặt ban đầu là 60, 90, 120 hoặc 180 phút."; return false; }
            if (localStart.TimeOfDay.Add(TimeSpan.FromMinutes(durationMinutes)) >= TimeSpan.FromDays(1))
            { reason = "Quán đóng cửa lúc 23:00. Khoảng sử dụng phải kết thúc chậm nhất 23:00, không kéo sang ngày hôm sau."; return false; }
            var proposed = localStart.AddMinutes(durationMinutes);
            if (!TryValidateSessionInterval(localStart, proposed, out reason)) return false;
            end = proposed.ToUniversalTime(); return true;
        }

        public static DateTimeOffset ValidateReservation(DateTimeOffset start, int durationMinutes, DateTimeOffset now)
        {
            if (!TryValidateReservation(start, durationMinutes, now, out var end, out var reason)) throw new ArgumentException(reason);
            return end;
        }

        public static bool TryGetOpenShiftEnd(DateTimeOffset now, out DateTimeOffset end, out string reason)
        {
            end = default(DateTimeOffset); reason = null;
            if (!CanUseVietnamTime(now)) { reason = "Thời gian sử dụng nằm ngoài phạm vi hỗ trợ."; return false; }
            var local = now.ToOffset(VietnamOffset); var time = local.TimeOfDay;
            if (time >= TimeSpan.FromHours(9) && time < TimeSpan.FromHours(12))
                end = new DateTimeOffset(local.Date.AddHours(12), VietnamOffset).ToUniversalTime();
            else if (time >= TimeSpan.FromHours(13) && time < TimeSpan.FromHours(23))
                end = new DateTimeOffset(local.Date.AddHours(23), VietnamOffset).ToUniversalTime();
            else
            {
                reason = time < TimeSpan.FromHours(9) ? "Quán bắt đầu phục vụ từ 09:00. Vui lòng chọn giờ từ 09:00."
                    : time < TimeSpan.FromHours(13) ? "Quán đang nghỉ trưa từ 12:00 đến 13:00. Vui lòng chọn giờ từ 13:00."
                    : "Quán đóng cửa lúc 23:00. Vui lòng chọn một ca mở cửa khác.";
                return false;
            }
            return true;
        }

        public static DateTimeOffset GetOpenShiftEnd(DateTimeOffset now)
        {
            if (!TryGetOpenShiftEnd(now, out var end, out var reason)) throw new ArgumentException(reason);
            return end;
        }

        public static bool TryValidateSessionInterval(DateTimeOffset start, DateTimeOffset end, out string reason)
        {
            reason = null;
            if (end <= start) { reason = "Giờ kết thúc phải sau giờ bắt đầu."; return false; }
            if (!CanUseVietnamTime(end)) { reason = "Thời gian sử dụng nằm ngoài phạm vi hỗ trợ."; return false; }
            if (!TryGetOpenShiftEnd(start, out var close, out reason)) return false;
            if (end <= close) return true;
            reason = start.ToOffset(VietnamOffset).Hour < 12
                ? "Khoảng sử dụng kéo qua giờ nghỉ trưa 12:00–13:00. Vui lòng chọn khoảng kết thúc chậm nhất 12:00 hoặc bắt đầu từ 13:00."
                : "Quán đóng cửa lúc 23:00. Khoảng sử dụng phải kết thúc chậm nhất 23:00.";
            return false;
        }

        public static void ValidateSessionInterval(DateTimeOffset start, DateTimeOffset end)
        {
            if (!TryValidateSessionInterval(start, end, out var reason)) throw new ArgumentException(reason);
        }

        public static string ReservationBoundaryNotice(DateTimeOffset end)
        {
            if (!CanUseVietnamTime(end)) return "";
            var time = end.ToOffset(VietnamOffset).TimeOfDay;
            if (time == TimeSpan.FromHours(12)) return "Lượt này kết thúc đúng 12:00, lúc quán nghỉ trưa 12:00–13:00. Khách nên đến sớm trước giờ bắt đầu đã đặt; nhận muộn sẽ không đủ thời lượng trước giờ nghỉ.";
            if (time == TimeSpan.FromHours(23)) return "Lượt này kết thúc đúng giờ đóng cửa 23:00. Khách nên đến sớm trước giờ bắt đầu đã đặt; nhận muộn sẽ không đủ thời lượng trước giờ đóng cửa.";
            return "";
        }
    }
}
