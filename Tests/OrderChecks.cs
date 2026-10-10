using System;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxOrderChecks
{
    private sealed class Clock : IClock { public DateTimeOffset Value; public int Reads; public DateTimeOffset UtcNow {get{Interlocked.Increment(ref Reads);return Value;}} }
    private sealed class GateClock : IClock
    {
        public readonly ManualResetEventSlim Entered=new ManualResetEventSlim(false),Release=new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow {get{Entered.Set();if(!Release.Wait(10000))throw new Exception("Writer gate timeout");return Time(13);}}
    }
    private static DateTimeOffset Time(int h,int m=0){return new DateTimeOffset(2026,10,8,h,m,0,TimeSpan.FromHours(7));}
    private static string Utc(DateTimeOffset value){return value.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);}
    private static object Sql(SqliteDatabase db,string sql){using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=sql;return cmd.ExecuteScalar();}}
    private static long Count(SqliteDatabase db,string sql){return Convert.ToInt64(Sql(db,sql));}
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static OrderLineRequest[] Cart(int id=1,int quantity=1){return new[]{new OrderLineRequest{ServiceId=id,Quantity=quantity}};}
    private static bool Attempt(Action action){try{action();return true;}catch(InvalidOperationException){return false;}}
    private static void VerifyMenu(SqliteDatabase db,Clock clock,LoginSession actor,OrderService service,int sessionId)
    {
        clock.Value=Time(13).AddSeconds(11).AddTicks(1234);
        Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Order.Create';");
        var orders=Count(db,"SELECT COUNT(*) FROM Orders");var audits=Count(db,"SELECT COUNT(*) FROM AuditLog");clock.Reads=0;
        var menu=service.ReadMenuGuest("+84 901.111-111",sessionId);
        Assert(menu.CanCreate && menu.Items.Count==1 && menu.Items.Single().ServiceId==2 && menu.CheckedAt==clock.Value && clock.Reads==1,"Menu exposed inactive services or wrong normalized phone/time.");
        Assert(service.ReadMenuStaff(actor,sessionId).Items.Count==1,"Create-only staff menu required unrelated rights.");
        clock.Reads=0;var workspace=service.ReadWorkspaceStaff(actor);
        Assert(workspace.CanCreate && !workspace.CanView && !workspace.CanConfirm && !workspace.CanCancel && workspace.Sessions.All(s=>s.IsActive) && workspace.Sessions.Any(s=>s.SessionId==sessionId) && clock.Reads==1 && workspace.CheckedAt==clock.Value,"Create-only workspace required unrelated rights or exposed history.");
        foreach(var name in new[]{"CustomerId","PhoneNumber","FullName","RoomId","ReservationId"})Assert(typeof(OrderSessionChoice).GetProperty(name)==null,"Order selector exposed unnecessary "+name);
        Reject<UnauthorizedAccessException>(()=>service.ReadWorkspaceStaff(null));
        var cart=new[]{new OrderLineRequest{ServiceId=2,Quantity=2},new OrderLineRequest{ServiceId=2,Quantity=3}};
        clock.Reads=0;var preview=service.PreviewGuest("0901111111",sessionId,cart);
        Assert(preview.Items.Count==1 && preview.Items.Single().Quantity==5 && preview.Amount==175000m && preview.CheckedAt==clock.Value && clock.Reads==1,"Cart preview lost merged quantity/current prices/one clock.");
        Assert(service.PreviewStaff(actor,sessionId,cart).Amount==preview.Amount,"Staff preview used unrelated rights or prices.");
        Assert(Count(db,"SELECT COUNT(*) FROM Orders")==orders && Count(db,"SELECT COUNT(*) FROM AuditLog")==audits,"Menu/preview wrote data.");
        foreach(var type in new[]{typeof(OrderMenu),typeof(OrderMenuItem),typeof(OrderPreview)})
            foreach(var name in new[]{"CustomerId","PhoneNumber","FullName","CreatedByUserId","RoomSessionId"})Assert(type.GetProperty(name)==null,"Public menu/cart leaks "+name);
        Reject<UnauthorizedAccessException>(()=>service.ReadMenuStaff(null,sessionId));
        Reject<InvalidOperationException>(()=>service.ReadMenuGuest("0902222222",sessionId));
        Reject<InvalidOperationException>(()=>service.PreviewGuest("0902222222",sessionId,cart));
        Reject<ArgumentException>(()=>service.ReadMenuGuest("bad",sessionId));
        Reject<ArgumentException>(()=>service.PreviewGuest("0901111111",sessionId,new OrderLineRequest[0]));
        Reject<ArgumentException>(()=>service.PreviewStaff(actor,sessionId,Cart(2,11)));
        Reject<ArgumentException>(()=>service.PreviewGuest("0901111111",sessionId,new[]{new OrderLineRequest{ServiceId=2,Quantity=6},new OrderLineRequest{ServiceId=2,Quantity=5}}));
        Reject<InvalidOperationException>(()=>service.PreviewGuest("0901111111",sessionId,Cart(1)));
        foreach(var time in new[]{Time(12),Time(23)})
        {
            clock.Value=time;Assert(!service.ReadMenuGuest("0901111111",sessionId).CanCreate && !service.ReadMenuStaff(actor,sessionId).CanCreate,"Menu enabled new order outside shift.");
            Reject<ArgumentException>(()=>service.PreviewGuest("0901111111",sessionId,cart));Reject<ArgumentException>(()=>service.PreviewStaff(actor,sessionId,cart));
        }
        clock.Value=Time(13);Sql(db,"UPDATE Customers SET PhoneNumber='0909999999';");
        Reject<InvalidOperationException>(()=>service.ReadMenuGuest("0901111111",sessionId));Reject<InvalidOperationException>(()=>service.PreviewGuest("0901111111",sessionId,cart));
        Assert(service.ReadMenuGuest("0909999999",sessionId).CanCreate,"Menu ignored changed current phone.");Sql(db,"UPDATE Customers SET PhoneNumber='0901111111';");
        Sql(db,"UPDATE RoomSessions SET Status='Completed',ActualEndTime='"+Utc(Time(13,30))+"';");
        Assert(service.ReadWorkspaceStaff(actor).Sessions.Count==0,"Create-only selector exposed completed history.");
        Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Order.View';");
        workspace=service.ReadWorkspaceStaff(actor);Assert(workspace.CanView && workspace.Sessions.Any(s=>s.SessionId==sessionId && !s.IsActive),"Order.View selector required Session.View or omitted history.");
        Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Order.View');");
        Reject<InvalidOperationException>(()=>service.ReadMenuGuest("0901111111",sessionId));Reject<InvalidOperationException>(()=>service.ReadMenuStaff(actor,sessionId));
        Reject<InvalidOperationException>(()=>service.PreviewGuest("0901111111",sessionId,cart));Reject<InvalidOperationException>(()=>service.PreviewStaff(actor,sessionId,cart));
        Sql(db,"UPDATE RoomSessions SET Status='Active',ActualEndTime=NULL;");
        preview=service.PreviewGuest("0901111111",sessionId,cart);Sql(db,"UPDATE Services SET Name='Bánh mới',Price=45000 WHERE ServiceId=2;");
        var saved=service.CreateGuest("0901111111",sessionId,cart);
        Assert(preview.Amount==175000m && saved.Items.Single().UnitPrice==45000 && saved.Items.Single().ServiceNameSnapshot=="Bánh mới","Preview froze name/price for subsequent create.");
        service.PreviewGuest("0901111111",sessionId,cart);Sql(db,"UPDATE Services SET IsActive=0 WHERE ServiceId=2;");orders=Count(db,"SELECT COUNT(*) FROM Orders");
        Reject<InvalidOperationException>(()=>service.CreateGuest("0901111111",sessionId,cart));
        Assert(service.ReadMenuGuest("0901111111",sessionId).Items.Count==0 && Count(db,"SELECT COUNT(*) FROM Orders")==orders,"Stale cart created inactive order or empty menu seeded services.");
        Sql(db,"UPDATE Services SET IsActive=1,Price=9223372036854775807 WHERE ServiceId=2;");
        Assert(service.PreviewGuest("0901111111",sessionId,Cart(2,10)).Amount==(decimal)long.MaxValue*10,"Cart total overflowed long.");
        Sql(db,"UPDATE Services SET Price=45000 WHERE ServiceId=2;DELETE FROM RolePermission WHERE RoleId='Staff';");
        Reject<UnauthorizedAccessException>(()=>service.ReadWorkspaceStaff(actor));
        Reject<UnauthorizedAccessException>(()=>service.ReadMenuStaff(actor,sessionId));Reject<UnauthorizedAccessException>(()=>service.PreviewStaff(actor,sessionId,cart));
        // Staff must fail after revocation; the anonymous route remains a separate explicit API.
        Assert(service.ReadMenuGuest("0901111111",sessionId).CanCreate,"Staff revocation incorrectly disabled Guest route.");
    }
    public static void Run()
    {
        var file=Path.Combine(Path.GetTempPath(),"MusicBoxOrders_"+Guid.NewGuid().ToString("N")+".db");var db=new SqliteDatabase(file);
        try
        {
            const string password="Orders-Test-2026!";var auth=new AuthenticationService(db);
            var admin=auth.SetupAdminAsync("admin","Admin thử",password).GetAwaiter().GetResult();
            Sql(db,@"INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('O1','Phòng món',1,'test.png',1,'test');
INSERT INTO Services(Name,Category,Price,IsActive) VALUES('Nước cam','Đồ uống',25000,1),('Bánh','Đồ ăn',35000,1);
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'operator','operator','OPERATOR',PasswordHash,'operator','Nhân viên món',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('operator','Staff');DELETE FROM RolePermission WHERE RoleId='Staff';");
            var actor=auth.LoginAsync("operator",password).GetAwaiter().GetResult();var clock=new Clock{Value=Time(9)};
            var source=new RoomSessionService(db,clock).CreateWalkIn(admin,new WalkInRequest{RoomId=1,FullName="Khách riêng",PhoneNumber="0901111111"});var sessionId=source.Session.RoomSessionId;
            clock.Value=Time(13).AddSeconds(12).AddTicks(1234);
            // Reconstruct a populated v6 database; a v7 conflict must rollback every schema change.
            var logs=Count(db,"SELECT COUNT(*) FROM AuditLog");
            Sql(db,"DROP TABLE OrderItems;DROP TABLE Orders;PRAGMA user_version=6;CREATE TABLE OrderItems(Fixture TEXT);");
            Reject<SQLiteException>(()=>db.Initialize());
            Assert(Count(db,"PRAGMA user_version")==6 && Count(db,"SELECT COUNT(*) FROM sqlite_master WHERE name='Orders'")==0 && Count(db,"SELECT COUNT(*) FROM RoomSessions")==1,"Failed v7 migration left partial schema or lost sessions.");
            Sql(db,"DROP TABLE OrderItems;");db.Initialize();db.Initialize();
            Assert(Count(db,"PRAGMA user_version")==7 && Count(db,"SELECT COUNT(*) FROM Services")==2 && Count(db,"SELECT COUNT(*) FROM Customers")==1 && Count(db,"SELECT COUNT(*) FROM AuditLog")==logs && Count(db,"SELECT COUNT(*) FROM Orders")==0,"Migration lost v6 rows or seeded orders.");
            var service=new OrderService(db,clock);
            Reject<UnauthorizedAccessException>(()=>service.CreateStaff(null,sessionId,Cart()));
            Reject<UnauthorizedAccessException>(()=>service.CreateStaff(actor,sessionId,Cart()));
            Reject<UnauthorizedAccessException>(()=>service.ListStaff(actor,sessionId));
            Reject<InvalidOperationException>(()=>service.CreateGuest("0902222222",sessionId,Cart()));
            Reject<InvalidOperationException>(()=>service.ListGuest("0902222222",sessionId));
            Reject<ArgumentException>(()=>service.CreateGuest("bad",sessionId,Cart()));
            Reject<ArgumentException>(()=>service.CreateGuest("0901111111",sessionId,null));
            Reject<ArgumentException>(()=>service.CreateGuest("0901111111",sessionId,new OrderLineRequest[0]));
            foreach(var quantity in new[]{0,-1,11})Reject<ArgumentException>(()=>service.CreateGuest("0901111111",sessionId,Cart(1,quantity)));
            Reject<ArgumentException>(()=>service.CreateGuest("0901111111",sessionId,new[]{new OrderLineRequest{ServiceId=1,Quantity=6},new OrderLineRequest{ServiceId=1,Quantity=5}}));
            Reject<InvalidOperationException>(()=>service.CreateGuest("0901111111",sessionId,Cart(999)));
            clock.Reads=0;var first=service.CreateGuest("+84 901.111-111",sessionId,new[]{new OrderLineRequest{ServiceId=1,Quantity=4},new OrderLineRequest{ServiceId=1,Quantity=6}});
            Assert(first.Status=="Pending" && first.Items.Count==1 && first.Items[0].Quantity==10 && first.Items[0].UnitPrice==25000 && first.CreatedAt==clock.Value && clock.Reads==1,"Create lost normalized phone/merged quantity/snapshot/clock ticks.");
            Assert(Sql(db,"SELECT CreatedByUserId FROM Orders WHERE OrderId="+first.OrderId)==DBNull.Value && Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Order.Create' AND ActorType='Guest' AND UserId IS NULL")==1,"Guest stored staff creator.");
            foreach(var name in new[]{"CustomerId","PhoneNumber","FullName","RoomSessionId","CreatedByUserId"})Assert(typeof(ServiceOrder).GetProperty(name)==null,"Public order leaks "+name);
            Sql(db,"UPDATE Services SET Name='Tên mới',Price=99000,IsActive=0 WHERE ServiceId=1;");
            var readAudits=Count(db,"SELECT COUNT(*) FROM AuditLog");
            var receipt=service.ListGuest("0901111111",sessionId).Single();
            Assert(receipt.Items[0].UnitPrice==25000 && receipt.Items[0].ServiceNameSnapshot=="Nước cam" && receipt.Status=="Pending" && Count(db,"SELECT COUNT(*) FROM AuditLog")==readAudits,"Catalog change altered old order or list wrote data.");
            Reject<InvalidOperationException>(()=>service.CreateGuest("0901111111",sessionId,Cart()));
            Reject<InvalidOperationException>(()=>service.CancelGuest("0902222222",first.OrderId));
            Reject<UnauthorizedAccessException>(()=>service.ConfirmStaff(actor,first.OrderId));
            Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Order.Confirm';");clock.Value=Time(23);
            Assert(service.ConfirmStaff(actor,first.OrderId).Status=="Completed","Confirm-only could not serve inactive existing item after hours.");
            Reject<InvalidOperationException>(()=>service.CancelGuest("0901111111",first.OrderId));Reject<InvalidOperationException>(()=>service.ConfirmStaff(actor,first.OrderId));
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Order.Create';");clock.Value=Time(13);
            var served=service.CreateStaff(actor,sessionId,Cart(2));Assert(served.Status=="Completed" && (string)Sql(db,"SELECT CreatedByUserId FROM Orders WHERE OrderId="+served.OrderId)==actor.UserId,"Create-only staff required View/Confirm or wrong actor/state.");
            Reject<UnauthorizedAccessException>(()=>service.ListStaff(actor,sessionId));
            foreach(var time in new[]{Time(8,59),Time(12),Time(12,59),Time(23)})
            {clock.Value=time;Reject<ArgumentException>(()=>service.CreateGuest("0901111111",sessionId,Cart(2)));Reject<ArgumentException>(()=>service.CreateStaff(actor,sessionId,Cart(2)));}
            foreach(var time in new[]{Time(9),Time(11,59).AddSeconds(59),Time(13),Time(22,59).AddSeconds(59)})
            {clock.Value=time;var pending=service.CreateGuest("0901111111",sessionId,Cart(2));clock.Value=Time(12);Assert(service.CancelGuest("0901111111",pending.OrderId).Status=="Cancelled","Existing Pending not cancellable during break.");}
            clock.Value=Time(13);logs=Count(db,"SELECT COUNT(*) FROM Orders");
            Sql(db,"CREATE TRIGGER FailItem BEFORE INSERT ON OrderItems WHEN NEW.ServiceId=2 BEGIN SELECT RAISE(ABORT,'item failure'); END;UPDATE Services SET IsActive=1 WHERE ServiceId=1;");
            Reject<SQLiteException>(()=>service.CreateGuest("0901111111",sessionId,new[]{new OrderLineRequest{ServiceId=1,Quantity=1},new OrderLineRequest{ServiceId=2,Quantity=1}}));
            Assert(Count(db,"SELECT COUNT(*) FROM Orders")==logs,"Item failure committed partial order.");Sql(db,"DROP TRIGGER FailItem;");
            var pendingAudit=service.CreateGuest("0901111111",sessionId,Cart());
            Assert(pendingAudit.Items.Single().UnitPrice==99000 && pendingAudit.Items.Single().ServiceNameSnapshot=="Tên mới","New order did not use current server price/name.");
            Sql(db,"CREATE TRIGGER FailOrderAudit BEFORE INSERT ON AuditLog WHEN NEW.EntityName='Order' BEGIN SELECT RAISE(ABORT,'audit failure'); END;");
            logs=Count(db,"SELECT COUNT(*) FROM Orders");Reject<SQLiteException>(()=>service.CreateGuest("0901111111",sessionId,Cart()));
            Reject<SQLiteException>(()=>service.CancelGuest("0901111111",pendingAudit.OrderId));
            Assert(Count(db,"SELECT COUNT(*) FROM Orders")==logs && (string)Sql(db,"SELECT Status FROM Orders WHERE OrderId="+pendingAudit.OrderId)=="Pending","Audit failure did not rollback create/state.");Sql(db,"DROP TRIGGER FailOrderAudit;");
            Sql(db,"UPDATE Customers SET PhoneNumber='0909999999';");
            Reject<InvalidOperationException>(()=>service.ListGuest("0901111111",sessionId));Reject<InvalidOperationException>(()=>service.CancelGuest("0901111111",pendingAudit.OrderId));
            Assert(service.ListGuest("0909999999",sessionId).Count==logs,"Current phone could not view own orders.");Sql(db,"UPDATE Customers SET PhoneNumber='0901111111';");
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code IN ('Order.Confirm','Order.Cancel','Order.View');");
            for(var iteration=0;iteration<5;iteration++)
            {
                var pending=service.CreateGuest("0901111111",sessionId,Cart());var before=Count(db,"SELECT COUNT(*) FROM AuditLog WHERE EntityName='Order'");
                using(var gate=new ManualResetEventSlim(false))
                {
                    var a=Task.Run(()=>{gate.Wait();return Attempt(()=>service.ConfirmStaff(actor,pending.OrderId));});
                    var b=Task.Run(()=>{gate.Wait();return Attempt(()=>service.CancelGuest("0901111111",pending.OrderId));});gate.Set();Task.WaitAll(a,b);
                    Assert(a.Result!=b.Result && Count(db,"SELECT COUNT(*) FROM AuditLog WHERE EntityName='Order'")==before+1,"Confirm/cancel both committed.");
                }
            }
            var staffCancel=service.CreateGuest("0901111111",sessionId,Cart());clock.Value=Time(23);Assert(service.CancelStaff(actor,staffCancel.OrderId).Status=="Cancelled","Staff cancel after close failed.");
            clock.Value=Time(13);var stale=service.CreateGuest("0901111111",sessionId,Cart());
            Sql(db,"UPDATE RoomSessions SET Status='Completed',ActualEndTime='"+Utc(Time(13))+"';");
            Reject<InvalidOperationException>(()=>service.CreateGuest("0901111111",sessionId,Cart()));Reject<InvalidOperationException>(()=>service.CancelGuest("0901111111",stale.OrderId));
            Reject<InvalidOperationException>(()=>service.ConfirmStaff(actor,stale.OrderId));Reject<InvalidOperationException>(()=>service.CancelStaff(actor,stale.OrderId));
            Reject<InvalidOperationException>(()=>service.ListGuest("0901111111",sessionId));Assert(service.ListStaff(actor,sessionId).Count>0,"Staff View lost history.");
            Sql(db,"UPDATE RoomSessions SET Status='Active',ActualEndTime=NULL;");
            Reject<SQLiteException>(()=>Sql(db,"UPDATE OrderItems SET Quantity=1.5;"));Reject<SQLiteException>(()=>Sql(db,"UPDATE OrderItems SET Quantity=11;"));
            Reject<SQLiteException>(()=>Sql(db,"UPDATE OrderItems SET UnitPrice=0;"));Reject<SQLiteException>(()=>Sql(db,"UPDATE Orders SET RoomSessionId=99999;"));
            Reject<SQLiteException>(()=>Sql(db,"INSERT INTO OrderItems(OrderId,ServiceId,ServiceNameSnapshot,Quantity,UnitPrice) SELECT OrderId,ServiceId,ServiceNameSnapshot,Quantity,UnitPrice FROM OrderItems LIMIT 1;"));
            Reject<SQLiteException>(()=>Sql(db,"DELETE FROM Services WHERE ServiceId=1;"));
            var held=new GateClock();var waitingClock=new Clock{Value=Time(22,59)};
            var holding=Task.Run(()=>new NoShowService(db,held).ProcessExpired());Assert(held.Entered.Wait(10000),"Writer gate did not start.");
            using(var entered=new ManualResetEventSlim(false))
            {
                var waiting=Task.Run(()=>{entered.Set();try{new OrderService(db,waitingClock).CreateGuest("0901111111",sessionId,Cart());return false;}catch(ArgumentException){return true;}});
                try{Assert(entered.Wait(10000) && waitingClock.Reads==0,"Order clock read before writer.");waitingClock.Value=Time(23);}
                finally{held.Release.Set();}
                Task.WaitAll(holding,waiting);Assert(waiting.Result && waitingClock.Reads==1,"Order accepted pre-lock open time.");
            }
            held.Entered.Dispose();held.Release.Dispose();
            // Real writers: creating first keeps the snapshot even when the queued catalog edit stops selling.
            var catalog=new ServiceCatalogService(db);var original=catalog.ListForManagement(admin).First(i=>i.ServiceId==1);
            var creatingClock=new GateClock();var creating=Task.Run(()=>new OrderService(db,creatingClock).CreateGuest("0901111111",sessionId,Cart()));
            Assert(creatingClock.Entered.Wait(10000),"Order writer gate did not start.");
            var editing=Task.Run(()=>catalog.Save(admin,original,new ServiceEdit{Name="Ngừng bán sau đặt",Category=original.Category,Price=888000,IsActive=false}));
            creatingClock.Release.Set();Task.WaitAll(creating,editing);
            Assert(creating.Result.Items.Single().UnitPrice==original.Price && creating.Result.Items.Single().ServiceNameSnapshot==original.Name && creating.Result.Status=="Pending" && Count(db,"SELECT IsActive FROM Services WHERE ServiceId=1")==0,"Queued catalog writer altered or cancelled existing snapshot.");
            creatingClock.Entered.Dispose();creatingClock.Release.Dispose();
            Reject<InvalidOperationException>(()=>service.CreateGuest("0901111111",sessionId,Cart()));
            VerifyMenu(db,clock,actor,service,sessionId);
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Order.View');");Reject<UnauthorizedAccessException>(()=>service.ListStaff(actor,sessionId));
            auth.Logout(actor);Reject<UnauthorizedAccessException>(()=>service.ConfirmStaff(actor,stale.OrderId));
            Reject<UnauthorizedAccessException>(()=>service.ReadWorkspaceStaff(actor));
            var savedOrders=Count(db,"SELECT COUNT(*) FROM Orders");var savedItems=Count(db,"SELECT COUNT(*) FROM OrderItems");var savedAudits=Count(db,"SELECT COUNT(*) FROM AuditLog");
            db.Initialize();Assert(Count(db,"PRAGMA foreign_key_check")==0 && Count(db,"PRAGMA user_version")==7 && Count(db,"SELECT COUNT(*) FROM Orders")==savedOrders && Count(db,"SELECT COUNT(*) FROM OrderItems")==savedItems && Count(db,"SELECT COUNT(*) FROM AuditLog")==savedAudits,"Reopening lost orders/items/audits, or FK/schema broken.");
            Console.WriteLine("PASS Orders: v6-v7 preservation/rollback/idempotence, private current-phone Active routes, live independent permissions, Guest Pending/Staff Completed, merged 1-10/cart/server snapshots/inactive items, open-hour ticks/after-hours pending, terminal guards, FK/check/unique, item/audit rollback, confirm-cancel races and clock after writer on temporary SQLite.");
        }
        finally{SQLiteConnection.ClearAllPools();foreach(var suffix in new[]{"","-wal","-shm","-journal"})if(File.Exists(file+suffix))File.Delete(file+suffix);}
    }
}
