using System;
using System.Collections.Generic;
using System.Data;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class StaffBookingService : IBookingService
    {
        private readonly SqliteDatabase database;
        private readonly LoginSession session;
        private readonly IClock clock;
        private readonly PermissionService permissions;
        private readonly GuestBookingService catalog;
        private readonly ReservationService reservations;
        public StaffBookingService(SqliteDatabase database, LoginSession session) : this(database, session, new SystemClock()) { }
        public StaffBookingService(SqliteDatabase database, LoginSession session, IClock clock)
        {
            this.database = database ?? throw new ArgumentNullException(nameof(database));
            this.session = session; this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            permissions = new PermissionService(database); catalog = new GuestBookingService(database, clock);
            reservations = new ReservationService(database, clock);
        }
        public List<PublicRoom> ListRooms()
        {
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                permissions.Demand(session, "Reservation.Create", connection, transaction);
                return GuestBookingService.ReadRooms(connection, transaction);
            }
        }
        public ReservationAvailability Preview(ReservationRequest request)
        {
            permissions.Demand(session, "Reservation.Create");
            if (request == null) throw new ArgumentException("Cần chọn phòng và giờ đặt.");
            var room = request.RoomId; var start = request.StartTime; var duration = request.DurationMinutes;
            var phone = PhoneNumberNormalizer.Normalize(request.PhoneNumber);
            new NoShowService(database, clock).ProcessExpired();
            using (var connection = database.OpenConnection())
            using (var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                permissions.Demand(session, "Reservation.Create", connection, transaction);
                return catalog.PreviewAt(connection, transaction, room, phone, start, duration, clock.UtcNow);
            }
        }
        public Reservation Create(ReservationRequest request) => reservations.CreateStaff(session, request);
        public string GetImagePath(string imageUrl) => catalog.GetImagePath(imageUrl);
    }
}
