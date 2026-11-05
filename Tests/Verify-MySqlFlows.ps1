#requires -PSEdition Desktop
param(
    [Parameter(Mandatory=$true)][string]$Suite,
    [string]$BuildDirectory = "$PSScriptRoot\..\MusicBoxManagement.Wpf\bin\Release",
    [string]$OutputDirectory
)
$ErrorActionPreference='Stop'
# Explicit allowlist: never route to a mixed-provider suite or import tool.
$allowed=@('Account','AccountBatch','AuditLog','Availability','Billing','BookingRead','Calendar',
    'CheckIn','Checkout','Customer','Dashboard','GuestSessions','Initialize','Invoice','NoShowLock',
    'Order','OrderBatch','PermissionMatrix','ReadBatch','RemainingReads','Reservation','RevenueReport',
    'Room','RoomType','Service','Sessions','StaffReservation','WalkIn',
    'BookingHoursUi','CounterUi','FeedbackUi','OperationsUi','InvoicePrinting','Foundation')
if($allowed -notcontains $Suite){throw 'Suite is not on the MySQL-only allowlist.'}
if($Suite -eq 'Foundation'){
    & "$PSScriptRoot\Verify-MySqlFoundation.ps1" -BuildDirectory $BuildDirectory
    exit 0
}
$source=Join-Path $PSScriptRoot ('MySql'+$Suite+'Checks.cs')
if((Get-Content -LiteralPath $source -Raw) -match '(?i)sqlite|SqliteDatabase|MySqlImport'){
    throw 'Refusing a source with a legacy-provider or import dependency.'
}
. "$PSScriptRoot\..\Tools\Load-AivenBuild.ps1" -BuildDirectory $BuildDirectory
$dependencies=@('MySqlConnector.dll','Microsoft.AspNet.Identity.Core.dll',
    'Microsoft.AspNet.Identity.EntityFramework.dll','EntityFramework.dll','SkiaSharp.dll',
    'System.Memory.dll','System.Buffers.dll','System.Numerics.Vectors.dll','System.Runtime.CompilerServices.Unsafe.dll')
foreach($dll in $dependencies){[void][Reflection.Assembly]::LoadFrom((Join-Path $buildPath $dll))}
$refs=@('System.dll','System.Core.dll','System.Data.dll','System.Xml.dll','System.Numerics.dll',
    'System.ComponentModel.DataAnnotations.dll','System.IO.Compression.dll')+
    @($dependencies|ForEach-Object{Join-Path $buildPath $_})+
    @(Join-Path $buildPath 'MusicBoxManagement.Wpf.exe')
$ui=@('BookingHoursUi','CounterUi','FeedbackUi','OperationsUi','InvoicePrinting')
if($ui -contains $Suite){
    if([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA'){throw 'Use powershell.exe -STA.'}
    foreach($name in @('WindowsBase','PresentationCore','PresentationFramework','System.Xaml','System.Printing','ReachFramework')){
        Add-Type -AssemblyName $name
        $refs+=([Reflection.Assembly]::LoadWithPartialName($name).Location)
    }
    if(!$OutputDirectory){$OutputDirectory=Join-Path $env:TEMP ('MusicBoxFlow_'+$Suite+'_'+[Guid]::NewGuid().ToString('N'))}
}
$compiler=New-Object System.CodeDom.Compiler.CompilerParameters
$compiler.CompilerOptions='/nowarn:1701'
$compiler.ReferencedAssemblies.AddRange([string[]]$refs)
Add-Type -Path $source -CompilerParameters $compiler
$checks=('MusicBoxMySql'+$Suite+'Checks') -as [type]
Write-Output ('RUN '+$Suite+' build='+$buildPath)
if($ui -contains $Suite){
    $theme=(Resolve-Path "$PSScriptRoot\..\MusicBoxManagement.Wpf\App.xaml").Path
    if($Suite -eq 'FeedbackUi'){$checks::Run($theme,$OutputDirectory,$false)}
    else{$checks::Run($theme,$OutputDirectory)}
    Write-Output ('Images: '+$OutputDirectory)
}else{$checks::Run()}
Write-Output ('COMPLETE '+$Suite)
