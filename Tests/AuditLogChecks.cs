using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxAuditLogChecks
{
    private sealed class Clock : IClock
    {
        public int Reads; public Action OnRead;
        public DateTimeOffset UtcNow { get { Reads++; if(OnRead!=null) OnRead(); return new DateTimeOffset(2026,10,18,8,0,0,TimeSpan.Zero); } }
    }
    private static object Sql(SqliteDatabase db,string sql)
    { using(var c=db.OpenConnection())using(var cmd=c.CreateCommand()){cmd.CommandText=sql;return cmd.ExecuteScalar();} }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject<T>(Action action) where T:Exception
    {try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static int Insert(SqliteDatabase db,string actor,string user,string action,string entity,string time)
    {
        using(var c=db.OpenConnection())using(var cmd=c.CreateCommand())
        {
            cmd.CommandText="INSERT INTO AuditLog(ActorType,UserId,Action,EntityName,EntityId,Description,CreatedAt) VALUES(@actor,@user,@action,@entity,'42','Nội dung đầy đủ',@time); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@actor",actor);cmd.Parameters.AddWithValue("@user",(object)user??DBNull.Value);
            cmd.Parameters.AddWithValue("@action",action);cmd.Parameters.AddWithValue("@entity",entity);cmd.Parameters.AddWithValue("@time",time);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }
    public static void Run()
    {
        var dir=Path.Combine(Path.GetTempPath(),"MusicBoxAuditLog_"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var db=new SqliteDatabase(Path.Combine(dir,"audit.db"));var auth=new AuthenticationService(db);const string password="Audit-Test-2026!";
            var admin=auth.SetupAdminAsync("admin","Admin ban đầu",password).GetAwaiter().GetResult();
            var accounts=new AccountManagementService(db);
            accounts.CreateAsync(admin,new AccountEdit{UserName="staff",FullName="Staff",Role="Staff",IsActive=true},password).GetAwaiter().GetResult();
            accounts.CreateAsync(admin,new AccountEdit{UserName="manager",FullName="Manager",Role="Manager",IsActive=true},password).GetAwaiter().GetResult();
            var staff=auth.LoginAsync("staff",password).GetAwaiter().GetResult();var manager=auth.LoginAsync("manager",password).GetAwaiter().GetResult();
            var clock=new Clock();var service=new AuditLogService(db,clock);var day=new DateTime(2026,10,18);
            var real=new CustomerService(db).Save(admin,null,new CustomerEdit{FullName="Khách thử",PhoneNumber="0901234567"});
            Check(service.Search(admin,action:"Customer.Create",entityName:"Customer").Items.Single().EntityId==real.CustomerId.ToString(),"Real business service audit missing.");
            Sql(db,"DELETE FROM AuditLog;");
            Insert(db,"Staff",admin.UserId,"Boundary","Room","2026-10-17T16:59:59.9999999+00:00");
            var first=Insert(db,"Staff",admin.UserId,"Boundary","Room","2026-10-17T17:00:00+00:00");
            var legacy=Insert(db,"Guest",null,"Boundary","Room","2026-10-17T17:00:00.1Z");
            var last=Insert(db,"System",null,"Boundary","Room","2026-10-18T16:59:59.9999999+00:00");
            Insert(db,"Staff",admin.UserId,"Boundary","Room","2026-10-18T17:00:00.0000000+00:00");
            var filtered=service.Search(admin,day,day);Check(filtered.Items.Select(i=>i.AuditLogId).SequenceEqual(new[]{last,legacy,first}),"UTC+7 inclusive days, ticks or legacy fractions lost.");
            Check(service.Read(admin,last).CreatedAt.Ticks%TimeSpan.TicksPerSecond==9999999,"Timestamp precision lost.");
            Check(service.Read(admin,legacy).ActorLabel=="Khách"&&service.Read(admin,last).UserLabel=="-"&&service.Read(admin,first).CreatedLabel=="00:00:00 18/10/2026","Public labels/UTC+7 wrong.");
            var old=service.Read(admin,first);Sql(db,"UPDATE AspNetUsers SET FullName='Tên hiện hành' WHERE Id='"+admin.UserId+"';");
            Check(service.Read(admin,first).UserLabel=="Tên hiện hành (admin)"&&old.FullName=="Admin ban đầu","User metadata not current or detached.");
            Check(service.Search(admin,day,day," Guest "," Boundary "," Room ").Items.Single().AuditLogId==legacy,"Filters should trim and combine AND.");
            Check(service.Search(admin,actorType:"Guest",action:"boundary").Items.Count==0&&service.Search(admin,entityName:"Missing").Items.Count==0,"Exact filters leaked nonmatches.");
            clock.Reads=0;var empty=service.Search(admin,action:"Missing");Check(clock.Reads==1&&empty.CheckedAt.Offset==TimeSpan.Zero&&!empty.HasMore&&empty.NextBeforeId==null,"Clock/page empty contract wrong.");
            var page=service.Search(admin,pageSize:2);Check(page.HasMore&&page.NextBeforeId==page.Items.Last().AuditLogId,"Cursor contract wrong.");
            Insert(db,"Guest",null,"New","Room","2026-10-18T10:00:00Z");
            var ids=new List<int>(page.Items.Select(i=>i.AuditLogId));
            while(page.HasMore){page=service.Search(admin,beforeId:page.NextBeforeId,pageSize:2);ids.AddRange(page.Items.Select(i=>i.AuditLogId));}
            Check(ids.Count==5&&ids.Distinct().Count()==5,"Cursor paging duplicated/lost original records after new insert.");
            var offsetId=Insert(db,"System",null,"Offset","Room","2026-10-18T00:00:00.0000000+07:00");
            Check(service.Search(admin,day,day,action:"Offset").Items.Single().AuditLogId==offsetId,"Legacy explicit timezone offset lost in day filter.");
            var readOnly=service.Search(admin).Items as IList<AuditLogEntry>;
            Check(readOnly!=null&&readOnly.IsReadOnly,"Items mutable.");Reject<NotSupportedException>(()=>readOnly.Clear());
            Reject<ArgumentException>(()=>service.Search(admin,pageSize:0));Reject<ArgumentException>(()=>service.Search(admin,pageSize:201));
            Reject<ArgumentException>(()=>service.Search(admin,beforeId:0));Reject<ArgumentException>(()=>service.Search(admin,day.AddDays(1),day));
            Reject<ArgumentException>(()=>service.Search(admin,actorType:"staff"));Reject<ArgumentException>(()=>service.Search(admin,action:new string('a',101)));
            Reject<ArgumentException>(()=>service.Search(admin,entityName:new string('a',101)));Reject<ArgumentException>(()=>service.Read(admin,0));
            Reject<ArgumentException>(()=>service.Search(admin,toDate:DateTime.MaxValue));Reject<ArgumentException>(()=>service.Search(admin,fromDate:DateTime.MinValue));
            Reject<InvalidOperationException>(()=>service.Read(admin,int.MaxValue));
            Sql(db,"INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code='Audit.View'; INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Audit.View';");
            foreach(var actor in new[]{staff,manager,(LoginSession)null})
            {Reject<UnauthorizedAccessException>(()=>service.Search(actor));Reject<UnauthorizedAccessException>(()=>service.Read(actor,first));}
            var count=Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog;"));
            Sql(db,"CREATE TRIGGER AuditReadOnlyInsert BEFORE INSERT ON AuditLog BEGIN SELECT RAISE(ABORT,'read must not log'); END; CREATE TRIGGER AuditReadOnlyUpdate BEFORE UPDATE ON AuditLog BEGIN SELECT RAISE(ABORT,'read must not update'); END; CREATE TRIGGER AuditReadOnlyDelete BEFORE DELETE ON AuditLog BEGIN SELECT RAISE(ABORT,'read must not delete'); END;");
            service.Search(admin);service.Read(admin,first);Check(Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog;"))==count,"Read changed audit data.");
            // WAL permits a writer while a read snapshot is open. Clock hook runs after current
            // permissions establish that snapshot; account labels and rows must use it too.
            Sql(db,"PRAGMA journal_mode=WAL;");
            clock.OnRead=()=>{Sql(db,"UPDATE AspNetUsers SET FullName='Đổi sau snapshot',IsActive=0 WHERE Id='"+admin.UserId+"';");clock.OnRead=null;};
            var snapshot=service.Search(admin);Check(snapshot.Items.First(i=>i.UserId==admin.UserId).FullName=="Tên hiện hành","Account/audit reads did not share permission snapshot.");
            Reject<UnauthorizedAccessException>(()=>service.Search(admin));Reject<UnauthorizedAccessException>(()=>service.Read(admin,first));
            Sql(db,"UPDATE AspNetUsers SET IsActive=1,SecurityStamp='revoked' WHERE Id='"+admin.UserId+"';");Reject<UnauthorizedAccessException>(()=>service.Search(admin));
            Sql(db,"DROP TRIGGER AuditReadOnlyInsert; DROP TRIGGER AuditReadOnlyUpdate; DROP TRIGGER AuditReadOnlyDelete;");
            admin=auth.LoginAsync("admin",password).GetAwaiter().GetResult();Sql(db,"UPDATE AspNetUserRoles SET RoleId='Manager' WHERE UserId='"+admin.UserId+"';");Reject<UnauthorizedAccessException>(()=>service.Search(admin));
            Sql(db,"UPDATE AspNetUserRoles SET RoleId='Admin' WHERE UserId='"+admin.UserId+"';");auth.Logout(admin);Reject<UnauthorizedAccessException>(()=>service.Read(admin,first));
            Check(Convert.ToInt32(Sql(db,"PRAGMA user_version;"))==SqliteDatabase.CurrentSchemaVersion,"Read service changed schema.");
            Console.WriteLine("Audit log checks passed (temporary database, read-only/snapshot/date precision/cursor/current authorization). ");
        }
        finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
    }
}
