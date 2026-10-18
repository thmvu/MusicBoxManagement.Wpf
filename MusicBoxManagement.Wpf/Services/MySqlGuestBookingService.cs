using System;
using System.Collections.Generic;
using System.Data;
using MySqlConnector;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class MySqlGuestBookingService : IBookingService
    {
        private readonly MySqlDatabase database;
        private readonly IClock clock;
        private readonly MySqlAvailabilityService availability;
        private readonly MySqlReservationService reservations;
        private readonly MySqlNoShowService noShows;
        private readonly MySqlRoomService images;
        public MySqlGuestBookingService(MySqlDatabase database, string storageRoot) : this(database, storageRoot, new SystemClock()) { }
        public MySqlGuestBookingService(MySqlDatabase database, string storageRoot, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            images = new MySqlRoomService(database, storageRoot, clock);
            availability = new MySqlAvailabilityService(database, clock); reservations = new MySqlReservationService(database, clock); noShows = new MySqlNoShowService(database, clock);
        }
        public List<PublicRoom> ListRooms()
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
                return ReadRooms(connection, transaction);
        }
        internal static List<PublicRoom> ReadRooms(MySqlConnection connection, MySqlTransaction transaction)
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
            using (var transaction=MySqlDatabase.BeginReadTransaction(connection))
                return PreviewAt(connection,transaction,room,phone,start,duration,clock.UtcNow);
        }
        internal ReservationAvailability PreviewAt(MySqlConnection connection, MySqlTransaction transaction,
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
        public PublicRoomDay ReadDay(int roomId, DateTime date, int durationMinutes) => new MySqlGuestCalendarService(database, clock).Read(roomId, date, durationMinutes);
        // Reuse the validated local image-path helper, without querying protected management data.
        public string GetImagePath(string imageUrl) => images.GetImagePath(imageUrl);
    }
}
