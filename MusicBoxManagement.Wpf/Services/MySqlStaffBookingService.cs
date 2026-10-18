using System;
using System.Collections.Generic;
using System.Data;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class MySqlStaffBookingService : IBookingService
    {
        private readonly MySqlDatabase database;
        private readonly LoginSession session;
        private readonly IClock clock;
        private readonly MySqlPermissionService permissions;
        private readonly MySqlGuestBookingService catalog;
        private readonly MySqlReservationService reservations;
        public MySqlStaffBookingService(MySqlDatabase database, LoginSession session, string storageRoot) : this(database, session, storageRoot, new SystemClock()) { }
        public MySqlStaffBookingService(MySqlDatabase database, LoginSession session, string storageRoot, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.session = session; this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new MySqlPermissionService(database); catalog = new MySqlGuestBookingService(database, storageRoot, clock);
            reservations = new MySqlReservationService(database, clock);
        }
        public List<PublicRoom> ListRooms()
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            {
                DemandRead(connection, transaction);
                return MySqlGuestBookingService.ReadRooms(connection, transaction);
            }
        }
        public ReservationAvailability Preview(ReservationRequest request)
        {
            permissions.Demand(session, "Reservation.Create");
            if (request == null) throw new ArgumentException("Cần chọn phòng và giờ đặt.");
            var room = request.RoomId; var start = request.StartTime; var duration = request.DurationMinutes;
            var phone = PhoneNumberNormalizer.Normalize(request.PhoneNumber);
            new MySqlNoShowService(database, clock).ProcessExpired();
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            {
                DemandRead(connection, transaction);
                return catalog.PreviewAt(connection, transaction, room, phone, start, duration, clock.UtcNow);
            }
        }
        public Reservation Create(ReservationRequest request) => reservations.CreateStaff(session, request);
        public PublicRoomDay ReadDay(int roomId, DateTime date, int durationMinutes)
        {
            using (var connection = database.OpenConnection())
            using (var transaction = MySqlDatabase.BeginReadTransaction(connection))
            {
                DemandRead(connection, transaction);
                return MySqlGuestCalendarService.ReadAt(database, connection, transaction, roomId, date, durationMinutes, clock.UtcNow);
            }
        }
        public string GetImagePath(string imageUrl) => catalog.GetImagePath(imageUrl);
        private void DemandRead(MySqlConnector.MySqlConnection connection, MySqlConnector.MySqlTransaction transaction)
        {
            if (!permissions.ReadAccess(session, connection, transaction).Permissions.ContainsKey("Reservation.Create"))
                throw new UnauthorizedAccessException("Bạn không có quyền đặt phòng hộ khách.");
        }
    }
}
