#requires -PSEdition Desktop
param([string]$BuildDirectory = "$PSScriptRoot\..\MusicBoxManagement.Wpf\bin\Debug")
$ErrorActionPreference = 'Stop'
$buildPath = (Resolve-Path $BuildDirectory).Path
Add-Type -Path (Join-Path $buildPath 'System.Data.SQLite.dll')
[void][Reflection.Assembly]::LoadFrom((Join-Path $buildPath 'MusicBoxManagement.Wpf.exe'))

function Assert-True($value, $message) {
    if (!$value) { throw $message }
}

$testFile = Join-Path ([IO.Path]::GetTempPath()) ('MusicBoxWpf_' + [Guid]::NewGuid().ToString('N') + '.db')
$database = [MusicBoxManagement.Wpf.Data.SqliteDatabase]::new($testFile)
$service = [MusicBoxManagement.Wpf.Services.RoomTypeService]::new($database)
try {
    $first = $service.List()
    Assert-True (Test-Path -LiteralPath $testFile) 'SQLite file was not created.'
    Assert-True ($first.Count -eq 2) 'Expected exactly Standard and VIP.'
    Assert-True ($first[0].Code -eq 'STANDARD' -and $first[0].PricePerHour -eq 120000) 'Standard seed does not match the plan.'
    Assert-True ($first[1].Code -eq 'VIP' -and $first[1].PricePerHour -eq 200000) 'VIP seed does not match the plan.'
    Assert-True ($first[0].Amenities.Contains('Điều hòa')) 'Vietnamese text did not survive SQLite storage.'

    # Acquire a writer BEFORE changing any row, then exercise a second connection.
    $connection = $database.OpenConnection()
    $secondConnection = $database.OpenConnection()
    try {
        $transaction = [MusicBoxManagement.Wpf.Data.SqliteDatabase]::BeginWriteTransaction($connection)
        try {
            $secondConnection.DefaultTimeout = 1
            $busy = $false
            try {
                $unexpected = [MusicBoxManagement.Wpf.Data.SqliteDatabase]::BeginWriteTransaction($secondConnection)
                $unexpected.Dispose()
            } catch {
                $cause = $_.Exception
                while ($cause.InnerException) { $cause = $cause.InnerException }
                $busy = ($cause -is [System.Data.SQLite.SQLiteException] -and [int]$cause.ResultCode -eq 5)
            }
            Assert-True $busy 'A second writer was not rejected with SQLITE_BUSY before the first write.'
            $command = $connection.CreateCommand()
            try {
                $command.Transaction = $transaction
                $command.CommandText = "UPDATE RoomTypes SET PricePerHour = 140000 WHERE Code = 'STANDARD';"
                [void]$command.ExecuteNonQuery()
            } finally { $command.Dispose() }
            $transaction.Commit()
        } finally { $transaction.Dispose() }

        $transaction = [MusicBoxManagement.Wpf.Data.SqliteDatabase]::BeginWriteTransaction($secondConnection)
        try {
            $command = $secondConnection.CreateCommand()
            try {
                $command.Transaction = $transaction
                $command.CommandText = "SELECT PricePerHour FROM RoomTypes WHERE Code = 'STANDARD';"
                Assert-True ([long]$command.ExecuteScalar() -eq 140000) 'The next writer did not read the committed value.'
                $command.CommandText = "UPDATE RoomTypes SET PricePerHour = 150000 WHERE Code = 'STANDARD';"
                [void]$command.ExecuteNonQuery()
                $command.CommandText = "UPDATE RoomTypes SET PricePerHour = -1 WHERE Code = 'VIP';"
                $failed = $false
                try { [void]$command.ExecuteNonQuery() } catch { $failed = $true }
                Assert-True $failed 'Expected a constraint failure inside the transaction.'
            } finally { $command.Dispose() }
            # No commit: disposing must roll back the earlier valid update too.
        } finally { $transaction.Dispose() }
        $command = $secondConnection.CreateCommand()
        try {
            $command.CommandText = "SELECT PricePerHour FROM RoomTypes WHERE Code = 'STANDARD';"
            Assert-True ([long]$command.ExecuteScalar() -eq 140000) 'Disposal after failure did not roll back all writes.'
        } finally { $command.Dispose() }

        $transaction = [MusicBoxManagement.Wpf.Data.SqliteDatabase]::BeginWriteTransaction($connection)
        try {
            $command = $connection.CreateCommand()
            try {
                $command.Transaction = $transaction
                $command.CommandText = "UPDATE RoomTypes SET PricePerHour = 160000 WHERE Code = 'STANDARD';"
                [void]$command.ExecuteNonQuery()
            } finally { $command.Dispose() }
            $transaction.Rollback()
        } finally { $transaction.Dispose() }
        $command = $connection.CreateCommand()
        try {
            $command.CommandText = "SELECT PricePerHour FROM RoomTypes WHERE Code = 'STANDARD';"
            Assert-True ([long]$command.ExecuteScalar() -eq 140000) 'Explicit rollback did not restore the committed value.'
        } finally { $command.Dispose() }
    } finally {
        $secondConnection.Dispose()
        $connection.Dispose()
    }

    $connection = $database.OpenConnection()
    try {
        $command = $connection.CreateCommand()
        try {
            $command.CommandText = 'PRAGMA foreign_keys;'
            Assert-True ([int]$command.ExecuteScalar() -eq 1) 'Foreign keys are disabled.'
            $command.CommandText = "UPDATE RoomTypes SET Name = 'Standard đã chỉnh', PricePerHour = 135000 WHERE Code = 'STANDARD';"
            [void]$command.ExecuteNonQuery()
            $command.CommandText = "UPDATE RoomTypes SET PricePerHour = -1 WHERE Code = 'VIP';"
            $rejected = $false
            try { [void]$command.ExecuteNonQuery() } catch { $rejected = $true }
            Assert-True $rejected 'SQLite accepted a negative hourly price.'
            $command.CommandText = "UPDATE RoomTypes SET PricePerHour = 125000.5 WHERE Code = 'VIP';"
            $rejected = $false
            try { [void]$command.ExecuteNonQuery() } catch { $rejected = $true }
            Assert-True $rejected 'SQLite accepted a fractional VND price.'
        } finally { $command.Dispose() }
    } finally { $connection.Dispose() }

    $reopened = [MusicBoxManagement.Wpf.Services.RoomTypeService]::new(
        [MusicBoxManagement.Wpf.Data.SqliteDatabase]::new($testFile)).List()
    Assert-True ($reopened.Count -eq 2) 'Reopening duplicated the seed data.'
    Assert-True ($reopened[0].Name -eq 'Standard đã chỉnh' -and $reopened[0].PricePerHour -eq 135000) 'Initialization overwrote existing changes.'

    $connection = $database.OpenConnection()
    try {
        $command = $connection.CreateCommand()
        try {
            $command.CommandText = 'PRAGMA user_version = 2;'
            [void]$command.ExecuteNonQuery()
        } finally { $command.Dispose() }
    } finally { $connection.Dispose() }
    $rejected = $false
    try { $database.Initialize() } catch { $rejected = $true }
    Assert-True $rejected 'An unsupported newer schema was silently accepted.'
    Write-Output 'PASS SQLite creation, Vietnamese text, persisted changes, seed idempotence, constraints, schema guard, commit/rollback and competing writers.'
} finally {
    # Delete only the randomly named database created by this test.
    if (Test-Path -LiteralPath $testFile) { Remove-Item -LiteralPath $testFile }
}
