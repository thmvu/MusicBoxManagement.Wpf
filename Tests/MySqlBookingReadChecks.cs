using System;
using System.Globalization;
using System.IO;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxMySqlBookingReadChecks
{
    private sealed class Clock : IClock { public DateTimeOffset Now; public int Reads; public Action Hook; public DateTimeOffset UtcNow { get { Reads++; if(Hook!=null)Hook(); return Now; } } }
    private static DateTimeOffset Time(int hour,int minute=0) { return new DateTimeOffset(2026,10,18,hour,minute,0,TimeSpan.FromHours(7)); }
    private static string Utc(DateTimeOffset value) { return value.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture); }
    private static void Assert(bool value,string message) { if(!value)throw new Exception(message); }
    private static void Reject<T>(Action action) where T:Exception {try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static object Sql(MySqlDatabase db,string sql) {using(var c=db.OpenConnection())using(var q=c.CreateCommand()){q.CommandText=sql;return q.ExecuteScalar();}}
    private static long Count(MySqlDatabase db,string table) {return Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM "+table));}
    private static ReservationRequest Input(int room,string phone,int hour) {return new ReservationRequest{RoomId=room,PhoneNumber=phone,FullName="Khách",StartTime=Time(hour),DurationMinutes=60};}
    private static void Clear(MySqlDatabase db) {Sql(db,"DELETE FROM RoomSessions; DELETE FROM Reservations; DELETE FROM Customers; DELETE FROM AuditLog;");}
    private static void Seed(MySqlDatabase db,int room,int customer,DateTimeOffset start)
    {Sql(db,"INSERT INTO Reservations(RoomId,CustomerId,StartTime,EndTime,Status,CreatedAt) VALUES("+room+","+customer+",'"+Utc(start)+"','"+Utc(start.AddHours(1))+"','Confirmed','"+Utc(Time(9))+"');");}
    private static string Slot(PublicRoomDay day,int hour,int minute=0) {return day.Slots.Single(x=>x.Start==Time(hour,minute)).State;}
    public static void Run()
    {
        const string password="Booking-Read-Test-2026!";
        var name="musicbox_test_"+Guid.NewGuid().ToString("N"); var db=new MySqlDatabase(name);
        var root=Path.Combine(Path.GetTempPath(),"MusicBox-BookingReads-"+Guid.NewGuid().ToString("N"));
        var clock=new Clock{Now=Time(9)};
        try
        {
            Reject<ArgumentException>(()=>new MySqlGuestBookingService(db,"relative"));
            Reject<ArgumentNullException>(()=>new MySqlGuestBookingService(db,root,null));
            var auth=new MySqlAuthenticationService(db);auth.SetupAdminAsync("admin","Admin",password).GetAwaiter().GetResult();
            Sql(db,"INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,Description,IsActive,InactiveReason,CreatedAt) VALUES('A1','Phòng A',1,'Content/uploads/rooms/11111111111111111111111111111111.png','Mô tả',1,NULL,'"+Utc(Time(9))+"'),('B2','Phòng B',2,'Content/uploads/rooms/22222222222222222222222222222222.png',NULL,1,NULL,'"+Utc(Time(9))+"'),('C3','Phòng khóa',1,'Content/uploads/rooms/33333333333333333333333333333333.png',NULL,0,'Test lock','"+Utc(Time(9))+"');");
            Sql(db,"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'staff','staff','STAFF',PasswordHash,'staff','Staff',1 FROM AspNetUsers LIMIT 1; INSERT INTO AspNetUserRoles VALUES('staff','Staff'); DELETE FROM RolePermission WHERE RoleId='Staff'; INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Reservation.Create';");
            var session=auth.LoginAsync("staff",password).GetAwaiter().GetResult();
            IBookingService guest=new MySqlGuestBookingService(db,root,clock), staff=new MySqlStaffBookingService(db,session,root,clock);
            var rooms=guest.ListRooms(); Assert(rooms.Count==2&&rooms[0].RoomCode=="A1"&&rooms[1].Description==null&&rooms[0].Description=="Mô tả"&&rooms[0].Capacity>0&&rooms[0].PricePerHour>0,"Public catalog failed.");
            Assert(staff.ListRooms().Count==2,"Create-only Staff needs unrelated permissions.");
            Assert(guest.GetImagePath(rooms[0].ImageUrl)==Path.Combine(root,"Content","uploads","rooms","11111111111111111111111111111111.png"),"Local image root ignored.");
            foreach(var path in new[]{"../musicbox.db","Content/uploads/rooms/../../secret.png","https://example.com/a.png","Content/uploads/rooms/x.png",null})Reject<ArgumentException>(()=>guest.GetImagePath(path));
            Assert(!Directory.Exists(root),"Reads unexpectedly created storage files.");
            foreach(var type in new[]{typeof(PublicRoom),typeof(PublicRoomDay),typeof(PublicBookingSlot),typeof(ReservationAvailability)})
                foreach(var property in type.GetProperties()) Assert(!new[]{"CustomerId","PhoneNumber","FullName","ReservationId","RoomSessionId","UserId"}.Contains(property.Name),"Public DTO exposes private/source data.");
            var beforeAudit=Count(db,"AuditLog");var day=guest.ReadDay(1,Time(9).Date,60);
            Assert(day.Slots.Count==28&&day.CheckedAt==clock.Now&&Slot(day,9)=="Available"&&Slot(day,12)=="Break"&&Slot(day,12,30)=="Break"&&Slot(day,11,30)=="Closed"&&Slot(day,22)=="Available"&&Slot(day,22,30)=="Closed","Day slots/shift boundaries failed.");
            foreach(var duration in new[]{60,90,120,180})Assert(guest.ReadDay(1,Time(9).Date,duration).DurationMinutes==duration,"Allowed duration rejected.");
            Reject<ArgumentException>(()=>guest.ReadDay(1,Time(9).Date,30));Reject<ArgumentException>(()=>guest.ReadDay(1,Time(9).Date.AddDays(-1),60));Reject<ArgumentException>(()=>guest.ReadDay(1,Time(9).Date.AddDays(31),60));
            Assert(guest.ReadDay(1,Time(9).Date.AddDays(30),60).Slots.Count==28,"Inclusive day +30 rejected.");
            Reject<InvalidOperationException>(()=>guest.ReadDay(3,Time(9).Date,60));Reject<InvalidOperationException>(()=>guest.ReadDay(999,Time(9).Date,60));
            clock.Reads=0;staff.ReadDay(1,Time(9).Date,60);Assert(clock.Reads==1,"Staff day did not share one clock.");
            var snapshotClock=new Clock{Now=Time(9)};
            snapshotClock.Hook=()=>{Sql(db,"UPDATE Rooms SET IsActive=0,InactiveReason='Concurrent lock' WHERE RoomId=1;");};
            var snapshotStaff=new MySqlStaffBookingService(db,session,root,snapshotClock);
            Assert(snapshotStaff.ReadDay(1,Time(9).Date,60).Slots.Count==28,"Day failed to keep the permission/room snapshot.");
            Reject<InvalidOperationException>(()=>staff.ReadDay(1,Time(9).Date,60));
            Sql(db,"UPDATE Rooms SET IsActive=1,InactiveReason=NULL WHERE RoomId=1;");
            clock.Now=Time(9).AddTicks(1);Assert(Slot(guest.ReadDay(1,Time(9).Date,60),9)=="Past","Subsecond now was rounded.");clock.Now=Time(9);
            Assert(Count(db,"AuditLog")==beforeAudit&&Count(db,"Reservations")==0,"Catalog/calendar wrote audit or bookings.");
            Reject<ArgumentException>(()=>guest.Preview(null));Reject<ArgumentException>(()=>guest.Preview(Input(1,"bad",13)));
            Assert(guest.Preview(Input(1,"+84 912345678",13)).CanBook&&staff.Preview(Input(1,"0912345678",13)).CanBook&&Count(db,"Customers")==0,"Preview created customer or failed normalization.");
            var booked=guest.Create(Input(1,"0912345678",13));Assert(booked.CreatedByUserId==null,"Guest route became Staff.");
            Assert(!guest.Preview(Input(1,"0987654321",13)).CanBook&&!guest.Preview(Input(2,"+84 912345678",13)).CanBook,"Preview ignored Room/normalized Customer overlap.");
            var staffed=staff.Create(Input(2,"0987654321",15));Assert(staffed.CreatedByUserId==session.UserId&&Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.Create' AND ActorType='Staff'"))==1,"Staff route bypassed CreateStaff/actor.");
            Assert(Slot(guest.ReadDay(1,Time(9).Date,60),13)=="Busy","Calendar ignored real reservation.");
            Sql(db,"UPDATE Customers SET PhoneNumber='0901234567' WHERE PhoneNumber='0912345678';");
            Assert(guest.Preview(Input(2,"0912345678",13)).CanBook&&!guest.Preview(Input(2,"0901234567",13)).CanBook,"Preview used historical phone membership.");
            // A free slot is a preview only: the actual writer must recheck the now-busy slot.
            Assert(staff.Preview(Input(1,"0976543210",16)).CanBook,"Free preview failed.");guest.Create(Input(1,"0965432109",16));Reject<InvalidOperationException>(()=>staff.Create(Input(1,"0976543210",16)));
            Clear(db);Sql(db,"INSERT INTO Customers(FullName,PhoneNumber) VALUES('A','0912345678');");var customer=Convert.ToInt32(Sql(db,"SELECT CustomerId FROM Customers LIMIT 1"));Seed(db,1,customer,Time(10));
            clock.Now=Time(10,15).AddTicks(-1);Assert(Slot(guest.ReadDay(1,Time(9).Date,60),10,30)=="Busy","Confirmed grace ended early.");
            clock.Now=Time(10,15);beforeAudit=Count(db,"AuditLog");Assert(Slot(guest.ReadDay(1,Time(9).Date,60),10,30)=="Available"&&(string)Sql(db,"SELECT Status FROM Reservations LIMIT 1")=="Confirmed"&&Count(db,"AuditLog")==beforeAudit,"Calendar wrote NoShow or ignored exact grace.");
            Assert(guest.Preview(new ReservationRequest{RoomId=1,PhoneNumber="0912345678",StartTime=Time(10,30),DurationMinutes=60}).CanBook&&(string)Sql(db,"SELECT Status FROM Reservations LIMIT 1")=="NoShow","Preview failed maintenance before reading.");
            Clear(db);clock.Now=Time(9);Sql(db,"INSERT INTO Customers(FullName,PhoneNumber) VALUES('A','0912345678');");customer=Convert.ToInt32(Sql(db,"SELECT CustomerId FROM Customers LIMIT 1"));
            Sql(db,"INSERT INTO RoomSessions(RoomId,CustomerId,ActualStartTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES(1,"+customer+",'"+Utc(Time(9))+"',100000,'A1','STANDARD','Standard','Active');");
            Assert(Slot(guest.ReadDay(1,Time(9).Date,60),9)=="Busy"&&Slot(guest.ReadDay(1,Time(9).Date,60),10)=="Available","Walk-in must block immediate booking without holding future slots.");
            Assert(guest.ReadDay(1,Time(9).Date.AddDays(1),60).Slots.Count(x=>x.State=="Busy")==0,"Walk-in incorrectly holds tomorrow.");
            // Current access is checked inside the protected snapshot, not just on construction.
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';");
            Reject<UnauthorizedAccessException>(()=>staff.ListRooms());Reject<UnauthorizedAccessException>(()=>staff.ReadDay(1,Time(9).Date,60));Reject<UnauthorizedAccessException>(()=>staff.Preview(Input(2,"0987654321",13)));Reject<UnauthorizedAccessException>(()=>staff.Create(Input(2,"0987654321",13)));
            Assert(guest.ListRooms().Count==2,"Staff revoked access leaked into Guest route.");
            Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Reservation.Create';");
            // Revoke during maintenance: the second read authorization still rejects.
            var maintenanceClock=new Clock{Now=Time(9)};maintenanceClock.Hook=()=>{if(maintenanceClock.Reads==1)Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';");};
            Reject<UnauthorizedAccessException>(()=>new MySqlStaffBookingService(db,session,root,maintenanceClock).Preview(Input(2,"0987654321",13)));
            Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Reservation.Create'; UPDATE AspNetUsers SET SecurityStamp='changed' WHERE Id='staff';");
            Reject<UnauthorizedAccessException>(()=>staff.ListRooms());Reject<UnauthorizedAccessException>(()=>staff.ReadDay(1,Time(9).Date,60));
            Assert(Convert.ToInt32(Sql(db,"SELECT Version FROM _MusicBoxSchemaVersion"))==MySqlDatabase.CurrentSchemaVersion,"Read layer changed schema.");
            Console.WriteLine("PASS MySQL BookingReads: full Guest/Staff IBookingService, public catalog/local images, current Create-only permissions, preview Customer/Room/NoShow, read-only 28-slot calendar, grace/ticks/walk-in/stale writer and revoked access.");
        }
        finally
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"\Amusicbox_test_[0-9a-f]{32}\z"))throw new Exception("Unsafe booking read cleanup.");
            Sql(new MySqlDatabase("defaultdb"),"DROP DATABASE IF EXISTS `"+name+"`");
        }
    }
}
