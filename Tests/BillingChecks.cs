using System;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxBillingChecks
{
    private sealed class Clock : IClock
    { public DateTimeOffset Value; public int Reads; public DateTimeOffset UtcNow { get { Interlocked.Increment(ref Reads); return Value; } } }
    private sealed class GateClock : IClock
    {
        public DateTimeOffset Value;
        public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false), Release = new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow { get { Entered.Set(); if (!Release.Wait(10000)) throw new Exception("Billing snapshot gate timeout."); return Value; } }
    }
    private sealed class SignalClock : IClock
    { public DateTimeOffset Value; public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false); public DateTimeOffset UtcNow { get { Entered.Set(); return Value; } } }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static object Sql(SqliteDatabase db, string sql) { using (var c = db.OpenConnection()) using (var cmd = c.CreateCommand()) { cmd.CommandText = sql; return cmd.ExecuteScalar(); } }
    private static string Utc(DateTimeOffset value) { return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture); }
    private static DateTimeOffset Time(int h) { return new DateTimeOffset(2026,10,10,h,0,0,TimeSpan.FromHours(7)); }
    private static OrderLineRequest[] Cart(int quantity) { return new[] { new OrderLineRequest { ServiceId = 1, Quantity = quantity } }; }
    private static decimal ExactRoom(long rate, long ticks)
    {
        BigInteger remainder; var amount = BigInteger.DivRem(new BigInteger(rate) * ticks, new BigInteger(TimeSpan.TicksPerHour), out remainder);
        if (remainder * 2 >= TimeSpan.TicksPerHour) amount += 1;
        return (decimal)amount;
    }
    private static SessionBill InTransaction(BillingService service, SQLiteConnection connection, SQLiteTransaction transaction, int id)
    {
        try { return (SessionBill)typeof(BillingService).GetMethod("CalculateActive", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(service, new object[] { connection, transaction, id }); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MusicBoxBilling_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var db = new SqliteDatabase(Path.Combine(directory, "billing.db")); var auth = new AuthenticationService(db); const string password = "Billing-Test-2026!";
            var admin = auth.SetupAdminAsync("admin", "Admin thử", password).GetAwaiter().GetResult();
            Sql(db, @"INSERT INTO Rooms(RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES('B1','Tính tiền',1,'test.png',1,'test'),('B2','Booking',1,'test.png',1,'test');
INSERT INTO Services(Name,Category,Price,IsActive) VALUES('Nước cam','Đồ uống',25000,1);
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive) SELECT 'bill','bill','BILL',PasswordHash,'bill','Nhân viên xem phiên',1 FROM AspNetUsers LIMIT 1;
INSERT INTO AspNetUserRoles VALUES('bill','Staff');DELETE FROM RolePermission WHERE RoleId='Staff';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.View';");
            var actor = auth.LoginAsync("bill", password).GetAwaiter().GetResult();
            var start = Time(9).AddSeconds(12).AddTicks(1234); var clock = new Clock { Value = start };
            var sessions = new RoomSessionService(db, clock); var active = sessions.CreateWalkIn(admin, new WalkInRequest { RoomId = 1, FullName = "Khách riêng", PhoneNumber = "0901111111" });
            var id = active.Session.RoomSessionId; var service = new BillingService(db, clock); var orders = new OrderService(db, clock);
            foreach (var entry in new[] { Tuple.Create(0L,0m), Tuple.Create(TimeSpan.TicksPerHour,120000m), Tuple.Create(TimeSpan.TicksPerMinute*80,160000m), Tuple.Create(TimeSpan.TicksPerMinute*147,294000m), Tuple.Create(TimeSpan.TicksPerSecond*90,3000m), Tuple.Create(1L,0m) })
            {
                clock.Value = start.AddTicks(entry.Item1).ToOffset(TimeSpan.FromHours(3)); clock.Reads = 0;
                var bill = service.ReadGuest("+84 901.111-111", id);
                Assert(bill.RoomCharge == entry.Item2 && bill.TotalAmount == entry.Item2 && bill.ServiceCharge == 0 && bill.UsedTicks == entry.Item1 && bill.UsedMinutes == entry.Item1 / (decimal)TimeSpan.TicksPerMinute && clock.Reads == 1, "Wrong duration/room charge/zero minimum/one clock.");
                Assert(bill.ActualStartTime == start && bill.BillingEndTime == clock.Value && bill.BillingEndTime.Offset == TimeSpan.Zero && bill.RoomCode == "B1", "Billing lost UTC/actual ticks/snapshot.");
            }
            foreach (var rate in new[] { 1L, 120000L, long.MaxValue, long.MaxValue - 1 })
            {
                Sql(db, "UPDATE RoomSessions SET HourlyRate=" + rate + " WHERE RoomSessionId=" + id);
                foreach (var ticks in new[] { TimeSpan.TicksPerHour/2-1, TimeSpan.TicksPerHour/2, TimeSpan.TicksPerHour/2+1, TimeSpan.TicksPerHour*2+1234567 })
                { clock.Value = start.AddTicks(ticks); Assert(service.ReadGuest("0901111111", id).RoomCharge == ExactRoom(rate,ticks), "Tick/half-dong/large rate rounding differs from exact integer oracle."); }
                clock.Value = DateTimeOffset.MaxValue;
                Assert(service.ReadGuest("0901111111",id).RoomCharge == ExactRoom(rate,(clock.Value-start).Ticks), "Maximum duration/rate overflowed or lost integer precision.");
            }
            Sql(db, "UPDATE RoomSessions SET HourlyRate=120000 WHERE RoomSessionId=" + id); clock.Value = start.AddTicks(-1); Reject<InvalidOperationException>(() => service.ReadGuest("0901111111",id));
            clock.Value = Time(10); orders.CreateStaff(admin,id,Cart(2)); var pending = orders.CreateGuest("0901111111",id,Cart(3)); var cancelled = orders.CreateGuest("0901111111",id,Cart(4)); orders.CancelGuest("0901111111",cancelled.OrderId);
            Sql(db, "UPDATE Services SET Name='Đổi tên',Price=99000,IsActive=0; UPDATE RoomTypes SET Name='Loại sửa',PricePerHour=999000; UPDATE Rooms SET Name='Phòng sửa';");
            var auditCount = Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog")); var orderCount = Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM Orders"));
            clock.Value = start.AddMinutes(147); var read = service.ReadStaff(actor,id);
            Assert(read.RoomCharge == 294000m && read.ServiceCharge == 50000m && read.TotalAmount == 344000m && read.HourlyRate == 120000 && read.RoomCode == "B1" && read.RoomTypeName == "Standard" && read.CompletedOrderCount == 1 && read.PendingOrderCount == 1 && read.CancelledOrderCount == 1, "Status filtering/catalog changes altered snapshots or Staff read required unrelated rights.");
            // Usage continues after the walk-in deadline and closing; no hold/end time is a billing cap.
            clock.Value = start.AddDays(1); Assert(service.ReadGuest("0901111111",id).RoomCharge == 2880000m, "Billing capped usage at shift/deadline.");
            Assert(Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog")) == auditCount && Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM Orders")) == orderCount && (string)Sql(db,"SELECT Status FROM Orders WHERE OrderId="+pending.OrderId) == "Pending" && Sql(db,"SELECT ActualEndTime FROM RoomSessions WHERE RoomSessionId="+id) == DBNull.Value && Sql(db,"SELECT ExpectedEndTime FROM RoomSessions WHERE RoomSessionId="+id) == DBNull.Value, "Bill preview wrote audit/orders/session or ended walk-in.");
            foreach (var name in new[] { "CustomerId", "PhoneNumber", "FullName", "RoomId", "ReservationId", "CreatedByUserId", "Orders" }) Assert(typeof(SessionBill).GetProperty(name) == null, "Public bill exposes " + name);
            Reject<ArgumentException>(() => service.ReadGuest("bad",id)); Reject<InvalidOperationException>(() => service.ReadGuest("0902222222",id)); Reject<InvalidOperationException>(() => service.ReadGuest("0901111111",999)); Reject<UnauthorizedAccessException>(() => service.ReadStaff(null,id));
            Sql(db,"UPDATE Customers SET PhoneNumber='0909999999' WHERE PhoneNumber='0901111111';"); Reject<InvalidOperationException>(() => service.ReadGuest("0901111111",id)); Assert(service.ReadGuest("0909999999",id).ServiceCharge == 50000m,"New current phone could not read own bill."); Sql(db,"UPDATE Customers SET PhoneNumber='0901111111' WHERE PhoneNumber='0909999999';");
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code IN('Order.View','Session.CheckOut');"); Reject<UnauthorizedAccessException>(() => service.ReadStaff(actor,id));
            Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.View';UPDATE AspNetUsers SET IsActive=0 WHERE Id='bill';"); Reject<UnauthorizedAccessException>(() => service.ReadStaff(actor,id)); Sql(db,"UPDATE AspNetUsers SET IsActive=1 WHERE Id='bill';");
            Sql(db,"UPDATE AspNetUsers SET SecurityStamp='changed' WHERE Id='bill';"); Reject<UnauthorizedAccessException>(() => service.ReadStaff(actor,id)); Sql(db,"UPDATE AspNetUsers SET SecurityStamp='bill' WHERE Id='bill';");
            clock.Value = Time(13); orders.ConfirmStaff(admin,pending.OrderId); Assert(service.ReadGuest("0901111111",id).ServiceCharge == 125000m,"Fresh read missed new Completed charge.");
            VerifySnapshot(db,clock,admin,id,service,orders);
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Session.View');");
            VerifyWriter(db,clock,actor,id,service,start);
            Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Session.View';");
            clock.Value = Time(13); var priorServices = service.ReadGuest("0901111111",id).ServiceCharge;
            Sql(db,"UPDATE Services SET Price=9223372036854775807 WHERE ServiceId=1;");orders.CreateStaff(admin,id,Cart(10));
            var largeBill = service.ReadGuest("0901111111",id);
            Assert(largeBill.ServiceCharge == priorServices + (decimal)long.MaxValue*10 && largeBill.TotalAmount == largeBill.RoomCharge+largeBill.ServiceCharge && largeBill.TotalAmount > long.MaxValue,"Billing service totals overflowed long or used catalogue price instead of snapshot.");
            // Booking uses actual/snapshot, including time past ExpectedEnd; the reservation remains unchanged.
            clock.Value = Time(9); Sql(db,"UPDATE Services SET IsActive=1,Price=25000;UPDATE RoomTypes SET PricePerHour=120000;");
            var source = new ReservationService(db,clock).CreateGuest(new ReservationRequest { RoomId=2,FullName="Khách booking",PhoneNumber="0902222222",StartTime=Time(13),DurationMinutes=60 });
            clock.Value = Time(13).AddSeconds(12).AddTicks(1234); var bookingSession = sessions.CheckIn(admin,source.ReservationId); orders.CreateStaff(admin,bookingSession.RoomSessionId,Cart(1));
            clock.Value = bookingSession.ExpectedEndTime.Value.AddMinutes(2); var bookingBill = service.ReadGuest("0902222222",bookingSession.RoomSessionId);
            Assert(bookingBill.RoomCharge == 124000m && bookingBill.ServiceCharge == 25000m && bookingBill.UsedMinutes == 62m && bookingBill.ActualStartTime == bookingSession.ActualStartTime && (string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId="+source.ReservationId) == "CheckedIn", "Booking bill used planned times/capped end/other session items or changed reservation.");
            Sql(db,"UPDATE RoomSessions SET Status='Completed',ActualEndTime='"+Utc(clock.Value)+"' WHERE RoomSessionId="+id); Reject<InvalidOperationException>(() => service.ReadGuest("0901111111",id)); Reject<InvalidOperationException>(() => service.ReadStaff(admin,id));
            auth.Logout(actor); Reject<UnauthorizedAccessException>(() => service.ReadStaff(actor,bookingSession.RoomSessionId));
            Assert(Convert.ToInt64(Sql(db,"PRAGMA user_version")) == SqliteDatabase.CurrentSchemaVersion,"Billing changed schema.");
            Console.WriteLine("PASS Billing: actual UTC/ticks/60-80-147 minutes/seconds/zero/half-dong/exact large-rate oracle, immutable snapshots, Completed-only/decimal totals, current phone/private DTO/live Session.View, read-only/no cap, one read snapshot vs confirm, shared writer/rollback/clock after lock, booking/walk-in/Completed guards on temporary SQLite.");
        }
        finally
        {
            var target = Path.GetFullPath(directory);
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(tempRoot,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("MusicBoxBilling_",StringComparison.Ordinal))
                throw new Exception("Test cleanup target is outside its temporary directory.");
            Directory.Delete(target,true);
        }
    }
    private static void VerifySnapshot(SqliteDatabase db, Clock clock, LoginSession admin, int id, BillingService service, OrderService orders)
    {
        Sql(db,"UPDATE Services SET IsActive=1,Price=25000;");clock.Value=Time(13);var pending=orders.CreateGuest("0901111111",id,Cart(1));var before=service.ReadGuest("0901111111",id);
        var gate=new GateClock{Value=clock.Value};var reader=Task.Run(()=>new BillingService(db,gate).ReadStaff(admin,id));
        Task<ServiceOrder> writer=null;var signal=new SignalClock{Value=clock.Value};
        try
        {
            Assert(gate.Entered.Wait(5000),"Reader did not reach clock after snapshot.");writer=Task.Run(()=>new OrderService(db,signal).ConfirmStaff(admin,pending.OrderId));Assert(signal.Entered.Wait(5000),"Concurrent confirm did not acquire writer.");
            Assert(!writer.IsCompleted,"Confirm committed through unfinished read snapshot.");gate.Release.Set();Assert(reader.Wait(10000) && writer.Wait(10000),"Snapshot/confirm did not finish.");
            Assert(reader.Result.ServiceCharge==before.ServiceCharge && reader.Result.PendingOrderCount==before.PendingOrderCount && service.ReadGuest("0901111111",id).ServiceCharge==before.ServiceCharge+25000m,"Billing mixed old/new order snapshot or cached results.");
        }
        finally { gate.Release.Set(); if(reader!=null)reader.Wait(10000); if(writer!=null)writer.Wait(10000); }
    }
    private static void VerifyWriter(SqliteDatabase db, Clock clock, LoginSession checkoutActor, int id, BillingService service, DateTimeOffset start)
    {
        clock.Value=Time(13);var orders=new OrderService(db,clock);var pending=orders.CreateGuest("0901111111",id,Cart(1));var before=service.ReadGuest("0901111111",id);
        using(var connection=db.OpenConnection())using(var other=db.OpenConnection())using(var transaction=SqliteDatabase.BeginWriteTransaction(connection))
        {
            new PermissionService(db).Demand(checkoutActor,"Session.CheckOut",connection,transaction);
            Reject<ArgumentException>(()=>InTransaction(service,other,transaction,id));Reject<ArgumentException>(()=>InTransaction(service,connection,null,id));
            using(var command=connection.CreateCommand()){command.Transaction=transaction;command.CommandText="UPDATE Orders SET Status='Completed' WHERE OrderId="+pending.OrderId;command.ExecuteNonQuery();}
            clock.Reads=0;clock.Value=Time(13).AddMinutes(1);var current=InTransaction(service,connection,transaction,id);
            Assert(current.ServiceCharge==before.ServiceCharge+25000m && current.BillingEndTime==clock.Value && clock.Reads==1,"Shared writer did not see uncommitted order/one fresh clock.");transaction.Rollback();
        }
        Assert(service.ReadGuest("0901111111",id).ServiceCharge==before.ServiceCharge && (string)Sql(db,"SELECT Status FROM Orders WHERE OrderId="+pending.OrderId)=="Pending","Calculating committed caller transaction.");
        var started=new ManualResetEventSlim(false);Task<SessionBill> waiting=null;clock.Reads=0;
        using(var connection=db.OpenConnection())using(var transaction=SqliteDatabase.BeginWriteTransaction(connection))
        {
            try
            {
                waiting=Task.Run(()=>{using(var c=db.OpenConnection()){started.Set();using(var tx=SqliteDatabase.BeginWriteTransaction(c)){new PermissionService(db).Demand(checkoutActor,"Session.CheckOut",c,tx);return InTransaction(service,c,tx,id);}}});
                Assert(started.Wait(5000),"Writer fixture did not start.");Thread.Sleep(100);Assert(clock.Reads==0,"Billing clock read before waiting writer lock.");clock.Value=Time(13).AddMinutes(3);
                using(var command=connection.CreateCommand()){command.Transaction=transaction;command.CommandText="UPDATE Orders SET Status='Completed' WHERE OrderId="+pending.OrderId;command.ExecuteNonQuery();}transaction.Commit();
            }
            finally { if(transaction.Connection!=null)transaction.Rollback(); }
        }
        Assert(waiting.Wait(10000) && waiting.Result.BillingEndTime==clock.Value && waiting.Result.ServiceCharge==before.ServiceCharge+25000m && waiting.Result.RoomCharge==ExactRoom(120000,(clock.Value-start).Ticks),"Shared calculator used stale pre-lock time/data.");
    }
}
