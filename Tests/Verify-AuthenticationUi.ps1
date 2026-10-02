#requires -PSEdition Desktop
param(
    [string]$BuildDirectory = "$PSScriptRoot\..\MusicBoxManagement.Wpf\bin\Debug",
    [string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('MusicBoxUi_' + [Guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') { throw 'Run with powershell.exe -STA.' }
$buildPath = (Resolve-Path $BuildDirectory).Path
$assemblies = @('System.Data.SQLite.dll', 'Microsoft.AspNet.Identity.Core.dll',
    'Microsoft.AspNet.Identity.EntityFramework.dll', 'EntityFramework.dll', 'MusicBoxManagement.Wpf.exe')
foreach ($assembly in $assemblies) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $buildPath $assembly))
}
$wpfAssemblies = @('WindowsBase', 'PresentationCore', 'PresentationFramework', 'System.Xaml')
foreach ($assembly in $wpfAssemblies) { Add-Type -AssemblyName $assembly }
$references = @('System.dll', 'System.Core.dll', 'System.Data.dll', 'System.Xml.dll', 'System.ComponentModel.DataAnnotations.dll') +
    @($assemblies | Where-Object { $_ -like '*.dll' } | ForEach-Object { Join-Path $buildPath $_ }) +
    @([Reflection.Assembly]::LoadFrom((Join-Path $buildPath 'MusicBoxManagement.Wpf.exe')).FullName) +
    @($wpfAssemblies | ForEach-Object { [Reflection.Assembly]::LoadWithPartialName($_).Location })
Add-Type -Path (Join-Path $PSScriptRoot 'AuthenticationUiChecks.cs') -ReferencedAssemblies $references
[MusicBoxAuthenticationUiChecks]::Run((Resolve-Path "$PSScriptRoot\..\MusicBoxManagement.Wpf\App.xaml").Path, $OutputDirectory)
Write-Output "UI images: $OutputDirectory"
