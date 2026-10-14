#requires -PSEdition Desktop
param([string]$BuildDirectory = "$PSScriptRoot\..\MusicBoxManagement.Wpf\bin\VerifyDebug")
$ErrorActionPreference = 'Stop'
$buildPath = (Resolve-Path $BuildDirectory).Path
# Windows PowerShell does not consume the WPF exe's binding redirects.
# Resolve only dependencies physically shipped in this verified build folder.
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Reflection;
public static class MusicBoxMySqlTestResolver
{
    public static void Install(string path)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name).Name;
            if (name != Path.GetFileName(name)) return null;
            var file = Path.Combine(path, name + ".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
    }
}
'@
[MusicBoxMySqlTestResolver]::Install($buildPath)
foreach ($assembly in @('System.Memory.dll','System.Buffers.dll','System.Numerics.Vectors.dll',
    'System.Runtime.CompilerServices.Unsafe.dll','System.Threading.Tasks.Extensions.dll',
    'Microsoft.Extensions.DependencyInjection.Abstractions.dll','Microsoft.Extensions.Logging.Abstractions.dll',
    'System.Diagnostics.DiagnosticSource.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $buildPath $assembly))
}
[void][Reflection.Assembly]::LoadFrom((Join-Path $buildPath 'MySqlConnector.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $buildPath 'MusicBoxManagement.Wpf.exe'))
