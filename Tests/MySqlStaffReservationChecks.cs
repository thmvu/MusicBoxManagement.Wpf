using System;
using System.Globalization;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxMySqlStaffReservationChecks
{
    private sealed class Clock:IClock{public DateTimeOffset Value;public int Reads;public Action Hook;public DateTimeOffset UtcNow{get{Reads++;if(Hook!=null)Hook();return Value;}}}
    private static DateTimeOffset At(int day,int hour){return new DateTimeOffset(2026,10,day,hour,0,0,TimeSpan.FromHours(7));}
    private static string Utc(DateTimeOffset value){return value.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);}
    private static object Sql(MySqlDatabase db,string sql){using(var c=db.OpenConnection())using(var q=c.CreateCommand()){q.CommandText=sql;return q.ExecuteScalar();}}
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    public static void Run()
    {
        var name="musicbox_test_"+Guid.NewGuid().ToString("N");var db=new MySqlDatabase(name);const string password="Staff-Booking-2026!";
        Console.WriteLine("Staff reservation test database: "+name);
        try
        {
            var auth=new MySqlAuthenticationService(db);var admin=auth.SetupAdminAsync("admin","Admin",password).GetAwaiter().GetResult();
            Sql(db,"INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,CreatedAt) VALUES(1,'R1',1,'Phòng', 'test.png',1,'"+Utc(At(18,9))+"');INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'staff','staff','STAFF',PasswordHash,'staff','Staff',1 FROM AspNetUsers LIMIT 1;INSERT INTO AspNetUserRoles VALUES('staff','Staff');DELETE FROM RolePermission WHERE RoleId='Staff';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Reservation.View';");
            var staff=auth.LoginAsync("staff",password).GetAwaiter().GetResult();var clock=new Clock{Value=At(18,9)};
            var reads=new MySqlStaffReservationService(db,clock);var writer=new MySqlReservationService(db,clock);
            var guest=writer.CreateGuest(new ReservationRequest{RoomId=1,FullName="Khách",PhoneNumber="0912345678",StartTime=At(18,13),DurationMinutes=60});
            var byStaff=writer.CreateStaff(admin,new ReservationRequest{RoomId=1,FullName="Khách hai",PhoneNumber="0987654321",StartTime=At(19,9),DurationMinutes=60});
            var list=reads.Search(staff);Assert(list.Items.Count==2&&!list.CanCancel&&!list.CanCreate&&!list.CanCheckIn,"View-only demanded extra permissions or capabilities wrong.");
            var detail=list.Items.Single(r=>r.ReservationId==guest.ReservationId);
            Assert(detail.CustomerName=="Khách"&&detail.PhoneNumber=="0912345678"&&detail.CreatedByName=="Khách"&&detail.CurrentHourlyRate==120000&&detail.IsCancellable,"Guest details/current price/grace failed.");
            Assert(list.Items.Single(r=>r.ReservationId==byStaff.ReservationId).CreatedByName=="Admin","Staff creator name lost.");
            Assert(reads.Search(staff,At(18,9).Date,At(18,9).Date,"+84 912 345 678","Confirmed").Items.Count==1,"Date/phone/status AND filter failed.");
            Reject<ArgumentException>(()=>reads.Search(staff,phoneNumber:"123"));Reject<ArgumentException>(()=>reads.Search(staff,status:"wrong"));
            Reject<ArgumentException>(()=>reads.Search(staff,At(19,9).Date,At(18,9).Date));Reject<ArgumentException>(()=>reads.Search(staff,DateTime.MinValue,null));
            Reject<UnauthorizedAccessException>(()=>reads.Search(null));Reject<UnauthorizedAccessException>(()=>reads.Cancel(staff,guest.ReservationId,"Không có quyền"));
            Sql(db,"UPDATE Customers SET PhoneNumber='0901234567' WHERE CustomerId="+guest.CustomerId);
            Assert(reads.Search(staff,phoneNumber:"0912345678").Items.Count==0&&reads.Search(staff,phoneNumber:"0901234567").Items.Count==1,"Historical phone membership leaked.");
            Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Reservation.Cancel'");
            Assert(reads.Search(staff).CanCancel,"Current Cancel permission not reflected.");
            reads.Cancel(staff,guest.ReservationId,"Đổi lịch");
            var cancelled=reads.Search(staff,status:"Cancelled").Items.Single();Assert(cancelled.CancellationReason=="Đổi lịch"&&!cancelled.IsCancellable,"Cancellation not read back.");
            Assert(Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Reservation.Cancel' AND ActorType='Staff'"))==1,"Cancellation audit missing.");
            Reject<InvalidOperationException>(()=>reads.Cancel(staff,guest.ReservationId,"Lặp"));
            clock.Value=At(19,9).AddMinutes(15).AddTicks(-1);Assert(reads.Search(staff,status:"Confirmed").Items.Single().IsCancellable,"Grace ended early.");
            clock.Value=clock.Value.AddTicks(1);Assert(reads.Search(staff,status:"NoShow").Items.Count==1,"Maintenance did not expire exact grace.");
            // Revoke after initial demand while maintenance reads its clock.
            var revokeClock=new Clock{Value=clock.Value};revokeClock.Hook=()=>{if(revokeClock.Reads==1)Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';");};
            Reject<UnauthorizedAccessException>(()=>new MySqlStaffReservationService(db,revokeClock).Search(staff));
            Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Reservation.View';");
            // The protected read snapshot stays consistent if data/access change after its clock.
            var snapshotClock=new Clock{Value=clock.Value};snapshotClock.Hook=()=>{if(snapshotClock.Reads==2)Sql(db,"UPDATE Customers SET FullName='Changed' WHERE CustomerId="+guest.CustomerId+";DELETE FROM RolePermission WHERE RoleId='Staff';");};
            var snapshot=new MySqlStaffReservationService(db,snapshotClock).Search(staff);
            Assert(snapshot.Items.Single(r=>r.ReservationId==guest.ReservationId).CustomerName=="Khách","Private data read outside authorization snapshot.");
            Reject<UnauthorizedAccessException>(()=>reads.Search(staff));
            Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Reservation.Cancel';");
            Reject<UnauthorizedAccessException>(()=>reads.Search(staff)); // Cancel-only cannot read PII.
            Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Reservation.View';UPDATE AspNetUsers SET SecurityStamp='new' WHERE Id='staff';");
            Reject<UnauthorizedAccessException>(()=>reads.Search(staff));
            Assert(Convert.ToInt32(Sql(db,"SELECT Version FROM _MusicBoxSchemaVersion"))==MySqlDatabase.CurrentSchemaVersion,"Read layer changed schema.");
            foreach(var start in new[]{At(18,0).AddTicks(-1),At(18,0),At(19,0).AddTicks(-1),At(19,0)})
                Sql(db,"INSERT INTO Reservations(RoomId,CustomerId,StartTime,EndTime,Status,CreatedAt) VALUES(1,"+guest.CustomerId+",'"+Utc(start)+"','"+Utc(start.AddHours(1))+"','Cancelled','"+Utc(At(18,9))+"');");
            Assert(reads.Search(admin,At(18,0).Date,At(18,0).Date,"0901234567","Cancelled").Items.Count==3,"Vietnam date boundary lost a tick.");
            // Source-session fixture checks read/cancel guards only; real MySQL check-in is a later step.
            Sql(db,"UPDATE Reservations SET Status='CheckedIn' WHERE ReservationId="+byStaff.ReservationId+";INSERT INTO RoomSessions(CustomerId,RoomId,ReservationId,ActualStartTime,ExpectedEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES("+byStaff.CustomerId+",1,"+byStaff.ReservationId+",'"+Utc(At(19,9))+"','"+Utc(At(19,10))+"',120000,'R1','STANDARD','Standard','Active');");
            var source=reads.Search(admin,status:"CheckedIn").Items.Single();Assert(source.SessionId.HasValue&&!source.IsCancellable,"Source session details/guard failed.");
            Reject<InvalidOperationException>(()=>reads.Cancel(admin,byStaff.ReservationId,"Đã nhận phòng"));
            Console.WriteLine("PASS MySQL StaffReservations: View-only/current flags, PII snapshot/phone/date/status, real CancelStaff/audit/replay, NoShow ticks, revoke during maintenance/read, Cancel-only and stamp guards.");
        }
        catch(Exception error){Console.WriteLine("Staff reservation failure before cleanup: "+error);throw;}
        finally
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"\Amusicbox_test_[0-9a-f]{32}\z"))throw new Exception("Unsafe cleanup.");
            Sql(new MySqlDatabase("defaultdb"),"DROP DATABASE IF EXISTS `"+name+"`");
        }
    }
}
