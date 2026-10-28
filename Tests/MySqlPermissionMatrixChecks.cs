using System;
using System.Collections;
using System.Collections.Generic;
using MySqlConnector;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxMySqlPermissionMatrixChecks
{
    private const string Password = "Permission-Test-2026!";
    private sealed class Clock:IClock
    { public DateTimeOffset Value = new DateTimeOffset(2026,10,28,13,0,0,TimeSpan.FromHours(7)); public int Reads; public DateTimeOffset UtcNow { get { Interlocked.Increment(ref Reads); return Value; } } }
    private sealed class GateClock:IClock,IDisposable
    {
        public readonly ManualResetEventSlim Entered=new ManualResetEventSlim(false),Release=new ManualResetEventSlim(false);
        public DateTimeOffset UtcNow { get { Entered.Set();if(!Release.Wait(12000))throw new Exception("Account writer gate timed out.");return new DateTimeOffset(2026,10,28,13,0,0,TimeSpan.FromHours(7)); } }
        public void Dispose(){Release.Set();Entered.Dispose();Release.Dispose();}
    }
    private sealed class SignaledCodes:IEnumerable<string>,IDisposable
    {
        private readonly string code; public readonly ManualResetEventSlim Entered=new ManualResetEventSlim(false);
        public SignaledCodes(string code){this.code=code;}
        public IEnumerator<string> GetEnumerator(){yield return code;Entered.Set();}
        IEnumerator IEnumerable.GetEnumerator(){return GetEnumerator();}
        public void Dispose(){Entered.Dispose();}
    }
    private static object Sql(MySqlDatabase db, string sql)
    { using(var c=db.OpenConnection()) using(var cmd=c.CreateCommand()) { cmd.CommandText=sql; return cmd.ExecuteScalar(); } }
    private static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    private static void Reject<T>(Action action) where T:Exception
    { try { action(); } catch(T) { return; } throw new Exception("Expected "+typeof(T).Name); }
    private static long Audits(MySqlDatabase db) { return Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Permission.Update';")); }
    public static void Run()
    {
        var name="musicbox_test_"+Guid.NewGuid().ToString("N");
        Console.WriteLine("Permission matrix test database: "+name);
        try
        {
            var db=new MySqlDatabase(name);
            var auth=new MySqlAuthenticationService(db);
            var admin=auth.SetupAdminAsync("admin","Admin",Password).GetAwaiter().GetResult();
            var accounts=new MySqlAccountManagementService(db);
            var staff=accounts.CreateAsync(admin,new AccountEdit{UserName="staff",FullName="Staff",Role="Staff",IsActive=true},Password).GetAwaiter().GetResult();
            var manager=accounts.CreateAsync(admin,new AccountEdit{UserName="manager",FullName="Manager",Role="Manager",IsActive=true},Password).GetAwaiter().GetResult();
            var staffSession=auth.LoginAsync(staff.UserName,Password).GetAwaiter().GetResult();
            var managerSession=auth.LoginAsync(manager.UserName,Password).GetAwaiter().GetResult();
            var clock=new Clock();var service=new MySqlPermissionManagementService(db,clock);var permissions=new MySqlPermissionService(db);
            new MySqlStaffReservationService(db,clock).ForPermissions(admin);
            var original=service.Read(admin,"Staff");var managerOriginal=service.Read(admin,"Manager");
            Check(original.PermissionCodes.Contains("Invoice.View")&&!original.PermissionCodes.Contains("Report.View"),"Seed projection wrong.");
            Reject<UnauthorizedAccessException>(()=>service.Read(null,"Staff"));
            Reject<UnauthorizedAccessException>(()=>service.Read(staffSession,"Staff"));
            Reject<UnauthorizedAccessException>(()=>new MySqlStaffReservationService(db).ForPermissions(staffSession));
            Reject<UnauthorizedAccessException>(()=>service.Read(managerSession,"Staff"));
            Reject<UnauthorizedAccessException>(()=>service.Update(managerSession,original,new string[0]));
            Reject<ArgumentException>(()=>service.Read(admin,"Admin"));
            Reject<ArgumentException>(()=>service.Read(admin,"Owner"));
            Reject<ArgumentException>(()=>service.Update(admin,null,new string[0]));
            foreach(var role in new[]{"Staff","Manager"})
            {
                var snapshot=service.Read(admin,role);
                foreach(var code in new[]{"User.Manage","Permission.Manage","Audit.View","No.Such.Permission","dashboard.view"," Dashboard.View",""})
                    Reject<ArgumentException>(()=>service.Update(admin,snapshot,new[]{code}));
                Reject<ArgumentException>(()=>service.Update(admin,snapshot,new[]{"Invoice.Print"}));
                Reject<ArgumentException>(()=>service.Update(admin,snapshot,new string[]{null}));
                Reject<ArgumentException>(()=>service.Update(admin,snapshot,null));
            }
            Reject<ArgumentException>(()=>service.Update(admin,original,new[]{"Report.View","Report.Export"}));
            Reject<ArgumentException>(()=>service.Update(admin,managerOriginal,new[]{"Report.Export"}));
            Check(Audits(db)==0,"Read/invalid update wrote audit.");
            var noOp=service.Update(admin,original,original.PermissionCodes.Concat(original.PermissionCodes));
            Check(noOp.Revision==original.Revision&&Audits(db)==0&&clock.Reads==0,"Read/no-op/duplicate normalization wrote data or took clock.");
            var changed=service.Update(admin,original,new[]{"Room.Manage","Invoice.View","Invoice.Print"});
            Check(changed.Revision!=original.Revision&&Audits(db)==1&&clock.Reads==1,"Revision/audit/clock missing.");
            Check(permissions.HasPermission(staffSession,"Room.Manage")&&!permissions.HasPermission(staffSession,"Reservation.View"),"Existing session did not receive current permissions.");
            Check(service.Read(admin,"Manager").Revision==managerOriginal.Revision,"Staff change modified Manager.");
            Reject<InvalidOperationException>(()=>service.Update(admin,original,original.PermissionCodes));
            var managers=service.Update(admin,managerOriginal,new[]{"Report.View","Report.Export"});
            Check(permissions.HasPermission(managerSession,"Report.Export"),"Manager report permission missing.");
            service.Update(admin,managers,new string[0]);Check(!permissions.HasPermission(managerSession,"Report.View"),"Manager revoke cached.");
            // Invalid stored grants are masked in reads but included in stale revision and removed by save.
            var beforeBad=service.Read(admin,"Staff");
            Sql(db,"INSERT INTO Permission(Code,Name) VALUES('Legacy.Unknown','Legacy'); INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code IN('Legacy.Unknown','Permission.Manage','Report.View','Report.Export');");
            var withBad=service.Read(admin,"Staff");
            Check(withBad.Revision!=beforeBad.Revision&&withBad.PermissionCodes.SequenceEqual(beforeBad.PermissionCodes),"Raw invalid grants omitted from stale token or leaked to effective DTO.");
            Check(!permissions.HasPermission(staffSession,"Permission.Manage")&&!permissions.HasPermission(staffSession,"Report.View"),"Invalid stored grants bypass invariants.");
            Reject<InvalidOperationException>(()=>service.Update(admin,beforeBad,beforeBad.PermissionCodes));
            var cleaned=service.Update(admin,withBad,withBad.PermissionCodes);
            Check(cleaned.Revision==beforeBad.Revision,"Save did not clean invalid grants.");
            Check(Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM RolePermission rp JOIN Permission p ON p.PermissionId=rp.PermissionId WHERE rp.RoleId='Staff' AND p.Code IN('Legacy.Unknown','Permission.Manage','Report.View','Report.Export');"))==0,"Invalid grants remained stored.");
            // Missing dependency in persisted data is hidden and can be cleaned too.
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Staff' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Invoice.View');");
            var dependent=service.Read(admin,"Staff");Check(!dependent.PermissionCodes.Contains("Invoice.Print"),"Orphan dependency displayed effective.");
            service.Update(admin,dependent,dependent.PermissionCodes);
            Check(Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM RolePermission rp JOIN Permission p ON p.PermissionId=rp.PermissionId WHERE rp.RoleId='Staff' AND p.Code='Invoice.Print';"))==0,"Orphan dependency not cleaned.");
            // Data and audit must roll back together.
            var rollback=service.Read(admin,"Staff");var count=Audits(db);
            Sql(db,"CREATE TRIGGER FailPermissionAudit BEFORE INSERT ON AuditLog FOR EACH ROW BEGIN IF NEW.Action='Permission.Update' THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='fixture audit failure'; END IF; END");
            Reject<MySqlException>(()=>service.Update(admin,rollback,new[]{"Dashboard.View"}));
            Check(service.Read(admin,"Staff").Revision==rollback.Revision&&Audits(db)==count,"Audit rollback lost atomicity.");Sql(db,"DROP TRIGGER FailPermissionAudit;");
            Check((string)Sql(db,"SELECT ActorType FROM AuditLog WHERE Action='Permission.Update' ORDER BY AuditLogId DESC LIMIT 1;")=="Staff"
                &&(string)Sql(db,"SELECT EntityName FROM AuditLog WHERE Action='Permission.Update' ORDER BY AuditLogId DESC LIMIT 1;")=="RolePermission","Audit route wrong.");
            Console.WriteLine("PASS Matrix validation/dependencies/current grants/legacy cleanup/no-op/rollback");
            // Two real service writers with the same original must not silently overwrite one another.
            var start=new ManualResetEventSlim(false);var shared=service.Read(admin,"Staff");
            Func<string,bool> writer=code=>{start.Wait();try{service.Update(admin,shared,new[]{code});return true;}catch(InvalidOperationException){return false;}};
            var one=Task.Run(()=>writer("Calendar.View"));var two=Task.Run(()=>writer("Session.View"));start.Set();
            Check(Task.WaitAll(new Task[]{one,two},20000)&&((one.Result?1:0)+(two.Result?1:0))==1,"Concurrent stale writers both won or timed out.");start.Dispose();
            // Clock is taken after waiting for this role; an empty matrix also has a stable lock resource.
            var waitingOriginal=service.Read(admin,"Staff"); clock.Reads=0;
            using(var selected=new SignaledCodes("Dashboard.View"))
            using(var connection=db.OpenConnection()) using(var transaction=MySqlDatabase.BeginWriteTransaction(connection))
            {
                using(var command=connection.CreateCommand()){command.Transaction=transaction;command.CommandText="SELECT Id FROM AspNetRoles WHERE Id='Staff' FOR UPDATE;";command.ExecuteScalar();}
                var waiting=Task.Run(()=>service.Update(admin,waitingOriginal,selected));
                Check(selected.Entered.Wait(5000),"Waiting writer precheck did not complete.");Thread.Sleep(150);Check(!waiting.IsCompleted&&clock.Reads==0,"Writer bypassed role lock or took clock before wait.");
                clock.Value=clock.Value.AddMinutes(7).AddTicks(1234567);transaction.Commit();Check(waiting.Wait(15000),"Waiting writer did not finish.");
                Check(waiting.Result.PermissionCodes.Single()=="Dashboard.View"&&clock.Reads==1
                    &&(string)Sql(db,"SELECT CreatedAt FROM AuditLog WHERE Action='Permission.Update' ORDER BY AuditLogId DESC LIMIT 1;")==clock.Value.ToUniversalTime().ToString("O"),"Fresh clock/ticks after role wait wrong.");
            }
            // A real account writer has an uncommitted lock operation; the matrix precheck sees the old Admin,
            // then waits on the target role. The account writer commits before that role is released.
            var backup=accounts.CreateAsync(admin,new AccountEdit{UserName="backup",FullName="Admin dự phòng",Role="Admin",IsActive=true},Password).GetAwaiter().GetResult();
            var backupSession=auth.LoginAsync(backup.UserName,Password).GetAwaiter().GetResult();
            var target=accounts.List(backupSession).Single(u=>u.UserId==admin.UserId);
            waitingOriginal=service.Read(admin,"Staff");count=Audits(db);clock.Reads=0;
            using(var gate=new GateClock()) using(var selected=new SignaledCodes("Calendar.View"))
            using(var connection=db.OpenConnection()) using(var transaction=MySqlDatabase.BeginWriteTransaction(connection))
            {
                using(var command=connection.CreateCommand()){command.Transaction=transaction;command.CommandText="SELECT Id FROM AspNetRoles WHERE Id='Staff' FOR UPDATE;";command.ExecuteScalar();}
                var locking=Task.Run(()=>new MySqlAccountManagementService(db,gate).UpdateAsync(backupSession,target,
                    new AccountEdit{UserName=target.UserName,FullName=target.FullName,Role="Admin",IsActive=false}).GetAwaiter().GetResult());
                Task<bool> waiting=null;var released=false;
                try
                {
                    Check(gate.Entered.Wait(12000),"Real account writer did not prepare the lock.");
                    waiting=Task.Run(()=>{try{service.Update(admin,waitingOriginal,selected);return false;}catch(UnauthorizedAccessException){return true;}});
                    Check(selected.Entered.Wait(5000),"Matrix precheck could not read pre-commit Admin.");Thread.Sleep(150);Check(!waiting.IsCompleted&&clock.Reads==0,"Matrix bypassed held role.");
                    gate.Release.Set();Check(locking.Wait(8000),"Account writer could not commit before role unlock.");transaction.Commit();released=true;
                    Check(waiting.Wait(15000)&&waiting.Result&&clock.Reads==0&&Audits(db)==count,"Revoked actor wrote matrix/audit after role wait.");
                }
                finally{gate.Release.Set();if(!released)transaction.Rollback();locking.GetAwaiter().GetResult();if(waiting!=null)waiting.GetAwaiter().GetResult();}
            }
            var locked=accounts.List(backupSession).Single(u=>u.UserId==target.UserId);
            accounts.UpdateAsync(backupSession,locked,new AccountEdit{UserName=locked.UserName,FullName=locked.FullName,Role="Admin",IsActive=true}).GetAwaiter().GetResult();
            Reject<UnauthorizedAccessException>(()=>service.Read(admin,"Staff"));admin=auth.LoginAsync("admin",Password).GetAwaiter().GetResult();
            // Read/Update recheck account and role, not the Admin role cached at login.
            var prior=service.Read(admin,"Staff");count=Audits(db);
            Sql(db,"UPDATE AspNetUserRoles SET RoleId='Manager' WHERE UserId='"+admin.UserId+"';");
            Reject<UnauthorizedAccessException>(()=>service.Read(admin,"Staff"));Reject<UnauthorizedAccessException>(()=>service.Update(admin,prior,new string[0]));
            Check(Audits(db)==count,"Revoked writer changed audit.");
            Sql(db,"UPDATE AspNetUserRoles SET RoleId='Admin' WHERE UserId='"+admin.UserId+"'; DELETE FROM RolePermission WHERE RoleId='Admin';");
            Check(permissions.GetStaffAccess(admin).Permissions.Count==PermissionCatalog.All.Count,"Admin lost fixed permissions.");
            Sql(db,"UPDATE AspNetUsers SET IsActive=0 WHERE Id='"+admin.UserId+"';");
            Reject<UnauthorizedAccessException>(()=>service.Update(admin,prior,new string[0]));
            Sql(db,"UPDATE AspNetUsers SET IsActive=1 WHERE Id='"+admin.UserId+"';");
            auth.Logout(admin);Reject<UnauthorizedAccessException>(()=>service.Read(admin,"Staff"));Reject<UnauthorizedAccessException>(()=>service.Update(admin,prior,new string[0]));
            Check(Convert.ToInt32(Sql(db,"SELECT Version FROM _MusicBoxSchemaVersion;"))==MySqlDatabase.CurrentSchemaVersion,"Matrix unexpectedly migrated schema.");
            Console.WriteLine("PASS MySQL Permission matrix: Admin-only/current rights, independent roles, immutable Admin/Staff limits/dependencies, raw revision/stale/no-op/legacy cleanup, atomic audit rollback, concurrent real matrix writers, fresh clock and real account lock after role wait; GUID only, no provider/UI/import");
        }
        catch(Exception error){Console.WriteLine("Matrix failure before cleanup: "+error);throw;}
        finally
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"\Amusicbox_test_[0-9a-f]{32}\z"))throw new Exception("Unsafe cleanup.");
            Sql(new MySqlDatabase("defaultdb"),"DROP DATABASE IF EXISTS `"+name+"`");
        }
    }
}
