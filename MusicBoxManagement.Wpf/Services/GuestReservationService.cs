using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Globalization;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;

namespace MusicBoxManagement.Wpf.Services
{
    public sealed class GuestReservationService
    {
        private readonly SqliteDatabase database;
        private readonly IClock clock;
        private readonly NoShowService noShows;
        public GuestReservationService(SqliteDatabase database) : this(database,new SystemClock()) { }
        public GuestReservationService(SqliteDatabase database,IClock clock)
        { this.database=database??throw new ArgumentNullException(nameof(database));this.clock=clock??throw new ArgumentNullException(nameof(clock));noShows=new NoShowService(database,clock); }
        internal GuestSessionService ForSessions() => new GuestSessionService(database, clock);
        internal OrderService ForOrders() => new OrderService(database, clock);
        internal BillingService ForBilling() => new BillingService(database, clock);
        public GuestReservationLookup Lookup(string phoneNumber)
        {
            var phone=PhoneNumberNormalizer.Normalize(phoneNumber);
            database.Initialize();noShows.ProcessExpired();
            using(var connection=database.OpenConnection())
            using(var transaction=connection.BeginTransaction(IsolationLevel.ReadCommitted))
            using(var command=connection.CreateCommand())
            {
                var now=clock.UtcNow.ToUniversalTime();command.Transaction=transaction;
                command.CommandText=@"SELECT b.ReservationId,r.RoomCode,r.Name,b.StartTime,b.EndTime FROM Reservations b
JOIN Customers c ON c.CustomerId=b.CustomerId JOIN Rooms r ON r.RoomId=b.RoomId
WHERE c.PhoneNumber=@phone AND b.Status='Confirmed' AND b.StartTime>@cutoff
ORDER BY b.StartTime,b.ReservationId;";
                command.Parameters.AddWithValue("@phone",phone);command.Parameters.AddWithValue("@cutoff",Utc(now.AddMinutes(-15)));
                var items=new List<GuestReservation>();
                using(var reader=command.ExecuteReader())while(reader.Read())
                {
                    var start=Parse(reader.GetString(3));
                    items.Add(new GuestReservation{ReservationId=reader.GetInt32(0),RoomCode=reader.GetString(1),RoomName=reader.GetString(2),
                        StartTime=start,EndTime=Parse(reader.GetString(4)),CanCancel=now<=start.AddHours(-2)});
                }
                return new GuestReservationLookup{Items=items,CheckedAt=now,ActiveSession=GuestSessionService.ReadActiveAt(connection,transaction,phone,now)};
            }
        }
        public void Cancel(string phoneNumber,int reservationId)
        {
            var phone=PhoneNumberNormalizer.Normalize(phoneNumber);
            using(var connection=database.OpenConnection())
            using(var transaction=SqliteDatabase.BeginWriteTransaction(connection))
            using(var command=connection.CreateCommand())
            {
                var now=clock.UtcNow.ToUniversalTime();command.Transaction=transaction;
                command.CommandText=@"SELECT b.StartTime,b.Status,EXISTS(SELECT 1 FROM RoomSessions s WHERE s.ReservationId=b.ReservationId)
FROM Reservations b JOIN Customers c ON c.CustomerId=b.CustomerId WHERE b.ReservationId=@id AND c.PhoneNumber=@phone;";
                command.Parameters.AddWithValue("@id",reservationId);command.Parameters.AddWithValue("@phone",phone);
                DateTimeOffset start;string status;bool hasSession;
                using(var reader=command.ExecuteReader())
                {
                    if(!reader.Read())throw new InvalidOperationException("Không tìm thấy booking của SĐT này. Hãy tra cứu lại.");
                    start=Parse(reader.GetString(0));status=reader.GetString(1);hasSession=reader.GetInt32(2)!=0;
                }
                if(status!="Confirmed" || now>=start.AddMinutes(15) || hasSession)
                    throw new InvalidOperationException("Booking đã đổi trạng thái hoặc hết hạn. Hãy tra cứu lại.");
                if(now>start.AddHours(-2))throw new InvalidOperationException("Còn dưới 2 giờ đến giờ đặt. Vui lòng liên hệ cửa hàng để xử lý.");
                command.CommandText="UPDATE Reservations SET Status='Cancelled',CancellationReason='Customer cancelled online' WHERE ReservationId=@id AND Status='Confirmed';";
                command.ExecuteNonQuery();
                AuditService.WriteGuest(connection,transaction,"Reservation.Cancel","Reservation",reservationId.ToString(CultureInfo.InvariantCulture),"Customer cancelled online",now);
                transaction.Commit();
            }
        }
        private static DateTimeOffset Parse(string value)=>DateTimeOffset.ParseExact(value,"O",CultureInfo.InvariantCulture);
        private static string Utc(DateTimeOffset value)=>value.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);
    }
}
