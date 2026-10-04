using System.Data.SQLite;

namespace MusicBoxManagement.Wpf.Data
{
    internal static class ServiceSchema
    {
        internal static void Create(SQLiteCommand command)
        {
            command.Parameters.Clear();
            command.CommandText = @"CREATE TABLE Services (
ServiceId INTEGER PRIMARY KEY,
Name TEXT NOT NULL CHECK(length(trim(Name)) BETWEEN 1 AND 100),
Category TEXT NOT NULL CHECK(Category IN ('Đồ uống','Đồ ăn','Khác')),
Price INTEGER NOT NULL CHECK(typeof(Price)='integer' AND Price>0),
Description TEXT NULL CHECK(Description IS NULL OR length(Description)<=2000),
IsActive INTEGER NOT NULL CHECK(IsActive IN (0,1))
);
PRAGMA user_version=6;";
            command.ExecuteNonQuery();
        }
    }
}
