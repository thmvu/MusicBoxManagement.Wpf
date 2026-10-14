#requires -PSEdition Desktop
param(
    [string]$BuildDirectory = "$PSScriptRoot\..\MusicBoxManagement.Wpf\bin\VerifyDebug",
    [string]$DatabaseName = 'musicbox'
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Load-AivenBuild.ps1" -BuildDirectory $BuildDirectory
$database = [MusicBoxManagement.Wpf.Data.MySqlDatabase]::new($DatabaseName)
$database.Initialize()
$connection = $database.OpenConnection()
try {
    $command = $connection.CreateCommand()
    try {
        $command.CommandText = 'SELECT Version FROM _MusicBoxSchemaVersion WHERE Id=1'
        Write-Output ('MySQL database=' + $DatabaseName + ', schema=' + $command.ExecuteScalar())
        $command.CommandText = 'SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE()'
        Write-Output ('Tables=' + $command.ExecuteScalar())
        $command.CommandText = 'SELECT COUNT(*) FROM RoomTypes'
        Write-Output ('RoomTypes=' + $command.ExecuteScalar())
        $command.CommandText = 'SELECT COUNT(*) FROM Permission'
        Write-Output ('Permissions=' + $command.ExecuteScalar())
    } finally { $command.Dispose() }
} finally { $connection.Dispose() }
