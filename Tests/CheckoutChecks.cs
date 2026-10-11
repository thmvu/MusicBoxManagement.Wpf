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

public static class MusicBoxCheckoutChecks
{
    private sealed class Clock : IClock
    { public DateTimeOffset Value; public int Reads; public DateTimeOffset UtcNow { get { Interlocked.Increment(ref Reads); return Value; } } }
    private sealed class GateClock : IClock, IDisposable
    {
        public DateTimeOffset Value;
        public readonly ManualResetEventSlim Entered=new ManualResetEventSlim(false), Release=new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow { get { Entered.Set(); if(!Release.Wait(10000))throw new Exception("Checkout clock gate timed out.");return Value; } }
        public void Dispose(){Entered.Dispose();Release.Dispose();}
    }
    private sealed class Fixture
    {
        public SqliteDatabase Db; public LoginSession Admin; public Clock Time; public int Next;
        public RoomSession New(bool booking)
        {
            Time.Value=At(13);Next++;
            var code="C"+Next;var phone="090"+Next.ToString("D7",CultureInfo.InvariantCulture);
            Sql(Db,"INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('"+code+"','Phòng thử',1,'test.png',1,'test');");
            var room=Convert.ToInt32(Sql(Db,"SELECT RoomId FROM Rooms WHERE RoomCode='"+code+"';"));
            var sessions=new RoomSessionService(Db,Time);
            if(!booking)return sessions.CreateWalkIn(Admin,new WalkInRequest{RoomId=room,FullName="Khách thử",PhoneNumber=phone}).Session;
            var source=new ReservationService(Db,Time).CreateGuest(new ReservationRequest{RoomId=room,FullName="Khách thử",PhoneNumber=phone,StartTime=Time.Value,DurationMinutes=60});
            return sessions.CheckIn(Admin,source.ReservationId);
        }
    }
    private static DateTimeOffset At(int hour){return new DateTimeOffset(2026,10,11,hour,0,0,TimeSpan.FromHours(7));}
    private static string Utc(DateTimeOffset value){return value.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);}
    private static object Sql(SqliteDatabase db,string sql){using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=sql;return cmd.ExecuteScalar();}}
    private static long Count(SqliteDatabase db,string sql){return Convert.ToInt64(Sql(db,sql));}
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static OrderLineRequest[] Cart(int quantity){return new[]{new OrderLineRequest{ServiceId=1,Quantity=quantity}};}
    private static string Phone(Fixture f,RoomSession session){return (string)Sql(f.Db,"SELECT PhoneNumber FROM Customers WHERE CustomerId="+session.CustomerId+";");}
    private static void Wait(Task task){if(!task.Wait(15000))throw new Exception("Checkout task timed out.");}

    public static void Run()
    {
        var directory=Path.Combine(Path.GetTempPath(),"MusicBoxCheckout_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var db=new SqliteDatabase(Path.Combine(directory,"checkout.db"));var auth=new AuthenticationService(db);const string password="Checkout-Test-2026!";
            var admin=auth.SetupAdminAsync("admin","Admin thử",password).GetAwaiter().GetResult();
            Sql(db,@"INSERT INTO Services(Name,Category,Price,IsActive) VALUES('Nước cam','Đồ uống',25000,1);
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'cashier','cashier','CASHIER',PasswordHash,'cashier','Thu ngân',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('cashier','Staff');DELETE FROM RolePermission WHERE RoleId='Staff';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.CheckOut';");
            var actor=auth.LoginAsync("cashier",password).GetAwaiter().GetResult();var clock=new Clock();var f=new Fixture{Db=db,Admin=admin,Time=clock};
            var checkout=new CheckoutService(db,clock);var orders=new OrderService(db,clock);var first=f.New(true);var phone=Phone(f,first);
            orders.CreateStaff(admin,first.RoomSessionId,Cart(2));var pending=orders.CreateGuest(phone,first.RoomSessionId,Cart(2));
            var cancelled=orders.CreateGuest(phone,first.RoomSessionId,Cart(1));orders.CancelGuest(phone,cancelled.OrderId);
            clock.Value=At(13).AddSeconds(90).AddTicks(1234);clock.Reads=0;var audit=Count(db,"SELECT COUNT(*) FROM AuditLog;");
            var preview=checkout.PreviewStaff(actor,first.RoomSessionId);
            Assert(preview.Bill.RoomCharge==3000 && preview.Bill.ServiceCharge==50000 && preview.Bill.PendingOrderCount==1 && preview.CompletedOrders.Single().Items.Single().UnitPrice==25000 && clock.Reads==1,"Preview needed unrelated rights or wrong bill/snapshot/clock.");
            Assert(Count(db,"SELECT COUNT(*) FROM Invoices;")==0 && Count(db,"SELECT COUNT(*) FROM AuditLog;")==audit && (string)Sql(db,"SELECT Status FROM Orders WHERE OrderId="+pending.OrderId)=="Pending" && Count(db,"SELECT COUNT(*) FROM RoomSessions WHERE ActualEndTime IS NOT NULL;")==0,"Preview wrote/cancelled/ended.");
            Reject<UnauthorizedAccessException>(()=>new BillingService(db,clock).ReadStaff(actor,first.RoomSessionId));
            Reject<UnauthorizedAccessException>(()=>orders.ListStaff(actor,first.RoomSessionId));
            Reject<UnauthorizedAccessException>(()=>checkout.ConfirmStaff(null,first.RoomSessionId,"Cash"));
            foreach(var method in new[]{null,"","cash","Card"," Cash"})Reject<ArgumentException>(()=>checkout.ConfirmStaff(actor,first.RoomSessionId,method));
            Assert(Count(db,"SELECT COUNT(*) FROM Invoices;")==0,"Invalid payment created invoice.");
            clock.Value=At(13).AddMinutes(2).AddTicks(1234);orders.ConfirmStaff(admin,pending.OrderId);
            Sql(db,"UPDATE RoomTypes SET PricePerHour=999000;UPDATE Services SET Name='Tên mới',Price=99000,IsActive=0;UPDATE AspNetUsers SET FullName='Thu ngân đổi tên' WHERE Id='cashier';");clock.Reads=0;
            var paid=checkout.ConfirmStaff(actor,first.RoomSessionId,"BankTransfer");
            Assert(paid.RoomCharge==4000 && paid.ServiceCharge==100000 && paid.TotalAmount==104000 && paid.PaidAt==clock.Value.ToUniversalTime() && paid.PaidAt.Offset==TimeSpan.Zero && paid.PaymentMethod=="BankTransfer" && paid.ProcessedByNameSnapshot=="Thu ngân đổi tên" && paid.ProcessedByUserId==actor.UserId && clock.Reads==1,"Confirm did not read fresh time/orders/snapshots/cashier.");
            Assert((string)Sql(db,"SELECT ActualEndTime FROM RoomSessions WHERE RoomSessionId="+first.RoomSessionId)==Utc(paid.PaidAt) && (string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+first.ReservationId)=="Completed" && (string)Sql(db,"SELECT Status FROM RoomSessions WHERE RoomSessionId="+first.RoomSessionId)=="Completed","Booked checkout did not finish source/session.");
            Assert(Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action IN('Session.CheckOut','Invoice.Create') AND CreatedAt='"+Utc(paid.PaidAt)+"';")==2,"Checkout audit used another clock.");
            var guestAfter=new GuestReservationService(db,clock).Lookup(phone);
            var history=new StaffSessionService(db,clock).Search(admin,phone,"Completed").Items.Single();
            Assert(guestAfter.ActiveSession==null && guestAfter.Items.Count==0 && history.Session.ActualEndTime==paid.PaidAt && history.Session.RoomSessionId==first.RoomSessionId,"Real checkout did not clear Guest Active/booking or show Completed history.");
            Reject<InvalidOperationException>(()=>new BillingService(db,clock).ReadGuest(phone,first.RoomSessionId));
            Sql(db,"UPDATE AspNetUsers SET FullName='Tên sau thanh toán' WHERE Id='cashier';UPDATE RoomTypes SET PricePerHour=120000;UPDATE Services SET Name='Nước cam',Price=25000,IsActive=1;");
            audit=Count(db,"SELECT COUNT(*) FROM AuditLog;");clock.Value=At(9);clock.Reads=0;
            var replay=checkout.ConfirmStaff(actor,first.RoomSessionId,"invalid-on-replay");
            Assert(replay.InvoiceId==paid.InvoiceId && replay.PaidAt==paid.PaidAt && replay.TotalAmount==paid.TotalAmount && replay.PaymentMethod=="BankTransfer" && replay.ProcessedByNameSnapshot==paid.ProcessedByNameSnapshot && Count(db,"SELECT COUNT(*) FROM AuditLog;")==audit && clock.Reads==0,"Replay changed frozen invoice/payment/audit or read clock.");
            Reject<InvalidOperationException>(()=>checkout.PreviewStaff(actor,first.RoomSessionId));
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code IN('Session.View','Order.Confirm','Invoice.View');");
            Reject<UnauthorizedAccessException>(()=>checkout.ConfirmStaff(actor,first.RoomSessionId,"Cash"));Reject<UnauthorizedAccessException>(()=>checkout.PreviewStaff(actor,first.RoomSessionId));
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.CheckOut';UPDATE AspNetUsers SET SecurityStamp='changed' WHERE Id='cashier';");Reject<UnauthorizedAccessException>(()=>checkout.ConfirmStaff(actor,first.RoomSessionId,"Cash"));
            Sql(db,"UPDATE AspNetUsers SET SecurityStamp='cashier' WHERE Id='cashier';");
            Reject<SQLiteException>(()=>Sql(db,"UPDATE Invoices SET TotalAmount='1';"));Reject<SQLiteException>(()=>Sql(db,"DELETE FROM Invoices;"));

            var walk=f.New(false);var pendingWalk=orders.CreateGuest(Phone(f,walk),walk.RoomSessionId,Cart(1));
            clock.Value=At(23).AddMinutes(10);var walkPaid=checkout.ConfirmStaff(actor,walk.RoomSessionId,"Cash");
            Assert(walkPaid.RoomCharge==1220000 && walkPaid.ServiceCharge==0 && walkPaid.PaymentMethod=="Cash" && (string)Sql(db,"SELECT Status FROM Orders WHERE OrderId="+pendingWalk.OrderId)=="Cancelled" && Count(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Order.Cancel' AND EntityId='"+pendingWalk.OrderId+"' AND CreatedAt='"+Utc(walkPaid.PaidAt)+"';")==1,"Walk-in capped at deadline/closing or charged Pending/missing cancel audit.");
            clock.Value=At(14);var roomId=walk.RoomId;
            var next=new RoomSessionService(db,clock).CreateWalkIn(admin,new WalkInRequest{RoomId=roomId,FullName="Lượt sau",PhoneNumber=Phone(f,walk)});
            Assert(next.Session.RoomSessionId!=walk.RoomSessionId,"Checkout did not release Room/Customer for real next walk-in.");
            checkout.ConfirmStaff(actor,next.Session.RoomSessionId,"Cash");

            Rollback(f,actor);
            var money=f.New(false);Sql(db,"UPDATE RoomSessions SET HourlyRate=9223372036854775807 WHERE RoomSessionId="+money.RoomSessionId+";UPDATE Services SET Price=9223372036854775807;");
            orders.CreateStaff(admin,money.RoomSessionId,Cart(10));clock.Value=At(13).AddHours(2);
            var huge=checkout.ConfirmStaff(actor,money.RoomSessionId,"Cash");
            Assert(huge.RoomCharge==2m*long.MaxValue && huge.ServiceCharge==10m*long.MaxValue && huge.TotalAmount==12m*long.MaxValue && (string)Sql(db,"SELECT typeof(TotalAmount) FROM Invoices WHERE InvoiceId="+huge.InvoiceId)=="text","Large whole-dong amounts lost precision or overflowed Int64 storage.");
            Assert(checkout.ConfirmStaff(actor,money.RoomSessionId,"Cash").TotalAmount==huge.TotalAmount,"Large invoice round trip changed amount.");Sql(db,"UPDATE Services SET Price=25000;");
            var zero=f.New(false);var zeroPaid=checkout.ConfirmStaff(actor,zero.RoomSessionId,"Cash");Assert(zeroPaid.TotalAmount==0,"Zero duration charged minimum.");

            SchemaChecks(f,paid);Concurrency(f,actor);ClockAfterWriter(f,actor);
            Sql(db,"UPDATE AspNetUsers SET IsActive=0 WHERE Id='cashier';");Reject<UnauthorizedAccessException>(()=>checkout.ConfirmStaff(actor,first.RoomSessionId,"Cash"));
            Sql(db,"UPDATE AspNetUsers SET IsActive=1 WHERE Id='cashier';");auth.Logout(actor);Reject<UnauthorizedAccessException>(()=>checkout.ConfirmStaff(actor,first.RoomSessionId,"Cash"));
            Migration(Path.Combine(directory,"migration.db"));
            Console.WriteLine("PASS Checkout: v7-v8 preservation/rollback, independent live CheckOut/read-only preview, fresh actual UTC ticks/Completed-only/decimal TEXT snapshots, Cash/BankTransfer/source/Pending/audit atomicity, immutable unique invoice/replay, rollback failures, next real walk-in, real checkout-order/extend/create races both orders and clock after writer on temporary SQLite.");
        }
        finally
        {
            SQLiteConnection.ClearAllPools();
            var target=Path.GetFullPath(directory);var temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!target.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(target).StartsWith("MusicBoxCheckout_",StringComparison.Ordinal))throw new Exception("Unsafe checkout test cleanup.");
            Directory.Delete(target,true);
        }
    }
    private static void Rollback(Fixture f,LoginSession actor)
    {
        var db=f.Db;var session=f.New(true);var orders=new OrderService(db,f.Time);var checkout=new CheckoutService(db,f.Time);
        var pending=orders.CreateGuest(Phone(f,session),session.RoomSessionId,Cart(1));f.Time.Value=At(13).AddMinutes(1);
        var audit=Count(db,"SELECT COUNT(*) FROM AuditLog;");
        foreach(var trigger in new[]{"CREATE TRIGGER fail_checkout BEFORE INSERT ON Invoices BEGIN SELECT RAISE(ABORT,'invoice failed');END;",
            "CREATE TRIGGER fail_checkout BEFORE INSERT ON AuditLog WHEN NEW.Action='Invoice.Create' BEGIN SELECT RAISE(ABORT,'audit failed');END;"})
        {
            Sql(db,trigger);Reject<SQLiteException>(()=>checkout.ConfirmStaff(actor,session.RoomSessionId,"Cash"));Sql(db,"DROP TRIGGER fail_checkout;");
            Assert(Count(db,"SELECT COUNT(*) FROM Invoices WHERE RoomSessionId="+session.RoomSessionId)==0 && (string)Sql(db,"SELECT Status FROM RoomSessions WHERE RoomSessionId="+session.RoomSessionId)=="Active" && Sql(db,"SELECT ActualEndTime FROM RoomSessions WHERE RoomSessionId="+session.RoomSessionId)==DBNull.Value && (string)Sql(db,"SELECT Status FROM Orders WHERE OrderId="+pending.OrderId)=="Pending" && (string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+session.ReservationId)=="CheckedIn" && Count(db,"SELECT COUNT(*) FROM AuditLog;")==audit,"Failed invoice/audit left partial session/source/order/invoice/log.");
        }
        Sql(db,"UPDATE Reservations SET Status='Cancelled' WHERE ReservationId="+session.ReservationId+";");Reject<InvalidOperationException>(()=>checkout.ConfirmStaff(actor,session.RoomSessionId,"Cash"));
        Assert((string)Sql(db,"SELECT Status FROM Orders WHERE OrderId="+pending.OrderId)=="Pending","Invalid source cancelled Pending.");Sql(db,"UPDATE Reservations SET Status='CheckedIn' WHERE ReservationId="+session.ReservationId+";");
        f.Time.Value=At(12);Reject<InvalidOperationException>(()=>checkout.ConfirmStaff(actor,session.RoomSessionId,"Cash"));f.Time.Value=At(13);
        Reject<InvalidOperationException>(()=>checkout.ConfirmStaff(actor,999999,"Cash"));
        var completed=f.New(false);Sql(db,"UPDATE RoomSessions SET Status='Completed',ActualEndTime=ActualStartTime WHERE RoomSessionId="+completed.RoomSessionId+";");Reject<InvalidOperationException>(()=>checkout.ConfirmStaff(actor,completed.RoomSessionId,"Cash"));
        Assert(Count(db,"SELECT COUNT(*) FROM Invoices WHERE RoomSessionId="+completed.RoomSessionId)==0,"Invented paid invoice for legacy Completed fixture.");
    }
    private static void SchemaChecks(Fixture f,Invoice paid)
    {
        var db=f.Db;var guard=f.New(false);
        Func<string,string,string,string,string> insert=(amount,method,user,paidAt)=>"INSERT INTO Invoices(RoomSessionId,InvoiceNumber,RoomCharge,ServiceCharge,TotalAmount,PaymentMethod,ProcessedByUserId,ProcessedByNameSnapshot,PaidAt) VALUES("+guard.RoomSessionId+",'guard','"+amount+"','0','0','"+method+"','"+user+"','Tên','"+paidAt+"');";
        foreach(var invalid in new[]{"-1","1.5","01","1e3","","79228162514264337593543950336"})Reject<SQLiteException>(()=>Sql(db,insert(invalid,"Cash",f.Admin.UserId,Utc(At(13)))));
        Reject<SQLiteException>(()=>Sql(db,insert("0","Card",f.Admin.UserId,Utc(At(13)))));
        Reject<SQLiteException>(()=>Sql(db,insert("0","Cash","missing",Utc(At(13)))));
        Reject<SQLiteException>(()=>Sql(db,insert("0","Cash",f.Admin.UserId,"2026-10-11")));
        Reject<SQLiteException>(()=>Sql(db,"INSERT INTO Invoices(RoomSessionId,InvoiceNumber,RoomCharge,ServiceCharge,TotalAmount,PaymentMethod,ProcessedByUserId,ProcessedByNameSnapshot,PaidAt) SELECT RoomSessionId,'duplicate','0','0','0','Cash',ProcessedByUserId,ProcessedByNameSnapshot,PaidAt FROM Invoices WHERE InvoiceId="+paid.InvoiceId+";"));
        Reject<SQLiteException>(()=>Sql(db,"INSERT INTO Invoices(RoomSessionId,InvoiceNumber,RoomCharge,ServiceCharge,TotalAmount,PaymentMethod,ProcessedByUserId,ProcessedByNameSnapshot,PaidAt) SELECT "+guard.RoomSessionId+",InvoiceNumber,'0','0','0','Cash',ProcessedByUserId,ProcessedByNameSnapshot,PaidAt FROM Invoices WHERE InvoiceId="+paid.InvoiceId+";"));
        Reject<SQLiteException>(()=>Sql(db,"INSERT INTO Invoices(RoomSessionId,InvoiceNumber,RoomCharge,ServiceCharge,TotalAmount,PaymentMethod,ProcessedByUserId,ProcessedByNameSnapshot,PaidAt) SELECT 999999,'missing-session','0','0','0','Cash',ProcessedByUserId,ProcessedByNameSnapshot,PaidAt FROM Invoices WHERE InvoiceId="+paid.InvoiceId+";"));
        Assert(Count(db,"SELECT COUNT(*) FROM Invoices WHERE RoomSessionId="+guard.RoomSessionId)==0,"Invalid invoice guard wrote data.");
    }
    private static void Migration(string file)
    {
        var db=new SqliteDatabase(file);var auth=new AuthenticationService(db);var admin=auth.SetupAdminAsync("admin","Admin nâng cấp","Migration-Test-2026!").GetAwaiter().GetResult();
        Sql(db,"INSERT INTO Services(Name,Category,Price,IsActive) VALUES('Món giữ lại','Đồ ăn',30000,1);");
        var f=new Fixture{Db=db,Admin=admin,Time=new Clock()};var session=f.New(true);var orders=new OrderService(db,f.Time);
        var pending=orders.CreateGuest(Phone(f,session),session.RoomSessionId,Cart(2));orders.CreateStaff(admin,session.RoomSessionId,Cart(1));
        Sql(db,"UPDATE RoomTypes SET PricePerHour=123000 WHERE RoomTypeId=1;DROP TABLE Invoices;PRAGMA user_version=7;CREATE TABLE Invoices(Fixture TEXT);");
        Reject<SQLiteException>(()=>db.Initialize());Assert(Count(db,"PRAGMA user_version;")==7 && Count(db,"SELECT PricePerHour FROM RoomTypes WHERE RoomTypeId=1;")==123000,"Failed migration changed schema/old data.");
        Sql(db,"DROP TABLE Invoices;");db.Initialize();db.Initialize();Assert(Count(db,"PRAGMA user_version;")==8 && Count(db,"SELECT PricePerHour FROM RoomTypes WHERE RoomTypeId=1;")==123000 && Count(db,"SELECT COUNT(*) FROM Invoices;")==0 && Sql(db,"PRAGMA foreign_key_check;")==null,"Migration lost v7 data/seeded invoices or FK invalid.");
        Assert(Count(db,"SELECT COUNT(*) FROM AspNetUsers;")==1 && Count(db,"SELECT COUNT(*) FROM Customers;")==1 && Count(db,"SELECT COUNT(*) FROM Reservations WHERE Status='CheckedIn';")==1 && Count(db,"SELECT COUNT(*) FROM RoomSessions WHERE Status='Active';")==1 && Count(db,"SELECT COUNT(*) FROM Orders;")==2 && Count(db,"SELECT SUM(Quantity) FROM OrderItems;")==3 && (string)Sql(db,"SELECT Status FROM Orders WHERE OrderId="+pending.OrderId)=="Pending" && Count(db,"SELECT HourlyRate FROM RoomSessions WHERE RoomSessionId="+session.RoomSessionId)==120000,"Migration changed accounts/customers/source/session/orders/snapshot.");
    }
    private static void Concurrency(Fixture f,LoginSession actor)
    {
        var db=f.Db;var simultaneous=f.New(false);f.Time.Value=At(13).AddMinutes(1);
        using(var barrier=new Barrier(2))
        {
            var service=new CheckoutService(db,f.Time);
            var one=Task.Run(()=>{barrier.SignalAndWait();return service.ConfirmStaff(actor,simultaneous.RoomSessionId,"Cash");});
            var two=Task.Run(()=>{barrier.SignalAndWait();return service.ConfirmStaff(actor,simultaneous.RoomSessionId,"BankTransfer");});
            Wait(Task.WhenAll(one,two));Assert(one.Result.InvoiceId==two.Result.InvoiceId && one.Result.PaymentMethod==two.Result.PaymentMethod && Count(db,"SELECT COUNT(*) FROM Invoices WHERE RoomSessionId="+simultaneous.RoomSessionId)==1,"Two checkout writers created different invoices.");
        }
        foreach(var operation in new[]{"Confirm","Cancel","Extend","CreateGuest","CreateStaff"})
        foreach(var checkoutFirst in new[]{true,false})
        {
            var session=f.New(true);var normalOrders=new OrderService(db,f.Time);var pending=normalOrders.CreateGuest(Phone(f,session),session.RoomSessionId,Cart(1));
            using(var gate=new GateClock{Value=At(13).AddMinutes(1)})
            {
                var gatedCheckout=new CheckoutService(db,gate);var normalCheckout=new CheckoutService(db,f.Time);var gatedOrders=new OrderService(db,gate);var gatedSessions=new RoomSessionService(db,gate);
                Action other=()=>{
                    if(operation=="Confirm")gatedOrders.ConfirmStaff(f.Admin,pending.OrderId);
                    else if(operation=="Cancel")gatedOrders.CancelGuest(Phone(f,session),pending.OrderId);
                    else if(operation=="Extend")gatedSessions.ExtendStaff(f.Admin,session.RoomSessionId,session.ExpectedEndTime.Value,30);
                    else if(operation=="CreateGuest")gatedOrders.CreateGuest(Phone(f,session),session.RoomSessionId,Cart(1));
                    else gatedOrders.CreateStaff(f.Admin,session.RoomSessionId,Cart(1));
                };
                Task first=null,second=null;Invoice invoice=null;
                try
                {
                    if(checkoutFirst)
                    {
                        first=Task.Run(()=>{invoice=gatedCheckout.ConfirmStaff(actor,session.RoomSessionId,"Cash");});
                        Assert(gate.Entered.Wait(5000),"Checkout did not own writer before competitor.");
                        second=Task.Run(()=>Reject<InvalidOperationException>(other));
                    }
                    else
                    {
                        first=Task.Run(other);Assert(gate.Entered.Wait(5000),"Order/extension did not own writer first.");
                        f.Time.Value=At(13).AddMinutes(2);second=Task.Run(()=>{invoice=normalCheckout.ConfirmStaff(actor,session.RoomSessionId,"Cash");});
                    }
                    Thread.Sleep(100);Assert(!second.IsCompleted,"Competing writer did not wait for business transaction.");gate.Release.Set();Wait(Task.WhenAll(first,second));
                    var serviceCharge=!checkoutFirst&&(operation=="Confirm"||operation=="CreateStaff")?25000m:0m;
                    Assert(invoice.ServiceCharge==serviceCharge && invoice.RoomCharge==(checkoutFirst?2000m:4000m) && Count(db,"SELECT COUNT(*) FROM Orders WHERE RoomSessionId="+session.RoomSessionId+" AND Status='Pending';")==0,"Checkout/order race charged stale data or left Pending.");
                    if(operation=="Extend")Assert((string)Sql(db,"SELECT ExpectedEndTime FROM RoomSessions WHERE RoomSessionId="+session.RoomSessionId)==Utc(checkoutFirst?session.ExpectedEndTime.Value:session.ExpectedEndTime.Value.AddMinutes(30)),"Extension/checkout race changed expected time after completion.");
                }
                finally{gate.Release.Set();if(first!=null)Wait(first);if(second!=null)Wait(second);}
            }
        }
    }
    private static void ClockAfterWriter(Fixture f,LoginSession actor)
    {
        var session=f.New(false);var orders=new OrderService(f.Db,f.Time);var pending=orders.CreateGuest(Phone(f,session),session.RoomSessionId,Cart(1));
        using(var connection=f.Db.OpenConnection())using(var transaction=SqliteDatabase.BeginWriteTransaction(connection))
        {
            f.Time.Reads=0;var started=new ManualResetEventSlim(false);var task=Task.Run(()=>{started.Set();return new CheckoutService(f.Db,f.Time).ConfirmStaff(actor,session.RoomSessionId,"Cash");});
            Assert(started.Wait(5000),"Checkout task not scheduled.");Thread.Sleep(100);Assert(!task.IsCompleted&&f.Time.Reads==0,"Checkout read clock before acquiring writer.");
            using(var command=connection.CreateCommand()){command.Transaction=transaction;command.CommandText="UPDATE Orders SET Status='Completed' WHERE OrderId="+pending.OrderId+";";command.ExecuteNonQuery();}
            f.Time.Value=At(13).AddMinutes(5).AddSeconds(12).AddTicks(3456);transaction.Commit();Wait(task);started.Dispose();
            Assert(task.Result.PaidAt==f.Time.Value && task.Result.ServiceCharge==25000 && f.Time.Reads==1,"Checkout trusted stale orders/clock before writer lock.");
        }
    }
}
