using System;
using System.Diagnostics;
using System.Threading.Tasks;
using MusicBoxManagement.Wpf.Data;
using MySqlConnector;

public static class MusicBoxMySqlInitializeChecks
{
    private static object Sql(MySqlConnection c, string sql)
    { using (var q = c.CreateCommand()) { q.CommandText = sql; return q.ExecuteScalar(); } }
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        var name = "musicbox_test_" + Guid.NewGuid().ToString("N");
        var db = new MySqlDatabase(name);
        Console.WriteLine("Initialize fixture: " + name);
        try
        {
            db.Initialize(); // Missing database fallback, real DDL and seed.
            using (var c = db.OpenConnection())
            {
                Assert(Convert.ToInt32(Sql(c, "SELECT COUNT(*) FROM Permission")) == 27, "Fresh seed missing.");
                var lockSql = "'musicbox-schema:" + name + "'";
                Assert(Convert.ToInt32(Sql(c, "SELECT GET_LOCK(" + lockSql + ",0)")) == 1, "Fixture lock unavailable.");
                var watch = Stopwatch.StartNew();
                var reader = Task.Run(() => db.Initialize());
                bool finished;
                try { finished = reader.Wait(TimeSpan.FromSeconds(5)); }
                finally { Sql(c, "SELECT RELEASE_LOCK(" + lockSql + ")"); }
                reader.GetAwaiter().GetResult();
                Assert(finished, "Ready schema initialization waited for unnecessary schema lock.");
                Console.WriteLine("Ready initialize under held schema lock: " + watch.ElapsedMilliseconds + "ms");
                Assert(Convert.ToInt32(Sql(c, "SELECT COUNT(*) FROM AuditLog")) == 0, "Read init wrote audit.");
                Sql(c, "UPDATE _MusicBoxSchemaVersion SET Version=99 WHERE Id=1");
                var blocked = false;
                try { db.Initialize(); } catch (InvalidOperationException e) { blocked = e.Message.Contains("mới hơn"); }
                Assert(blocked, "Same instance cached version or accepted newer schema.");
                Sql(c, "UPDATE _MusicBoxSchemaVersion SET Version=1 WHERE Id=1");
                db.Initialize();
                // Do not guess recovery on unmanaged contents when marker table is missing.
                Sql(c, "DROP TABLE _MusicBoxSchemaVersion");
                blocked = false;
                try { db.Initialize(); } catch (InvalidOperationException e) { blocked = e.Message.Contains("chưa được quản lý"); }
                Assert(blocked, "Unmanaged database was changed instead of rejected.");
                // Owned GUID only: simulate an interrupted empty version0 installation.
                Sql(c, "DROP DATABASE `" + name + "`");
            }
            using (var c = new MySqlDatabase("defaultdb").OpenConnection())
            {
                Sql(c, "CREATE DATABASE `" + name + "` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_bin");
                Sql(c, "CREATE TABLE `" + name + "`._MusicBoxSchemaVersion(Id INT PRIMARY KEY,Version INT NOT NULL)");
            }
            db.Initialize(); // Missing marker row: old recovery inserts version0 and initializes.
            using (var c = db.OpenConnection())
            {
                Assert(Convert.ToInt32(Sql(c, "SELECT Version FROM _MusicBoxSchemaVersion WHERE Id=1")) == 1, "Missing row recovery failed.");
                Assert(Convert.ToInt32(Sql(c, "SELECT COUNT(*) FROM RoomTypes")) == 2, "Recovery seed missing.");
            }
            Console.WriteLine("PASS initialize fast read/current guard/missing database/unmanaged marker/missing row recovery; owned GUID only.");
        }
        catch (Exception e) { Console.WriteLine("Initialize failure before cleanup: " + e); throw; }
        finally
        {
            using (var c = new MySqlDatabase("defaultdb").OpenConnection()) Sql(c, "DROP DATABASE IF EXISTS `" + name + "`");
        }
    }
}
