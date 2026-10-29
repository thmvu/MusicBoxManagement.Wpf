#requires -PSEdition Desktop
param([string]$BuildDirectory = "$PSScriptRoot\..\MusicBoxManagement.Wpf\bin\Debug")
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\..\Tools\Load-AivenBuild.ps1" -BuildDirectory $BuildDirectory
$assemblies = @('MySqlConnector.dll','System.Data.SQLite.dll','Microsoft.AspNet.Identity.Core.dll',
    'Microsoft.AspNet.Identity.EntityFramework.dll','EntityFramework.dll')
foreach ($assembly in $assemblies) { [void][Reflection.Assembly]::LoadFrom((Join-Path $buildPath $assembly)) }
$references = @('System.dll','System.Core.dll','System.Data.dll','System.ComponentModel.DataAnnotations.dll','System.IO.Compression.dll','System.Xml.dll') +
    @($assemblies | ForEach-Object { Join-Path $buildPath $_ }) +
    @([Reflection.Assembly]::LoadFrom((Join-Path $buildPath 'MusicBoxManagement.Wpf.exe')).FullName)
Add-Type -Path (Join-Path $PSScriptRoot 'MySqlRevenueReportChecks.cs') -ReferencedAssemblies $references
[MusicBoxMySqlRevenueReportChecks]::Run()
