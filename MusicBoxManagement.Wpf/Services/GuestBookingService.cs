using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class GuestBookingService : IBookingService
    {
        private readonly SqliteDatabase database;
        private readonly IClock clock;
        private readonly AvailabilityService availability;
        private readonly ReservationService reservations;
        private readonly NoShowService noShows;
        public GuestBookingService(SqliteDatabase database) : this(database, new SystemClock()) { }
        public GuestBookingService(SqliteDatabase database, IClock clock)
        {
            this.database = database; this.clock = clock;
            availability = new AvailabilityService(database, clock); reservations = new ReservationService(database, clock); noShows = new NoShowService(database, clock);
        }
        public List<PublicRoom> ListRooms()
        {
            database.Initialize();
            using (var connection = database.OpenConnection())
                return ReadRooms(connection, null);
        }
        internal static List<PublicRoom> ReadRooms(SQLiteConnection connection, SQLiteTransaction transaction)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"SELECT r.RoomId,r.RoomCode,r.Name,t.Name,t.Capacity,t.PricePerHour,t.Amenities,r.Description,r.ImageUrl
FROM Rooms r JOIN RoomTypes t ON t.RoomTypeId=r.RoomTypeId WHERE r.IsActive=1 ORDER BY r.RoomCode;";
                var rooms = new List<PublicRoom>();
                using (var reader = command.ExecuteReader()) while (reader.Read()) rooms.Add(new PublicRoom {
                    RoomId=reader.GetInt32(0), RoomCode=reader.GetString(1), Name=reader.GetString(2), TypeName=reader.GetString(3), Capacity=reader.GetInt32(4),
                    PricePerHour=reader.GetInt64(5), Amenities=reader.GetString(6), Description=reader.IsDBNull(7)?null:reader.GetString(7), ImageUrl=reader.GetString(8) });
                return rooms;
            }
        }
        public ReservationAvailability Preview(ReservationRequest request)
        {
            if (request == null) throw new ArgumentException("Cần chọn phòng và giờ đặt.");
            var room=request.RoomId; var start=request.StartTime; var duration=request.DurationMinutes;
            var phone=PhoneNumberNormalizer.Normalize(request.PhoneNumber);
            noShows.ProcessExpired();
            using (var connection=database.OpenConnection())
            using (var transaction=connection.BeginTransaction(IsolationLevel.ReadCommitted))
                return PreviewAt(connection,transaction,room,phone,start,duration,clock.UtcNow);
        }
        internal ReservationAvailability PreviewAt(SQLiteConnection connection, SQLiteTransaction transaction,
            int room, string phone, DateTimeOffset start, int duration, DateTimeOffset now)
        {
            using (var command=connection.CreateCommand())
            {
                command.Transaction=transaction; command.CommandText="SELECT CustomerId FROM Customers WHERE PhoneNumber=@phone;";
                command.Parameters.AddWithValue("@phone",phone); var customer=command.ExecuteScalar();
                return availability.CheckReservationAt(connection,transaction,room,customer==null?(int?)null:Convert.ToInt32(customer),start,duration,now);
            }
        }
        public Reservation Create(ReservationRequest request) => reservations.CreateGuest(request);
        public PublicRoomDay ReadDay(int roomId, DateTime date, int durationMinutes) => new GuestCalendarService(database, clock).Read(roomId, date, durationMinutes);
        // Reuse the validated local image-path helper, without querying protected management data.
        public string GetImagePath(string imageUrl) => new RoomService(database).GetImagePath(imageUrl);
    }
}
