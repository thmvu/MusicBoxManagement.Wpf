#requires -PSEdition Desktop
param([string]$BuildDirectory,[string]$DatabaseName='musicbox')
$ErrorActionPreference='Stop'
if (!$BuildDirectory) { $BuildDirectory = Join-Path $PSScriptRoot '..\MusicBoxManagement.Wpf\bin\Release' }
. "$PSScriptRoot\Load-AivenBuild.ps1" -BuildDirectory $BuildDirectory
[void][Reflection.Assembly]::LoadFrom((Join-Path $buildPath 'System.Data.SQLite.dll'))
$path=[MusicBoxManagement.Wpf.Data.SqliteDatabase]::DefaultPath
if(!(Test-Path -LiteralPath $path)){throw 'Không tìm thấy SQLite nguồn.'}
$builder=[System.Data.SQLite.SQLiteConnectionStringBuilder]::new();$builder.DataSource=$path;$builder.ReadOnly=$true;$builder.Pooling=$false
$c=[System.Data.SQLite.SQLiteConnection]::new($builder.ConnectionString);$c.Open()
try{
    $q=$c.CreateCommand();$q.CommandText='PRAGMA user_version';Write-Output ('Source SQLite version: '+$q.ExecuteScalar())
    $q.CommandText="SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";$r=$q.ExecuteReader();$names=@();while($r.Read()){$names+=$r.GetString(0)};$r.Dispose()
    foreach($name in $names){if($name -cnotmatch '^[A-Za-z_][A-Za-z0-9_]*$'){throw 'Unexpected table name'};$q.CommandText='SELECT COUNT(*) FROM ['+$name+']';Write-Output ('Source '+$name+': '+$q.ExecuteScalar())}
}finally{$c.Dispose()}
$c=[MusicBoxManagement.Wpf.Data.MySqlDatabase]::new($DatabaseName).OpenConnection()
try{
    $q=$c.CreateCommand();$q.CommandText='SHOW TABLES';$r=$q.ExecuteReader();$names=@();while($r.Read()){$names+=$r.GetString(0)};$r.Dispose()
    foreach($name in $names){if($name -cnotmatch '^[A-Za-z_][A-Za-z0-9_]*$'){throw 'Unexpected table name'};$q.CommandText='SELECT COUNT(*) FROM '+$name;Write-Output ('Target '+$name+': '+$q.ExecuteScalar())}
}finally{$c.Dispose()}
