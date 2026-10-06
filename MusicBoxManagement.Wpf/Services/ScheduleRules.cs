namespace MusicBoxManagement.Wpf.Services
{
    internal static class ScheduleRules
    {
        // Shared with booking availability. @cutoff is now minus the 15-minute grace.
        internal const string HoldsSql = @"SELECT RoomId,CustomerId,StartTime AS HoldStart,EndTime AS HoldEnd,ReservationId FROM Reservations
 WHERE Status='Confirmed' AND StartTime>@cutoff
UNION ALL
SELECT RoomId,CustomerId,ActualStartTime,ExpectedEndTime,ReservationId FROM RoomSessions
 WHERE Status='Active' AND ReservationId IS NOT NULL";
    }
}
