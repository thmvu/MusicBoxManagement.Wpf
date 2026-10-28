using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MySqlConnector;

public static class MusicBoxMySqlCheckoutChecks
{
    private const string Password="Checkout-MySql-2026!";
    private sealed class Clock : IClock
    {public DateTimeOffset Value;public int Reads;public DateTimeOffset UtcNow{get{Interlocked.Increment(ref Reads);return Value;}}}
    private sealed class GateClock : IClock,IDisposable
    {
        public DateTimeOffset Value;
        public readonly ManualResetEventSlim Entered=new ManualResetEventSlim(false),Release=new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow{get{Entered.Set();if(!Release.Wait(12000))throw new Exception("Checkout gate timeout");return Value;}}
        public void Dispose(){Release.Set();Entered.Dispose();Release.Dispose();}
    }
    private sealed class Fixture
    {
        public MySqlDatabase Db;public LoginSession Admin,Actor,Viewer;public Clock Time;private int next;
        private readonly Dictionary<int,string> phones=new Dictionary<int,string>();
        public RoomSession New(bool booking=false,DateTimeOffset? actual=null)
        {
            next++;var phone="090"+next.ToString("D7",CultureInfo.InvariantCulture);var code="C"+next;
            Time.Value=actual??At(13);
            var room=Convert.ToInt32(Sql(Db,"INSERT INTO Rooms(RoomCode,RoomTypeId,Name,ImageUrl,IsActive,CreatedAt) VALUES('"+code+"',1,'Phòng test','test.png',1,'"+Utc(Time.Value)+"');SELECT LAST_INSERT_ID();"));
            var core=new MySqlRoomSessionService(Db,Time);RoomSession result;
            if(!booking)result=core.CreateWalkIn(Admin,new WalkInRequest{RoomId=room,FullName="Khách test",PhoneNumber=phone}).Session;
            else
            {
                var local=Time.Value.ToOffset(TimeSpan.FromHours(7));
                var start=new DateTimeOffset(local.Year,local.Month,local.Day,local.Hour,local.Minute/30*30,0,TimeSpan.FromHours(7));
                var reservation=new MySqlReservationService(Db,new Clock{Value=At(9)}).CreateGuest(new ReservationRequest{RoomId=room,FullName="Khách test",PhoneNumber=phone,StartTime=start,DurationMinutes=60});
                result=core.CheckIn(Admin,reservation.ReservationId);
            }
            phones.Add(result.RoomSessionId,phone);return result;
        }
        public string Phone(RoomSession s){return phones[s.RoomSessionId];}
        public MySqlCheckoutService Checkout(){return new MySqlCheckoutService(Db,Time);}
        public MySqlOrderService Orders(){return new MySqlOrderService(Db,Time);}
    }
    private static DateTimeOffset At(int h,int m=0){return new DateTimeOffset(2026,10,28,h,m,0,TimeSpan.FromHours(7));}
    private static string Utc(DateTimeOffset t){return t.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);}
    private static object Sql(MySqlDatabase db,string sql){using(var c=db.OpenConnection())using(var q=c.CreateCommand()){q.CommandText=sql;return q.ExecuteScalar();}}
    private static long Count(MySqlDatabase db,string sql){return Convert.ToInt64(Sql(db,sql));}
    private static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
    private static T Reject<T>(Action action)where T:Exception{try{action();}catch(T e){return e;}throw new Exception("Expected "+typeof(T).Name);}
    private static OrderLineRequest[] Cart(int quantity=1){return new[]{new OrderLineRequest{ServiceId=1,Quantity=quantity}};}
    private static string Status(Fixture f,string table,string key,int id){return (string)Sql(f.Db,"SELECT Status FROM "+table+" WHERE "+key+"="+id);}
    private static long Invoices(Fixture f,RoomSession s){return Count(f.Db,"SELECT COUNT(*) FROM Invoices WHERE RoomSessionId="+s.RoomSessionId);}
    private static bool Attempt(Action action){try{action();return true;}catch(InvalidOperationException){return false;}}

    public static void Run()
    {
        var name="musicbox_test_"+Guid.NewGuid().ToString("N");var db=new MySqlDatabase(name);
        Console.WriteLine("Checkout test database: "+name);
        try
        {
            var auth=new MySqlAuthenticationService(db);var admin=auth.SetupAdminAsync("admin","Admin test",Password).GetAwaiter().GetResult();
            Sql(db,@"INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'checkout','checkout','CHECKOUT',PasswordHash,'checkout','Thu ngân test',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('checkout','Staff');
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
SELECT 'viewer','viewer','VIEWER',PasswordHash,'viewer','Chỉ xem',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('viewer','Manager');DELETE FROM RolePermission WHERE RoleId IN ('Staff','Manager');
INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.CheckOut';
INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code IN ('Session.View','Order.View');");
            new MySqlServiceCatalogService(db).Save(admin,null,new ServiceEdit{Name="Trà",Category="Đồ uống",Price=25000,IsActive=true});
            var f=new Fixture{Db=db,Admin=admin,Actor=auth.LoginAsync("checkout",Password).GetAwaiter().GetResult(),Viewer=auth.LoginAsync("viewer",Password).GetAwaiter().GetResult(),Time=new Clock()};
            PaymentAndReplay(f);
            Rollback(f);
            Console.WriteLine("PASS Checkout current CheckOut-only/preview/fresh amount/snapshots/decimal/replay/rollback");
            Duplicate(f);
            OrderedRaces(f);
            Console.WriteLine("PASS Checkout duplicate requests and 14 ordered races with real Order/Staff-Guest extension writers");
            ReadSnapshots(f);
            FreshClockAndLogout(f,auth);
            Assert(Count(db,"SELECT COUNT(*) FROM RoomSessions s LEFT JOIN Invoices i ON i.RoomSessionId=s.RoomSessionId WHERE s.Status='Completed' AND i.InvoiceId IS NULL;")==0,"Completed session without real checkout invoice");
            Assert(Count(db,"SELECT COUNT(*) FROM Orders o JOIN RoomSessions s ON s.RoomSessionId=o.RoomSessionId WHERE s.Status='Completed' AND o.Status='Pending';")==0,"Completed session retains Pending");
            Assert(Count(db,"SELECT COUNT(*) FROM Reservations r JOIN RoomSessions s ON s.ReservationId=r.ReservationId WHERE s.Status='Completed' AND r.Status<>'Completed';")==0,"Source did not complete");
            Assert(Count(db,"SELECT Version FROM _MusicBoxSchemaVersion;")==MySqlDatabase.CurrentSchemaVersion,"Schema changed");
            Console.WriteLine("PASS MySQL Checkout/Invoice: live independent CheckOut, RR read-only selector/preview, fresh actual ticks/decimal/snapshots, atomic Pending/source/session/invoice/audit, rollback and idempotent replay, unique immutable invoices, 14 real ordered writer races, MVCC preview, clock/actor after Room waits; GUID DB only, no provider/import/history-print claims.");
        }
        finally
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"\Amusicbox_test_[0-9a-f]{32}\z"))throw new Exception("Unsafe cleanup");
            Sql(new MySqlDatabase("defaultdb"),"DROP DATABASE IF EXISTS `"+name+"`");
        }
    }
    private static void PaymentAndReplay(Fixture f)
    {
        var s=f.New(true,At(13).AddSeconds(12).AddTicks(1234567));var service=f.Checkout();var orders=f.Orders();var phone=f.Phone(s);
        orders.CreateStaff(f.Admin,s.RoomSessionId,Cart(2));var serve=orders.CreateGuest(phone,s.RoomSessionId,Cart(3));var pending=orders.CreateGuest(phone,s.RoomSessionId,Cart());var cancelled=orders.CreateGuest(phone,s.RoomSessionId,Cart(4));orders.CancelGuest(phone,cancelled.OrderId);
        Assert(service.ListActiveStaff(f.Actor).Any(i=>i.SessionId==s.RoomSessionId),"CheckOut-only selector denied");
        foreach(var property in new[]{"CustomerId","RoomId","FullName","PhoneNumber","ReservationId"})Assert(typeof(CheckoutSessionChoice).GetProperty(property)==null,"Selector PII leak: "+property);
        Reject<UnauthorizedAccessException>(()=>service.ListActiveStaff(null));Reject<UnauthorizedAccessException>(()=>service.PreviewStaff(f.Viewer,s.RoomSessionId));Reject<UnauthorizedAccessException>(()=>service.ConfirmStaff(f.Viewer,s.RoomSessionId,"Cash"));
        Reject<UnauthorizedAccessException>(()=>new MySqlBillingService(f.Db,f.Time).ReadStaff(f.Actor,s.RoomSessionId));Reject<UnauthorizedAccessException>(()=>orders.ListStaff(f.Actor,s.RoomSessionId));
        var audits=Count(f.Db,"SELECT COUNT(*) FROM AuditLog;");f.Time.Value=s.ActualStartTime.AddMinutes(1);f.Time.Reads=0;var preview=service.PreviewStaff(f.Actor,s.RoomSessionId);
        Assert(f.Time.Reads==1&&preview.Bill.RoomCharge==2000m&&preview.Bill.ServiceCharge==50000m&&preview.Bill.PendingOrderCount==2&&preview.CompletedOrders.Count==1&&preview.Bill.TotalAmount==52000m,"Preview/Completed list/clock mismatch");
        Assert(Invoices(f,s)==0&&Sql(f.Db,"SELECT ActualEndTime FROM RoomSessions WHERE RoomSessionId="+s.RoomSessionId)==DBNull.Value&&Status(f,"Orders","OrderId",pending.OrderId)=="Pending"&&Count(f.Db,"SELECT COUNT(*) FROM AuditLog;")==audits,"Preview wrote data");
        foreach(var method in new[]{null,"","Card","cash"}){f.Time.Reads=0;Reject<ArgumentException>(()=>service.ConfirmStaff(f.Actor,s.RoomSessionId,method));Assert(f.Time.Reads==0&&Invoices(f,s)==0,"Invalid method charged/wrote");}
        f.Time.Value=s.ActualStartTime.AddSeconds(90);orders.ConfirmStaff(f.Admin,serve.OrderId);
        Sql(f.Db,"UPDATE RoomTypes SET Name='Loại đổi',PricePerHour=999000 WHERE RoomTypeId=1;UPDATE Services SET Name='Món đổi',Price=99000,IsActive=0 WHERE ServiceId=1;");
        f.Time.Value=s.ActualStartTime.AddMinutes(2).ToOffset(TimeSpan.FromHours(3));f.Time.Reads=0;var invoice=service.ConfirmStaff(f.Actor,s.RoomSessionId,"BankTransfer");
        Assert(f.Time.Reads==1&&invoice.RoomCharge==4000m&&invoice.ServiceCharge==125000m&&invoice.TotalAmount==129000m&&invoice.PaymentMethod=="BankTransfer"&&invoice.ProcessedByUserId==f.Actor.UserId&&invoice.ProcessedByNameSnapshot=="Thu ngân test"&&invoice.PaidAt==f.Time.Value&&invoice.PaidAt.Offset==TimeSpan.Zero,"Confirm trusted preview/catalog or lost decimal/ticks/cashier");
        Assert(Status(f,"RoomSessions","RoomSessionId",s.RoomSessionId)=="Completed"&&Status(f,"Reservations","ReservationId",s.ReservationId.Value)=="Completed"&&Status(f,"Orders","OrderId",pending.OrderId)=="Cancelled","Checkout did not atomically finish/cancel");
        Assert((string)Sql(f.Db,"SELECT ActualEndTime FROM RoomSessions WHERE RoomSessionId="+s.RoomSessionId)==Utc(invoice.PaidAt)&&(string)Sql(f.Db,"SELECT ExpectedEndTime FROM RoomSessions WHERE RoomSessionId="+s.RoomSessionId)==Utc(s.ExpectedEndTime.Value),"PaidAt/actual/expected changed");
        Assert((string)Sql(f.Db,"SELECT EndTime FROM Reservations WHERE ReservationId="+s.ReservationId.Value)==Utc(At(14))&&Count(f.Db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Order.Cancel' AND EntityId='"+pending.OrderId+"' AND ActorType='Staff' AND UserId='checkout' AND CreatedAt='"+Utc(invoice.PaidAt)+"';")==1,"Source interval/cancel audit wrong");
        Assert(new MySqlGuestReservationService(f.Db,f.Time).Lookup(phone).ActiveSession==null,"Guest retains completed Active");Reject<InvalidOperationException>(()=>service.PreviewStaff(f.Actor,s.RoomSessionId));
        Reject<InvalidOperationException>(()=>new MySqlBillingService(f.Db,f.Time).ReadGuest(phone,s.RoomSessionId));Reject<InvalidOperationException>(()=>orders.CreateGuest(phone,s.RoomSessionId,Cart()));
        audits=Count(f.Db,"SELECT COUNT(*) FROM AuditLog;");Sql(f.Db,"UPDATE AspNetUsers SET FullName='Tên thu ngân đổi' WHERE Id='checkout';");f.Time.Value=At(15).AddDays(1);f.Time.Reads=0;
        var replay=service.ConfirmStaff(f.Actor,s.RoomSessionId,"not-a-method");Assert(replay.InvoiceId==invoice.InvoiceId&&replay.PaymentMethod==invoice.PaymentMethod&&replay.TotalAmount==invoice.TotalAmount&&replay.ProcessedByNameSnapshot=="Thu ngân test"&&f.Time.Reads==0&&Count(f.Db,"SELECT COUNT(*) FROM AuditLog;")==audits&&Invoices(f,s)==1,"Replay changed invoice/clock/audit");
        Reject<UnauthorizedAccessException>(()=>service.ConfirmStaff(f.Viewer,s.RoomSessionId,"Cash"));
        Reject<MySqlException>(()=>Sql(f.Db,"UPDATE Invoices SET TotalAmount=0 WHERE InvoiceId="+invoice.InvoiceId));Reject<MySqlException>(()=>Sql(f.Db,"DELETE FROM Invoices WHERE InvoiceId="+invoice.InvoiceId));
        Sql(f.Db,"UPDATE AspNetUsers SET FullName='Thu ngân test' WHERE Id='checkout';UPDATE RoomTypes SET Name='Standard',PricePerHour=120000 WHERE RoomTypeId=1;UPDATE Services SET Name='Trà',Price=25000,IsActive=1 WHERE ServiceId=1;");
        f.Time.Value=invoice.PaidAt;var next=new MySqlRoomSessionService(f.Db,f.Time).CreateWalkIn(f.Admin,new WalkInRequest{RoomId=s.RoomId,FullName="Khách tiếp",PhoneNumber=phone}).Session;
        Assert(next.RoomSessionId!=s.RoomSessionId&&next.CustomerId==s.CustomerId&&!next.ReservationId.HasValue,"Checkout did not release room/customer");service.ConfirmStaff(f.Actor,next.RoomSessionId,"Cash");
        var large=f.New();Sql(f.Db,"UPDATE Services SET Price=9223372036854775807 WHERE ServiceId=1;");orders.CreateStaff(f.Admin,large.RoomSessionId,Cart(10));f.Time.Value=At(13).AddMinutes(1);
        var big=service.ConfirmStaff(f.Actor,large.RoomSessionId,"Cash");Assert(big.ServiceCharge==(decimal)long.MaxValue*10&&big.TotalAmount==big.ServiceCharge+2000m&&big.TotalAmount>long.MaxValue,"DECIMAL invoice lost large money precision");Sql(f.Db,"UPDATE Services SET Price=25000 WHERE ServiceId=1;");
        var overnight=f.New(false,At(22,59));f.Time.Value=At(0).AddDays(1);var midnight=service.ConfirmStaff(f.Actor,overnight.RoomSessionId,"Cash");Assert(midnight.InvoiceNumber.StartsWith("MB-20261029-",StringComparison.Ordinal)&&midnight.PaidAt==f.Time.Value&&midnight.RoomCharge==122000m,"After-hours checkout/date/clock capped bill");
        var zero=f.New();f.Time.Value=zero.ActualStartTime.AddTicks(-1);Reject<InvalidOperationException>(()=>service.ConfirmStaff(f.Actor,zero.RoomSessionId,"Cash"));Assert(Invoices(f,zero)==0&&Status(f,"RoomSessions","RoomSessionId",zero.RoomSessionId)=="Active","Negative clock changed session");
        f.Time.Value=zero.ActualStartTime;Assert(service.ConfirmStaff(f.Actor,zero.RoomSessionId,"Cash").TotalAmount==0m,"Checkout added minimum charge");
    }
    private static void Rollback(Fixture f)
    {
        var s=f.New(true);var pending=f.Orders().CreateGuest(f.Phone(s),s.RoomSessionId,Cart());f.Time.Value=At(13).AddMinutes(1);var before=Count(f.Db,"SELECT COUNT(*) FROM AuditLog;");
        var failures=new[]{
            "CREATE TRIGGER FailCheckout BEFORE UPDATE ON Reservations FOR EACH ROW BEGIN IF NEW.Status='Completed' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='source failure'; END IF; END;",
            "CREATE TRIGGER FailCheckout BEFORE UPDATE ON RoomSessions FOR EACH ROW BEGIN IF NEW.Status='Completed' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='session failure'; END IF; END;",
            "CREATE TRIGGER FailCheckout BEFORE INSERT ON Invoices FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='invoice failure';"};
        var auditFailures=new[]{"Order.Cancel","Session.CheckOut","Invoice.Create"}.Select(action=>"CREATE TRIGGER FailCheckout BEFORE INSERT ON AuditLog FOR EACH ROW BEGIN IF NEW.Action='"+action+"' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='audit failure'; END IF; END;");
        foreach(var trigger in failures.Concat(auditFailures))
        {
            Sql(f.Db,trigger);try{Reject<MySqlException>(()=>f.Checkout().ConfirmStaff(f.Actor,s.RoomSessionId,"Cash"));}finally{Sql(f.Db,"DROP TRIGGER FailCheckout;");}
            Assert(Invoices(f,s)==0&&Status(f,"RoomSessions","RoomSessionId",s.RoomSessionId)=="Active"&&Status(f,"Reservations","ReservationId",s.ReservationId.Value)=="CheckedIn"&&Status(f,"Orders","OrderId",pending.OrderId)=="Pending"&&Sql(f.Db,"SELECT ActualEndTime FROM RoomSessions WHERE RoomSessionId="+s.RoomSessionId)==DBNull.Value&&Count(f.Db,"SELECT COUNT(*) FROM AuditLog;")==before,"Failure left partial checkout state/audit");
        }
        var invoice=f.Checkout().ConfirmStaff(f.Actor,s.RoomSessionId,"Cash");Assert(Invoices(f,s)==1&&invoice.TotalAmount==2000m,"Rollback retry double charged or counted Pending");
    }
    private static void Duplicate(Fixture f)
    {
        var s=f.New();var oneClock=new Clock{Value=At(13).AddMinutes(1)};var twoClock=new Clock{Value=At(13).AddMinutes(2)};
        using(var gate=new ManualResetEventSlim(false))
        {
            var one=Task.Run(()=>{gate.Wait();return new MySqlCheckoutService(f.Db,oneClock).ConfirmStaff(f.Actor,s.RoomSessionId,"Cash");});
            var two=Task.Run(()=>{gate.Wait();return new MySqlCheckoutService(f.Db,twoClock).ConfirmStaff(f.Actor,s.RoomSessionId,"BankTransfer");});gate.Set();Task.WaitAll(one,two);
            Assert(one.Result.InvoiceId==two.Result.InvoiceId&&one.Result.PaymentMethod==two.Result.PaymentMethod&&one.Result.TotalAmount==two.Result.TotalAmount&&Invoices(f,s)==1&&oneClock.Reads+twoClock.Reads==1,"Duplicate checkouts created different invoices/took replay clock");
        }
    }
    private static void OrderedRaces(Fixture f)
    {
        foreach(var action in new[]{"CreateGuest","CreateStaff","Confirm","CancelGuest","CancelStaff","ExtendStaff","ExtendGuest"})
        foreach(var checkoutFirst in new[]{true,false})
        {
            var extending=action.StartsWith("Extend",StringComparison.Ordinal);var s=f.New(extending);var phone=f.Phone(s);ServiceOrder pending=null;
            if(action=="Confirm"||action.StartsWith("Cancel",StringComparison.Ordinal))pending=f.Orders().CreateGuest(phone,s.RoomSessionId,Cart());
            Func<IClock,object> operation=clock=>
            {
                var orders=new MySqlOrderService(f.Db,clock);
                switch(action)
                {
                    case "CreateGuest":return orders.CreateGuest(phone,s.RoomSessionId,Cart());
                    case "CreateStaff":return orders.CreateStaff(f.Admin,s.RoomSessionId,Cart());
                    case "Confirm":return orders.ConfirmStaff(f.Admin,pending.OrderId);
                    case "CancelGuest":return orders.CancelGuest(phone,pending.OrderId);
                    case "CancelStaff":return orders.CancelStaff(f.Admin,pending.OrderId);
                    case "ExtendStaff":return new MySqlRoomSessionService(f.Db,clock).ExtendStaff(f.Admin,s.RoomSessionId,s.ExpectedEndTime.Value,30);
                    default:return new MySqlGuestSessionService(f.Db,clock).Extend(phone,s.RoomSessionId,s.ExpectedEndTime.Value,30);
                }
            };
            using(var gate=new GateClock{Value=At(13).AddMinutes(1)})
            {
                Task<object> first=null;Task<bool> second=null;Invoice invoice=null;var normalClock=new Clock{Value=At(13).AddMinutes(2)};
                try
                {
                    if(checkoutFirst)
                    {
                        first=Task.Run<object>(()=>{invoice=new MySqlCheckoutService(f.Db,gate).ConfirmStaff(f.Actor,s.RoomSessionId,"Cash");return invoice;});
                        Assert(gate.Entered.Wait(12000),"Checkout first writer never got locks");second=Task.Run(()=>Attempt(()=>operation(normalClock)));
                    }
                    else
                    {
                        first=Task.Run(()=>operation(gate));Assert(gate.Entered.Wait(12000),"Order/extension first writer never got locks");
                        second=Task.Run(()=>{invoice=new MySqlCheckoutService(f.Db,normalClock).ConfirmStaff(f.Actor,s.RoomSessionId,"Cash");return true;});
                    }
                    Thread.Sleep(150);Assert(!second.IsCompleted,"Competing writer bypassed resource lock: "+action);gate.Release.Set();Task.WaitAll(first,second);
                    Assert(second.Result==!checkoutFirst,"Unexpected ordered race outcome: "+action);
                    var charge=!checkoutFirst&&(action=="Confirm"||action=="CreateStaff")?25000m:0m;
                    Assert(invoice.ServiceCharge==charge&&invoice.RoomCharge==(checkoutFirst?2000m:4000m)&&Invoices(f,s)==1&&Count(f.Db,"SELECT COUNT(*) FROM Orders WHERE RoomSessionId="+s.RoomSessionId+" AND Status='Pending';")==0,"Race charged stale money or left Pending: "+action);
                    if(extending)Assert((string)Sql(f.Db,"SELECT ExpectedEndTime FROM RoomSessions WHERE RoomSessionId="+s.RoomSessionId)==Utc(checkoutFirst?s.ExpectedEndTime.Value:s.ExpectedEndTime.Value.AddMinutes(30)),"Extension changed after Completed");
                }
                finally{gate.Release.Set();if(first!=null)first.GetAwaiter().GetResult();if(second!=null)second.GetAwaiter().GetResult();}
            }
        }
    }
    private static void ReadSnapshots(Fixture f)
    {
        var s=f.New();var pending=f.Orders().CreateGuest(f.Phone(s),s.RoomSessionId,Cart());
        using(var gate=new GateClock{Value=At(13).AddMinutes(1)})
        {
            var reader=Task.Run(()=>new MySqlCheckoutService(f.Db,gate).PreviewStaff(f.Actor,s.RoomSessionId));
            try
            {
                Assert(gate.Entered.Wait(12000),"Preview not ready");var writer=Task.Run(()=>f.Orders().ConfirmStaff(f.Admin,pending.OrderId));Assert(writer.Wait(8000),"RR preview blocked real Order writer");
                gate.Release.Set();var old=reader.GetAwaiter().GetResult();var fresh=f.Checkout().PreviewStaff(f.Actor,s.RoomSessionId);
                Assert(old.Bill.ServiceCharge==0&&old.CompletedOrders.Count==0&&old.Bill.PendingOrderCount==1&&fresh.Bill.ServiceCharge==25000m&&fresh.CompletedOrders.Count==1,"Preview mixed bill/details across snapshots");
            }
            finally{gate.Release.Set();reader.GetAwaiter().GetResult();}
        }
        using(var gate=new GateClock{Value=At(13).AddMinutes(1)})
        {
            var reader=Task.Run(()=>new MySqlCheckoutService(f.Db,gate).PreviewStaff(f.Actor,s.RoomSessionId));
            try
            {
                Assert(gate.Entered.Wait(12000),"Preview/checkout gate not ready");f.Time.Value=At(13).AddMinutes(2);
                var writer=Task.Run(()=>f.Checkout().ConfirmStaff(f.Actor,s.RoomSessionId,"Cash"));Assert(writer.Wait(8000),"RR preview blocked checkout writer");gate.Release.Set();
                Assert(reader.GetAwaiter().GetResult().Bill.RoomCharge==2000m&&writer.Result.RoomCharge==4000m,"Preview/confirm did not keep distinct clocks");Reject<InvalidOperationException>(()=>f.Checkout().PreviewStaff(f.Actor,s.RoomSessionId));
            }
            finally{gate.Release.Set();reader.GetAwaiter().GetResult();}
        }
    }
    private static void FreshClockAndLogout(Fixture f,MySqlAuthenticationService auth)
    {
        var s=f.New();var pending=f.Orders().CreateGuest(f.Phone(s),s.RoomSessionId,Cart());
        using(var gate=new GateClock{Value=At(13).AddMinutes(1)})
        {
            var first=Task.Run(()=>new MySqlOrderService(f.Db,gate).ConfirmStaff(f.Admin,pending.OrderId));Task<Invoice> waiting=null;
            try
            {
                Assert(gate.Entered.Wait(12000),"Order wait-clock gate not ready");f.Time.Value=At(13).AddMinutes(1);f.Time.Reads=0;
                waiting=Task.Run(()=>f.Checkout().ConfirmStaff(f.Actor,s.RoomSessionId,"BankTransfer"));Thread.Sleep(150);Assert(!waiting.IsCompleted&&f.Time.Reads==0,"Checkout clock before Room lock");
                f.Time.Value=At(13).AddMinutes(3).AddSeconds(12).AddTicks(7654321);gate.Release.Set();Task.WaitAll(first,waiting);
                Assert(waiting.Result.PaidAt==f.Time.Value&&waiting.Result.RoomCharge==6426m&&waiting.Result.ServiceCharge==25000m&&f.Time.Reads==1,"Checkout trusted pre-lock time/order data");
            }
            finally{gate.Release.Set();first.GetAwaiter().GetResult();if(waiting!=null)waiting.GetAwaiter().GetResult();}
        }
        var blocked=f.New();var audit=Count(f.Db,"SELECT COUNT(*) FROM AuditLog;");Task<bool> next=null;
        using(var c=f.Db.OpenConnection())using(var tx=MySqlDatabase.BeginWriteTransaction(c))using(var q=c.CreateCommand())
        {
            q.Transaction=tx;q.CommandText="SELECT RoomId FROM Rooms WHERE RoomId="+blocked.RoomId+" FOR UPDATE;";q.ExecuteScalar();
            f.Time.Reads=0;next=Task.Run(()=>{try{f.Checkout().ConfirmStaff(f.Actor,blocked.RoomSessionId,"Cash");return false;}catch(UnauthorizedAccessException){return true;}});
            Thread.Sleep(150);Assert(!next.IsCompleted&&f.Time.Reads==0,"Logout test did not wait for Room");auth.Logout(f.Actor);tx.Commit();
        }
        Assert(next.GetAwaiter().GetResult()&&Invoices(f,blocked)==0&&Status(f,"RoomSessions","RoomSessionId",blocked.RoomSessionId)=="Active"&&Count(f.Db,"SELECT COUNT(*) FROM AuditLog;")==audit,"Logout while waiting wrote payment");
        f.Actor=auth.LoginAsync("checkout",Password).GetAwaiter().GetResult();f.Checkout().ConfirmStaff(f.Actor,blocked.RoomSessionId,"Cash");
        Sql(f.Db,"DELETE FROM RolePermission WHERE RoleId='Staff';");Reject<UnauthorizedAccessException>(()=>f.Checkout().ConfirmStaff(f.Actor,blocked.RoomSessionId,"Cash"));Reject<UnauthorizedAccessException>(()=>f.Checkout().ListActiveStaff(f.Actor));
        Sql(f.Db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.CheckOut';UPDATE AspNetUsers SET IsActive=0 WHERE Id='checkout';");Reject<UnauthorizedAccessException>(()=>f.Checkout().ConfirmStaff(f.Actor,blocked.RoomSessionId,"Cash"));
        Sql(f.Db,"UPDATE AspNetUsers SET IsActive=1,SecurityStamp='revoked' WHERE Id='checkout';");Reject<UnauthorizedAccessException>(()=>f.Checkout().ConfirmStaff(f.Actor,blocked.RoomSessionId,"Cash"));
        f.Actor=auth.LoginAsync("checkout",Password).GetAwaiter().GetResult();f.Time.Reads=0;Assert(f.Checkout().ConfirmStaff(f.Actor,blocked.RoomSessionId,"other").RoomSessionId==blocked.RoomSessionId&&f.Time.Reads==0,"Fresh actor replay failed or took clock");
    }
}
