using System;
using System.IO;
using System.Linq;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using SkiaSharp;
public static class MusicBoxMySqlImportChecks
{
    private sealed class Clock:IClock {public DateTimeOffset Value=new DateTimeOffset(2026,10,29,9,0,0,TimeSpan.FromHours(7));public DateTimeOffset UtcNow{get{return Value;}}}
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void Reject<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static object Sql(MySqlDatabase db,string sql){using(var c=db.OpenConnection())using(var q=c.CreateCommand()){q.CommandText=sql;return q.ExecuteScalar();}}
    public static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"MusicBoxImport_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var name="musicbox_test_"+Guid.NewGuid().ToString("N");var target=new MySqlDatabase(name);Console.WriteLine("Import test database: "+name);
        try
        {
            var path=Path.Combine(root,"source","musicbox.db");Directory.CreateDirectory(Path.GetDirectoryName(path));var source=new SqliteDatabase(path);var auth=new AuthenticationService(source);const string password="Import-Verify-2026!";
            var admin=auth.SetupAdminAsync("admin","Admin import",password).GetAwaiter().GetResult();var clock=new Clock();var image=Path.Combine(root,"fixture.png");
            using(var bitmap=new SKBitmap(16,16)){bitmap.Erase(SKColors.Gray);using(var frame=SKImage.FromBitmap(bitmap))using(var bytes=frame.Encode(SKEncodedImageFormat.Png,100))File.WriteAllBytes(image,bytes.ToArray());}
            var room=new RoomService(source,clock).Create(admin,new RoomCreate{RoomCode="IMPORT1",RoomTypeId=1,Name="Phòng import",ImageFilePath=image});
            new ServiceCatalogService(source).Save(admin,null,new ServiceEdit{Name="Trà import",Category="Đồ uống",Price=25000,IsActive=true});
            clock.Value=clock.Value.AddHours(4).AddTicks(1234567);var session=new RoomSessionService(source,clock).CreateWalkIn(admin,new WalkInRequest{RoomId=room,FullName="Khách import",PhoneNumber="0901234567"}).Session;
            new OrderService(source,clock).CreateStaff(admin,session.RoomSessionId,new[]{new OrderLineRequest{ServiceId=1,Quantity=2}});clock.Value=clock.Value.AddHours(1);var invoice=new CheckoutService(source,clock).ConfirmStaff(admin,session.RoomSessionId,"Cash");
            var permissions=new PermissionManagementService(source,clock);permissions.Update(admin,permissions.Read(admin,"Manager"),new[]{"Dashboard.View"});
            var backup=MySqlDataMigration.Backup(path,Path.GetDirectoryName(path),Path.Combine(root,"backups"));MySqlDataMigration.VerifyArchive(backup.DirectoryPath);
            var restored=MySqlDataMigration.RestoreSqliteCopy(backup.DirectoryPath,Path.Combine(root,"restored"));Reject<InvalidOperationException>(()=>MySqlDataMigration.RestoreSqliteCopy(backup.DirectoryPath,Path.Combine(root,"restored")));
            var restoredDb=new SqliteDatabase(restored);var restoredActor=new AuthenticationService(restoredDb).LoginAsync("admin",password).GetAwaiter().GetResult();Check(new InvoiceService(restoredDb).Read(restoredActor,invoice.InvoiceId).Invoice.TotalAmount==invoice.TotalAmount,"Restored copy lost login/invoice");MySqlDataMigration.VerifyArchive(backup.DirectoryPath);
            var corrupt=Path.Combine(Path.GetDirectoryName(path),"corrupt.db");File.Copy(path,corrupt);
            using(var broken=new SqliteDatabase(corrupt).OpenConnection())using(var q=broken.CreateCommand())
            {q.CommandText="UPDATE RoomSessions SET ActualEndTime='"+clock.Value.AddTicks(1).ToUniversalTime().ToString("O")+"' WHERE RoomSessionId="+session.RoomSessionId;q.ExecuteNonQuery();}
            Reject<InvalidOperationException>(()=>MySqlDataMigration.Backup(corrupt,Path.GetDirectoryName(path),Path.Combine(root,"backups")));
            var legacyPath=Path.Combine(root,"legacy.db");
            using(var legacy=new SqliteDatabase(legacyPath).OpenConnection())using(var q=legacy.CreateCommand())
            {
                q.CommandText="CREATE TABLE RoomTypes(RoomTypeId INTEGER PRIMARY KEY,Code TEXT NOT NULL UNIQUE,Name TEXT NOT NULL,Capacity INTEGER NOT NULL,PricePerHour INTEGER NOT NULL,Amenities TEXT NOT NULL,Description TEXT NULL);INSERT INTO RoomTypes VALUES(1,'STANDARD','Standard',4,333333,'TV',NULL),(2,'VIP','VIP',6,444444,'TV',NULL);PRAGMA user_version=1;";q.ExecuteNonQuery();
            }
            var rawLegacy=MySqlDataMigration.Backup(legacyPath,Path.Combine(root,"legacy-content"),Path.Combine(root,"backups"));var prepared=MySqlDataMigration.PrepareV8Snapshot(rawLegacy.DirectoryPath);
            using(var old=new System.Data.SQLite.SQLiteConnection(new System.Data.SQLite.SQLiteConnectionStringBuilder{DataSource=rawLegacy.DatabasePath,ReadOnly=true,Pooling=false}.ConnectionString))
            {old.Open();using(var q=old.CreateCommand()){q.CommandText="PRAGMA user_version";Check(Convert.ToInt32(q.ExecuteScalar())==1,"Preparation modified original archive");}}
            using(var upgraded=new System.Data.SQLite.SQLiteConnection(new System.Data.SQLite.SQLiteConnectionStringBuilder{DataSource=prepared.DatabasePath,ReadOnly=true,Pooling=false}.ConnectionString))
            {upgraded.Open();using(var q=upgraded.CreateCommand()){q.CommandText="SELECT PricePerHour FROM RoomTypes WHERE Code='STANDARD'";Check(Convert.ToInt64(q.ExecuteScalar())==333333,"Upgrade copy changed source room rate");}}
            target.Initialize();Sql(target,"CREATE TRIGGER FailImport BEFORE INSERT ON AuditLog FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='import fixture rollback'");
            Reject<MySqlConnector.MySqlException>(()=>MySqlDataMigration.Import(backup.DirectoryPath,target));Check(Convert.ToInt32(Sql(target,"SELECT COUNT(*) FROM AspNetUsers"))==0&&Convert.ToInt32(Sql(target,"SELECT COUNT(*) FROM RoomTypes"))==2,"Import failure did not restore seed transaction");Sql(target,"DROP TRIGGER FailImport");
            var imported=MySqlDataMigration.Import(backup.DirectoryPath,target);Check(imported.Count==15&&imported.Single(t=>t.Table=="Invoices").Rows==1,"Import count wrong");MySqlDataMigration.Verify(backup.DirectoryPath,target);
            var onlineAuth=new MySqlAuthenticationService(target);var online=onlineAuth.LoginAsync("admin",password).GetAwaiter().GetResult();Check(online.UserId==admin.UserId,"Identity hash/ID changed");
            var actual=new MySqlInvoiceService(target).Read(online,invoice.InvoiceId);Check(actual.Invoice.TotalAmount==invoice.TotalAmount&&actual.ActualStartTime==session.ActualStartTime&&actual.Items.Single().Quantity==2,"Import lost decimal/ticks/snapshot/items");
            Check(new MySqlPermissionManagementService(target).Read(online,"Manager").PermissionCodes.SequenceEqual(new[]{"Dashboard.View"}),"Import re-seeded custom grants");
            var installed=Path.Combine(root,"mysql-content");MySqlDataMigration.InstallContent(backup.DirectoryPath,installed);Reject<InvalidOperationException>(()=>MySqlDataMigration.InstallContent(backup.DirectoryPath,installed));var roomDto=new MySqlRoomService(target,installed).GetForEdit(online,room);Check(File.Exists(new MySqlRoomService(target,installed).GetImagePath(roomDto.ImageUrl)),"Imported photo not readable");
            var auditBefore=Convert.ToInt64(Sql(target,"SELECT COUNT(*) FROM AuditLog"));Reject<InvalidOperationException>(()=>MySqlDataMigration.Import(backup.DirectoryPath,target));Check(Convert.ToInt64(Sql(target,"SELECT COUNT(*) FROM AuditLog"))==auditBefore,"Refused import changed audit");Reject<InvalidOperationException>(()=>MySqlDataMigration.Verify(backup.DirectoryPath,target));
            var snapshotDb=backup.DatabasePath;var bytesOriginal=File.ReadAllBytes(snapshotDb);File.AppendAllText(snapshotDb,"tamper");Reject<InvalidOperationException>(()=>MySqlDataMigration.VerifyArchive(backup.DirectoryPath));File.WriteAllBytes(snapshotDb,bytesOriginal);MySqlDataMigration.VerifyArchive(backup.DirectoryPath);
            Console.WriteLine("PASS import: consistent SQLite Backup+Content/hash manifest, 15 table normalized hashes/IDs/FK/Identity/custom grants/decimal/ticks/snapshots, audit failure rollback, no overwrite/replay, tamper rejection; GUID only");
        }
        catch(Exception error){Console.WriteLine("Import failure before cleanup: "+error);throw;}
        finally
        {
            if(!System.Text.RegularExpressions.Regex.IsMatch(name,@"\Amusicbox_test_[0-9a-f]{32}\z"))throw new Exception("Unsafe DB cleanup");Sql(new MySqlDatabase("defaultdb"),"DROP DATABASE IF EXISTS "+name);
            var full=Path.GetFullPath(root);if(!full.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("MusicBoxImport_",StringComparison.Ordinal))throw new Exception("Unsafe folder cleanup");System.Data.SQLite.SQLiteConnection.ClearAllPools();Directory.Delete(full,true);
        }
    }
}
