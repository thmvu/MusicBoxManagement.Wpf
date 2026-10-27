using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MySqlConnector;

public static class MusicBoxMySqlBillingChecks
{
    private sealed class Clock : IClock
    { public DateTimeOffset Value; public int Reads; public DateTimeOffset UtcNow {get{Interlocked.Increment(ref Reads);return Value;}} }
    private sealed class GateClock : IClock, IDisposable
    {
        public DateTimeOffset Value;
        public readonly ManualResetEventSlim Entered=new ManualResetEventSlim(false),Release=new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow {get{Entered.Set();if(!Release.Wait(10000))throw new Exception("Billing snapshot gate timeout");return Value;}}
        public void Dispose(){Release.Set();Entered.Dispose();Release.Dispose();}
    }
    private static DateTimeOffset At(int h){return new DateTimeOffset(2026,10,28,h,0,0,TimeSpan.FromHours(7));}
    private static string Utc(DateTimeOffset t){return t.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);}
    private static object Sql(MySqlDatabase db,string sql){using(var c=db.OpenConnection())using(var q=c.CreateCommand()){q.CommandText=sql;return q.ExecuteScalar();}}
    private static long Count(MySqlDatabase db,string sql){return Convert.ToInt64(Sql(db,sql));}
    private static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
    private static T Reject<T>(Action action)where T:Exception{try{action();}catch(T e){return e;}throw new Exception("Expected "+typeof(T).Name);}
    private static OrderLineRequest[] Cart(int quantity){return new[]{new OrderLineRequest{ServiceId=1,Quantity=quantity}};}
    private static decimal ExactRoom(long rate,long ticks)
    {
        BigInteger remainder;var whole=BigInteger.DivRem(new BigInteger(rate)*ticks,new BigInteger(TimeSpan.TicksPerHour),out remainder);
        if(remainder*2>=TimeSpan.TicksPerHour)whole+=1;
        return (decimal)whole;
    }
    private static SessionBill InTransaction(MySqlBillingService service,MySqlConnection c,MySqlTransaction tx,int id)
    {
        try{return (SessionBill)typeof(MySqlBillingService).GetMethod("CalculateActive",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(service,new object[]{c,tx,id});}
        catch(TargetInvocationException e){throw e.InnerException;}
    }
    private static void ChangePhone(MySqlDatabase db,LoginSession admin,string oldPhone,string newPhone)
    {
        var customers=new MySqlCustomerService(db);var current=customers.Search(admin,null,oldPhone).Items.Single();
        customers.Save(admin,current,new CustomerEdit{FullName=current.FullName,PhoneNumber=newPhone});
    }
    private static void EditService(MySqlDatabase db,LoginSession admin,string name,long price,bool active)
    {
        var catalog=new MySqlServiceCatalogService(db);var current=catalog.ListForManagement(admin).Single();
        catalog.Save(admin,current,new ServiceEdit{Name=name,Category=current.Category,Price=price,IsActive=active});
    }
    private static void EditType(MySqlDatabase db,LoginSession admin,string name,long rate)
    {
        var catalog=new MySqlRoomTypeService(db);var current=catalog.GetForEdit(admin,1);
        catalog.Update(admin,current,new RoomTypeEdit{Name=name,Capacity=current.Capacity,PricePerHour=rate,Amenities=current.Amenities,Description=current.Description});
    }
    public static void Run()
    {
        var name="musicbox_test_"+Guid.NewGuid().ToString("N");var db=new MySqlDatabase(name);
        try
        {
            const string password="Billing-MySql-2026!";var auth=new MySqlAuthenticationService(db);
            var admin=auth.SetupAdminAsync("admin","Admin",password).GetAwaiter().GetResult();
            Sql(db,@"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'viewer','viewer','VIEWER',PasswordHash,'viewer','Xem phiên',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('viewer','Staff');
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'checkout','checkout','CHECKOUT',PasswordHash,'checkout','Chỉ thanh toán',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('checkout','Manager');DELETE FROM RolePermission WHERE RoleId IN ('Staff','Manager');
INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.View';
INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Session.CheckOut';");
            var viewer=auth.LoginAsync("viewer",password).GetAwaiter().GetResult();var checkout=auth.LoginAsync("checkout",password).GetAwaiter().GetResult();
            for(var r=1;r<=3;r++)Sql(db,"INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,CreatedAt) VALUES("+r+",'B"+r+"',1,'Phòng','test.png',1,'"+Utc(At(9))+"');");
            new MySqlServiceCatalogService(db).Save(admin,null,new ServiceEdit{Name="Trà",Category="Đồ uống",Price=25000,IsActive=true});
            var start=At(9).AddSeconds(12).AddTicks(1234567);var clock=new Clock{Value=start};var sessions=new MySqlRoomSessionService(db,clock);
            var id=sessions.CreateWalkIn(admin,new WalkInRequest{RoomId=1,FullName="Khách riêng",PhoneNumber="0901111111"}).Session.RoomSessionId;
            var expired=new MySqlReservationService(db,new Clock{Value=At(9)}).CreateGuest(new ReservationRequest{RoomId=3,FullName="Chưa đến",PhoneNumber="0903333333",StartTime=At(9),DurationMinutes=60});
            var service=new MySqlBillingService(db,clock);var orders=new MySqlOrderService(db,clock);
            Arithmetic(db,clock,service,id,start);
            Console.WriteLine("PASS Billing exact integer oracle: ticks/half-dong/large rates/durations/UTC/no minimum");
            AccessAndSnapshots(db,admin,viewer,checkout,clock,service,orders,id,start,expired.ReservationId);
            Console.WriteLine("PASS Billing current phone/live View/public DTO/Completed-only/catalog snapshots/read-only/no cap");
            ConcurrentReads(db,admin,viewer,clock,service,orders,id);
            SharedWriter(db,admin,checkout,clock,service,orders,id,start);
            Console.WriteLine("PASS Billing RR snapshots vs real writers and shared Serializable calculator/rollback/fresh clock");
            EditType(db,admin,"Standard",120000);EditService(db,admin,"Trà",25000,true);clock.Value=At(9);
            var booking=new MySqlReservationService(db,clock).CreateGuest(new ReservationRequest{RoomId=2,FullName="Khách booking",PhoneNumber="0902222222",StartTime=At(13),DurationMinutes=60});
            clock.Value=At(13).AddSeconds(12).AddTicks(1234567);var booked=sessions.CheckIn(admin,booking.ReservationId);orders.CreateStaff(admin,booked.RoomSessionId,Cart(1));
            clock.Value=booked.ExpectedEndTime.Value.AddMinutes(2);var bill=service.ReadGuest("0902222222",booked.RoomSessionId);
            Assert(bill.UsedMinutes==62m&&bill.RoomCharge==124000m&&bill.ServiceCharge==25000m&&bill.ActualStartTime==booked.ActualStartTime,"Booking used planned time/capped ExpectedEnd/other session items");
            Assert((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+booking.ReservationId)=="CheckedIn"&&(string)Sql(db,"SELECT EndTime FROM Reservations WHERE ReservationId="+booking.ReservationId)==Utc(booking.EndTime),"Read mutated booking");
            // Read/guard fixture only: no MySQL checkout or invoice is claimed.
            Sql(db,"UPDATE RoomSessions SET Status='Completed',ActualEndTime='"+Utc(clock.Value)+"' WHERE RoomSessionId="+id+";");
            Reject<InvalidOperationException>(()=>service.ReadGuest("0901111111",id));Reject<InvalidOperationException>(()=>service.ReadStaff(admin,id));
            Reject<UnauthorizedAccessException>(()=>service.ReadStaff(checkout,booked.RoomSessionId));auth.Logout(viewer);Reject<UnauthorizedAccessException>(()=>service.ReadStaff(viewer,booked.RoomSessionId));
            Assert(Count(db,"SELECT Version FROM _MusicBoxSchemaVersion;")==MySqlDatabase.CurrentSchemaVersion&&Count(db,"SELECT COUNT(*) FROM Invoices;")==0,"Billing changed schema/created invoice");
            Console.WriteLine("PASS MySQL Billing: exact actual ticks/UTC/one clock/decimal/AwayFromZero, immutable price snapshots, Completed-only/status counts, live independent Session.View and current phone, read-only/no cap, RR snapshot vs real Order/Customer writers, shared writer scope/isolation/rollback/fresh lock clock; GUID DB only, no checkout/provider/import claims.");
        }
        finally
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"\Amusicbox_test_[0-9a-f]{32}\z"))throw new Exception("Unsafe cleanup");
            Sql(new MySqlDatabase("defaultdb"),"DROP DATABASE IF EXISTS `"+name+"`");
        }
    }
    private static void Arithmetic(MySqlDatabase db,Clock clock,MySqlBillingService service,int id,DateTimeOffset start)
    {
        foreach(var pair in new[]{Tuple.Create(0L,0m),Tuple.Create(TimeSpan.TicksPerHour,120000m),Tuple.Create(TimeSpan.TicksPerMinute*80,160000m),Tuple.Create(TimeSpan.TicksPerMinute*147,294000m),Tuple.Create(TimeSpan.TicksPerSecond*90,3000m),Tuple.Create(1L,0m)})
        {
            clock.Value=start.AddTicks(pair.Item1).ToOffset(TimeSpan.FromHours(3));clock.Reads=0;var bill=service.ReadGuest("+84 901.111-111",id);
            Assert(clock.Reads==1&&bill.UsedTicks==pair.Item1&&bill.UsedMinutes==pair.Item1/(decimal)TimeSpan.TicksPerMinute&&bill.RoomCharge==pair.Item2&&bill.TotalAmount==pair.Item2&&bill.ServiceCharge==0,"Duration/one clock/no minimum wrong");
            Assert(bill.ActualStartTime==start&&bill.BillingEndTime==clock.Value&&bill.BillingEndTime.Offset==TimeSpan.Zero&&bill.RoomCode=="B1","Actual/UTC/ticks snapshot lost");
        }
        foreach(var rate in new[]{1L,120000L,long.MaxValue,long.MaxValue-1})
        {
            // Arithmetic fixture changes snapshot directly; normal services never reprice a session.
            Sql(db,"UPDATE RoomSessions SET HourlyRate="+rate+" WHERE RoomSessionId="+id);
            foreach(var ticks in new[]{TimeSpan.TicksPerHour/2-1,TimeSpan.TicksPerHour/2,TimeSpan.TicksPerHour/2+1,TimeSpan.TicksPerHour*2+1234567})
            {clock.Value=start.AddTicks(ticks);Assert(service.ReadGuest("0901111111",id).RoomCharge==ExactRoom(rate,ticks),"Rounding differs from BigInteger oracle");}
            clock.Value=DateTimeOffset.MaxValue;Assert(service.ReadGuest("0901111111",id).RoomCharge==ExactRoom(rate,(clock.Value-start).Ticks),"Max rate/duration lost precision");
        }
        Sql(db,"UPDATE RoomSessions SET HourlyRate=120000 WHERE RoomSessionId="+id);clock.Value=start.AddTicks(-1);Reject<InvalidOperationException>(()=>service.ReadGuest("0901111111",id));
    }
    private static void AccessAndSnapshots(MySqlDatabase db,LoginSession admin,LoginSession viewer,LoginSession checkout,Clock clock,MySqlBillingService service,MySqlOrderService orders,int id,DateTimeOffset start,int expired)
    {
        clock.Value=At(10);orders.CreateStaff(admin,id,Cart(2));var pending=orders.CreateGuest("0901111111",id,Cart(3));var cancelled=orders.CreateGuest("0901111111",id,Cart(4));orders.CancelGuest("0901111111",cancelled.OrderId);
        EditService(db,admin,"Tên/giá mới",99000,false);EditType(db,admin,"Loại mới",999000);
        var audits=Count(db,"SELECT COUNT(*) FROM AuditLog;");var count=Count(db,"SELECT COUNT(*) FROM Orders;");clock.Value=start.AddMinutes(147);clock.Reads=0;var bill=service.ReadStaff(viewer,id);
        Assert(clock.Reads==1&&bill.RoomCharge==294000m&&bill.ServiceCharge==50000m&&bill.TotalAmount==344000m&&bill.HourlyRate==120000&&bill.RoomTypeName=="Standard"&&bill.CompletedOrderCount==1&&bill.PendingOrderCount==1&&bill.CancelledOrderCount==1,"View-only/Completed-only/snapshot/counts failed");
        clock.Value=start.AddDays(1);Assert(service.ReadGuest("0901111111",id).RoomCharge==2880000m,"Walk-in billing capped at ReturnBy/closing");
        Assert(Count(db,"SELECT COUNT(*) FROM AuditLog;")==audits&&Count(db,"SELECT COUNT(*) FROM Orders;")==count&&(string)Sql(db,"SELECT Status FROM Orders WHERE OrderId="+pending.OrderId)=="Pending"&&Sql(db,"SELECT ActualEndTime FROM RoomSessions WHERE RoomSessionId="+id)==DBNull.Value&&Sql(db,"SELECT ExpectedEndTime FROM RoomSessions WHERE RoomSessionId="+id)==DBNull.Value&&(string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+expired)=="Confirmed","Read wrote maintenance/audit/order/session");
        foreach(var property in new[]{"CustomerId","FullName","PhoneNumber","RoomId","ReservationId","CreatedByUserId","Orders","InvoiceId"})Assert(typeof(SessionBill).GetProperty(property)==null,"Public bill leaks "+property);
        Reject<ArgumentException>(()=>service.ReadGuest("bad",id));Reject<UnauthorizedAccessException>(()=>service.ReadStaff(null,id));Reject<UnauthorizedAccessException>(()=>service.ReadStaff(checkout,id));
        Assert(Reject<InvalidOperationException>(()=>service.ReadGuest("0902222222",id)).Message==Reject<InvalidOperationException>(()=>service.ReadGuest("0902222222",999999)).Message,"Foreign session disclosed existence");
        ChangePhone(db,admin,"0901111111","0909999999");Reject<InvalidOperationException>(()=>service.ReadGuest("0901111111",id));Assert(service.ReadGuest("0909999999",id).ServiceCharge==50000m,"Current phone lost bill");ChangePhone(db,admin,"0909999999","0901111111");
        Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Order.View';");Reject<UnauthorizedAccessException>(()=>service.ReadStaff(viewer,id));
        Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.View';UPDATE AspNetUsers SET IsActive=0 WHERE Id='viewer';");Reject<UnauthorizedAccessException>(()=>service.ReadStaff(viewer,id));
        Sql(db,"UPDATE AspNetUsers SET IsActive=1,SecurityStamp='changed' WHERE Id='viewer';");Reject<UnauthorizedAccessException>(()=>service.ReadStaff(viewer,id));Sql(db,"UPDATE AspNetUsers SET SecurityStamp='viewer' WHERE Id='viewer';DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Order.View');");
        clock.Value=At(13);orders.ConfirmStaff(admin,pending.OrderId);Assert(service.ReadStaff(viewer,id).ServiceCharge==125000m,"Fresh read missed served order");
        EditService(db,admin,"Trà",long.MaxValue,true);orders.CreateStaff(admin,id,Cart(10));bill=service.ReadGuest("0901111111",id);
        Assert(bill.ServiceCharge==125000m+(decimal)long.MaxValue*10&&bill.TotalAmount>long.MaxValue&&bill.TotalAmount==bill.RoomCharge+bill.ServiceCharge,"Service total overflowed Int64");EditService(db,admin,"Trà",25000,true);
    }
    private static void ConcurrentReads(MySqlDatabase db,LoginSession admin,LoginSession viewer,Clock clock,MySqlBillingService service,MySqlOrderService orders,int id)
    {
        clock.Value=At(13);var pending=orders.CreateGuest("0901111111",id,Cart(1));var before=service.ReadGuest("0901111111",id);
        using(var gate=new GateClock{Value=clock.Value})
        {
            var reader=Task.Run(()=>new MySqlBillingService(db,gate).ReadStaff(viewer,id));Task<ServiceOrder> writer=null;
            try
            {
                Assert(gate.Entered.Wait(10000),"Read did not reach clock after orders");writer=Task.Run(()=>orders.ConfirmStaff(admin,pending.OrderId));
                // InnoDB RR read-only uses MVCC: the writer can COMMIT before this read closes.
                Assert(writer.Wait(8000),"Read-only billing blocked a real Order writer");gate.Release.Set();var old=reader.GetAwaiter().GetResult();
                Assert(old.ServiceCharge==before.ServiceCharge&&old.PendingOrderCount==before.PendingOrderCount&&service.ReadGuest("0901111111",id).ServiceCharge==before.ServiceCharge+25000m,"Read mixed/cached order snapshots");
            }
            finally{gate.Release.Set();reader.GetAwaiter().GetResult();if(writer!=null)writer.GetAwaiter().GetResult();}
        }
        using(var gate=new GateClock{Value=clock.Value})
        {
            var reader=Task.Run(()=>new MySqlBillingService(db,gate).ReadGuest("0901111111",id));
            try{Assert(gate.Entered.Wait(10000),"Phone snapshot read not ready");ChangePhone(db,admin,"0901111111","0909999999");gate.Release.Set();Assert(reader.GetAwaiter().GetResult().SessionId==id,"In-flight membership lost snapshot");Reject<InvalidOperationException>(()=>service.ReadGuest("0901111111",id));}
            finally{gate.Release.Set();reader.GetAwaiter().GetResult();}
        }
        ChangePhone(db,admin,"0909999999","0901111111");
        using(var gate=new GateClock{Value=clock.Value})
        {
            var reader=Task.Run(()=>new MySqlBillingService(db,gate).ReadStaff(viewer,id));
            try{Assert(gate.Entered.Wait(10000),"Permission snapshot not ready");Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';");gate.Release.Set();Assert(reader.GetAwaiter().GetResult().SessionId==id,"In-flight rights lost snapshot");Reject<UnauthorizedAccessException>(()=>service.ReadStaff(viewer,id));}
            finally{gate.Release.Set();reader.GetAwaiter().GetResult();}
        }
        Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.View';");
    }
    private static void LockForBill(MySqlDatabase db,MySqlConnection c,MySqlTransaction tx,int id)
    {
        int room,customer;int? source;
        using(var discovery=db.OpenConnection())using(var q=discovery.CreateCommand())
        {q.CommandText="SELECT RoomId,CustomerId,ReservationId FROM RoomSessions WHERE RoomSessionId="+id;using(var r=q.ExecuteReader()){Assert(r.Read(),"Missing fixture session");room=r.GetInt32(0);customer=r.GetInt32(1);source=r.IsDBNull(2)?(int?)null:r.GetInt32(2);}}
        MySqlAvailabilityService.LockResources(c,tx,room,customer);
        using(var q=c.CreateCommand())
        {
            q.Transaction=tx;if(source.HasValue){q.CommandText="SELECT ReservationId FROM Reservations WHERE ReservationId="+source.Value+" FOR UPDATE;";q.ExecuteScalar();}
            q.CommandText="SELECT RoomSessionId FROM RoomSessions WHERE RoomSessionId="+id+" FOR UPDATE;";q.ExecuteScalar();
            q.CommandText="SELECT OrderId FROM Orders WHERE RoomSessionId="+id+" ORDER BY OrderId FOR UPDATE;";using(var r=q.ExecuteReader())while(r.Read()){}
        }
    }
    private static void SharedWriter(MySqlDatabase db,LoginSession admin,LoginSession checkout,Clock clock,MySqlBillingService service,MySqlOrderService orders,int id,DateTimeOffset start)
    {
        clock.Value=At(13);var pending=orders.CreateGuest("0901111111",id,Cart(1));var before=service.ReadGuest("0901111111",id);
        using(var c=db.OpenConnection())using(var other=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))
        {
            new MySqlPermissionService(db).Demand(checkout,"Session.CheckOut",c,tx);LockForBill(db,c,tx,id);
            Reject<ArgumentException>(()=>InTransaction(service,other,tx,id));Reject<ArgumentException>(()=>InTransaction(service,c,null,id));
            // Shared writer fixture only, deliberately rolled back: no checkout/payment performed.
            using(var q=c.CreateCommand()){q.Transaction=tx;q.CommandText="UPDATE Orders SET Status='Completed' WHERE OrderId="+pending.OrderId;q.ExecuteNonQuery();}
            clock.Reads=0;clock.Value=At(13).AddMinutes(1);var bill=InTransaction(service,c,tx,id);
            Assert(clock.Reads==1&&bill.ServiceCharge==before.ServiceCharge+25000m&&bill.BillingEndTime==clock.Value&&tx.Connection==c,"Shared calculator did not use caller data/transaction/clock");tx.Rollback();
        }
        Assert(service.ReadGuest("0901111111",id).ServiceCharge==before.ServiceCharge&&(string)Sql(db,"SELECT Status FROM Orders WHERE OrderId="+pending.OrderId)=="Pending","Calculator committed caller transaction");
        using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginReadTransaction(c))
        {
            // Preview caller contract: live CheckOut and calculator in the same RR snapshot.
            var access=(StaffAccess)typeof(MySqlPermissionService).GetMethod("ReadAccess",BindingFlags.Instance|BindingFlags.NonPublic)
                .Invoke(new MySqlPermissionService(db),new object[]{checkout,c,tx});
            Assert(access.Permissions.ContainsKey("Session.CheckOut")&&!access.Permissions.ContainsKey("Session.View"),"Preview fixture has unexpected rights");
            clock.Reads=0;var preview=InTransaction(service,c,tx,id);
            Assert(clock.Reads==1&&preview.ServiceCharge==before.ServiceCharge&&preview.BillingEndTime==clock.Value,"RR preview calculator required View or changed data");
        }
        using(var c=db.OpenConnection())using(var tx=c.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))Reject<ArgumentException>(()=>InTransaction(service,c,tx,id));
        using(var c=new MySqlDatabase("defaultdb").OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))Reject<ArgumentException>(()=>InTransaction(service,c,tx,id));
        clock.Reads=0;Task<SessionBill> waiting=null;
        using(var entered=new ManualResetEventSlim(false))
        using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))using(var q=c.CreateCommand())
        {
            try
            {
                q.Transaction=tx;q.CommandText="SELECT RoomId FROM Rooms WHERE RoomId=1 FOR UPDATE;";q.ExecuteScalar();
                waiting=Task.Run(()=>{using(var next=db.OpenConnection())using(var nextTx=MySqlDatabase.BeginWriteTransaction(next)){new MySqlPermissionService(db).Demand(checkout,"Session.CheckOut",next,nextTx);entered.Set();LockForBill(db,next,nextTx,id);return InTransaction(service,next,nextTx,id);}});
                Assert(entered.Wait(10000),"Shared writer not ready");Thread.Sleep(150);Assert(!waiting.IsCompleted&&clock.Reads==0,"Billing clock read before Room wait");
                q.CommandText="UPDATE Orders SET Status='Completed' WHERE OrderId="+pending.OrderId;q.ExecuteNonQuery();clock.Value=At(13).AddMinutes(3);tx.Commit();
            }
            finally{if(tx.Connection!=null)tx.Rollback();}
        }
        var fresh=waiting.GetAwaiter().GetResult();Assert(clock.Reads==1&&fresh.BillingEndTime==clock.Value&&fresh.ServiceCharge==before.ServiceCharge+25000m&&fresh.RoomCharge==ExactRoom(120000,(clock.Value-start).Ticks),"Shared writer used stale clock/orders after lock wait");
    }
}
