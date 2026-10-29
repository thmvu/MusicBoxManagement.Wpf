#requires -PSEdition Desktop
param(
    [string]$BuildDirectory = "$PSScriptRoot\..\MusicBoxManagement.Wpf\bin\Debug",
    [string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('MusicBoxMySqlOperations_' + [Guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Run with powershell.exe -STA.' }
. "$PSScriptRoot\..\Tools\Load-AivenBuild.ps1" -BuildDirectory $BuildDirectory
$assemblies = @('MySqlConnector.dll','System.Data.SQLite.dll','Microsoft.AspNet.Identity.Core.dll',
    'Microsoft.AspNet.Identity.EntityFramework.dll','EntityFramework.dll','SkiaSharp.dll')
foreach ($assembly in $assemblies) { [void][Reflection.Assembly]::LoadFrom((Join-Path $buildPath $assembly)) }
$wpfAssemblies = @('WindowsBase','PresentationCore','PresentationFramework','System.Xaml','System.Printing','ReachFramework')
foreach ($assembly in $wpfAssemblies) { Add-Type -AssemblyName $assembly }
$references = @('System.dll','System.Core.dll','System.Data.dll','System.Xml.dll','System.ComponentModel.DataAnnotations.dll') +
    @($assemblies | ForEach-Object { Join-Path $buildPath $_ }) +
    @([Reflection.Assembly]::LoadFrom((Join-Path $buildPath 'MusicBoxManagement.Wpf.exe')).FullName) +
    @($wpfAssemblies | ForEach-Object { [Reflection.Assembly]::LoadWithPartialName($_).Location })
Add-Type -Path (Join-Path $PSScriptRoot 'MySqlOperationsUiChecks.cs') -ReferencedAssemblies $references
[MusicBoxMySqlOperationsUiChecks]::Run((Resolve-Path "$PSScriptRoot\..\MusicBoxManagement.Wpf\App.xaml").Path, $OutputDirectory)
Write-Output "MySQL print preview images (no printer used): $OutputDirectory"
