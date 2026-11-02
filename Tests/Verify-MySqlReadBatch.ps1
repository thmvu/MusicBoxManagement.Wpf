#requires -PSEdition Desktop
param([string]$BuildDirectory)
$ErrorActionPreference='Stop'
if(!$BuildDirectory){$BuildDirectory=Join-Path $PSScriptRoot '..\MusicBoxManagement.Wpf\bin\Debug'}
. "$PSScriptRoot\..\Tools\Load-AivenBuild.ps1" -BuildDirectory $BuildDirectory
$refs=@('System.dll','System.Core.dll','System.Data.dll')+@(Join-Path $buildPath 'MySqlConnector.dll')+@([Reflection.Assembly]::LoadFrom((Join-Path $buildPath 'MusicBoxManagement.Wpf.exe')).FullName)
Add-Type -Path (Join-Path $PSScriptRoot 'MySqlReadBatchChecks.cs') -ReferencedAssemblies $refs
[MusicBoxMySqlReadBatchChecks]::Run()
