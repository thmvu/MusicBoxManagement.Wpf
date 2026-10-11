using System.Data.SQLite;

namespace MusicBoxManagement.Wpf.Data
{
    internal static class InvoiceSchema
    {
        internal static void Create(SQLiteCommand command)
        {
            command.Parameters.Clear();
            // Whole-dong decimal amounts use canonical TEXT, never SQLite REAL (binary rounding).
            command.CommandText = @"CREATE TABLE Invoices (
InvoiceId INTEGER PRIMARY KEY,
RoomSessionId INTEGER NOT NULL UNIQUE REFERENCES RoomSessions(RoomSessionId),
InvoiceNumber TEXT NOT NULL UNIQUE CHECK(length(trim(InvoiceNumber)) BETWEEN 1 AND 50),
RoomCharge TEXT NOT NULL CHECK(length(RoomCharge) BETWEEN 1 AND 29 AND RoomCharge NOT GLOB '*[^0-9]*' AND (RoomCharge='0' OR substr(RoomCharge,1,1)<>'0') AND (length(RoomCharge)<29 OR RoomCharge<='79228162514264337593543950335')),
ServiceCharge TEXT NOT NULL CHECK(length(ServiceCharge) BETWEEN 1 AND 29 AND ServiceCharge NOT GLOB '*[^0-9]*' AND (ServiceCharge='0' OR substr(ServiceCharge,1,1)<>'0') AND (length(ServiceCharge)<29 OR ServiceCharge<='79228162514264337593543950335')),
TotalAmount TEXT NOT NULL CHECK(length(TotalAmount) BETWEEN 1 AND 29 AND TotalAmount NOT GLOB '*[^0-9]*' AND (TotalAmount='0' OR substr(TotalAmount,1,1)<>'0') AND (length(TotalAmount)<29 OR TotalAmount<='79228162514264337593543950335')),
PaymentMethod TEXT NOT NULL CHECK(PaymentMethod IN ('Cash','BankTransfer')),
ProcessedByUserId TEXT NOT NULL REFERENCES AspNetUsers(Id),
ProcessedByNameSnapshot TEXT NOT NULL CHECK(length(trim(ProcessedByNameSnapshot)) BETWEEN 1 AND 100),
PaidAt TEXT NOT NULL CHECK(PaidAt GLOB '????-??-??T??:??:??.???????+00:00' AND julianday(PaidAt) IS NOT NULL)
);
CREATE INDEX IX_Invoices_PaidAt ON Invoices(PaidAt);
CREATE TRIGGER Invoices_NoUpdate BEFORE UPDATE ON Invoices BEGIN SELECT RAISE(ABORT,'Invoice is immutable'); END;
CREATE TRIGGER Invoices_NoDelete BEFORE DELETE ON Invoices BEGIN SELECT RAISE(ABORT,'Invoice is immutable'); END;
PRAGMA user_version=8;";
            command.ExecuteNonQuery();
        }
    }
}
