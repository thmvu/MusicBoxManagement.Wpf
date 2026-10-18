using System;
using System.Collections.Generic;
using System.Data;
using MySqlConnector;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class MySqlGuestCalendarService
    {
        private readonly MySqlDatabase database;
        private readonly IClock clock;
        public MySqlGuestCalendarService(MySqlDatabase database) : this(database, new SystemClock()) { }
        public MySqlGuestCalendarService(MySqlDatabase database, IClock clock)
        { this.database = database ?? throw new ArgumentNullException(nameof(database)); this.clock = clock ?? throw new ArgumentNullException(nameof(clock)); }
        public PublicRoomDay Read(int roomId, DateTime date, int durationMinutes)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
                return ReadAt(database, connection, transaction, roomId, date, durationMinutes, clock.UtcNow);
        }
        internal static PublicRoomDay ReadAt(MySqlDatabase database, MySqlConnection connection, MySqlTransaction transaction,
            int roomId, DateTime date, int duration, DateTimeOffset now)
        {
            var today = now.ToOffset(BookingHours.VietnamOffset).Date;
            if (date.Date < today || date.Date > today.AddDays(30)) throw new ArgumentException("Ngày xem phải từ hôm nay đến 30 ngày tới.");
            if (!BookingHours.InitialDurations.Contains(duration)) throw new ArgumentException("Thời lượng là 60, 90, 120 hoặc 180 phút.");
            var range = CalendarRange.Day(date); var result = new PublicRoomDay { Date = date.Date, DurationMinutes = duration, CheckedAt = now.ToUniversalTime() };
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction; command.CommandText = "SELECT RoomCode,Name FROM Rooms WHERE RoomId=@room AND IsActive=1;";
                command.Parameters.AddWithValue("@room", roomId);
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read()) throw new InvalidOperationException("Phòng không còn nhận đặt. Hãy làm mới danh sách phòng.");
                    result.RoomCode = reader.GetString(0); result.RoomName = reader.GetString(1);
                }
            }
            var slots = new List<PublicBookingSlot>(); var availability = new MySqlAvailabilityService(database);
            for (var minute = 540; minute < 1380; minute += 30)
            {
                var start = range.Start.AddMinutes(minute); string state;
                if (minute >= 720 && minute < 780) state = "Break";
                else if (start < now) state = "Past";
                else
                {
                    try { BookingHours.ValidateReservation(start, duration, now); state = availability.CheckReservationAt(connection, transaction, roomId, null, start, duration, now).CanBook ? "Available" : "Busy"; }
                    catch (ArgumentException) { state = "Closed"; }
                }
                slots.Add(new PublicBookingSlot { Start = start, State = state });
            }
            result.Slots = slots.AsReadOnly(); return result;
        }
    }
}
