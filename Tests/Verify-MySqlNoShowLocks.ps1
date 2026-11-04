#requires -PSEdition Desktop
param([string]$BuildDirectory,[ValidateSet('Locks','Reservations','CheckIn')][string]$Suite='Locks')
$ErrorActionPreference='Stop'
if(!$BuildDirectory){$BuildDirectory=Join-Path $PSScriptRoot '..\MusicBoxManagement.Wpf\bin\Debug'}
. "$PSScriptRoot\..\Tools\Load-AivenBuild.ps1" -BuildDirectory $BuildDirectory
$deps=@('MySqlConnector.dll','Microsoft.AspNet.Identity.Core.dll','Microsoft.AspNet.Identity.EntityFramework.dll','EntityFramework.dll')
foreach($dll in $deps){[void][Reflection.Assembly]::LoadFrom((Join-Path $buildPath $dll))}
$refs=@('System.dll','System.Core.dll','System.Data.dll','System.ComponentModel.DataAnnotations.dll')+@($deps|ForEach-Object{Join-Path $buildPath $_})+@([Reflection.Assembly]::LoadFrom((Join-Path $buildPath 'MusicBoxManagement.Wpf.exe')).FullName)
switch($Suite){
 'Locks'{Add-Type -Path (Join-Path $PSScriptRoot 'MySqlNoShowLockChecks.cs') -ReferencedAssemblies $refs;[MusicBoxMySqlNoShowLockChecks]::Run()}
 'Reservations'{Add-Type -Path (Join-Path $PSScriptRoot 'MySqlReservationChecks.cs') -ReferencedAssemblies $refs;[MusicBoxMySqlReservationChecks]::Run()}
 'CheckIn'{Add-Type -Path (Join-Path $PSScriptRoot 'MySqlCheckInChecks.cs') -ReferencedAssemblies $refs;[MusicBoxMySqlCheckInChecks]::Run()}
}
