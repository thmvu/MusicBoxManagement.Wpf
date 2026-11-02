using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using MusicBoxManagement.Wpf.Data;
using MusicBoxManagement.Wpf.Models;
using MusicBoxManagement.Wpf.Services;
using MySqlConnector;

public static class MusicBoxMySqlOrderBatchChecks
{
    private sealed class Clock : IClock
    {
        public DateTimeOffset Value;
        public DateTimeOffset UtcNow { get { return Value; } }
    }
    private static readonly MethodInfo Batch = typeof(MySqlOrderService).GetMethod("ReadOrders", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly MethodInfo Single = typeof(MySqlOrderService).GetMethod("ReadOrder", BindingFlags.Static | BindingFlags.NonPublic);
    private static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static object Sql(MySqlDatabase db, string sql)
    { using (var c = db.OpenConnection()) using (var q = c.CreateCommand()) { q.CommandText = sql; return q.ExecuteScalar(); } }
    private static List<ServiceOrder> Read(MySqlConnection c, MySqlTransaction tx, int session)
    { return (List<ServiceOrder>)Batch.Invoke(null, new object[] { c, tx, session }); }
    private static long Selects(MySqlConnection c, MySqlTransaction tx)
    {
        using (var q = c.CreateCommand())
        {
            q.Transaction = tx; q.CommandText = "SHOW SESSION STATUS LIKE 'Com_select';";
            using (var r = q.ExecuteReader()) { Assert(r.Read(), "Missing session Com_select"); return long.Parse(r.GetString(1), CultureInfo.InvariantCulture); }
        }
    }
    private static string Signature(IEnumerable<ServiceOrder> orders)
    {
        return string.Join("|", orders.Select(o => o.OrderId + ":" + o.Status + ":" + o.CreatedAt.ToString("O", CultureInfo.InvariantCulture) + ":" +
            string.Join(";", o.Items.Select(i => i.ServiceId + "," + i.ServiceNameSnapshot + "," + i.Quantity + "," + i.UnitPrice))));
    }
    private static void EquivalentToSingle(MySqlConnection c, MySqlTransaction tx, int session, int[] expectedIds, List<string> budgets)
    {
        var before = Selects(c, tx);
        var actual = Read(c, tx, session);
        var count = Selects(c, tx) - before;
        Console.WriteLine("ReadOrders session=" + session + " orders=" + expectedIds.Length + " Com_select=" + count);
        Assert(actual.Select(o => o.OrderId).SequenceEqual(expectedIds), "Order sorting/session isolation changed");
        var expected = expectedIds.Select(id => (ServiceOrder)Single.Invoke(null, new object[] { c, tx, id })).ToList();
        Assert(Signature(actual) == Signature(expected), "Batched orders differ from single-order oracle (status/ticks/item ordering/snapshot/quantity/Int64 price)");
        if (count > 1) budgets.Add("orders=" + expectedIds.Length + ": actual SELECTs " + count + ", expected <=1");
    }
    private static OrderLineRequest[] Cart(params int[] services)
    { return services.Select(id => new OrderLineRequest { ServiceId = id, Quantity = id == 1 ? 10 : 2 }).ToArray(); }
    public static void Run()
    {
        var name = "musicbox_test_" + Guid.NewGuid().ToString("N");
        var db = new MySqlDatabase(name);
        Console.WriteLine("Order batching GUID database: " + name);
        try
        {
            var auth = new MySqlAuthenticationService(db);
            var admin = auth.SetupAdminAsync("admin", "Admin", "Order-Batch-2026!").GetAwaiter().GetResult();
            var clock = new Clock { Value = new DateTimeOffset(2026, 11, 2, 13, 0, 0, TimeSpan.FromHours(7)).AddTicks(1234567) };
            var rooms = new MySqlRoomSessionService(db, clock);
            for (var id = 1; id <= 3; id++)
                Sql(db, "INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,CreatedAt) VALUES(" + id + ",'OB" + id + "',1,'Order batch','test.png',1,'" + clock.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + "');");
            var empty = rooms.CreateWalkIn(admin, new WalkInRequest { RoomId = 1, FullName = "Empty", PhoneNumber = "0901000101" }).Session.RoomSessionId;
            var main = rooms.CreateWalkIn(admin, new WalkInRequest { RoomId = 2, FullName = "Main", PhoneNumber = "0901000102" }).Session.RoomSessionId;
            var other = rooms.CreateWalkIn(admin, new WalkInRequest { RoomId = 3, FullName = "Other", PhoneNumber = "0901000103" }).Session.RoomSessionId;
            var catalog = new MySqlServiceCatalogService(db);
            catalog.Save(admin, null, new ServiceEdit { Name = "Snapshot tea", Category = "Đồ uống", Price = long.MaxValue, IsActive = true });
            catalog.Save(admin, null, new ServiceEdit { Name = "Snapshot cake", Category = "Đồ ăn", Price = 35000, IsActive = true });
            var orders = new MySqlOrderService(db, clock);
            var ids = new List<int>(); var budgets = new List<string>();
            using (var c = db.OpenConnection())
            using (var tx = MySqlDatabase.BeginReadTransaction(c))
                EquivalentToSingle(c, tx, empty, new int[0], budgets);
            ids.Add(orders.CreateGuest("0901000102", main, Cart(2, 1)).OrderId);
            using (var c = db.OpenConnection())
            using (var tx = MySqlDatabase.BeginReadTransaction(c))
                EquivalentToSingle(c, tx, main, ids.ToArray(), budgets);
            clock.Value = clock.Value.AddTicks(1);
            var completed = orders.CreateStaff(admin, main, Cart(2)); ids.Add(completed.OrderId);
            var cancelled = orders.CreateGuest("0901000102", main, Cart(1)); ids.Add(cancelled.OrderId);
            orders.CancelGuest("0901000102", cancelled.OrderId);
            var foreign = orders.CreateStaff(admin, other, Cart(1, 2));
            ids.Add(orders.CreateGuest("0901000102", main, Cart(2)).OrderId);
            var original = catalog.ListForManagement(admin).Single(s => s.ServiceId == 1);
            catalog.Save(admin, original, new ServiceEdit { Name = "Changed catalog", Category = original.Category, Price = 1, IsActive = false });
            using (var c = db.OpenConnection())
            using (var tx = MySqlDatabase.BeginReadTransaction(c))
            {
                EquivalentToSingle(c, tx, main, ids.ToArray(), budgets);
                EquivalentToSingle(c, tx, other, new[] { foreign.OrderId }, budgets);
                var saved = Read(c, tx, main);
                Assert(saved.Select(o => o.Status).SequenceEqual(new[] { "Pending", "Completed", "Cancelled", "Pending" }), "Order statuses changed");
                Assert(saved[0].CreatedAt == clock.Value.AddTicks(-1) && saved[1].CreatedAt == clock.Value, "Exact creation ticks changed");
                Assert(saved[0].Items.Select(i => i.ServiceId).SequenceEqual(new[] { 1, 2 }) &&
                    saved[0].Items[0].ServiceNameSnapshot == "Snapshot tea" && saved[0].Items[0].Quantity == 10 &&
                    saved[0].Items[0].UnitPrice == long.MaxValue, "Order item sorting/old catalog snapshot or Int64 amount changed");
                var before = Signature(Read(c, tx, main));
                ids.Add(orders.CreateStaff(admin, main, Cart(2)).OrderId);
                Assert(Signature(Read(c, tx, main)) == before, "RR reader mixed in concurrently committed order");
            }
            using (var c = db.OpenConnection())
            using (var tx = MySqlDatabase.BeginReadTransaction(c))
                EquivalentToSingle(c, tx, main, ids.ToArray(), budgets);
            // A source fixture in this owned GUID transaction checks LEFT JOIN behavior and
            // visibility of the caller's uncommitted write. It rolls back, creates no payment.
            using (var c = db.OpenConnection())
            using (var tx = MySqlDatabase.BeginWriteTransaction(c))
            using (var q = c.CreateCommand())
            {
                q.Transaction = tx;
                q.CommandText = "INSERT INTO Orders(RoomSessionId,Status,CreatedAt) VALUES(@session,'Pending',@now); SELECT LAST_INSERT_ID();";
                q.Parameters.AddWithValue("@session", empty);
                q.Parameters.AddWithValue("@now", clock.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                var noItems = Convert.ToInt32(q.ExecuteScalar());
                EquivalentToSingle(c, tx, empty, new[] { noItems }, budgets);
                Assert(Read(c, tx, empty).Single().Items.Count == 0, "Order without items disappeared");
                tx.Rollback();
            }
            Assert(budgets.Count == 0, "ReadOrders constant SELECT budget failed: " + string.Join("; ", budgets));
            Console.WriteLine("PASS MySQL order batching: 0/1/many, statuses/ticks/Int64 snapshots/order, foreign session isolation, RR concurrent writer, Serializable own-write/empty items; <=1 SELECT per helper call.");
        }
        catch (Exception error) { Console.WriteLine("FAIL owned database " + name + ": " + error); throw; }
        finally
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"\Amusicbox_test_[0-9a-f]{32}\z")) throw new Exception("Unsafe cleanup");
            Sql(new MySqlDatabase("defaultdb"), "DROP DATABASE IF EXISTS `" + name + "`");
        }
    }
}
