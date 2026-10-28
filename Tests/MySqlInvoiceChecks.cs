using System;
using MySqlConnector;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxMySqlInvoiceChecks
{
    private sealed class Clock:IClock
    { public DateTimeOffset Value;public int Reads;public DateTimeOffset UtcNow{get{Interlocked.Increment(ref Reads);return Value;}} }
    private sealed class GateClock:IClock
    {
        public DateTimeOffset Value;public readonly ManualResetEventSlim Entered=new ManualResetEventSlim(false),Release=new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow{get{Entered.Set();if(!Release.Wait(10000))throw new Exception("Invoice read gate timed out.");return Value;}}
    }
    private static object Sql(MySqlDatabase db,string sql){using(var c=db.OpenConnection())using(var command=c.CreateCommand()){command.CommandText=sql;return command.ExecuteScalar();}}
    private static long Count(MySqlDatabase db,string sql){return Convert.ToInt64(Sql(db,sql));}
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static DateTimeOffset At(int day,int hour){return new DateTimeOffset(2026,10,day,hour,0,0,TimeSpan.FromHours(7));}
    private static OrderLineRequest[] Cart(int quantity){return new[]{new OrderLineRequest{ServiceId=1,Quantity=quantity}};}
    public static void Run()
    {
        var name="musicbox_test_"+Guid.NewGuid().ToString("N");var db=new MySqlDatabase(name);
        Console.WriteLine("Invoice test database: "+name);
        try
        {
            var auth=new MySqlAuthenticationService(db);const string password="Invoice-Test-2026!";
            var admin=auth.SetupAdminAsync("admin","Admin tạo",password).GetAwaiter().GetResult();
            Sql(db,@"INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('I1','Phòng một',1,'test.png',1,'2026-10-12T02:00:00.0000000+00:00'),('I2','Phòng hai',1,'test.png',1,'2026-10-12T02:00:00.0000000+00:00'),('I3','Phòng lớn',1,'test.png',1,'2026-10-12T02:00:00.0000000+00:00');
INSERT INTO Services(Name,Category,Price,IsActive) VALUES('Món gốc','Đồ uống',25000,1);
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'reader','reader','READER',PasswordHash,'reader','Người xem hóa đơn',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('reader','Staff');DELETE FROM RolePermission WHERE RoleId='Staff';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Invoice.View';");
            var actor=auth.LoginAsync("reader",password).GetAwaiter().GetResult();var clock=new Clock{Value=At(12,13).AddSeconds(12).AddTicks(1234)};
            new MySqlStaffReservationService(db,clock).ForInvoices(actor);
            var sessions=new MySqlRoomSessionService(db,clock);var orders=new MySqlOrderService(db,clock);var checkout=new MySqlCheckoutService(db,clock);var service=new MySqlInvoiceService(db,clock);
            var first=sessions.CreateWalkIn(admin,new WalkInRequest{RoomId=1,FullName="Khách",PhoneNumber="0901111111"}).Session;
            orders.CreateStaff(admin,first.RoomSessionId,Cart(2));orders.CreateStaff(admin,first.RoomSessionId,Cart(1));
            var cancelled=orders.CreateGuest("0901111111",first.RoomSessionId,Cart(1));orders.CancelGuest("0901111111",cancelled.OrderId);
            orders.CreateGuest("0901111111",first.RoomSessionId,Cart(1));clock.Value=first.ActualStartTime.AddSeconds(90).AddTicks(3456);
            var paid=checkout.ConfirmStaff(admin,first.RoomSessionId,"Cash");clock.Reads=0;
            var audit=Count(db,"SELECT COUNT(*) FROM AuditLog;");var result=service.Search(actor);
            Assert(result.Items.Count==1 && result.Items.Single().InvoiceId==paid.InvoiceId && result.Items.Single().TotalAmount==78000 && !result.CanPrint && clock.Reads==1 && result.CheckedAt.Offset==TimeSpan.Zero,"Invoice.View-only search/decimal/one clock wrong.");
            Reject<UnauthorizedAccessException>(()=>sessions.ReadWalkIn(actor,first.RoomSessionId));Reject<UnauthorizedAccessException>(()=>orders.ListStaff(actor,first.RoomSessionId));
            Assert(service.Search(actor,"  "+paid.InvoiceNumber.ToLowerInvariant()+"  ").Items.Count==1 && service.Search(actor,paid.InvoiceNumber,phoneNumber:"+84 901.111-111").Items.Count==1 && service.Search(actor,phoneNumber:"0902222222").Items.Count==0,"Exact number/trim/case/canonical phone/combined filters wrong.");
            Assert(service.Search(actor,"% OR 1=1 --").Items.Count==0 && service.Search(actor,"MB-").Items.Count==0,"Code search used wildcard or unsafe SQL.");
            foreach(var kind in new[]{DateTimeKind.Unspecified,DateTimeKind.Utc,DateTimeKind.Local})
                Assert(service.Search(actor,fromDate:new DateTime(2026,10,12,23,59,0,kind),toDate:new DateTime(2026,10,12,0,0,0,kind)).Items.Count==1,"Invoice filter interpreted a date as an instant.");
            Reject<ArgumentException>(()=>service.Search(actor,fromDate:new DateTime(2026,10,13),toDate:new DateTime(2026,10,12)));
            Reject<ArgumentException>(()=>service.Search(actor,toDate:DateTime.MaxValue));Reject<ArgumentException>(()=>service.Search(actor,invoiceNumber:new string('X',51)));
            Reject<ArgumentException>(()=>service.Search(actor,phoneNumber:"901111111"));Reject<ArgumentException>(()=>service.Read(actor,0));Reject<InvalidOperationException>(()=>service.Read(actor,999999));
            clock.Reads=0;var details=service.Read(actor,paid.InvoiceId);
            Assert(details.StoreName=="Music Box" && details.RoomCodeSnapshot=="I1" && details.RoomTypeCodeSnapshot=="STANDARD" && details.HourlyRate==120000 && details.ActualEndTime==paid.PaidAt && details.UsedTicks==900003456 && details.Items.Count==2 && details.Items.Sum(i=>i.Amount)==75000 && details.Invoice.TotalAmount==78000 && clock.Reads==1 && !details.CanPrint,"Details lost seconds/ticks/snapshots, grouped separate orders, or included cancelled/Pending.");
            Reject<UnauthorizedAccessException>(()=>service.ReadForPrint(actor,paid.InvoiceId));
            Sql(db,"UPDATE Rooms SET Name='Tên hiện tại',RoomTypeId=2,IsActive=0,InactiveReason='Khóa sau thanh toán' WHERE RoomId=1;UPDATE RoomTypes SET Name='Loại hiện tại',PricePerHour=999000;UPDATE Services SET Name='Món hiện tại',Price=99000,IsActive=0;UPDATE AspNetUsers SET FullName='Tên thu ngân mới' WHERE Id='"+admin.UserId+"';UPDATE Customers SET FullName='Tên khách mới',PhoneNumber='0909999999' WHERE CustomerId="+first.CustomerId+";");
            details=service.Read(actor,paid.InvoiceId);
            Assert(details.RoomTypeNameSnapshot=="Standard" && details.RoomTypeCodeSnapshot=="STANDARD" && details.RoomCodeSnapshot=="I1" && details.HourlyRate==120000 && details.Items.All(i=>i.ServiceNameSnapshot=="Món gốc"&&i.UnitPrice==25000) && details.Invoice.ProcessedByNameSnapshot=="Admin tạo" && details.Invoice.TotalAmount==paid.TotalAmount,"Catalog/user/customer changes rewrote old invoice.");
            Assert(service.Search(actor,phoneNumber:"0901111111").Items.Count==0 && service.Search(actor,phoneNumber:"+84 909.999-999").Items.Count==1 && service.Search(actor,paid.InvoiceNumber).Items.Count==1,"Phone history used old phone string instead of CustomerId/current phone.");
            Assert(Count(db,"SELECT COUNT(*) FROM AuditLog;")==audit && Count(db,"SELECT COUNT(*) FROM Invoices;")==1 && Count(db,"SELECT Version FROM _MusicBoxSchemaVersion;")==MySqlDatabase.CurrentSchemaVersion,"Invoice reads wrote data/audit/schema.");
            var paidUtc=paid.PaidAt.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);
            Sql(db,"UPDATE RoomSessions SET ActualEndTime='"+paid.PaidAt.AddSeconds(1).ToUniversalTime().ToString("O",CultureInfo.InvariantCulture)+"' WHERE RoomSessionId="+first.RoomSessionId+";");Reject<InvalidOperationException>(()=>service.Read(actor,paid.InvoiceId));
            Sql(db,"UPDATE RoomSessions SET ActualEndTime='"+paidUtc+"' WHERE RoomSessionId="+first.RoomSessionId+";UPDATE OrderItems SET UnitPrice=UnitPrice+1 WHERE OrderId=(SELECT MIN(OrderId) FROM Orders WHERE RoomSessionId="+first.RoomSessionId+");");Reject<InvalidOperationException>(()=>service.Read(actor,paid.InvoiceId));
            Sql(db,"UPDATE OrderItems SET UnitPrice=UnitPrice-1 WHERE OrderId=(SELECT MIN(OrderId) FROM Orders WHERE RoomSessionId="+first.RoomSessionId+");");Assert(service.Read(actor,paid.InvoiceId).Invoice.TotalAmount==paid.TotalAmount,"Restored fixture invoice changed frozen total.");
            Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Invoice.Print';");Assert(service.ReadForPrint(actor,paid.InvoiceId).CanPrint && service.Search(actor).CanPrint,"Print required unrelated permission.");
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Invoice.Print');");Reject<UnauthorizedAccessException>(()=>service.ReadForPrint(actor,paid.InvoiceId));Assert(!service.Read(actor,paid.InvoiceId).CanPrint,"Revoked Print flag cached or blocked View.");
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Invoice.Print';");
            Reject<UnauthorizedAccessException>(()=>service.ReadForPrint(actor,paid.InvoiceId));Reject<UnauthorizedAccessException>(()=>service.Search(actor));Reject<UnauthorizedAccessException>(()=>service.Read(actor,paid.InvoiceId));
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.CheckOut';");Reject<UnauthorizedAccessException>(()=>service.Read(actor,paid.InvoiceId));
            Reject<UnauthorizedAccessException>(()=>new MySqlStaffReservationService(db,clock).ForInvoices(actor));
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Invoice.View';");
            Reject<UnauthorizedAccessException>(()=>service.Search(null));Reject<UnauthorizedAccessException>(()=>service.Read(null,paid.InvoiceId));

            Sql(db,"UPDATE RoomTypes SET PricePerHour=120000;UPDATE Services SET Name='Món gốc',Price=25000,IsActive=1;");
            clock.Value=At(12,22);var boundary=sessions.CreateWalkIn(admin,new WalkInRequest{RoomId=2,FullName="Khách biên",PhoneNumber="0902222222"}).Session;
            clock.Value=At(13,0);var midnight=checkout.ConfirmStaff(admin,boundary.RoomSessionId,"BankTransfer");
            Assert(service.Search(actor,fromDate:new DateTime(2026,10,12),toDate:new DateTime(2026,10,12)).Items.Count==1 && service.Search(actor,fromDate:new DateTime(2026,10,13),toDate:new DateTime(2026,10,13)).Items.Single().InvoiceId==midnight.InvoiceId && service.Search(actor,toDate:new DateTime(2026,10,12)).Items.Count==1 && service.Search(actor,fromDate:new DateTime(2026,10,13)).Items.Count==1,"PaidAt midnight UTC+7/inclusive end/open bounds wrong.");
            clock.Value=At(13,13);var large=sessions.CreateWalkIn(admin,new WalkInRequest{RoomId=3,FullName="Khách lớn",PhoneNumber="0903333333"}).Session;
            Sql(db,"UPDATE RoomSessions SET HourlyRate=9223372036854775807 WHERE RoomSessionId="+large.RoomSessionId+";UPDATE Services SET Price=9223372036854775807;");orders.CreateStaff(admin,large.RoomSessionId,Cart(10));clock.Value=clock.Value.AddHours(2);
            var huge=checkout.ConfirmStaff(admin,large.RoomSessionId,"Cash");Assert(service.Read(actor,huge.InvoiceId).Invoice.TotalAmount==12m*long.MaxValue && service.Search(actor,huge.InvoiceNumber).Items.Single().TotalAmount==huge.TotalAmount && service.Read(actor,huge.InvoiceId).Items.Single().Amount==10m*long.MaxValue,"Invoice read lost money beyond Int64.");
            Assert(service.Search(actor).Items.Select(i=>i.InvoiceId).SequenceEqual(new[]{huge.InvoiceId,midnight.InvoiceId,paid.InvoiceId}),"Invoice sorting not PaidAt desc.");
            Sql(db,"UPDATE Services SET Price=25000;");clock.Value=At(13,16);var live=sessions.CreateWalkIn(admin,new WalkInRequest{RoomId=2,FullName="Đang dùng",PhoneNumber="0904444444"}).Session;
            Assert(service.Search(actor,phoneNumber:"0904444444").Items.Count==0,"Active provisional bill appeared as invoice.");
            var gate=new GateClock{Value=clock.Value};Task<InvoiceSearch> reading=null;Task<Invoice> writing=null;
            try
            {
                reading=Task.Run(()=>new MySqlInvoiceService(db,gate).Search(actor));Assert(gate.Entered.Wait(5000),"Read snapshot not reached.");
                clock.Value=clock.Value.AddMinutes(1);writing=Task.Run(()=>checkout.ConfirmStaff(admin,live.RoomSessionId,"Cash"));Thread.Sleep(100);
                Assert(writing.Wait(8000),"RR invoice reader blocked a real checkout writer.");gate.Release.Set();Assert(Task.WaitAll(new Task[]{reading,writing},15000),"Invoice concurrent read/write timed out.");
                Assert(reading.Result.Items.Count==3 && service.Search(actor).Items.Count==4,"Invoice search mixed two database snapshots or missed next committed invoice.");
            }
            finally{gate.Release.Set();if(reading!=null)reading.GetAwaiter().GetResult();if(writing!=null)writing.GetAwaiter().GetResult();gate.Entered.Dispose();gate.Release.Dispose();}
            Sql(db,"UPDATE AspNetUsers SET SecurityStamp='new' WHERE Id='reader';");Reject<UnauthorizedAccessException>(()=>service.Read(actor,paid.InvoiceId));Sql(db,"UPDATE AspNetUsers SET SecurityStamp='reader',IsActive=0 WHERE Id='reader';");Reject<UnauthorizedAccessException>(()=>service.Search(actor));Sql(db,"UPDATE AspNetUsers SET IsActive=1 WHERE Id='reader';");auth.Logout(actor);Reject<UnauthorizedAccessException>(()=>service.Read(actor,paid.InvoiceId));
            Console.WriteLine("PASS Invoices: live independent View/Print/current session, PaidAt UTC+7 date bounds/exact code/current phone/combined filters, immutable Room/Order/cashier snapshots/seconds/decimal beyond Int64, Completed-only/read-only/no schema changes and RR MVCC snapshot vs real MySQL checkout; GUID DB only, no provider/import claims.");
        }
        catch(Exception error)
        {
            Console.WriteLine("Invoice failure before cleanup: "+error.GetType().FullName+": "+error.Message);
            Console.WriteLine(error.StackTrace);
            throw;
        }
        finally
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"\Amusicbox_test_[0-9a-f]{32}\z"))throw new Exception("Unsafe cleanup");
            Sql(new MySqlDatabase("defaultdb"),"DROP DATABASE IF EXISTS `"+name+"`");
        }
    }
}
