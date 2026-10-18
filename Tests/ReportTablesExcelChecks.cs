using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;

public static class MusicBoxReportTablesExcelChecks
{
    private sealed class Clock:IClock{public int Reads;public DateTimeOffset Value=new DateTimeOffset(2026,10,18,10,0,0,TimeSpan.FromHours(7));public DateTimeOffset UtcNow{get{Reads++;return Value;}}}
    private static object Sql(SqliteDatabase db,string sql){using(var c=db.OpenConnection())using(var q=c.CreateCommand()){q.CommandText=sql;return q.ExecuteScalar();}}
    private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static XmlDocument Sheet(ReportExcelFile file)
    {
        using(var stream=new MemoryStream(file.GetBytes()))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))
        {
            Assert(zip.Entries.Count==6,"Invalid workbook package.");
            foreach(var entry in zip.Entries)using(var input=entry.Open()){var check=new XmlDocument();check.Load(input);}
            var sheet=new XmlDocument();using(var input=zip.GetEntry("xl/worksheets/sheet1.xml").Open())sheet.Load(input);
            Assert(sheet.SelectNodes("//*[namespace-uri()='']").Count==0&&sheet.SelectNodes("//*[local-name()='f']").Count==0,"Wrong namespace/formula injection.");return sheet;
        }
    }
    private static string Cell(XmlDocument doc,string address){var node=doc.SelectSingleNode("//*[local-name()='c' and @r='"+address+"']");return node==null?null:node.InnerText;}
    private static string Format(XmlDocument doc,string address){return doc.SelectSingleNode("//*[local-name()='c' and @r='"+address+"']").Attributes["s"].Value;}
    private static decimal Rounded(decimal value){return decimal.Parse(value.ToString("N2",CultureInfo.GetCultureInfo("vi-VN")),CultureInfo.GetCultureInfo("vi-VN"));}
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);var root=Path.Combine(Path.GetTempPath(),"MusicBoxTablesExcel_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var db=new SqliteDatabase(Path.Combine(root,"reports.db"));var auth=new AuthenticationService(db);const string password="Tables-Excel-2026!";
            var admin=auth.SetupAdminAsync("admin","Admin",password).GetAwaiter().GetResult();var accounts=new AccountManagementService(db);
            accounts.CreateAsync(admin,new AccountEdit{UserName="manager",FullName="Manager",Role="Manager"},password).GetAwaiter().GetResult();
            accounts.CreateAsync(admin,new AccountEdit{UserName="staff",FullName="Staff",Role="Staff"},password).GetAwaiter().GetResult();
            Sql(db,"DELETE FROM RolePermission WHERE RoleId IN ('Staff','Manager');INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.View';INSERT INTO RolePermission SELECT 'Staff',PermissionId FROM Permission WHERE Code IN ('Report.View','Report.Export');");
            var manager=auth.LoginAsync("manager",password).GetAwaiter().GetResult();var staff=auth.LoginAsync("staff",password).GetAwaiter().GetResult();
            var clock=new Clock();var rooms=new RoomUsageReportService(db,clock);var bookings=new BookingStatusReportService(db,clock);var sales=new ServiceSalesReportService(db,clock);var day=clock.Value.Date;
            Action<LoginSession>[] exports={s=>rooms.Export(s,day,day),s=>bookings.Export(s,day,day),s=>sales.Export(s,day,day),s=>sales.Export(s,day,day,null,true)};
            foreach(var export in exports){Reject<UnauthorizedAccessException>(()=>export(manager));Reject<UnauthorizedAccessException>(()=>export(staff));Reject<UnauthorizedAccessException>(()=>export(null));}
            Assert(!rooms.Read(manager,day,day).CanExport&&!bookings.Read(manager,day,day).CanExport&&!sales.Read(manager,day,day).CanExport,"View-only metadata allows export.");
            Sql(db,"INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.Export';");
            var empty=Sheet(bookings.Export(manager,day,day));Assert(Cell(empty,"B13")=="0"&&Cell(empty,"A13")=="TỔNG","Empty booking missing five zero statuses/total.");
            Assert(Cell(Sheet(rooms.Export(manager,day,day)),"G8")=="Không có dữ liệu","Zero denominator converted to 0%.");
            Sql(db,@"INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,InactiveReason,CreatedAt) VALUES
(1,'R1',1,'Phòng dài & tiếng Việt — mô tả phòng dùng để kiểm tra tên dài không bị cắt trong Excel','test.png',0,'Bảo trì','2026-10-18T02:00:00.0000000+00:00'),
(2,'R2',1,'Phòng hai','test.png',1,NULL,'2026-10-18T02:30:00.0000000+00:00');
INSERT INTO Customers(CustomerId,FullName,PhoneNumber) VALUES(1,'PRIVATE','0901111111'),(2,'PRIVATE TWO','0901111112');
INSERT INTO RoomSessions(RoomSessionId,CustomerId,RoomId,ActualStartTime,ActualEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES
(1,1,1,'2026-10-18T02:00:00.0000000+00:00','2026-10-18T02:30:00.0000000+00:00',120000,'R1','STANDARD','Standard','Completed'),
(2,2,2,'2026-10-18T02:30:00.0000000+00:00',NULL,120000,'R2','STANDARD','Standard','Active');
INSERT INTO Reservations(CustomerId,RoomId,StartTime,EndTime,Status,CreatedAt) VALUES
(1,1,'2026-10-18T02:45:00.0000000+00:00','2026-10-18T03:45:00.0000000+00:00','Confirmed','2026-10-01'),
(2,2,'2026-10-18T04:00:00.0000000+00:00','2026-10-18T05:00:00.0000000+00:00','Cancelled','2026-10-01');
INSERT INTO Orders(OrderId,RoomSessionId,Status,CreatedAt) VALUES(1,1,'Completed','2026-10-18T02:10:00.0000000+00:00');");
            Sql(db,"INSERT INTO Invoices(RoomSessionId,InvoiceNumber,RoomCharge,ServiceCharge,TotalAmount,PaymentMethod,ProcessedByUserId,ProcessedByNameSnapshot,PaidAt) VALUES(1,'PRIVATE-INVOICE','60000','500000','560000','Cash','"+admin.UserId+"','PRIVATE CASHIER','2026-10-18T02:30:00.0000000+00:00');");
            for(var id=1;id<=13;id++)Sql(db,"INSERT INTO Services(ServiceId,Name,Category,Price,IsActive) VALUES("+id+",'Current name','Khác',99999,"+(id==1?"0":"1")+");INSERT INTO OrderItems(OrderId,ServiceId,ServiceNameSnapshot,Quantity,UnitPrice) VALUES(1,"+id+",'"+(id==1?"=1+1 & snapshot":"Món snapshot "+id)+"',"+(id%10+1)+","+(id*1000)+");");
            var audit=Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog"));
            var roomData=rooms.Read(manager,day,day);clock.Reads=0;var roomFile=rooms.Export(manager,day,day);var room=Sheet(roomFile);
            Assert(clock.Reads==1&&Cell(room,"C8")=="Đã khóa"&&Cell(room,"D8")=="1"&&Cell(room,"A3").Contains("đã khóa"),"Inactive/completed/one clock export failed.");
            Assert(Format(room,"B8")=="10"&&decimal.Parse(room.SelectSingleNode("//*[local-name()='row' and @r='8']").Attributes["ht"].Value,CultureInfo.InvariantCulture)>=45,"Long room name is not wrapped/sized.");
            Assert(decimal.Parse(Cell(room,"E10"),CultureInfo.InvariantCulture)==Rounded(roomData.UsedMinutes)&&decimal.Parse(Cell(room,"G10"),CultureInfo.InvariantCulture)==Rounded(roomData.UtilizationPercent.Value)/100m&&Format(room,"G10")=="9","Weighted total/rounding/percentage failed.");
            var roomFiltered=Sheet(rooms.Export(manager,day,day,2));Assert(Cell(roomFiltered,"A8")=="R2"&&Cell(roomFiltered,"D9")=="0"&&Cell(roomFiltered,"A3").Contains("R2"),"Room filter ignored.");
            var bookingFile=bookings.Export(manager,day,day);var booking=Sheet(bookingFile);
            Assert(Cell(booking,"B8")=="0"&&Cell(booking,"B11")=="1"&&Cell(booking,"B12")=="1"&&Cell(booking,"B13")=="2","Effective NoShow/zero statuses failed.");
            Assert((string)Sql(db,"SELECT Status FROM Reservations WHERE ReservationId=1")=="Confirmed","Export wrote NoShow.");
            var saleData=sales.Read(manager,day,day);var saleFile=sales.Export(manager,day,day);var sale=Sheet(saleFile);
            var saleTotal=8+saleData.Rows.Count;Assert(Cell(sale,"C"+saleTotal)==saleData.TotalQuantity.ToString(CultureInfo.InvariantCulture)&&Cell(sale,"D"+saleTotal)==saleData.TotalRevenue.ToString(CultureInfo.InvariantCulture),"Service full totals differ from query.");
            var topFile=sales.Export(manager,day,day,null,true);var top=Sheet(topFile);
            Assert(Cell(top,"A18")=="TỔNG TOP 10"&&Cell(top,"C18")==saleData.TopItems.Sum(r=>r.Quantity).ToString()&&Cell(top,"D18")==saleData.TopItems.Sum(r=>r.Revenue).ToString(CultureInfo.InvariantCulture),"Top total includes hidden items.");
            for(var i=0;i<10;i++)Assert(Cell(top,"A"+(i+8))==saleData.TopItems[i].ServiceId.ToString(),"Top order changed.");
            var filterFile=sales.Export(manager,day,day,1);var filtered=Sheet(filterFile);
            Assert(Cell(filtered,"B8")=="=1+1 & snapshot"&&Cell(filtered,"D8")=="2000"&&Cell(filtered,"A3").Contains("ngừng bán"),"Inactive/filter/snapshot/formula-as-text failed.");
            foreach(var file in new[]{roomFile,bookingFile,saleFile,topFile,filterFile})
            {var sheet=Sheet(file);Assert(!sheet.InnerText.Contains("PRIVATE")&&!sheet.InnerText.Contains("0901111111"),"Export leaked PII.");file.SaveTo(Path.Combine(output,file==filterFile?"MusicBox_Mon_Loc.xlsx":file.FileName));}
            Assert(Convert.ToInt64(Sql(db,"SELECT COUNT(*) FROM AuditLog"))==audit,"Export wrote audit.");
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Manager' AND PermissionId=(SELECT PermissionId FROM Permission WHERE Code='Report.Export');");foreach(var export in exports)Reject<UnauthorizedAccessException>(()=>export(manager));
            Sql(db,"DELETE FROM RolePermission WHERE RoleId='Manager';INSERT INTO RolePermission SELECT 'Manager',PermissionId FROM Permission WHERE Code='Report.Export';");foreach(var export in exports)Reject<UnauthorizedAccessException>(()=>export(manager));
            Console.WriteLine("PASS report tables Excel: View+Export same-query snapshots, room filter/inactive/weighted percentage/NA, five booking statuses/effective grace no writes, ServiceId snapshots/inactive/Top10-only total, literal formula text, no PII and valid packages.");
        }
        finally
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();var target=Path.GetFullPath(root);var temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!target.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(target).StartsWith("MusicBoxTablesExcel_",StringComparison.Ordinal))throw new Exception("Unsafe cleanup.");Directory.Delete(target,true);
        }
    }
}
