#requires -PSEdition Desktop
param(
    [switch]$Apply,
    [string]$DatabaseName = 'musicbox',
    [string]$StorageRoot,
    [string]$BuildDirectory
)
$ErrorActionPreference = 'Stop'
if (!$BuildDirectory) { $BuildDirectory = Join-Path $PSScriptRoot '..\MusicBoxManagement.Wpf\bin\Release' }
if (!$StorageRoot) { $StorageRoot = $env:MUSICBOX_STORAGE_ROOT }
if (!$StorageRoot) { $StorageRoot = Join-Path $env:LOCALAPPDATA 'MusicBoxManagement.Wpf.MySql' }
if (![IO.Path]::IsPathRooted($StorageRoot)) { throw 'StorageRoot phải là đường dẫn tuyệt đối.' }
$StorageRoot = [IO.Path]::GetFullPath($StorageRoot)
$assetRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\assets\rooms'))
. "$PSScriptRoot\Load-AivenBuild.ps1" -BuildDirectory $BuildDirectory
[void][Reflection.Assembly]::LoadFrom((Join-Path $buildPath 'SkiaSharp.dll'))
# Reuse the application's decoder/size limit/PNG normalization; no fabricated LoginSession.
$imageHelper = [MusicBoxManagement.Wpf.Data.MySqlDatabase].Assembly.GetType('MusicBoxManagement.Wpf.Services.RoomImages')
$readPng = $imageHelper.GetMethod('ReadPng', [Reflection.BindingFlags]'Static,NonPublic')
$rooms = @(
    @{ Code='DEMO-S01'; Type='STANDARD'; Name='Phòng Mộc (Demo)'; Image='standard-oak-50mm.png' },
    @{ Code='DEMO-S02'; Type='STANDARD'; Name='Phòng Nắng (Demo)'; Image='standard-daylight-50mm.png' },
    @{ Code='DEMO-S03'; Type='STANDARD'; Name='Phòng Ấm (Demo)'; Image='standard-canon-50mm.png' },
    @{ Code='DEMO-V01'; Type='VIP'; Name='Phòng Aurora (Demo)'; Image='vip-canon-50mm.png' },
    @{ Code='DEMO-V02'; Type='VIP'; Name='Phòng Hoàng Hôn (Demo)'; Image='vip-evening-50mm.png' }
)
$customers = 1..6 | ForEach-Object {
    @{ Name=('Khách Demo {0:00}' -f $_); Phone=[MusicBoxManagement.Wpf.Services.PhoneNumberNormalizer]::Normalize(('00000001{0:00}' -f $_)) }
}
# Decode before acquiring database locks. These are existing generated assets, not new photos.
foreach ($room in $rooms) {
    $imagePath = [string](Join-Path $assetRoot $room.Image)
    $room.Png = [byte[]]$readPng.Invoke($null, [object[]]@($imagePath))
}
$db = [MusicBoxManagement.Wpf.Data.MySqlDatabase]::new($DatabaseName)
$connection = $db.OpenConnection()
$tx = $null
function SeedCommand([string]$Sql, [hashtable]$Values) {
    $q = $connection.CreateCommand(); $q.Transaction = $tx; $q.CommandText = $Sql
    foreach ($key in $Values.Keys) { [void]$q.Parameters.AddWithValue('@'+$key, $Values[$key]) }
    return $q
}
function AuditSeed([string]$Entity, [string]$Id, [string]$Description) {
    $q = SeedCommand 'INSERT INTO AuditLog(ActorType,UserId,Action,EntityName,EntityId,Description,CreatedAt) VALUES(''System'',NULL,''DemoSeed.Create'',@entity,@id,@description,@now)' @{entity=$Entity;id=$Id;description=$Description;now=$now}
    try { if ($q.ExecuteNonQuery() -ne 1) { throw 'Không ghi đủ nhật ký seed.' } } finally { $q.Dispose() }
}
$result = [Collections.Generic.List[object]]::new()
$ownedFiles = [Collections.Generic.List[string]]::new()
$commitAttempted = $false
try {
    $tx = if ($Apply) { [MusicBoxManagement.Wpf.Data.MySqlDatabase]::BeginWriteTransaction($connection) } else { [MusicBoxManagement.Wpf.Data.MySqlDatabase]::BeginReadTransaction($connection) }
    $q = SeedCommand 'SELECT COUNT(*) FROM AspNetUsers u JOIN AspNetUserRoles ur ON ur.UserId=u.Id WHERE u.IsActive=1 AND ur.RoleId=''Admin''' @{}
    try { if ([int]$q.ExecuteScalar() -lt 1) { throw 'Cần thiết lập Admin trước; script không tạo tài khoản.' } } finally { $q.Dispose() }
    $typeIds = @{}
    $q = SeedCommand 'SELECT RoomTypeId,Code FROM RoomTypes' @{}
    try { $r=$q.ExecuteReader(); try { while($r.Read()) { $typeIds[$r.GetString(1)]=$r.GetInt32(0) } } finally { $r.Dispose() } } finally { $q.Dispose() }
    if (!$typeIds.ContainsKey('STANDARD') -or !$typeIds.ContainsKey('VIP')) { throw 'Thiếu loại phòng Standard/VIP.' }
    $now = [DateTimeOffset]::UtcNow.ToString('O',[Globalization.CultureInfo]::InvariantCulture)
    foreach ($room in $rooms) {
        $q = SeedCommand 'SELECT RoomId FROM Rooms WHERE RoomCode=@code' @{code=$room.Code}
        try { $existing=$q.ExecuteScalar() } finally { $q.Dispose() }
        if ($null -ne $existing) { $state='SkippedExisting'; $id=[int]$existing }
        elseif (!$Apply) { $state='WouldCreate'; $id=$null }
        else {
            $relative='Content/uploads/rooms/'+[Guid]::NewGuid().ToString('N')+'.png'
            $file=Join-Path $StorageRoot $relative
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file))
            # CreateNew never overwrites a user's file; track only files owned by this run.
            $stream=[IO.File]::Open($file,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
            $ownedFiles.Add($file)
            try { $stream.Write($room.Png,0,$room.Png.Length) } finally { $stream.Dispose() }
            $q=SeedCommand 'INSERT INTO Rooms(RoomCode,RoomTypeId,Name,ImageUrl,Description,IsActive,InactiveReason,CreatedAt) VALUES(@code,@type,@name,@image,@description,1,NULL,@now); SELECT LAST_INSERT_ID();' @{code=$room.Code;type=$typeIds[$room.Type];name=$room.Name;image=$relative;description='Phòng mẫu để thử đặt phòng và vận hành Music Box.';now=$now}
            try { $id=[int]$q.ExecuteScalar() } finally { $q.Dispose() }
            AuditSeed 'Room' ([string]$id) ('Tạo phòng demo '+$room.Code+' bằng script Seed-MySqlDemo.')
            $state='Created'
        }
        $result.Add([pscustomobject]@{Entity='Room';Key=$room.Code;Id=$id;State=$state})
    }
    foreach ($customer in $customers) {
        $q=SeedCommand 'SELECT CustomerId FROM Customers WHERE PhoneNumber=@phone' @{phone=$customer.Phone}
        try { $existing=$q.ExecuteScalar() } finally { $q.Dispose() }
        if ($null -ne $existing) { $state='SkippedExisting'; $id=[int]$existing }
        elseif (!$Apply) { $state='WouldCreate'; $id=$null }
        else {
            $q=SeedCommand 'INSERT INTO Customers(FullName,PhoneNumber) VALUES(@name,@phone); SELECT LAST_INSERT_ID();' @{name=$customer.Name;phone=$customer.Phone}
            try { $id=[int]$q.ExecuteScalar() } finally { $q.Dispose() }
            AuditSeed 'Customer' ([string]$id) ('Tạo '+$customer.Name+' bằng script Seed-MySqlDemo; SĐT giả lập, không liên hệ.')
            $state='Created'
        }
        $result.Add([pscustomobject]@{Entity='Customer';Key=$customer.Phone;Id=$id;State=$state})
    }
    if ($Apply) { $commitAttempted=$true; $tx.Commit() }
    # Dry-run has no file, SQL, or audit writes. Repeat apply skips existing identifiers.
    $result
} catch {
    $errorRecord=$_
    $rolledBack=$false
    if ($tx -and !$commitAttempted) { try { $tx.Rollback(); $rolledBack=$true } catch { Write-Warning 'Không xác nhận rollback; giữ file để đối chiếu.' } }
    if ($rolledBack) { foreach ($file in $ownedFiles) { [IO.File]::Delete($file) } }
    # Uncertain COMMIT: preserve images; inspect DB before retry. Never remove shared/existing files.
    throw $errorRecord
} finally {
    if ($tx) { $tx.Dispose() }; $connection.Dispose()
}
