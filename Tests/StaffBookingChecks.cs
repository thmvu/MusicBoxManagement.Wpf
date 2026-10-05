using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MusicBoxManagement.Wpf.ViewModels;

public static class MusicBoxStaffBookingChecks
{
    private sealed class Clock:IClock {public DateTimeOffset UtcNow {get;set;}}
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject<T>(Action action) where T:Exception {try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static object Sql(SqliteDatabase db,string sql){using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=sql;return cmd.ExecuteScalar();}}
    private static long Count(SqliteDatabase db,string sql){return Convert.ToInt64(Sql(db,sql));}
    private static ReservationRequest Request(int room,int hour,string phone){return new ReservationRequest{RoomId=room,StartTime=new DateTimeOffset(2026,10,5,hour,0,0,TimeSpan.FromHours(7)),DurationMinutes=60,FullName="Tên vừa nhập",PhoneNumber=phone};}
    public static void Run()
    {
        var file=Path.Combine(Path.GetTempPath(),"MusicBoxStaffBooking_"+Guid.NewGuid().ToString("N")+".db");
        var db=new SqliteDatabase(file);var clock=new Clock{UtcNow=new DateTimeOffset(2026,10,5,2,0,0,TimeSpan.Zero)};const string password="StaffBooking-Test!";
        try
        {
            var auth=new AuthenticationService(db);var admin=auth.SetupAdminAsync("admin","Admin thử nghiệm",password).GetAwaiter().GetResult();
            Sql(db,@"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'staff','staff','STAFF',PasswordHash,'staff','Nhân viên đặt hộ',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('staff','Staff');
DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId IN(SELECT PermissionId FROM Permission WHERE Code LIKE 'Customer.%' OR Code='Reservation.View');
INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,InactiveReason,CreatedAt) VALUES('R1','Room 1',1,'test.png',1,NULL,'test'),('R2','Room 2',2,'test.png',1,NULL,'test'),('LOCK','Locked',1,'test.png',0,'Test','test');
INSERT INTO Customers(FullName,PhoneNumber) VALUES('Tên đã lưu','0912345678');");
            var staff=auth.LoginAsync("staff",password).GetAwaiter().GetResult();var route=new StaffBookingService(db,staff,clock);
            var list=new StaffReservationService(db,clock);
            Assert(route.ListRooms().Count==2 && list.ForBooking(staff)!=null,"Create-only staff could not load public room metadata/open form.");
            Reject<UnauthorizedAccessException>(()=>list.Search(staff));
            var denied=new StaffBookingService(db,null,clock);
            Reject<UnauthorizedAccessException>(()=>denied.ListRooms());Reject<UnauthorizedAccessException>(()=>denied.Preview(Request(1,13,"0912345678")));Reject<UnauthorizedAccessException>(()=>denied.Create(Request(1,13,"0912345678")));
            Assert(route.Preview(Request(1,13,"+84 912.345-678")).CanBook && Count(db,"SELECT COUNT(*) FROM Reservations;")==0 && Count(db,"SELECT COUNT(*) FROM Customers;")==1,"Staff preview wrote data or required Customer CRUD.");
            var first=route.Create(Request(1,13,"84912345678"));
            Assert(first.CreatedByUserId==staff.UserId && (string)Sql(db,"SELECT FullName FROM Customers WHERE PhoneNumber='0912345678';")=="Tên đã lưu" && Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.Create' AND ActorType='Staff' AND UserId='staff';")==1,"Staff route used Guest audit/overwrote customer name.");
            Assert(!route.Preview(Request(2,13,"0912345678")).CanBook,"Staff preview missed same-customer overlap in another room.");
            var before=Count(db,"SELECT COUNT(*) FROM Customers;");
            Reject<InvalidOperationException>(()=>route.Create(Request(1,13,"0900000000")));Assert(Count(db,"SELECT COUNT(*) FROM Customers;")==before,"Conflict left new customer.");
            Sql(db,"CREATE TRIGGER FailStaffBooking BEFORE INSERT ON AuditLog WHEN NEW.Action='Reservation.Create' BEGIN SELECT RAISE(ABORT,'test failure'); END;");
            Reject<SQLiteException>(()=>route.Create(Request(1,17,"0905555555")));Assert(Count(db,"SELECT COUNT(*) FROM Customers;")==before && Count(db,"SELECT COUNT(*) FROM Reservations;")==1,"Staff booking audit failure did not roll back.");
            Sql(db,"DROP TRIGGER FailStaffBooking;");
            Assert(route.Preview(Request(2,15,"0900000000")).CanBook,"Room lock fixture preview failed.");
            Sql(db,"UPDATE Rooms SET IsActive=0,InactiveReason='Test' WHERE RoomId=2;");
            Reject<InvalidOperationException>(()=>route.Create(Request(2,15,"0900000000")));Assert(Count(db,"SELECT COUNT(*) FROM Customers;")==before,"Submit trusted room preview after lock.");
            Sql(db,"UPDATE Rooms SET IsActive=1,InactiveReason=NULL WHERE RoomId=2;");
            var vm=new GuestBookingViewModel(route,clock,true);vm.RefreshAsync().GetAwaiter().GetResult();vm.StartTimeText="15:00";vm.FullName="Khách mới";vm.PhoneNumber="0987654321";
            Assert(vm.FormTitle.Contains("Đặt hộ") && vm.SubmitAsync().GetAwaiter().GetResult() && vm.LastCreatedReservationId.HasValue && !vm.CanBook,"Shared form did not use Staff route/lock successful submit.");
            Assert(!vm.SubmitAsync().GetAwaiter().GetResult() && Count(db,"SELECT COUNT(*) FROM Reservations;")==2,"Shared Staff form duplicated saved booking.");
            vm.StartNew();Assert(vm.FullName==null && vm.PhoneNumber==null && vm.CanInput && vm.LastCreatedReservationId.HasValue,"New draft retained guest data/lost saved ID.");
            vm.StartTimeText="17:00";vm.FullName="Chưa lưu";vm.PhoneNumber="0900000000";vm.PreviewAsync().GetAwaiter().GetResult();
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Reservation.Create');");
            Assert(!vm.SubmitAsync().GetAwaiter().GetResult() && vm.Rooms.Count==0 && vm.FullName==null && vm.PhoneNumber==null && !vm.CanInput && vm.CanClose,"Revoked Create fell back to Guest/retained fields or prevented closing.");
            vm.StartNew();Assert(!vm.CanBook,"New draft bypassed revoked permission.");
            Reject<UnauthorizedAccessException>(()=>route.ListRooms());Reject<UnauthorizedAccessException>(()=>route.Preview(Request(1,17,"0900000000")));Reject<UnauthorizedAccessException>(()=>list.ForBooking(staff));
            Assert(Count(db,"SELECT COUNT(*) FROM Reservations;")==2 && Count(db,"SELECT COUNT(*) FROM Customers WHERE PhoneNumber='0900000000';")==0,"Revoked submit wrote data.");
            Sql(db,"INSERT INTO RolePermission(RoleId,PermissionId) SELECT 'Staff',PermissionId FROM Permission WHERE Code IN('Reservation.Create','Reservation.View');");
            var parent=new ReservationsViewModel(list,staff,clock){FromDate=new DateTime(2026,10,5),ToDate=new DateTime(2026,10,5),PhoneQuery="0900000000",StatusQuery="Cancelled"};
            parent.SearchAsync().GetAwaiter().GetResult();var draft=parent.PrepareBookingAsync().GetAwaiter().GetResult();Assert(draft!=null,"Parent did not open Staff draft.");
            draft.RefreshAsync().GetAwaiter().GetResult();draft.SelectedDate=new DateTime(2026,10,6);draft.StartTimeText="13:00";draft.FullName="Khách ngày mai";draft.PhoneNumber="0901234567";
            Assert(draft.SubmitAsync().GetAwaiter().GetResult(),"Parent draft did not save.");parent.AfterBookingAsync(draft.LastCreatedReservationId).GetAwaiter().GetResult();
            Assert(parent.FromDate==null && parent.ToDate==null && parent.PhoneQuery==null && parent.StatusQuery=="Tất cả" && parent.Selected.ReservationId==draft.LastCreatedReservationId,"Parent kept filters hiding saved row.");
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Reservation.View');");
            parent.AfterBookingAsync(draft.LastCreatedReservationId).GetAwaiter().GetResult();Assert(parent.Items.Count==0 && parent.Status.Contains("Đã đặt hộ") && parent.Status.Contains("không còn quyền"),"Successful booking misreported after losing View.");
            auth.Logout(staff);Reject<UnauthorizedAccessException>(()=>route.Create(Request(1,17,"0900000000")));
            Assert(Count(db,"PRAGMA user_version;")==6,"Staff form changed schema.");
            Console.WriteLine("PASS Staff booking: Create-only/no unrelated CRUD, protected catalog/preview, phone overlap, Staff creator/audit, stale room/permission, atomic rollback, shared form duplicate/reset/privacy and parent refresh on temporary SQLite.");
        }
        finally{SQLiteConnection.ClearAllPools();if(File.Exists(file))File.Delete(file);}
    }
}
