#requires -PSEdition Desktop
param([string]$BuildDirectory="$PSScriptRoot\..\MusicBoxManagement.Wpf\bin\Debug")
$ErrorActionPreference='Stop'
. "$PSScriptRoot\..\Tools\Load-AivenBuild.ps1" -BuildDirectory $BuildDirectory
$names=@('MySqlConnector.dll','System.Data.SQLite.dll','Microsoft.AspNet.Identity.Core.dll','Microsoft.AspNet.Identity.EntityFramework.dll','EntityFramework.dll','SkiaSharp.dll')
foreach($name in $names){[void][Reflection.Assembly]::LoadFrom((Join-Path $buildPath $name))}
$refs=@('System.dll','System.Core.dll','System.Data.dll','System.Xml.dll','System.ComponentModel.DataAnnotations.dll')+@($names|ForEach-Object{Join-Path $buildPath $_})+@([Reflection.Assembly]::LoadFrom((Join-Path $buildPath 'MusicBoxManagement.Wpf.exe')).FullName)
Add-Type -Path "$PSScriptRoot\MySqlImportChecks.cs" -ReferencedAssemblies $refs
[MusicBoxMySqlImportChecks]::Run()
