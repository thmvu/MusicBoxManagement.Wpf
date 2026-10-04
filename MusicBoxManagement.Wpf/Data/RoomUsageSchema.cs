using System.Data.SQLite;

namespace MusicBoxManagement.Wpf.Data
{
    internal static class RoomUsageSchema
    {
        internal static void Create(SQLiteCommand command)
        {
            command.Parameters.Clear();
            command.CommandText = @"
CREATE TABLE Customers (
 CustomerId INTEGER PRIMARY KEY,
 FullName TEXT NOT NULL CHECK(length(trim(FullName)) BETWEEN 1 AND 100),
 PhoneNumber TEXT NOT NULL UNIQUE CHECK(length(PhoneNumber)=10 AND substr(PhoneNumber,1,1)='0' AND PhoneNumber NOT GLOB '*[^0-9]*')
);
CREATE TABLE Reservations (
 ReservationId INTEGER PRIMARY KEY,
 CustomerId INTEGER NOT NULL REFERENCES Customers(CustomerId),
 RoomId INTEGER NOT NULL REFERENCES Rooms(RoomId),
 StartTime TEXT NOT NULL CHECK(StartTime GLOB '????-??-??T??:??:??.???????+00:00' AND julianday(StartTime) IS NOT NULL),
 EndTime TEXT NOT NULL CHECK(EndTime GLOB '????-??-??T??:??:??.???????+00:00' AND julianday(EndTime) IS NOT NULL),
 Status TEXT NOT NULL CHECK(Status IN ('Confirmed','CheckedIn','Completed','Cancelled','NoShow')),
 CancellationReason TEXT NULL,
 CreatedByUserId TEXT NULL REFERENCES AspNetUsers(Id),
 CreatedAt TEXT NOT NULL,
 CHECK(StartTime < EndTime)
);
CREATE INDEX Reservations_Room_Status_Start ON Reservations(RoomId,Status,StartTime);
CREATE INDEX Reservations_Customer_Status_Start ON Reservations(CustomerId,Status,StartTime);
CREATE TABLE RoomSessions (
 RoomSessionId INTEGER PRIMARY KEY,
 CustomerId INTEGER NOT NULL REFERENCES Customers(CustomerId),
 RoomId INTEGER NOT NULL REFERENCES Rooms(RoomId),
 ReservationId INTEGER NULL REFERENCES Reservations(ReservationId),
 ActualStartTime TEXT NOT NULL CHECK(ActualStartTime GLOB '????-??-??T??:??:??.???????+00:00' AND julianday(ActualStartTime) IS NOT NULL),
 ExpectedEndTime TEXT NULL CHECK(ExpectedEndTime IS NULL OR (ExpectedEndTime GLOB '????-??-??T??:??:??.???????+00:00' AND julianday(ExpectedEndTime) IS NOT NULL)),
 ActualEndTime TEXT NULL CHECK(ActualEndTime IS NULL OR (ActualEndTime GLOB '????-??-??T??:??:??.???????+00:00' AND julianday(ActualEndTime) IS NOT NULL)),
 HourlyRate INTEGER NOT NULL CHECK(typeof(HourlyRate)='integer' AND HourlyRate>0),
 RoomCodeSnapshot TEXT NOT NULL CHECK(length(trim(RoomCodeSnapshot))>0),
 RoomTypeCodeSnapshot TEXT NOT NULL CHECK(RoomTypeCodeSnapshot IN ('STANDARD','VIP')),
 RoomTypeNameSnapshot TEXT NOT NULL CHECK(length(trim(RoomTypeNameSnapshot))>0),
 Status TEXT NOT NULL CHECK(Status IN ('Active','Completed')),
 CHECK(ExpectedEndTime IS NULL OR ExpectedEndTime > ActualStartTime),
 CHECK(ActualEndTime IS NULL OR ActualEndTime >= ActualStartTime),
 CHECK((Status='Active' AND ActualEndTime IS NULL) OR (Status='Completed' AND ActualEndTime IS NOT NULL)),
 CHECK(ReservationId IS NULL OR ExpectedEndTime IS NOT NULL)
);
CREATE UNIQUE INDEX RoomSessions_ActiveRoom ON RoomSessions(RoomId) WHERE Status='Active';
CREATE UNIQUE INDEX RoomSessions_ActiveCustomer ON RoomSessions(CustomerId) WHERE Status='Active';
CREATE UNIQUE INDEX RoomSessions_Reservation ON RoomSessions(ReservationId) WHERE ReservationId IS NOT NULL;
PRAGMA user_version=5;";
            command.ExecuteNonQuery();
        }
    }
}
