#requires -PSEdition Desktop
param([string]$BuildDirectory = "$PSScriptRoot\..\MusicBoxManagement.Wpf\bin\Debug")
$ErrorActionPreference = 'Stop'
$buildPath = (Resolve-Path $BuildDirectory).Path
$assemblies = @('System.Data.SQLite.dll', 'Microsoft.AspNet.Identity.Core.dll',
    'Microsoft.AspNet.Identity.EntityFramework.dll', 'EntityFramework.dll', 'MusicBoxManagement.Wpf.exe')
foreach ($assembly in $assemblies) { [void][Reflection.Assembly]::LoadFrom((Join-Path $buildPath $assembly)) }
$references = @('System.dll', 'System.Core.dll', 'System.Data.dll', 'System.Numerics.dll', 'System.ComponentModel.DataAnnotations.dll') +
    @($assemblies | Where-Object { $_ -like '*.dll' } | ForEach-Object { Join-Path $buildPath $_ }) +
    @([Reflection.Assembly]::LoadFrom((Join-Path $buildPath 'MusicBoxManagement.Wpf.exe')).FullName)
Add-Type -Path (Join-Path $PSScriptRoot 'BillingChecks.cs') -ReferencedAssemblies $references
[MusicBoxBillingChecks]::Run()
