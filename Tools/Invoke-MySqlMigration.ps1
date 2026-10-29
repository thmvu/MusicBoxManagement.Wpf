#requires -PSEdition Desktop
param(
    [Parameter(Mandatory=$true)][ValidateSet('Backup','PrepareV8','Import','Verify','InstallContent','RestoreSqliteCopy')][string]$Mode,
    [string]$SourcePath,[string]$StorageRoot,[string]$BackupParent,[string]$SnapshotDirectory,[string]$DestinationStorageRoot,
    [string]$DatabaseName='musicbox',
    [string]$BuildDirectory
)
$ErrorActionPreference='Stop'
if (!$BuildDirectory) { $BuildDirectory = Join-Path $PSScriptRoot '..\MusicBoxManagement.Wpf\bin\Release' }
. "$PSScriptRoot\Load-AivenBuild.ps1" -BuildDirectory $BuildDirectory
if($Mode -eq 'Backup'){
    if(!$SourcePath -or !$StorageRoot -or !$BackupParent){throw 'Backup cần SourcePath, StorageRoot và BackupParent.'}
    [MusicBoxManagement.Wpf.Data.MySqlDataMigration]::Backup($SourcePath,$StorageRoot,$BackupParent)
}elseif($Mode -eq 'PrepareV8'){
    if(!$SnapshotDirectory){throw 'Cần SnapshotDirectory gốc.'}
    [MusicBoxManagement.Wpf.Data.MySqlDataMigration]::PrepareV8Snapshot($SnapshotDirectory)
}elseif($Mode -eq 'RestoreSqliteCopy'){
    if(!$SnapshotDirectory -or !$DestinationStorageRoot){throw 'Cần SnapshotDirectory và DestinationStorageRoot.'}
    [MusicBoxManagement.Wpf.Data.MySqlDataMigration]::RestoreSqliteCopy($SnapshotDirectory,$DestinationStorageRoot)
}elseif($Mode -eq 'InstallContent'){
    if(!$SnapshotDirectory -or !$DestinationStorageRoot){throw 'Cần SnapshotDirectory và DestinationStorageRoot.'}
    [MusicBoxManagement.Wpf.Data.MySqlDataMigration]::InstallContent($SnapshotDirectory,$DestinationStorageRoot)
}else{
    if(!$SnapshotDirectory){throw 'Cần SnapshotDirectory đã được sao lưu.'}
    $db=[MusicBoxManagement.Wpf.Data.MySqlDatabase]::new($DatabaseName)
    if($Mode -eq 'Import'){[MusicBoxManagement.Wpf.Data.MySqlDataMigration]::Import($SnapshotDirectory,$db)}
    else{[MusicBoxManagement.Wpf.Data.MySqlDataMigration]::Verify($SnapshotDirectory,$db)}
}
