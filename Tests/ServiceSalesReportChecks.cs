using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxServiceSalesReportChecks
{
    private sealed class Clock : IClock { public DateTimeOffset Value; public int Reads; public DateTimeOffset UtcNow { get { Reads++; return Value; } } }
    private sealed class GateClock : IClock
    {
        public DateTimeOffset Value; public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false), Release = new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow { get { Entered.Set(); if (!Release.Wait(10000)) throw new Exception("Report snapshot timeout."); return Value; } }
    }
    private static object Sql(SqliteDatabase db, string sql) { using (var c = db.OpenConnection()) using (var q = c.CreateCommand()) { q.CommandText = sql; return q.ExecuteScalar(); } }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static DateTimeOffset At(int day, int hour) { return new DateTimeOffset(2026,10,day,hour,0,0,TimeSpan.FromHours(7)); }
    private static string Utc(DateTimeOffset value) { return value.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture); }
    private static OrderLineRequest[] Cart(int service, int quantity) { return new[] { new OrderLineRequest { ServiceId=service,Quantity=quantity } }; }
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"MusicBoxServiceSales_"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var db=new SqliteDatabase(Path.Combine(root,"report.db")); var auth=new AuthenticationService(db); const string password="Sales-Report-2026!";
            var admin=auth.SetupAdminAsync("admin","Admin",password).GetAwaiter().GetResult();
            var accounts=new AccountManagementService(db);
            accounts.CreateAsync(admin,new AccountEdit{UserName="manager",FullName="Manager",Role="Manager"},password).GetAwaiter().GetResult();
            accounts.CreateAsync(admin,new AccountEdit{UserName="staff",FullName="Staff",Role="Staff"},password).GetAwaiter().GetResult();
            Sql(db,"DELETE FROM RolePermission WHERE RoleId IN ('Staff','Manager');INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.View';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Report.View';");
            var manager=auth.LoginAsync("manager",password).GetAwaiter().GetResult(); var staff=auth.LoginAsync("staff",password).GetAwaiter().GetResult();
            var clock=new Clock{Value=At(18,9)}; var service=new ServiceSalesReportService(db,clock); var day=At(18,9).Date;
            clock.Reads=0; var empty=service.Read(manager,day,day);
            Assert(empty.Rows.Count==0&&empty.TopItems.Count==0&&empty.TotalRevenue==0&&clock.Reads==1,"Empty/one clock failed.");
            Reject<UnauthorizedAccessException>(()=>service.Read(null,day,day)); Reject<UnauthorizedAccessException>(()=>service.Read(staff,day,day));
            Reject<ArgumentException>(()=>service.Read(admin,day.AddDays(1),day)); Reject<ArgumentException>(()=>service.Read(admin,DateTime.MinValue,day));
            Reject<ArgumentException>(()=>service.Read(admin,day,DateTime.MaxValue)); Reject<ArgumentException>(()=>service.Read(admin,day,day,-1));
            Sql(db,@"INSERT INTO Rooms(RoomId,RoomCode,Name,RoomTypeId,ImageUrl,IsActive,CreatedAt) VALUES(1,'R1','Phòng',1,'test.png',1,'2026-10-01');
INSERT INTO Services(ServiceId,Name,Category,Price,IsActive) VALUES(1,'Trà','Đồ uống',10000,1),(2,'Trà','Đồ uống',20000,1),(3,'Chưa bán','Khác',5000,1);");
            var sessions=new RoomSessionService(db,clock); var orders=new OrderService(db,clock);
            var session=sessions.CreateWalkIn(admin,new WalkInRequest{RoomId=1,FullName="PRIVATE",PhoneNumber="0901111111"}).Session;
            orders.CreateStaff(admin,session.RoomSessionId,Cart(1,2)); orders.CreateStaff(admin,session.RoomSessionId,Cart(2,5));
            var pending=orders.CreateGuest("0901111111",session.RoomSessionId,Cart(1,9));
            Assert(service.Read(manager,day,day).Rows.Count==0,"Active orders included before invoice.");
            Sql(db,"UPDATE Services SET Name='Trà mới',Price=30000 WHERE ServiceId=1;");
            orders.CreateStaff(admin,session.RoomSessionId,Cart(1,2));
            clock.Value=At(18,10); new CheckoutService(db,clock).ConfirmStaff(admin,session.RoomSessionId,"Cash");
            Sql(db,"UPDATE Services SET Name='Danh mục khác',Price=90000,IsActive=0 WHERE ServiceId=1;");
            clock.Reads=0; var report=service.Read(manager,day,day);
            Assert(clock.Reads==1&&report.Rows.Count==2&&report.TotalQuantity==9&&report.TotalRevenue==180000,"Paid Completed snapshot total failed.");
            var first=report.Rows.Single(r=>r.ServiceId==1);
            Assert(first.Quantity==4&&first.Revenue==80000&&first.Name=="Trà mới","Grouped by name/current price instead of ServiceId/snapshot.");
            Assert(report.TopItems[0].ServiceId==2&&report.Rows[0].ServiceId==2,"Quantity/revenue ordering failed.");
            Assert(report.Services.Count==4&&report.Services.Single(r=>r.ServiceId==1).Label.Contains("ngừng bán"),"Inactive catalog excluded from filter.");
            Assert(service.Read(manager,day,day,1).TotalRevenue==80000&&service.Read(manager,day,day,3).Rows.Count==0,"Service filter failed.");
            Assert(service.Read(admin,day.AddDays(-1),day.AddDays(-1)).Rows.Count==0,"Filtered by order creation instead of PaidAt.");
            // Keep payment-boundary fixtures separate from the real checkout above.
            Sql(db,"DROP TRIGGER Invoices_NoUpdate;");
            foreach(var paid in new[]{At(18,0).AddTicks(-1),At(18,0),At(19,0).AddTicks(-1),At(19,0)})
            {
                Sql(db,"UPDATE Invoices SET PaidAt='"+Utc(paid)+"';");
                var expected=paid>=At(18,0)&&paid<At(19,0)?180000:0;
                Assert(service.Read(manager,day,day).TotalRevenue==expected,"PaidAt boundary lost a tick.");
            }
            using(var c=db.OpenConnection())using(var q=c.CreateCommand())
            {q.CommandText="PRAGMA ignore_check_constraints=ON;UPDATE Invoices SET PaidAt='2026-10-18T00:00:00+07:00';PRAGMA ignore_check_constraints=OFF;";q.ExecuteNonQuery();}
            Assert(service.Read(manager,day,day).TotalRevenue==180000,"Legacy offset PaidAt failed.");
            using(var c=db.OpenConnection())using(var q=c.CreateCommand())
            {q.CommandText="PRAGMA ignore_check_constraints=ON;UPDATE Invoices SET PaidAt='2026-10-18T10:00:00';PRAGMA ignore_check_constraints=OFF;";q.ExecuteNonQuery();}
            Reject<InvalidOperationException>(()=>service.Read(manager,day,day));
            Sql(db,"UPDATE Invoices SET PaidAt='"+Utc(At(18,10))+"';");
            Sql(db,"UPDATE OrderItems SET UnitPrice=9223372036854775807 WHERE OrderId IN (SELECT OrderId FROM Orders WHERE Status='Completed');");
            Assert(service.Read(manager,day,day).TotalRevenue==(decimal)long.MaxValue*9,"Decimal beyond Int64 lost precision.");
            var audit=Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog"));
            Sql(db,"CREATE TRIGGER Report_NoWrite BEFORE UPDATE ON Services BEGIN SELECT RAISE(ABORT,'read only');END;");
            Assert(service.Read(manager,day,day).TotalQuantity==9&&Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog"))==audit,"Report wrote data/audit.");
            Assert(!typeof(ServiceSalesReport).GetProperties().Concat(typeof(ServiceSalesRow).GetProperties()).Any(p=>p.GetSetMethod()!=null),"DTO has public setter.");
            var gate=new GateClock{Value=clock.Value}; var reading=Task.Run(()=>new ServiceSalesReportService(db,gate).Read(manager,day,day));
            Assert(gate.Entered.Wait(10000),"Snapshot not reached.");
            Task writing=null;
            try
            {
                writing=Task.Run(()=>Sql(db,"DELETE FROM RolePermission WHERE RoleId='Manager';"));
                Thread.Sleep(100);
                gate.Release.Set(); Assert(reading.GetAwaiter().GetResult().TotalQuantity==9,"Read snapshot changed after authorization.");
                writing.GetAwaiter().GetResult();
            }
            finally{gate.Release.Set();reading.GetAwaiter().GetResult();if(writing!=null)writing.GetAwaiter().GetResult();}
            Reject<UnauthorizedAccessException>(()=>service.Read(manager,day,day));
            Sql(db,"INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.View';UPDATE AspNetUsers SET SecurityStamp='changed' WHERE Id='"+manager.UserId+"';");
            Reject<UnauthorizedAccessException>(()=>service.Read(manager,day,day));
            for(var id=4;id<=15;id++)
                Sql(db,"INSERT INTO Services(ServiceId,Name,Category,Price,IsActive) VALUES("+id+",'Tie item','Khác',1,1);INSERT INTO OrderItems(OrderId,ServiceId,ServiceNameSnapshot,Quantity,UnitPrice) SELECT MIN(OrderId),"+id+",'Tie item',1,1 FROM Orders WHERE Status='Completed';");
            var ranked=service.Read(admin,day,day);
            Assert(ranked.Rows.Count==14&&ranked.TopItems.Count==10&&ranked.TopItems[0].ServiceId==2&&ranked.TopItems[1].ServiceId==1&&ranked.TopItems[2].ServiceId==4&&ranked.TopItems[9].ServiceId==11,"Top 10 cap/tie order failed.");
            Console.WriteLine("PASS ServiceSalesReport: real walk-in/order/checkout, PaidAt ticks/offsets, ServiceId grouping/snapshots/inactive/filter, Pending exclusion, decimal, current role/permission/stamp, read snapshot and no writes.");
        }
        finally
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            var target=Path.GetFullPath(root); var temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!target.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(target).StartsWith("MusicBoxServiceSales_",StringComparison.Ordinal))throw new Exception("Unsafe cleanup.");
            Directory.Delete(target,true);
        }
    }
}
