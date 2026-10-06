using System.Collections.Generic;
using System;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    // The shared form receives one fixed route: Guest or a signed-in Staff session.
    public interface IBookingService
    {
        List<PublicRoom> ListRooms();
        ReservationAvailability Preview(ReservationRequest request);
        Reservation Create(ReservationRequest request);
        string GetImagePath(string imageUrl);
        PublicRoomDay ReadDay(int roomId, DateTime date, int durationMinutes);
    }
}
