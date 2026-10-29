using System;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Xml;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
public static class MusicBoxMySqlRevenueReportChecks
{
    private sealed class Clock:IClock { public DateTimeOffset Value;public int Reads;public Action Hook;public DateTimeOffset UtcNow{get{Reads++;if(Hook!=null)Hook();return Value;}} }
    private static object Sql(MySqlDatabase db,string sql){using(var c=db.OpenConnection())using(var q=c.CreateCommand()){q.CommandText=sql;return q.ExecuteScalar();}}
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static DateTimeOffset At(int month,int day,int hour){return new DateTimeOffset(2026,month,day,hour,0,0,TimeSpan.FromHours(7));}
    public static void Run()
    {
        var name="musicbox_test_"+Guid.NewGuid().ToString("N");var db=new MySqlDatabase(name);Console.WriteLine("Revenue test database: "+name);
        try
        {
            const string password="Revenue-MySql-2026!";var auth=new MySqlAuthenticationService(db);var admin=auth.SetupAdminAsync("admin","Admin",password).GetAwaiter().GetResult();
            Sql(db,@"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'manager','manager','MANAGER',PasswordHash,'manager','Manager',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'staff','staff','STAFF',PasswordHash,'staff','Staff',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('manager','Manager'),('staff','Staff');DELETE FROM RolePermission WHERE RoleId IN ('Staff','Manager');
INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.View';
INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code IN ('Report.View','Report.Export');");
            var manager=auth.LoginAsync("manager",password).GetAwaiter().GetResult();var staff=auth.LoginAsync("staff",password).GetAwaiter().GetResult();
            var clock=new Clock{Value=At(10,29,13)};var reports=new MySqlReportService(db,clock);var from=new DateTime(2026,10,1);var to=new DateTime(2026,10,31);
            new MySqlStaffReservationService(db,clock).ForReports(manager);
            var empty=reports.ReadRevenue(manager,from,to);Check(empty.TotalAmount==0&&empty.InvoiceCount==0&&empty.Days.Count==0&&clock.Reads==1&&!empty.CanExport,"Empty/clock/permission wrong");
            Reject<UnauthorizedAccessException>(()=>reports.ReadRevenue(staff,from,to));Reject<UnauthorizedAccessException>(()=>reports.ReadRevenue(null,from,to));Reject<UnauthorizedAccessException>(()=>reports.ExportRevenue(manager,from,to));
            Reject<ArgumentException>(()=>reports.ReadRevenue(admin,to,from));Reject<ArgumentException>(()=>reports.ReadRevenue(admin,DateTime.MinValue,to));Reject<ArgumentException>(()=>reports.ReadRevenue(admin,from,DateTime.MaxValue));Reject<ArgumentException>(()=>reports.ReadRevenue(admin,from,to,"OTHER"));
            for(int i=1;i<=6;i++)Sql(db,"INSERT INTO Rooms(RoomId,RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES("+i+",'R"+i+"','Test',"+(i==2?2:1)+",'test.png',1,'2026-10-01T02:00:00.0000000+00:00');");
            var sessions=new MySqlRoomSessionService(db,clock);var checkout=new MySqlCheckoutService(db,clock);var orders=new MySqlOrderService(db,clock);
            clock.Value=At(9,30,22);var aSession=sessions.CreateWalkIn(admin,new WalkInRequest{RoomId=1,FullName="Test",PhoneNumber="0900000001"}).Session;
            clock.Value=At(10,1,0);var a=checkout.ConfirmStaff(admin,aSession.RoomSessionId,"Cash");
            clock.Value=At(10,2,13);var bSession=sessions.CreateWalkIn(admin,new WalkInRequest{RoomId=2,FullName="Test",PhoneNumber="0900000002"}).Session;
            Sql(db,"UPDATE RoomSessions SET HourlyRate=9223372036854775807 WHERE RoomSessionId="+bSession.RoomSessionId+"; INSERT INTO Services(Name,Category,Price,IsActive) VALUES('Món','Đồ uống',9223372036854775807,1);");
            orders.CreateStaff(admin,bSession.RoomSessionId,new[]{new OrderLineRequest{ServiceId=1,Quantity=10}});clock.Value=At(10,2,15);var b=checkout.ConfirmStaff(admin,bSession.RoomSessionId,"BankTransfer");
            var active=sessions.CreateWalkIn(admin,new WalkInRequest{RoomId=3,FullName="Active",PhoneNumber="0900000003"}).Session;orders.CreateStaff(admin,active.RoomSessionId,new[]{new OrderLineRequest{ServiceId=1,Quantity=10}});
            clock.Value=At(10,31,22);var cSession=sessions.CreateWalkIn(admin,new WalkInRequest{RoomId=4,FullName="Boundary",PhoneNumber="0900000004"}).Session;clock.Value=At(11,1,0).AddTicks(-1);var c=checkout.ConfirmStaff(admin,cSession.RoomSessionId,"Cash");
            clock.Value=At(10,31,22);var dSession=sessions.CreateWalkIn(admin,new WalkInRequest{RoomId=5,FullName="Excluded",PhoneNumber="0900000005"}).Session;clock.Value=At(11,1,0);var d=checkout.ConfirmStaff(admin,dSession.RoomSessionId,"Cash");
            var expected=a.TotalAmount+b.TotalAmount+c.TotalAmount;var result=reports.ReadRevenue(manager,from.AddHours(4),to.AddHours(23));
            Check(result.InvoiceCount==3&&result.TotalAmount==expected&&result.RoomCharge==a.RoomCharge+b.RoomCharge+c.RoomCharge&&result.ServiceCharge==b.ServiceCharge,"PaidAt/Active/decimal totals wrong");
            Check(result.Days.Count==3&&result.Days.First().Key=="2026-10-01"&&result.Days.Last().Key=="2026-10-31"&&result.Months.Single().Key=="2026-10"&&result.Months.Single().TotalAmount==expected,"Day/month groups wrong");
            Check(result.RoomTypes.Count==2&&result.RoomTypes.Single(r=>r.Key=="VIP").TotalAmount==12m*long.MaxValue,"Snapshot/large money wrong");
            Sql(db,"UPDATE Rooms SET RoomTypeId=1,IsActive=0,InactiveReason='Test' WHERE RoomId=2;UPDATE RoomTypes SET PricePerHour=1,Name='Changed';UPDATE Services SET Price=1,IsActive=0;");
            Check(reports.ReadRevenue(manager,from,to," vip ").TotalAmount==b.TotalAmount&&reports.ReadRevenue(admin,new DateTime(2026,9,30),new DateTime(2026,9,30)).InvoiceCount==0,"History repriced or session date used");
            Check(reports.ReadRevenue(admin,from,new DateTime(2026,11,1)).Months.Count==2,"Cross month wrong");
            foreach(var kind in new[]{DateTimeKind.Utc,DateTimeKind.Local,DateTimeKind.Unspecified})Check(reports.ReadRevenue(manager,DateTime.SpecifyKind(from,kind),DateTime.SpecifyKind(to,kind)).TotalAmount==expected,"Date Kind changed calendar day");
            var audit=Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog"));
            Sql(db,"INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.Export';");
            foreach(var mode in new[]{"Day","Month","RoomType"})
            {
                var file=reports.ExportRevenue(manager,from,to,null,mode);
                using(var memory=new MemoryStream(file.GetBytes()))using(var zip=new ZipArchive(memory,ZipArchiveMode.Read))
                {Check(zip.GetEntry("xl/worksheets/sheet1.xml")!=null,"Excel sheet missing");using(var reader=new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml").Open())){var xml=reader.ReadToEnd();var doc=new XmlDocument();doc.LoadXml(xml);Check(xml.Contains(expected.ToString(CultureInfo.InvariantCulture))&&doc.SelectNodes("//*[local-name()='f']").Count==0,"Excel total/precision/formula wrong");}}
            }
            Reject<ArgumentException>(()=>reports.ExportRevenue(admin,from,to,null,"Other"));
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Report.Export');");Reject<UnauthorizedAccessException>(()=>reports.ExportRevenue(manager,from,to));
            // Real checkout commits while a report snapshot is pinned by ReadAccess before the clock.
            clock.Value=At(10,3,13);var live=sessions.CreateWalkIn(admin,new WalkInRequest{RoomId=6,FullName="Race",PhoneNumber="0900000006"}).Session;
            clock.Hook=()=>{clock.Hook=null;var invoice=new MySqlCheckoutService(db,new Clock{Value=At(10,3,14)}).ConfirmStaff(admin,live.RoomSessionId,"Cash");Sql(db,"DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Report.View');");};
            Check(reports.ReadRevenue(manager,from,to).TotalAmount==expected,"RR mixed new invoice/revoked rights");
            Reject<UnauthorizedAccessException>(()=>reports.ReadRevenue(manager,from,to));Check(reports.ReadRevenue(admin,from,to).TotalAmount>expected,"Next snapshot missed invoice");
            Sql(db,"INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.View';UPDATE AspNetUsers SET SecurityStamp='revoked' WHERE Id='manager';");Reject<UnauthorizedAccessException>(()=>reports.ReadRevenue(manager,from,to));
            Sql(db,"UPDATE AspNetUsers SET SecurityStamp='manager',IsActive=0 WHERE Id='manager';");Reject<UnauthorizedAccessException>(()=>reports.ReadRevenue(manager,from,to));
            Sql(db,"UPDATE AspNetUsers SET IsActive=1 WHERE Id='manager';UPDATE AspNetUserRoles SET RoleId='Staff' WHERE UserId='manager';");Reject<UnauthorizedAccessException>(()=>reports.ReadRevenue(manager,from,to));auth.Logout(manager);Reject<UnauthorizedAccessException>(()=>reports.ReadRevenue(manager,from,to));
            Check(Convert.ToInt32(Sql(db,"SELECT Version FROM _MusicBoxSchemaVersion"))==MySqlDatabase.CurrentSchemaVersion,"Schema changed");
            Console.WriteLine("PASS MySQL RevenueReport/Excel: live RBAC, real checkout PaidAt/tick bounds/decimal beyond Int64/snapshot groups, Active exclusion, Excel three modes/current Export, RR vs real checkout/revoke; GUID only, no UI/import");
        }
        catch(Exception error){Console.WriteLine("Revenue failure before cleanup: "+error);throw;}
        finally{if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"\Amusicbox_test_[0-9a-f]{32}\z"))throw new Exception("Unsafe cleanup");Sql(new MySqlDatabase("defaultdb"),"DROP DATABASE IF EXISTS "+name);}
    }
}
