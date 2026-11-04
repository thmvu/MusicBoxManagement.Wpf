#requires -PSEdition Desktop
param([string]$BuildDirectory,[string]$OutputDirectory,[switch]$PreflightOnly)
$ErrorActionPreference='Stop'
if(!$BuildDirectory){$BuildDirectory=Join-Path $PSScriptRoot '..\MusicBoxManagement.Wpf\bin\Debug'}
if(!$OutputDirectory){$OutputDirectory=Join-Path $env:TEMP ('MusicBoxFeedbackUi_'+[Guid]::NewGuid().ToString('N'))}
. "$PSScriptRoot\..\Tools\Load-AivenBuild.ps1" -BuildDirectory $BuildDirectory
$dependencies=@('MySqlConnector.dll','Microsoft.AspNet.Identity.Core.dll','Microsoft.AspNet.Identity.EntityFramework.dll','EntityFramework.dll')
foreach($dll in $dependencies){[void][Reflection.Assembly]::LoadFrom((Join-Path $buildPath $dll))}
$wpf=@('WindowsBase','PresentationCore','PresentationFramework','System.Xaml')
foreach($name in $wpf){Add-Type -AssemblyName $name}
$refs=@('System.dll','System.Core.dll','System.Data.dll','System.Xml.dll','System.ComponentModel.DataAnnotations.dll')+@($dependencies|ForEach-Object{Join-Path $buildPath $_})+@([Reflection.Assembly]::LoadFrom((Join-Path $buildPath 'MusicBoxManagement.Wpf.exe')).FullName)+@($wpf|ForEach-Object{[Reflection.Assembly]::LoadWithPartialName($_).Location})
Add-Type -Path (Join-Path $PSScriptRoot 'MySqlFeedbackUiChecks.cs') -ReferencedAssemblies $refs
[MusicBoxMySqlFeedbackUiChecks]::Run((Join-Path $PSScriptRoot '..\MusicBoxManagement.Wpf\App.xaml'),$OutputDirectory,[bool]$PreflightOnly)
Write-Output ('Images: '+$OutputDirectory)
