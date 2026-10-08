using System.Data.SQLite;

namespace MusicBoxManagement.Wpf.Data
{
    internal static class OrderSchema
    {
        internal static void Create(SQLiteCommand command)
        {
            command.Parameters.Clear();
            command.CommandText = @"CREATE TABLE Orders (
OrderId INTEGER PRIMARY KEY,
RoomSessionId INTEGER NOT NULL REFERENCES RoomSessions(RoomSessionId),
CreatedByUserId TEXT NULL REFERENCES AspNetUsers(Id),
Status TEXT NOT NULL CHECK(Status IN ('Pending','Completed','Cancelled')),
CreatedAt TEXT NOT NULL CHECK(CreatedAt GLOB '????-??-??T??:??:??.???????+00:00' AND julianday(CreatedAt) IS NOT NULL)
);
CREATE INDEX IX_Orders_Session ON Orders(RoomSessionId,OrderId);
CREATE TABLE OrderItems (
OrderItemId INTEGER PRIMARY KEY,
OrderId INTEGER NOT NULL REFERENCES Orders(OrderId),
ServiceId INTEGER NOT NULL REFERENCES Services(ServiceId),
ServiceNameSnapshot TEXT NOT NULL CHECK(length(trim(ServiceNameSnapshot)) BETWEEN 1 AND 100),
Quantity INTEGER NOT NULL CHECK(typeof(Quantity)='integer' AND Quantity BETWEEN 1 AND 10),
UnitPrice INTEGER NOT NULL CHECK(typeof(UnitPrice)='integer' AND UnitPrice>0),
UNIQUE(OrderId,ServiceId)
);
PRAGMA user_version=7;";
            command.ExecuteNonQuery();
        }
    }
}
