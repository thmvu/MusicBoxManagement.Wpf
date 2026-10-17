#requires -PSEdition Desktop
param(
    [string]$BuildDirectory = "$PSScriptRoot\..\MusicBoxManagement.Wpf\bin\VerifyMySql3bDebug"
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\..\Tools\Load-AivenBuild.ps1" -BuildDirectory $BuildDirectory
$assemblies = @('MySqlConnector.dll','System.Data.SQLite.dll','Microsoft.AspNet.Identity.Core.dll',
    'Microsoft.AspNet.Identity.EntityFramework.dll','EntityFramework.dll','SkiaSharp.dll','System.Memory.dll','System.Buffers.dll',
    'System.Numerics.Vectors.dll','System.Runtime.CompilerServices.Unsafe.dll')
foreach ($assembly in $assemblies) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $buildPath $assembly))
}
$references = @('System.dll','System.Core.dll','System.Data.dll','System.ComponentModel.DataAnnotations.dll') +
    @($assemblies | ForEach-Object { Join-Path $buildPath $_ }) +
    @(Join-Path $buildPath 'MusicBoxManagement.Wpf.exe')
# MySqlConnector references System.Memory 4.0.1.2, SkiaSharp 4.0.5.0.
# Load-AivenBuild resolves the shipped version; WPF uses MSBuild binding redirects.
# Suppress only the corresponding compile-time unification warning.
$compiler = New-Object System.CodeDom.Compiler.CompilerParameters
$compiler.CompilerOptions = '/nowarn:1701'
$compiler.ReferencedAssemblies.AddRange([string[]]$references)
Add-Type -Path (Join-Path $PSScriptRoot 'MySqlRoomChecks.cs') -CompilerParameters $compiler
[MusicBoxMySqlRoomChecks]::Run()
