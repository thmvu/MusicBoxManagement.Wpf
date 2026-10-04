using System.Data.SQLite;

namespace MusicBoxManagement.Wpf.Data
{
    internal static class RoomSchema
    {
        internal static void Create(SQLiteCommand command)
        {
            command.Parameters.Clear();
            command.CommandText = @"
CREATE TABLE Rooms (
    RoomId INTEGER PRIMARY KEY,
    RoomCode TEXT NOT NULL COLLATE NOCASE UNIQUE CHECK (length(trim(RoomCode)) BETWEEN 1 AND 50),
    RoomTypeId INTEGER NOT NULL REFERENCES RoomTypes(RoomTypeId),
    Name TEXT NOT NULL CHECK (length(trim(Name)) BETWEEN 1 AND 100),
    ImageUrl TEXT NOT NULL CHECK (length(trim(ImageUrl)) BETWEEN 1 AND 500),
    Description TEXT NULL CHECK (Description IS NULL OR length(Description) <= 2000),
    IsActive INTEGER NOT NULL CHECK (IsActive IN (0, 1)),
    InactiveReason TEXT NULL CHECK (InactiveReason IS NULL OR length(InactiveReason) <= 1000),
    CreatedAt TEXT NOT NULL,
    CHECK (IsActive = 1 OR length(trim(coalesce(InactiveReason, ''))) > 0)
);
CREATE TRIGGER Rooms_ImmutableCode BEFORE UPDATE OF RoomCode ON Rooms
WHEN NEW.RoomCode != OLD.RoomCode COLLATE BINARY
BEGIN SELECT RAISE(ABORT, 'RoomCode is immutable'); END;
PRAGMA user_version = 4;";
            command.ExecuteNonQuery();
        }
    }
}
