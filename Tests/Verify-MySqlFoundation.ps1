#requires -PSEdition Desktop
param([string]$BuildDirectory = "$PSScriptRoot\..\MusicBoxManagement.Wpf\bin\VerifyDebug")
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\..\Tools\Load-AivenBuild.ps1" -BuildDirectory $BuildDirectory

function Assert-True($value, $message) { if (!$value) { throw $message } }
function Invoke-Sql($connection, $sql, $transaction = $null) {
    $command = $connection.CreateCommand()
    try {
        $command.CommandText = $sql
        $command.Transaction = $transaction
        $command.CommandTimeout = 30
        return $command.ExecuteScalar()
    } finally { $command.Dispose() }
}
function Assert-Rejected($connection, $sql, $message, $numbers) {
    $rejected = $false
    try { [void](Invoke-Sql $connection $sql) } catch {
        $cause = $_.Exception.GetBaseException()
        if ($cause -is [MySqlConnector.MySqlException] -and $numbers -contains $cause.Number) { $rejected = $true }
        else { throw }
    }
    Assert-True $rejected $message
}

# Only this GUID database may be dropped. Never read or alter the live SQLite file.
$testName = 'musicbox_test_' + [Guid]::NewGuid().ToString('N')
Write-Output "Foundation test database: $testName"
Assert-True ($testName -cmatch '^musicbox_test_[0-9a-f]{32}$') 'Unsafe test database name.'
$database = [MusicBoxManagement.Wpf.Data.MySqlDatabase]::new($testName)
$connection = $null
try {
    $database.Initialize()
    $database.Initialize()
    $connection = $database.OpenConnection()
    Assert-True ((Invoke-Sql $connection 'SELECT Version FROM _MusicBoxSchemaVersion WHERE Id=1') -eq 1) 'Schema version missing.'
    Assert-True ((Invoke-Sql $connection 'SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE()') -eq 16) 'Expected 15 business tables and version table.'
    Assert-True ((Invoke-Sql $connection 'SELECT COUNT(*) FROM RoomTypes') -eq 2) 'Seed duplicated.'
    Assert-True ((Invoke-Sql $connection "SELECT PricePerHour FROM RoomTypes WHERE Code='STANDARD'") -eq 120000) 'Standard seed incorrect.'
    Assert-True ((Invoke-Sql $connection "SELECT PricePerHour FROM RoomTypes WHERE Code='VIP'") -eq 200000) 'VIP seed incorrect.'
    Assert-True ((Invoke-Sql $connection "SELECT Amenities FROM RoomTypes WHERE Code='STANDARD'").Contains('Điều hòa')) 'UTF-8 did not round-trip.'
    Assert-True ((Invoke-Sql $connection 'SELECT COUNT(*) FROM Permission') -eq 27) 'Permissions missing.'
    Assert-True ((Invoke-Sql $connection 'SELECT COUNT(*) FROM AspNetRoles') -eq 3) 'Roles missing.'
    Assert-True ((Invoke-Sql $connection "SELECT COUNT(*) FROM RolePermission rp JOIN Permission p ON p.PermissionId=rp.PermissionId WHERE rp.RoleId='Staff' AND p.Code LIKE 'Report.%'") -eq 0) 'Staff received report permissions.'
    Assert-True ((Invoke-Sql $connection "SELECT COUNT(*) FROM RolePermission WHERE RoleId='Admin'") -eq 0) 'Admin permissions must stay implicit.'
    Assert-Rejected $connection "UPDATE RoomTypes SET Capacity=0 WHERE Code='STANDARD'" 'CHECK not enforced.' @(3819)
    Assert-Rejected $connection "INSERT INTO Customers(FullName,PhoneNumber) VALUES('Sai','0abcdefghj')" 'Invalid phone accepted.' @(3819)

    $transaction = $connection.BeginTransaction()
    try {
        [void](Invoke-Sql $connection "UPDATE RoomTypes SET PricePerHour=123456 WHERE Code='STANDARD'" $transaction)
        $transaction.Rollback()
    } finally { $transaction.Dispose() }
    Assert-True ((Invoke-Sql $connection "SELECT PricePerHour FROM RoomTypes WHERE Code='STANDARD'") -eq 120000) 'Rollback failed.'

    [void](Invoke-Sql $connection @"
INSERT INTO AspNetUsers(Id,UserName,NormalizedUserName,PasswordHash,SecurityStamp,FullName,IsActive)
VALUES('test','admin','ADMIN','fixture-not-a-password','stamp','Nhân viên thử',1);
INSERT INTO AspNetUserRoles(UserId,RoleId) VALUES('test','Admin');
INSERT INTO Customers(CustomerId,FullName,PhoneNumber) VALUES(1,'Khách thử','0900000001'),(2,'Khách hai','0900000002'),(3,'Khách ba','0900000003');
INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,CreatedAt)
SELECT 1,'P01',RoomTypeId,'Phòng một','test.png',1,'2026-10-14T02:00:00.0000001+00:00' FROM RoomTypes WHERE Code='STANDARD';
INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,CreatedAt)
SELECT 2,'P02',RoomTypeId,'Phòng hai','test.png',1,'2026-10-14T02:00:00.0000001+00:00' FROM RoomTypes WHERE Code='VIP';
INSERT INTO Rooms(RoomId,RoomCode,RoomTypeId,Name,ImageUrl,IsActive,CreatedAt)
SELECT 3,'P03',RoomTypeId,'Phòng ba','test.png',1,'2026-10-14T02:00:00.0000001+00:00' FROM RoomTypes WHERE Code='VIP';
INSERT INTO Reservations(ReservationId,CustomerId,RoomId,StartTime,EndTime,Status,CreatedAt)
VALUES(1,1,1,'2026-10-14T02:00:00.0000001+00:00','2026-10-14T03:00:00.0000001+00:00','CheckedIn','2026-10-14T01:00:00.0000001+00:00');
INSERT INTO RoomSessions(RoomSessionId,CustomerId,RoomId,ReservationId,ActualStartTime,ExpectedEndTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status)
VALUES(1,1,1,1,'2026-10-14T02:00:00.0000001+00:00','2026-10-14T03:00:00.0000001+00:00',120000,'P01','STANDARD','Standard','Active');
"@)
    Assert-True ((Invoke-Sql $connection 'SELECT ActualStartTime FROM RoomSessions WHERE RoomSessionId=1') -ceq '2026-10-14T02:00:00.0000001+00:00') 'UTC tick lost.'
    Assert-Rejected $connection "UPDATE Rooms SET RoomCode='p01' WHERE RoomId=1" 'Immutable room code changed.' @(1644)
    Assert-Rejected $connection "UPDATE Rooms SET IsActive=0,InactiveReason=NULL WHERE RoomId=1" 'Inactive room without reason accepted.' @(3819)
    Assert-Rejected $connection "UPDATE Rooms SET RoomTypeId=999999 WHERE RoomId=1" 'Foreign key not enforced.' @(1452)
    Assert-Rejected $connection "INSERT INTO Customers(FullName,PhoneNumber) VALUES('Trùng','0900000001')" 'Duplicate phone accepted.' @(1062)
    $sessionInsert = "INSERT INTO RoomSessions(CustomerId,RoomId,ActualStartTime,HourlyRate,RoomCodeSnapshot,RoomTypeCodeSnapshot,RoomTypeNameSnapshot,Status) VALUES({0},{1},'2026-10-14T02:00:00.0000001+00:00',120000,'P01','STANDARD','Standard','Active')"
    Assert-Rejected $connection ($sessionInsert -f 2,1) 'Second Active session for room accepted.' @(1062)
    Assert-Rejected $connection ($sessionInsert -f 1,2) 'Second Active session for customer accepted.' @(1062)
    [void](Invoke-Sql $connection ($sessionInsert -f 2,2))
    [void](Invoke-Sql $connection ($sessionInsert -f 3,3))
    Assert-True ((Invoke-Sql $connection 'SELECT COUNT(*) FROM RoomSessions WHERE ReservationId IS NULL AND ExpectedEndTime IS NULL') -eq 2) 'Walk-in null source/end lost.'
    [void](Invoke-Sql $connection "UPDATE RoomSessions SET Status='Completed',ActualEndTime='2026-10-14T03:00:00.0000001+00:00' WHERE RoomSessionId=1")
    [void](Invoke-Sql $connection ($sessionInsert -f 1,1))
    [void](Invoke-Sql $connection @"
INSERT INTO Services(ServiceId,Name,Category,Price,IsActive) VALUES(1,'Trà đào','Đồ uống',25000,1);
INSERT INTO Orders(OrderId,RoomSessionId,Status,CreatedAt) VALUES(1,1,'Completed','2026-10-14T02:10:00.0000001+00:00');
INSERT INTO OrderItems(OrderId,ServiceId,ServiceNameSnapshot,Quantity,UnitPrice) VALUES(1,1,'Trà đào',2,25000);
UPDATE Services SET Name='Tên mới',Price=30000,IsActive=0 WHERE ServiceId=1;
INSERT INTO Invoices(RoomSessionId,InvoiceNumber,RoomCharge,ServiceCharge,TotalAmount,PaymentMethod,ProcessedByUserId,ProcessedByNameSnapshot,PaidAt)
VALUES(1,'TEST-1',79228162514264337593543900335,50000,79228162514264337593543950335,'Cash','test','Nhân viên thử','2026-10-14T03:00:00.0000001+00:00');
"@)
    Assert-True ((Invoke-Sql $connection 'SELECT ServiceNameSnapshot FROM OrderItems WHERE OrderId=1') -ceq 'Trà đào') 'Old item name snapshot changed.'
    Assert-True ((Invoke-Sql $connection 'SELECT UnitPrice FROM OrderItems WHERE OrderId=1') -eq 25000) 'Old item price snapshot changed.'
    Assert-Rejected $connection "INSERT INTO OrderItems(OrderId,ServiceId,ServiceNameSnapshot,Quantity,UnitPrice) VALUES(1,1,'Trùng',1,25000)" 'Duplicate item accepted.' @(1062)
    Assert-Rejected $connection 'UPDATE OrderItems SET Quantity=11 WHERE OrderId=1' 'Quantity above 10 accepted.' @(3819)
    Assert-True ((Invoke-Sql $connection 'SELECT TotalAmount FROM Invoices WHERE RoomSessionId=1') -eq [decimal]::MaxValue) 'Large decimal amount lost precision.'
    Assert-Rejected $connection "UPDATE Invoices SET InvoiceNumber='EDIT'" 'Invoice update accepted.' @(1644)
    Assert-Rejected $connection 'DELETE FROM Invoices' 'Invoice delete accepted.' @(1644)
    [void](Invoke-Sql $connection 'UPDATE _MusicBoxSchemaVersion SET Version=99 WHERE Id=1')
    $newerBlocked = $false
    try { $database.Initialize() } catch { $newerBlocked = $_.Exception.GetBaseException().Message.Contains('mới hơn') }
    Assert-True $newerBlocked 'Newer schema was not blocked.'
    Write-Output 'PASS MySQL Aiven schema/seed/idempotence/UTF-8/RBAC/CHECK/FK/rollback/UTC ticks/Active uniqueness/walk-in/snapshots/decimal/immutable invoice/version guard.'
} catch {
    Write-Output ("Foundation failure before cleanup: " + $_.Exception.GetBaseException().GetType().FullName + ': ' + $_.Exception.GetBaseException().Message)
    throw
} finally {
    if ($connection) { $connection.Dispose() }
    # Recheck the fixed test prefix immediately before the cloud deletion.
    if ($testName -cmatch '^musicbox_test_[0-9a-f]{32}$') {
        $cleanup = [MusicBoxManagement.Wpf.Data.MySqlDatabase]::new($testName)
        $cleanupConnection = $null
        try {
            $cleanupConnection = $cleanup.OpenConnection()
            [void](Invoke-Sql $cleanupConnection ("DROP DATABASE ``" + $testName + "``"))
        } finally { if ($cleanupConnection) { $cleanupConnection.Dispose() } }
    }
}
