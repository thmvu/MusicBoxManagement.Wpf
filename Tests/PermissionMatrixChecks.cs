using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxPermissionMatrixChecks
{
    private const string Password = "Permission-Test-2026!";
    private static object Sql(SqliteDatabase db, string sql)
    { using(var c=db.OpenConnection()) using(var cmd=c.CreateCommand()) { cmd.CommandText=sql; return cmd.ExecuteScalar(); } }
    private static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    private static void Reject<T>(Action action) where T:Exception
    { try { action(); } catch(T) { return; } throw new Exception("Expected "+typeof(T).Name); }
    private static long Audits(SqliteDatabase db) { return Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog WHERE Action='Permission.Update';")); }
    public static void Run()
    {
        var directory=Path.Combine(Path.GetTempPath(),"MusicBoxPermissionMatrix_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var db=new SqliteDatabase(Path.Combine(directory,"permissions.db"));
            var auth=new AuthenticationService(db);
            var admin=auth.SetupAdminAsync("admin","Admin",Password).GetAwaiter().GetResult();
            var accounts=new AccountManagementService(db);
            var staff=accounts.CreateAsync(admin,new AccountEdit{UserName="staff",FullName="Staff",Role="Staff",IsActive=true},Password).GetAwaiter().GetResult();
            var manager=accounts.CreateAsync(admin,new AccountEdit{UserName="manager",FullName="Manager",Role="Manager",IsActive=true},Password).GetAwaiter().GetResult();
            var staffSession=auth.LoginAsync(staff.UserName,Password).GetAwaiter().GetResult();
            var managerSession=auth.LoginAsync(manager.UserName,Password).GetAwaiter().GetResult();
            var service=new PermissionManagementService(db);var permissions=new PermissionService(db);
            var original=service.Read(admin,"Staff");var managerOriginal=service.Read(admin,"Manager");
            Check(original.PermissionCodes.Contains("Invoice.View")&&!original.PermissionCodes.Contains("Report.View"),"Seed projection wrong.");
            Reject<UnauthorizedAccessException>(()=>service.Read(null,"Staff"));
            Reject<UnauthorizedAccessException>(()=>service.Read(staffSession,"Staff"));
            Reject<UnauthorizedAccessException>(()=>service.Update(managerSession,original,new string[0]));
            Reject<ArgumentException>(()=>service.Read(admin,"Admin"));
            Reject<ArgumentException>(()=>service.Read(admin,"Owner"));
            Reject<ArgumentException>(()=>service.Update(admin,null,new string[0]));
            foreach(var role in new[]{"Staff","Manager"})
            {
                var snapshot=service.Read(admin,role);
                foreach(var code in new[]{"User.Manage","Permission.Manage","Audit.View","No.Such.Permission"})
                    Reject<ArgumentException>(()=>service.Update(admin,snapshot,new[]{code}));
                Reject<ArgumentException>(()=>service.Update(admin,snapshot,new[]{"Invoice.Print"}));
                Reject<ArgumentException>(()=>service.Update(admin,snapshot,new string[]{null}));
                Reject<ArgumentException>(()=>service.Update(admin,snapshot,null));
            }
            Reject<ArgumentException>(()=>service.Update(admin,original,new[]{"Report.View","Report.Export"}));
            Reject<ArgumentException>(()=>service.Update(admin,managerOriginal,new[]{"Report.Export"}));
            Check(Audits(db)==0,"Read/invalid update wrote audit.");
            var noOp=service.Update(admin,original,original.PermissionCodes.Concat(original.PermissionCodes));
            Check(noOp.Revision==original.Revision&&Audits(db)==0,"No-op/duplicate normalization wrote data.");
            var changed=service.Update(admin,original,new[]{"Room.Manage","Invoice.View","Invoice.Print"});
            Check(changed.Revision!=original.Revision&&Audits(db)==1,"Revision/audit missing.");
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
            Sql(db,"CREATE TRIGGER FailPermissionAudit BEFORE INSERT ON AuditLog WHEN NEW.Action='Permission.Update' BEGIN SELECT RAISE(ABORT,'fixture audit failure'); END;");
            Reject<SQLiteException>(()=>service.Update(admin,rollback,new[]{"Dashboard.View"}));
            Check(service.Read(admin,"Staff").Revision==rollback.Revision&&Audits(db)==count,"Audit rollback lost atomicity.");Sql(db,"DROP TRIGGER FailPermissionAudit;");
            // Two real service writers with the same original must not silently overwrite one another.
            var start=new ManualResetEventSlim(false);var shared=service.Read(admin,"Staff");
            Func<string,bool> writer=code=>{start.Wait();try{service.Update(admin,shared,new[]{code});return true;}catch(InvalidOperationException){return false;}};
            var one=Task.Run(()=>writer("Calendar.View"));var two=Task.Run(()=>writer("Session.View"));start.Set();
            Check(Task.WaitAll(new Task[]{one,two},20000)&&((one.Result?1:0)+(two.Result?1:0))==1,"Concurrent stale writers both won or timed out.");start.Dispose();
            // Permission must be checked after waiting for another writer, not before acquiring it.
            var waitingOriginal=service.Read(admin,"Staff");Task<bool> waiting;
            using(var connection=db.OpenConnection()) using(var transaction=SqliteDatabase.BeginWriteTransaction(connection))
            {
                var entered=new ManualResetEventSlim(false);
                waiting=Task.Run(()=>{entered.Set();try{service.Update(admin,waitingOriginal,new[]{"Dashboard.View"});return false;}catch(UnauthorizedAccessException){return true;}});
                Check(entered.Wait(5000),"Waiting writer did not start.");
                using(var command=connection.CreateCommand())
                {
                    command.Transaction=transaction;
                    command.CommandText="UPDATE AspNetUsers SET IsActive=0 WHERE Id=@id;";
                    command.Parameters.AddWithValue("@id",admin.UserId);command.ExecuteNonQuery();
                }
                transaction.Commit();entered.Dispose();
            }
            Check(waiting.Wait(10000)&&waiting.Result,"Writer used permission from before lock wait.");
            Sql(db,"UPDATE AspNetUsers SET IsActive=1 WHERE Id='"+admin.UserId+"';");
            // Read/Update recheck account and role, not the Admin role cached at login.
            var prior=service.Read(admin,"Staff");count=Audits(db);
            Sql(db,"UPDATE AspNetUserRoles SET RoleId='Manager' WHERE UserId='"+admin.UserId+"';");
            Reject<UnauthorizedAccessException>(()=>service.Read(admin,"Staff"));Reject<UnauthorizedAccessException>(()=>service.Update(admin,prior,new string[0]));
            Check(Audits(db)==count,"Revoked writer changed audit.");
            Sql(db,"UPDATE AspNetUserRoles SET RoleId='Admin' WHERE UserId='"+admin.UserId+"'; DELETE FROM RolePermission WHERE RoleId='Admin';");
            Check(permissions.GetStaffAccess(admin).Permissions.Count==PermissionCatalog.All.Count,"Admin lost fixed permissions.");
            Sql(db,"UPDATE AspNetUsers SET IsActive=0 WHERE Id='"+admin.UserId+"';");
            Reject<UnauthorizedAccessException>(()=>service.Update(admin,prior,new string[0]));
            Check(Convert.ToInt32(Sql(db,"PRAGMA user_version;"))==SqliteDatabase.CurrentSchemaVersion,"Matrix unexpectedly migrated schema.");
            Console.WriteLine("Permission matrix checks passed (temporary database).");
        }
        finally { if(Directory.Exists(directory)) Directory.Delete(directory,true); }
    }
}
