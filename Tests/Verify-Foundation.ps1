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
    Write-Output 'PASS SQLite creation, Vietnamese text, persisted changes, seed idempotence, constraints and schema version guard.'
} finally {
    # Delete only the randomly named database created by this test.
    if (Test-Path -LiteralPath $testFile) { Remove-Item -LiteralPath $testFile }
}
