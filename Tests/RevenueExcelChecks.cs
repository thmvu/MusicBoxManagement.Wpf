using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxRevenueExcelChecks
{
    private sealed class Clock:IClock {public int Reads;public DateTimeOffset UtcNow{get{Reads++;return new DateTimeOffset(2026,10,20,9,0,0,TimeSpan.FromHours(7));}}}
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static object Sql(SqliteDatabase db,string sql){using(var c=db.OpenConnection())using(var q=c.CreateCommand()){q.CommandText=sql;return q.ExecuteScalar();}}
    private static XmlDocument Sheet(ReportExcelFile file)
    {
        using(var stream=new MemoryStream(file.GetBytes()))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))
        {
            Assert(zip.Entries.Count==6,"Workbook part count incorrect.");
            foreach(var entry in zip.Entries){using(var content=entry.Open()){var doc=new XmlDocument();doc.Load(content);}}
            Assert(zip.GetEntry("xl/vbaProject.bin")==null,"Macros present.");
            var sheet=new XmlDocument();using(var content=zip.GetEntry("xl/worksheets/sheet1.xml").Open())sheet.Load(content);
            Assert(sheet.SelectNodes("//*[namespace-uri()='']").Count==0,"Spreadsheet elements lost their namespace.");
            Assert(sheet.SelectNodes("//*[local-name()='f']").Count==0,"Unexpected formula in static authoritative report.");
            return sheet;
        }
    }
    private static string Cell(XmlDocument sheet,string address){var node=sheet.SelectSingleNode("//*[local-name()='c' and @r='"+address+"']");return node==null?null:node.InnerText;}
    private static void Invoice(SqliteDatabase db,string user,int id,string type,string paid,decimal total)
    {
        Sql(db,"INSERT INTO Customers VALUES("+id+",'PRIVATE','090000"+id.ToString("0000")+"');INSERT INTO RoomSessions(RoomSessionId,CustomerId,RoomId,ActualStartTime,ActualEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES("+id+","+id+",1,'2026-10-18T02:00:00.0000000+00:00','2026-10-18T03:00:00.0000000+00:00',120000,'R1','"+type+"','Historical','Completed');INSERT INTO Invoices(RoomSessionId,InvoiceNumber,RoomCharge,ServiceCharge,TotalAmount,PaymentMethod,ProcessedByUserId,ProcessedByNameSnapshot,PaidAt) VALUES("+id+",'EXCEL-"+id+"','"+total.ToString(CultureInfo.InvariantCulture)+"','0','"+total.ToString(CultureInfo.InvariantCulture)+"','Cash','"+user+"','Admin','"+paid+"');");
    }
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);var root=Path.Combine(Path.GetTempPath(),"MusicBoxExcelData_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var db=new SqliteDatabase(Path.Combine(root,"excel.db"));var auth=new AuthenticationService(db);const string password="Excel-Test-2026!";
            var admin=auth.SetupAdminAsync("admin","Admin",password).GetAwaiter().GetResult();var accounts=new AccountManagementService(db);
            accounts.CreateAsync(admin,new AccountEdit{UserName="manager",FullName="Manager",Role="Manager"},password).GetAwaiter().GetResult();
            accounts.CreateAsync(admin,new AccountEdit{UserName="staff",FullName="Staff",Role="Staff"},password).GetAwaiter().GetResult();
            Sql(db,"DELETE FROM RolePermission WHERE RoleId IN ('Staff','Manager');INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.View';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code IN ('Report.View','Report.Export');INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,CreatedAt) VALUES(1,'R1',1,'Phòng','test.png',1,'fixture');");
            var manager=auth.LoginAsync("manager",password).GetAwaiter().GetResult();var staff=auth.LoginAsync("staff",password).GetAwaiter().GetResult();
            var clock=new Clock();var service=new ReportService(db,clock);var from=new DateTime(2026,10,18);var to=from.AddDays(1);
            Assert(!service.ReadRevenue(manager,from,to).CanExport,"View-only can export.");clock.Reads=0;
            Reject<UnauthorizedAccessException>(()=>service.ExportRevenue(manager,from,to));Assert(clock.Reads==0,"Denied export read data/clock.");
            Reject<UnauthorizedAccessException>(()=>service.ExportRevenue(staff,from,to));Reject<UnauthorizedAccessException>(()=>service.ExportRevenue(null,from,to));
            Sql(db,"INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.Export';");
            Invoice(db,admin.UserId,1,"STANDARD","2026-10-18T03:00:00.0000000+00:00",150000);
            Invoice(db,admin.UserId,2,"VIP","2026-10-19T03:00:00.0000000+00:00",250000);
            var audit=Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog"));
            foreach(var mode in new[]{"Day","Month","RoomType"})
            {
                clock.Reads=0;var file=service.ExportRevenue(manager,from,to,null,mode);var sheet=Sheet(file);
                Assert(clock.Reads==1&&Cell(sheet,"A3").Contains(mode=="Day"?"Theo ngày":mode=="Month"?"Theo tháng":"Theo loại phòng"),"Export mode/one clock failed.");
                var totalRow=mode=="Month"?9:10;
                Assert(Cell(sheet,"A"+totalRow)=="TỔNG"&&Cell(sheet,"B"+totalRow)=="2"&&Cell(sheet,"E"+totalRow)=="400000","Wrong row/total by grouping.");
                Assert(Cell(sheet,"A2").Contains("18/10/2026")&&Cell(sheet,"A2").Contains("19/10/2026")&&Cell(sheet,"A4").Contains("UTC+7"),"Filter/export time absent.");
                Assert(!sheet.InnerText.Contains("PRIVATE")&&!sheet.InnerText.Contains("090000")&&!sheet.InnerText.Contains("EXCEL-"),"Report leaked PII/invoice identifiers.");
                file.SaveTo(Path.Combine(output,file.FileName));
            }
            var filtered=Sheet(service.ExportRevenue(manager,from,to,"VIP","Day"));Assert(Cell(filtered,"E8")=="250000"&&Cell(filtered,"B9")=="1"&&Cell(filtered,"A3").Contains("VIP"),"Snapshot type filter failed.");
            Assert(filtered.SelectSingleNode("//*[local-name()='c' and @r='E8']").Attributes["t"].Value=="n","Normal amount not numeric.");
            var empty=Sheet(service.ExportRevenue(admin,to.AddDays(1),to.AddDays(1)));Assert(Cell(empty,"A8")=="TỔNG"&&Cell(empty,"E8")=="0","Empty workbook totals failed.");
            Reject<ArgumentException>(()=>service.ExportRevenue(admin,from,to,null,"Invalid"));
            Invoice(db,admin.UserId,3,"STANDARD","2026-12-01T03:00:00.0000000+00:00",decimal.MaxValue);
            var largeFile=service.ExportRevenue(admin,new DateTime(2026,12,1),new DateTime(2026,12,1));var large=Sheet(largeFile);
            Assert(Cell(large,"E8")==decimal.MaxValue.ToString(CultureInfo.InvariantCulture)+" đ"&&large.SelectSingleNode("//*[local-name()='c' and @r='E8']").Attributes["t"].Value=="inlineStr","Large amount rounded in Excel.");
            Assert(large.SelectSingleNode("//*[local-name()='c' and @r='E8']").Attributes["s"].Value=="4","Large amount not explicitly formatted as text.");
            largeFile.SaveTo(Path.Combine(output,"MusicBox_DoanhThu_SoTienLon.xlsx"));
            var target=Path.Combine(root,"replace.xlsx");File.WriteAllText(target,"original");
            using(var locked=new FileStream(target,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                Reject<IOException>(()=>largeFile.SaveTo(target));
            Assert(File.ReadAllText(target)=="original"&&Directory.GetFiles(root,"*.tmp").Length==0,"Failed save overwrote original/leaked temporary file.");
            File.SetAttributes(target,FileAttributes.ReadOnly);
            try{Reject<IOException>(()=>largeFile.SaveTo(target));Assert(File.ReadAllText(target)=="original","Read-only target was overwritten.");}
            finally{File.SetAttributes(target,FileAttributes.Normal);}
            largeFile.SaveTo(target);Assert(File.ReadAllBytes(target)[0]==80,"Atomic replacement failed.");
            Reject<ArgumentException>(()=>largeFile.SaveTo(Path.Combine(root,"wrong.csv")));
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Report.Export');");
            Reject<UnauthorizedAccessException>(()=>service.ExportRevenue(manager,from,to));Assert(service.ReadRevenue(manager,from,to).InvoiceCount==2,"Export revocation removed View unexpectedly.");
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Manager';INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.Export';");
            Reject<UnauthorizedAccessException>(()=>service.ExportRevenue(manager,from,to));
            Assert(Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog"))==audit,"Export wrote audit/business data.");
            Console.WriteLine("PASS Revenue Excel: same authorized query/filter/grouping, View+Export current roles, valid ZIP/XML/namespace, metadata/totals/empty, numeric and exact large money, no PII/formulas/macros, atomic locked-file safety.");
        }
        finally
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();var target=Path.GetFullPath(root);var temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!target.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(target).StartsWith("MusicBoxExcelData_",StringComparison.Ordinal))throw new Exception("Unsafe cleanup.");Directory.Delete(target,true);
        }
    }
}
