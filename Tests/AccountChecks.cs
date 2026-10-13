using System;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxAccountChecks
{
    private const string Password="Accounts-Test-2026!",NewPassword="Accounts-New-2026!";
    private static object Sql(SqliteDatabase db,string sql){using(var c=db.OpenConnection())using(var command=c.CreateCommand()){command.CommandText=sql;return command.ExecuteScalar();}}
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static AccountEdit Edit(UserAccount user,string role=null,bool? active=null){return new AccountEdit{UserName=user.UserName,FullName=user.FullName,Role=role??user.Role,IsActive=active??user.IsActive};}
    private static UserAccount Get(AccountManagementService service,LoginSession actor,string id){return service.List(actor).Single(u=>u.UserId==id);}
    public static void Run()
    {
        var directory=Path.Combine(Path.GetTempPath(),"MusicBoxAccounts_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var db=new SqliteDatabase(Path.Combine(directory,"accounts.db"));var auth=new AuthenticationService(db);var admin=auth.SetupAdminAsync("admin","Admin gốc",Password).GetAwaiter().GetResult();var service=new AccountManagementService(db);
            Func<UserAccount,AccountEdit,UserAccount> update=(old,input)=>service.UpdateAsync(admin,old,input).GetAwaiter().GetResult();
            Func<AccountEdit,UserAccount> create=input=>service.CreateAsync(admin,input,Password).GetAwaiter().GetResult();
            var first=Get(service,admin,admin.UserId);Assert(first.Role=="Admin"&&first.IsActive&&service.List(admin).Count==1,"Initial Admin projection wrong.");
            Assert(!typeof(UserAccount).GetProperties().Any(p=>p.Name=="PasswordHash"||p.Name=="SecurityStamp"||p.Name=="Password"),"Public account DTO leaks credentials.");
            Reject<UnauthorizedAccessException>(()=>service.List(null));
            var staff=create(new AccountEdit{UserName=" staff.one ",FullName=" Nhân viên ",Role="Staff"});
            Assert(staff.UserName=="staff.one"&&staff.FullName=="Nhân viên"&&staff.Role=="Staff","Create trim/role wrong.");
            var staffSession=auth.LoginAsync(staff.UserName,Password).GetAwaiter().GetResult();var perms=new PermissionService(db);
            Reject<UnauthorizedAccessException>(()=>service.List(staffSession));Reject<UnauthorizedAccessException>(()=>service.CreateAsync(staffSession,Edit(staff),Password).GetAwaiter().GetResult());
            Sql(db,"INSERT OR IGNORE INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='User.Manage';");Reject<UnauthorizedAccessException>(()=>service.List(staffSession));
            Reject<ArgumentException>(()=>create(new AccountEdit{UserName="STAFF.ONE",FullName="Trùng",Role="Staff"}));
            Reject<ArgumentException>(()=>create(new AccountEdit{UserName="abc",FullName="Tên",Role="Owner"}));
            Reject<ArgumentException>(()=>service.CreateAsync(admin,new AccountEdit{UserName="abc",FullName="Tên",Role="Staff"},"short").GetAwaiter().GetResult());
            var change=Edit(staff);change.FullName="Tên mới";change.UserName="staff.renamed";var renamed=update(staff,change);
            Assert(renamed.Revision!=staff.Revision&&renamed.FullName=="Tên mới"&&renamed.UserName=="staff.renamed"&&perms.GetStaffAccess(staffSession).FullName=="Tên mới","Metadata update/opaque stale revision/current name wrong.");
            Reject<InvalidOperationException>(()=>update(staff,Edit(staff)));Reject<UnauthorizedAccessException>(()=>auth.LoginAsync("staff.one",Password).GetAwaiter().GetResult());
            var manager=update(renamed,Edit(renamed,"Manager"));Assert(manager.Role=="Manager","Role store did not persist exactly one role.");Reject<UnauthorizedAccessException>(()=>perms.GetStaffAccess(staffSession));
            var managerSession=auth.LoginAsync(manager.UserName,Password).GetAwaiter().GetResult();Assert(perms.HasPermission(managerSession,"Report.View"),"New role permission unavailable after login.");Reject<UnauthorizedAccessException>(()=>service.List(managerSession));
            var reset=service.ResetPasswordAsync(admin,manager,NewPassword).GetAwaiter().GetResult();Assert(reset.Revision!=manager.Revision&&reset.Role=="Manager","Reset changed role or returned old token.");
            Reject<UnauthorizedAccessException>(()=>perms.GetStaffAccess(managerSession));Reject<UnauthorizedAccessException>(()=>auth.LoginAsync(reset.UserName,Password).GetAwaiter().GetResult());
            var resetSession=auth.LoginAsync(reset.UserName,NewPassword).GetAwaiter().GetResult();
            var locked=update(reset,Edit(reset,active:false));Reject<UnauthorizedAccessException>(()=>perms.GetStaffAccess(resetSession));Reject<UnauthorizedAccessException>(()=>auth.LoginAsync(locked.UserName,NewPassword).GetAwaiter().GetResult());
            var unlocked=update(locked,Edit(locked,active:true));Reject<UnauthorizedAccessException>(()=>perms.GetStaffAccess(resetSession));Assert(auth.LoginAsync(unlocked.UserName,NewPassword).GetAwaiter().GetResult()!=null,"Unlock changed password or failed.");
            first=Get(service,admin,admin.UserId);Reject<InvalidOperationException>(()=>update(first,Edit(first,active:false)));Reject<InvalidOperationException>(()=>update(first,Edit(first,"Manager")));
            var audit=Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog;"));var original=Get(service,admin,unlocked.UserId);
            Sql(db,"CREATE TRIGGER FailAccountAudit BEFORE INSERT ON AuditLog WHEN NEW.Action='User.Update' BEGIN SELECT RAISE(ABORT,'fixture audit failure'); END;");
            Reject<SQLiteException>(()=>update(original,Edit(original,"Staff",false)));Assert(Get(service,admin,original.UserId).Revision==original.Revision&&Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog;"))==audit,"Update rollback lost metadata/hash/role/stamp/audit atomicity.");Sql(db,"DROP TRIGGER FailAccountAudit;");
            Sql(db,"CREATE TRIGGER FailResetAudit BEFORE INSERT ON AuditLog WHEN NEW.Action='User.ResetPassword' BEGIN SELECT RAISE(ABORT,'fixture reset failure'); END;");
            Reject<SQLiteException>(()=>service.ResetPasswordAsync(admin,original,Password).GetAwaiter().GetResult());Assert(Get(service,admin,original.UserId).Revision==original.Revision,"Reset audit failure persisted hash/stamp.");Sql(db,"DROP TRIGGER FailResetAudit;");
            Assert(auth.LoginAsync(original.UserName,NewPassword).GetAwaiter().GetResult()!=null,"Rolled back reset destroyed prior password.");
            var second=create(new AccountEdit{UserName="admin.two",FullName="Admin hai",Role="Admin"});var secondSession=auth.LoginAsync(second.UserName,Password).GetAwaiter().GetResult();first=Get(service,admin,admin.UserId);
            var start=new ManualResetEventSlim(false);Func<LoginSession,UserAccount,bool> lockOther=(actor,target)=>{start.Wait();try{service.UpdateAsync(actor,target,Edit(target,active:false)).GetAwaiter().GetResult();return true;}catch(UnauthorizedAccessException){return false;}catch(InvalidOperationException){return false;}};
            var a=Task.Run(()=>lockOther(admin,second));var b=Task.Run(()=>lockOther(secondSession,first));start.Set();Assert(Task.WaitAll(new Task[]{a,b},20000),"Concurrent Admin locks timed out.");
            Assert((a.Result?1:0)+(b.Result?1:0)==1&&Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AspNetUsers u JOIN AspNetUserRoles r ON r.UserId=u.Id WHERE u.IsActive=1 AND r.RoleId='Admin';"))==1,"Concurrent locks removed every Admin or both succeeded.");start.Dispose();
            var alive=a.Result?admin:secondSession;
            var barrier=new ManualResetEventSlim(false);Func<string,bool> createSame=name=>{barrier.Wait();try{service.CreateAsync(alive,new AccountEdit{UserName=name,FullName="Trùng đồng thời",Role="Staff"},Password).GetAwaiter().GetResult();return true;}catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}};
            var c=Task.Run(()=>createSame("same.user"));var d=Task.Run(()=>createSame("SAME.USER"));barrier.Set();Assert(Task.WaitAll(new Task[]{c,d},20000)&&((c.Result?1:0)+(d.Result?1:0))==1,"Concurrent normalized username uniqueness failed.");barrier.Dispose();
            Assert(Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AspNetUsers WHERE Id NOT IN(SELECT UserId FROM AspNetUserRoles);"))==0,"Account lost its role.");
            Assert(Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog WHERE Description LIKE '%"+NewPassword+"%' OR Description LIKE '%"+Password+"%';"))==0,"Password leaked to audit.");
            Assert(Convert.ToInt64(Sql(db,"PRAGMA user_version;"))==SqliteDatabase.CurrentSchemaVersion,"Accounts changed schema.");
            Console.WriteLine("PASS Accounts: Identity create/update/reset, current Admin-only RBAC, unique names/one role/private DTO, stale edits, role/password/lock revocation, self/last Admin concurrent protection and atomic audit rollback on temporary SQLite.");
        }
        finally{SQLiteConnection.ClearAllPools();var target=Path.GetFullPath(directory);var temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(!target.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(target).StartsWith("MusicBoxAccounts_",StringComparison.Ordinal))throw new Exception("Unsafe account cleanup.");Directory.Delete(target,true);}
    }
}
