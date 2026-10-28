using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MySqlConnector;

public static class MusicBoxMySqlOrderChecks
{
    private sealed class Clock : IClock
    {
        public DateTimeOffset Value; public int Reads; public Action Hook;
        public DateTimeOffset UtcNow { get { Interlocked.Increment(ref Reads);if(Hook!=null)Hook();return Value; } }
    }
    private sealed class GateClock : IClock, IDisposable
    {
        public DateTimeOffset Value;
        public readonly ManualResetEventSlim Entered=new ManualResetEventSlim(false),Release=new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow { get { Entered.Set();if(!Release.Wait(10000))throw new Exception("Writer gate timeout");return Value; } }
        public void Dispose(){Release.Set();Entered.Dispose();Release.Dispose();}
    }
    private static DateTimeOffset At(int h,int m=0){return new DateTimeOffset(2026,10,28,h,m,0,TimeSpan.FromHours(7));}
    private static string Utc(DateTimeOffset t){return t.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);}
    private static object Sql(MySqlDatabase db,string sql){using(var c=db.OpenConnection())using(var q=c.CreateCommand()){q.CommandText=sql;return q.ExecuteScalar();}}
    private static long Count(MySqlDatabase db,string sql){return Convert.ToInt64(Sql(db,sql));}
    private static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
    private static T Reject<T>(Action action)where T:Exception{try{action();}catch(T e){return e;}throw new Exception("Expected "+typeof(T).Name);}
    private static bool Attempt(Action action){try{action();return true;}catch(InvalidOperationException){return false;}}
    private static OrderLineRequest[] Cart(int id=1,int quantity=1){return new[]{new OrderLineRequest{ServiceId=id,Quantity=quantity}};}
    private static void Grant(MySqlDatabase db,params string[] codes)
    {
        Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';");
        foreach(var code in codes)Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='"+code+"';");
    }
    private static void ChangePhone(MySqlDatabase db,LoginSession admin,string oldPhone,string newPhone)
    {
        var customers=new MySqlCustomerService(db);var original=customers.Search(admin,null,oldPhone).Items.Single();
        customers.Save(admin,original,new CustomerEdit{FullName=original.FullName,PhoneNumber=newPhone});
    }
    private static void Edit(MySqlDatabase db,LoginSession admin,int id,string name,long price,bool active)
    {
        var catalog=new MySqlServiceCatalogService(db);var original=catalog.ListForManagement(admin).Single(s=>s.ServiceId==id);
        catalog.Save(admin,original,new ServiceEdit{Name=name,Category=original.Category,Price=price,Description=original.Description,IsActive=active});
    }
    public static void Run()
    {
        var name="musicbox_test_"+Guid.NewGuid().ToString("N");var db=new MySqlDatabase(name);
        Console.WriteLine("Order test database: "+name);
        try
        {
            const string password="Orders-MySql-2026!";var auth=new MySqlAuthenticationService(db);
            var admin=auth.SetupAdminAsync("admin","Admin",password).GetAwaiter().GetResult();
            Sql(db,@"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'operator','operator','OPERATOR',PasswordHash,'operator','Nhân viên',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('operator','Staff');DELETE FROM RolePermission WHERE RoleId='Staff';");
            var actor=auth.LoginAsync("operator",password).GetAwaiter().GetResult();
            for(var r=1;r<=3;r++)Sql(db,"INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,CreatedAt) VALUES("+r+",'O"+r+"',1,'Phòng','test.png',1,'"+Utc(At(9))+"');");
            var catalog=new MySqlServiceCatalogService(db);
            catalog.Save(admin,null,new ServiceEdit{Name="Trà",Category="Đồ uống",Price=25000,IsActive=true});
            catalog.Save(admin,null,new ServiceEdit{Name="Bánh",Category="Đồ ăn",Price=35000,IsActive=true});
            catalog.Save(admin,null,new ServiceEdit{Name="Ngừng bán",Category="Khác",Price=10000,IsActive=false});
            var clock=new Clock{Value=At(13).AddSeconds(12).AddTicks(1234567)};var sessions=new MySqlRoomSessionService(db,clock);
            var main=sessions.CreateWalkIn(admin,new WalkInRequest{RoomId=1,FullName="Khách riêng",PhoneNumber="0901111111"}).Session;
            var booking=new MySqlReservationService(db,new Clock{Value=At(9)}).CreateGuest(new ReservationRequest{RoomId=2,FullName="Khách booking",PhoneNumber="0902222222",StartTime=At(13),DurationMinutes=60});
            var booked=sessions.CheckIn(admin,booking.ReservationId);
            var history=sessions.CreateWalkIn(admin,new WalkInRequest{RoomId=3,FullName="Lịch sử",PhoneNumber="0903333333"}).Session;
            var service=new MySqlOrderService(db,clock);var historical=service.CreateStaff(admin,history.RoomSessionId,Cart());
            // Guard/history fixture only; no MySQL checkout claim.
            Sql(db,"UPDATE RoomSessions SET Status='Completed',ActualEndTime='"+Utc(At(14))+"' WHERE RoomSessionId="+history.RoomSessionId+";");
            ReadAndPermissions(db,admin,actor,clock,service,main.RoomSessionId,history.RoomSessionId,historical.OrderId);
            CreationAndStates(db,admin,actor,clock,service,main.RoomSessionId,booked.RoomSessionId);
            Console.WriteLine("PASS Orders menu/cart/current rights/snapshots/states/rollback/history");
            ConcurrentWriters(db,admin,actor,clock,service,main.RoomSessionId);
            Console.WriteLine("PASS Orders confirm/cancel and catalog writer races");
            SnapshotsAndWaits(db,admin,actor,clock,service,main.RoomSessionId);
            Assert(Count(db,"SELECT Version FROM _MusicBoxSchemaVersion;")==MySqlDatabase.CurrentSchemaVersion,"Schema changed");
            Console.WriteLine("PASS MySQL Orders: independent live rights, current normalized phone/public DTO, Active/Completed boundaries, read snapshots, merged 1-10/server price/decimal, Guest Pending/Staff Completed, terminal guards/outside hours, item/audit rollback, real confirm/cancel/catalog races, fresh clocks after Room/Service waits and phone recheck. GUID DB only; no checkout/provider/import claims.");
        }
        finally
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"\Amusicbox_test_[0-9a-f]{32}\z"))throw new Exception("Unsafe cleanup");
            Sql(new MySqlDatabase("defaultdb"),"DROP DATABASE IF EXISTS `"+name+"`");
        }
    }
    private static void ReadAndPermissions(MySqlDatabase db,LoginSession admin,LoginSession actor,Clock clock,MySqlOrderService service,int session,int history,int historicalOrder)
    {
        Reject<UnauthorizedAccessException>(()=>service.ReadWorkspaceStaff(null));Reject<UnauthorizedAccessException>(()=>service.ReadWorkspaceStaff(actor));
        Reject<UnauthorizedAccessException>(()=>service.CreateStaff(null,session,Cart()));Reject<UnauthorizedAccessException>(()=>service.CreateStaff(actor,session,Cart()));
        Grant(db,"Order.Create");clock.Reads=0;var workspace=service.ReadWorkspaceStaff(actor);
        Assert(clock.Reads==1&&workspace.CanCreate&&!workspace.CanView&&!workspace.CanConfirm&&!workspace.CanCancel&&workspace.Sessions.Count==2&&workspace.Sessions.All(s=>s.IsActive),"Create-only workspace required unrelated rights or exposed history");
        clock.Reads=0;var menu=service.ReadMenuGuest("+84 901.111-111",session);
        Assert(clock.Reads==1&&menu.CanCreate&&menu.CheckedAt==clock.Value&&menu.Items.Count==2&&!menu.Items.Any(i=>i.ServiceId==3),"Menu/current phone/single clock/inactive filtering wrong");
        Assert(service.ReadMenuStaff(actor,session).Items.Count==2,"Create-only menu denied");
        var cart=new[]{new OrderLineRequest{ServiceId=1,Quantity=2},new OrderLineRequest{ServiceId=1,Quantity=3}};
        var audits=Count(db,"SELECT COUNT(*) FROM AuditLog;");var orders=Count(db,"SELECT COUNT(*) FROM Orders;");clock.Reads=0;
        var preview=service.PreviewGuest("84901111111",session,cart);
        Assert(clock.Reads==1&&preview.Amount==125000m&&preview.Items.Single().Quantity==5&&service.PreviewStaff(actor,session,cart).Amount==preview.Amount,"Preview failed merged/current price/clock");
        Assert(Count(db,"SELECT COUNT(*) FROM Orders;")==orders&&Count(db,"SELECT COUNT(*) FROM AuditLog;")==audits,"Menu/preview wrote data");
        foreach(var type in new[]{typeof(ServiceOrder),typeof(OrderMenu),typeof(OrderPreview),typeof(OrderSessionChoice)})
            foreach(var property in new[]{"CustomerId","PhoneNumber","FullName","RoomId","ReservationId","RoomSessionId","CreatedByUserId"})
                Assert(type.GetProperty(property)==null,"Public DTO leak: "+property);
        foreach(var invalid in new[]{(OrderLineRequest[])null,new OrderLineRequest[0],new OrderLineRequest[]{null},Cart(0),Cart(1,0),Cart(1,-1),Cart(1,11),new[]{new OrderLineRequest{ServiceId=1,Quantity=6},new OrderLineRequest{ServiceId=1,Quantity=5}}})
        {Reject<ArgumentException>(()=>service.PreviewGuest("0901111111",session,invalid));Reject<ArgumentException>(()=>service.CreateGuest("0901111111",session,invalid));}
        Reject<InvalidOperationException>(()=>service.CreateGuest("0901111111",session,Cart(99999)));Reject<InvalidOperationException>(()=>service.PreviewGuest("0901111111",session,Cart(3)));
        Reject<InvalidOperationException>(()=>service.ReadMenuGuest("0902222222",session));Reject<InvalidOperationException>(()=>service.PreviewGuest("0902222222",session,cart));Reject<InvalidOperationException>(()=>service.ListGuest("0902222222",session));
        Reject<ArgumentException>(()=>service.ReadMenuGuest("bad",session));Reject<ArgumentException>(()=>service.CreateGuest("bad",session,Cart()));
        var saved=service.CreateStaff(actor,session,Cart());
        Assert(saved.Status=="Completed"&&(string)Sql(db,"SELECT CreatedByUserId FROM Orders WHERE OrderId="+saved.OrderId)==actor.UserId,"Create-only Staff not Completed/creator");
        Reject<UnauthorizedAccessException>(()=>service.ListStaff(actor,session));Reject<UnauthorizedAccessException>(()=>service.CancelStaff(actor,saved.OrderId));
        Grant(db,"Order.View");workspace=service.ReadWorkspaceStaff(actor);
        Assert(workspace.CanView&&!workspace.CanCreate&&workspace.Sessions.Count==3&&service.ListStaff(actor,history).Single().OrderId==historicalOrder,"View-only history required unrelated rights");
        Reject<UnauthorizedAccessException>(()=>service.ReadMenuStaff(actor,session));Reject<UnauthorizedAccessException>(()=>service.PreviewStaff(actor,session,cart));Reject<UnauthorizedAccessException>(()=>service.CreateStaff(actor,session,cart));
        Reject<InvalidOperationException>(()=>service.ListGuest("0903333333",history));Reject<InvalidOperationException>(()=>service.ReadMenuGuest("0903333333",history));Reject<InvalidOperationException>(()=>service.CreateGuest("0903333333",history,Cart()));
        var pending=service.CreateGuest("0901111111",session,Cart());Grant(db,"Order.Confirm");
        Reject<UnauthorizedAccessException>(()=>service.ReadWorkspaceStaff(actor));Assert(service.ConfirmStaff(actor,pending.OrderId).Status=="Completed","Confirm-only route required View/Create");
        pending=service.CreateGuest("0901111111",session,Cart());Grant(db,"Order.Cancel");Assert(service.CancelStaff(actor,pending.OrderId).Status=="Cancelled","Cancel-only route required View");
        Grant(db);Reject<UnauthorizedAccessException>(()=>service.CancelStaff(actor,pending.OrderId));Assert(service.ReadMenuGuest("0901111111",session).CanCreate,"Staff revoke disabled explicit Guest route");
    }
    private static void CreationAndStates(MySqlDatabase db,LoginSession admin,LoginSession actor,Clock clock,MySqlOrderService service,int session,int booked)
    {
        Grant(db,"Order.Create","Order.View","Order.Confirm","Order.Cancel");clock.Value=At(13).AddSeconds(21).AddTicks(7654321);clock.Reads=0;
        var first=service.CreateGuest("+84 901111111",session,new[]{new OrderLineRequest{ServiceId=1,Quantity=4},new OrderLineRequest{ServiceId=1,Quantity=6}});
        Assert(first.Status=="Pending"&&first.CreatedAt==clock.Value&&clock.Reads==1&&first.Items.Single().Quantity==10&&first.Items.Single().UnitPrice==25000,"Guest merge/clock/ticks/price wrong");
        Assert(Sql(db,"SELECT CreatedByUserId FROM Orders WHERE OrderId="+first.OrderId)==DBNull.Value&&Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Order.Create' AND ActorType='Guest' AND UserId IS NULL;")>0,"Guest creator/audit wrong");
        var preview=service.PreviewGuest("0901111111",session,Cart());Edit(db,admin,1,"Trà mới",45000,true);
        var saved=service.CreateGuest("0901111111",session,Cart());Assert(preview.Amount==25000m&&saved.Items.Single().UnitPrice==45000&&saved.Items.Single().ServiceNameSnapshot=="Trà mới","Preview froze catalog price/name");
        Edit(db,admin,1,"Dừng bán",99000,false);
        var receipt=service.ListGuest("0901111111",session).Single(o=>o.OrderId==first.OrderId);
        Assert(receipt.Items.Single().UnitPrice==25000&&receipt.Items.Single().ServiceNameSnapshot=="Trà"&&receipt.Status=="Pending","Catalog edit changed old snapshot/state");
        Reject<InvalidOperationException>(()=>service.CreateGuest("0901111111",session,Cart()));Assert(service.ConfirmStaff(actor,first.OrderId).Status=="Completed","Disabled service prevented serving existing Pending");
        Reject<InvalidOperationException>(()=>service.CancelGuest("0901111111",first.OrderId));Reject<InvalidOperationException>(()=>service.ConfirmStaff(actor,first.OrderId));
        service.CancelGuest("0901111111",saved.OrderId);Reject<InvalidOperationException>(()=>service.ConfirmStaff(actor,saved.OrderId));Reject<InvalidOperationException>(()=>service.CancelStaff(actor,saved.OrderId));
        Edit(db,admin,1,"Trà",long.MaxValue,true);Assert(service.PreviewStaff(actor,session,Cart(1,10)).Amount==(decimal)long.MaxValue*10,"Preview overflowed Int64");
        Assert(service.CreateGuest("0901111111",session,Cart(1,10)).Items.Single().UnitPrice==long.MaxValue,"BIGINT snapshot lost precision");Edit(db,admin,1,"Trà",25000,true);
        foreach(var value in new[]{At(9),At(12).AddTicks(-1),At(13),At(23).AddTicks(-1)})
        {clock.Value=value;Assert(service.CreateGuest("0901111111",session,Cart()).Status=="Pending","Open boundary rejected");}
        foreach(var value in new[]{At(8,59),At(12),At(23)})
        {
            clock.Value=At(13);var pending=service.CreateGuest("0901111111",session,Cart());clock.Value=value;
            Assert(!service.ReadMenuGuest("0901111111",session).CanCreate&&!service.ReadMenuStaff(actor,session).CanCreate,"Outside-hour menu enabled");
            Reject<ArgumentException>(()=>service.PreviewGuest("0901111111",session,Cart()));Reject<ArgumentException>(()=>service.CreateGuest("0901111111",session,Cart()));Reject<ArgumentException>(()=>service.CreateStaff(actor,session,Cart()));
            Assert(service.ConfirmStaff(actor,pending.OrderId).Status=="Completed","Existing Pending cannot be served outside hours");
            clock.Value=At(13);pending=service.CreateGuest("0901111111",session,Cart());clock.Value=value;Assert(service.CancelGuest("0901111111",pending.OrderId).Status=="Cancelled","Existing Pending cannot be cancelled outside hours");
        }
        clock.Value=At(15);Assert(service.CreateStaff(actor,booked,Cart()).Status=="Completed","Overdue booking session blocked new items while open");
        clock.Value=At(13);var orders=Count(db,"SELECT COUNT(*) FROM Orders;");var items=Count(db,"SELECT COUNT(*) FROM OrderItems;");var audits=Count(db,"SELECT COUNT(*) FROM AuditLog;");
        Sql(db,"CREATE TRIGGER FailOrderItem BEFORE INSERT ON OrderItems FOR EACH ROW BEGIN IF NEW.ServiceId=2 THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='test item failure'; END IF; END;");
        Reject<MySqlException>(()=>service.CreateGuest("0901111111",session,new[]{new OrderLineRequest{ServiceId=1,Quantity=1},new OrderLineRequest{ServiceId=2,Quantity=1}}));
        Assert(Count(db,"SELECT COUNT(*) FROM Orders;")==orders&&Count(db,"SELECT COUNT(*) FROM OrderItems;")==items&&Count(db,"SELECT COUNT(*) FROM AuditLog;")==audits,"Item failure left partial order");Sql(db,"DROP TRIGGER FailOrderItem;");
        var p=service.CreateGuest("0901111111",session,Cart());orders=Count(db,"SELECT COUNT(*) FROM Orders;");items=Count(db,"SELECT COUNT(*) FROM OrderItems;");audits=Count(db,"SELECT COUNT(*) FROM AuditLog;");
        Sql(db,"CREATE TRIGGER FailOrderAudit BEFORE INSERT ON AuditLog FOR EACH ROW BEGIN IF NEW.EntityName='Order' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='test audit failure'; END IF; END;");
        Reject<MySqlException>(()=>service.CreateStaff(actor,session,Cart()));Reject<MySqlException>(()=>service.CreateGuest("0901111111",session,Cart()));Reject<MySqlException>(()=>service.CancelGuest("0901111111",p.OrderId));Reject<MySqlException>(()=>service.ConfirmStaff(actor,p.OrderId));
        Assert(Count(db,"SELECT COUNT(*) FROM Orders;")==orders&&Count(db,"SELECT COUNT(*) FROM OrderItems;")==items&&Count(db,"SELECT COUNT(*) FROM AuditLog;")==audits&&(string)Sql(db,"SELECT Status FROM Orders WHERE OrderId="+p.OrderId)=="Pending","Audit rollback failed");Sql(db,"DROP TRIGGER FailOrderAudit;");
        ChangePhone(db,admin,"0901111111","0909999999");
        Reject<InvalidOperationException>(()=>service.ReadMenuGuest("0901111111",session));Reject<InvalidOperationException>(()=>service.PreviewGuest("0901111111",session,Cart()));Reject<InvalidOperationException>(()=>service.CreateGuest("0901111111",session,Cart()));Reject<InvalidOperationException>(()=>service.ListGuest("0901111111",session));Reject<InvalidOperationException>(()=>service.CancelGuest("0901111111",p.OrderId));
        Assert(service.ListGuest("0909999999",session).Any(o=>o.OrderId==p.OrderId)&&service.CancelGuest("0909999999",p.OrderId).Status=="Cancelled","New phone lost own orders");ChangePhone(db,admin,"0909999999","0901111111");
        var a=service.CreateGuest("0901111111",session,Cart());var b=service.CreateGuest("0901111111",session,Cart());Assert(a.OrderId!=b.OrderId,"Identical fresh carts were deduplicated without a request key");
        Reject<MySqlException>(()=>Sql(db,"UPDATE OrderItems SET Quantity=11 WHERE OrderId="+a.OrderId));Reject<MySqlException>(()=>Sql(db,"UPDATE OrderItems SET UnitPrice=0 WHERE OrderId="+a.OrderId));
        Reject<MySqlException>(()=>Sql(db,"INSERT INTO OrderItems(OrderId,ServiceId,ServiceNameSnapshot,Quantity,UnitPrice) SELECT OrderId,ServiceId,ServiceNameSnapshot,Quantity,UnitPrice FROM OrderItems WHERE OrderId="+a.OrderId));
        Reject<MySqlException>(()=>Sql(db,"DELETE FROM Services WHERE ServiceId=1;"));
    }
    private static void ConcurrentWriters(MySqlDatabase db,LoginSession admin,LoginSession actor,Clock clock,MySqlOrderService service,int session)
    {
        clock.Value=At(13);
        for(var round=0;round<3;round++)
        {
            var pending=service.CreateGuest("0901111111",session,Cart());var before=Count(db,"SELECT COUNT(*) FROM AuditLog WHERE EntityName='Order';");
            using(var gate=new ManualResetEventSlim(false))
            {
                var a=Task.Run(()=>{gate.Wait();return Attempt(()=>service.ConfirmStaff(actor,pending.OrderId));});
                var b=Task.Run(()=>{gate.Wait();return Attempt(()=>service.CancelGuest("0901111111",pending.OrderId));});gate.Set();Task.WaitAll(a,b);
                Assert(a.Result!=b.Result&&Count(db,"SELECT COUNT(*) FROM AuditLog WHERE EntityName='Order';")==before+1,"Confirm/cancel both changed state or duplicate audit");
            }
        }
        var foreign=service.CreateGuest("0901111111",session,Cart());Assert(Reject<InvalidOperationException>(()=>service.CancelGuest("0902222222",foreign.OrderId)).Message==Reject<InvalidOperationException>(()=>service.CancelGuest("0902222222",999999)).Message,"Foreign Order ID disclosed membership");
        using(var gate=new GateClock{Value=At(13)})
        {
            var creating=Task.Run(()=>new MySqlOrderService(db,gate).CreateGuest("0901111111",session,Cart()));
            try
            {
                Assert(gate.Entered.Wait(10000),"Order never reached clock after cart locks");
                var editing=Task.Run(()=>Edit(db,admin,1,"Đổi sau đặt",88000,false));Thread.Sleep(150);
                Assert(!editing.IsCompleted,"Catalog edit passed cart snapshot lock");gate.Release.Set();Task.WaitAll(creating,editing);
                Assert(creating.Result.Status=="Pending"&&creating.Result.Items.Single().UnitPrice==25000&&creating.Result.Items.Single().ServiceNameSnapshot=="Trà","Catalog writer altered existing snapshot");
            }
            finally{gate.Release.Set();creating.GetAwaiter().GetResult();}
        }
        Reject<InvalidOperationException>(()=>service.CreateGuest("0901111111",session,Cart()));Edit(db,admin,1,"Trà",25000,true);
    }
    private static void SnapshotsAndWaits(MySqlDatabase db,LoginSession admin,LoginSession actor,Clock clock,MySqlOrderService service,int session)
    {
        clock.Value=At(13);clock.Hook=()=>{clock.Hook=null;Edit(db,admin,1,"Snapshot mới",45000,true);};
        var preview=service.PreviewGuest("0901111111",session,Cart());Assert(preview.Amount==25000m&&service.PreviewGuest("0901111111",session,Cart()).Amount==45000m,"Preview escaped RR snapshot");
        clock.Hook=()=>{clock.Hook=null;ChangePhone(db,admin,"0901111111","0909999999");};
        Assert(service.ReadMenuGuest("0901111111",session).CanCreate,"Menu membership escaped RR snapshot");Reject<InvalidOperationException>(()=>service.ReadMenuGuest("0901111111",session));ChangePhone(db,admin,"0909999999","0901111111");
        clock.Hook=()=>{clock.Hook=null;Grant(db);};
        Assert(service.ReadWorkspaceStaff(actor).CanView,"Workspace permissions escaped RR snapshot");Reject<UnauthorizedAccessException>(()=>service.ReadWorkspaceStaff(actor));
        Grant(db,"Order.Create","Order.View","Order.Confirm","Order.Cancel");
        clock.Value=At(22,59);clock.Reads=0;var orders=Count(db,"SELECT COUNT(*) FROM Orders;");
        using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))using(var q=c.CreateCommand())
        {
            q.Transaction=tx;q.CommandText="SELECT RoomId FROM Rooms WHERE RoomId=1 FOR UPDATE;";q.ExecuteScalar();
            var waiting=Task.Run(()=>{try{service.CreateGuest("0901111111",session,Cart());return false;}catch(ArgumentException){return true;}});
            Thread.Sleep(200);Assert(!waiting.IsCompleted&&clock.Reads==0,"Order clock before Room lock");clock.Value=At(23);tx.Commit();Assert(waiting.GetAwaiter().GetResult()&&clock.Reads==1,"Order used stale open time after Room wait");
        }
        clock.Value=At(11,59);clock.Reads=0;
        using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))using(var q=c.CreateCommand())
        {
            q.Transaction=tx;q.CommandText="SELECT ServiceId FROM Services WHERE ServiceId=1 FOR UPDATE;";q.ExecuteScalar();
            var waiting=Task.Run(()=>{try{service.CreateGuest("0901111111",session,Cart());return false;}catch(ArgumentException){return true;}});
            Thread.Sleep(200);Assert(!waiting.IsCompleted&&clock.Reads==0,"Order clock before Service lock");clock.Value=At(12);tx.Commit();Assert(waiting.GetAwaiter().GetResult()&&clock.Reads==1,"Order used stale open time after Service wait");
        }
        Assert(Count(db,"SELECT COUNT(*) FROM Orders;")==orders,"Rejected wait created an order");
        clock.Value=At(13);
        using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))using(var q=c.CreateCommand())
        {
            q.Transaction=tx;q.CommandText="SELECT RoomId FROM Rooms WHERE RoomId=1 FOR UPDATE;";q.ExecuteScalar();
            var waiting=Task.Run(()=>Attempt(()=>service.CreateGuest("0901111111",session,Cart())));Thread.Sleep(200);
            ChangePhone(db,admin,"0901111111","0909999999");tx.Commit();Assert(!waiting.GetAwaiter().GetResult(),"Order trusted discovery phone after edit");
        }
        ChangePhone(db,admin,"0909999999","0901111111");var pending=service.CreateGuest("0901111111",session,Cart());
        using(var c=db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))using(var q=c.CreateCommand())
        {
            q.Transaction=tx;q.CommandText="SELECT RoomId FROM Rooms WHERE RoomId=1 FOR UPDATE;";q.ExecuteScalar();
            var waiting=Task.Run(()=>Attempt(()=>service.CancelGuest("0901111111",pending.OrderId)));Thread.Sleep(200);
            ChangePhone(db,admin,"0901111111","0909999999");tx.Commit();Assert(!waiting.GetAwaiter().GetResult()&&(string)Sql(db,"SELECT Status FROM Orders WHERE OrderId="+pending.OrderId)=="Pending","Cancel trusted discovery phone after edit");
        }
        ChangePhone(db,admin,"0909999999","0901111111");
        // Current permission/stamp and terminal-session guard. Checkout race waits for real port.
        Sql(db,"UPDATE RoomSessions SET Status='Completed',ActualEndTime='"+Utc(At(15))+"' WHERE RoomSessionId="+session+";");
        Reject<InvalidOperationException>(()=>service.CreateGuest("0901111111",session,Cart()));Reject<InvalidOperationException>(()=>service.ConfirmStaff(actor,pending.OrderId));Reject<InvalidOperationException>(()=>service.CancelStaff(actor,pending.OrderId));Reject<InvalidOperationException>(()=>service.CancelGuest("0901111111",pending.OrderId));
        Assert(service.ListStaff(actor,session).Count>0,"View lost completed history");Reject<InvalidOperationException>(()=>service.ListGuest("0901111111",session));
        Sql(db,"UPDATE AspNetUsers SET SecurityStamp='new-stamp' WHERE Id='operator';");Reject<UnauthorizedAccessException>(()=>service.ReadWorkspaceStaff(actor));Reject<UnauthorizedAccessException>(()=>service.ConfirmStaff(actor,pending.OrderId));
    }
}
