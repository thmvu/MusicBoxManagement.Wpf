using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

public static class MusicBoxGuestBookingChecks
{
    private sealed class Clock:IClock { public DateTimeOffset UtcNow { get;set; } }
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static object Sql(SqliteDatabase db,string sql){using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=sql;return cmd.ExecuteScalar();}}
    private static ReservationRequest Request(int room,string phone){return new ReservationRequest{RoomId=room,StartTime=new DateTimeOffset(2026,10,5,13,0,0,TimeSpan.FromHours(7)),DurationMinutes=60,FullName="A",PhoneNumber=phone};}
    public static void Run()
    {
        var file=Path.Combine(Path.GetTempPath(),"MusicBoxGuestBooking_"+Guid.NewGuid().ToString("N")+".db");
        var db=new SqliteDatabase(file);var clock=new Clock{UtcNow=new DateTimeOffset(2026,10,5,2,0,0,TimeSpan.Zero)};
        try
        {
            var service=new GuestBookingService(db,clock);Assert(service.ListRooms().Count==0,"Fresh catalog seeded demo rooms.");
            Sql(db,"INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,InactiveReason,CreatedAt) VALUES('R1','Room 1',1,'test.png',1,NULL,'test'),('R2','Room 2',2,'test.png',1,NULL,'test'),('LOCK','Locked',1,'test.png',0,'Private reason','test');");
            Assert(service.ListRooms().Count==2 && typeof(PublicRoom).GetProperty("InactiveReason")==null && typeof(PublicRoom).GetProperty("CustomerId")==null,"Public catalog leaked management fields/locked room.");
            var request=Request(1,"+84 912.345-678");Assert(service.Preview(request).CanBook,"Preview rejected empty room/new phone.");
            Assert(Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM Customers;"))==0 && Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM Reservations;"))==0,"Preview created customer/booking.");
            var saved=service.Create(request);Assert(!service.Preview(Request(2,"84912345678")).CanBook,"Phone-based preview missed customer conflict in another room.");
            Assert(service.Preview(Request(2,"0987654321")).CanBook,"Unrelated phone rejected.");
            clock.UtcNow=saved.StartTime.AddMinutes(15);
            var later=Request(1,"0912345678");later.StartTime=saved.StartTime.AddMinutes(30);
            Assert(service.Preview(later).CanBook && (string)Sql(db,"SELECT Status FROM Reservations LIMIT 1;")=="NoShow","Preview did not process expired Confirmed.");
            var vm=new GuestBookingViewModel(service,clock);vm.RefreshAsync().GetAwaiter().GetResult();
            vm.SelectedDate=new DateTime(2026,10,5);vm.StartTimeText="15:00";vm.FullName="B";vm.PhoneNumber="bad";
            Assert(!vm.SubmitAsync().GetAwaiter().GetResult() && vm.CanInput,"Invalid submit locked form or accepted phone.");
            vm.PhoneNumber="0987654321";Assert(vm.SubmitAsync().GetAwaiter().GetResult() && !vm.CanBook,"VM did not lock successful submit.");
            Assert(!vm.SubmitAsync().GetAwaiter().GetResult() && Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM Reservations;"))==2,"VM sent successful booking again.");
            vm.StartNew();Assert(vm.CanBook && vm.FullName==null && vm.PhoneNumber==null,"Start new retained guest data.");
            Assert(Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AspNetUsers;"))==0 && Convert.ToInt64(Sql(db,"PRAGMA user_version;"))==6,"Guest booking required user/schema change.");
            Console.WriteLine("PASS Guest booking: public catalog, phone preview/customer overlap, NoShow maintenance, no preview writes, VM validation/confirmation/reset and schema on temporary SQLite.");
        }
        finally{SQLiteConnection.ClearAllPools();if(File.Exists(file))File.Delete(file);}
    }
}
