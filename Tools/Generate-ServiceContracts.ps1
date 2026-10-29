#requires -PSEdition Desktop
param([string]$BuildDirectory="$PSScriptRoot\..\MusicBoxManagement.Wpf\bin\VerifyCompleteMySqlDebug")
$ErrorActionPreference='Stop'
. "$PSScriptRoot\Load-AivenBuild.ps1" -BuildDirectory $BuildDirectory
$names=@('AuthenticationService','PermissionService','RoomTypeService','RoomService','ServiceCatalogService','CustomerService','GuestReservationService','GuestSessionService','OrderService','BillingService','RoomSessionService','StaffSessionService','CheckoutService','DashboardService','AccountManagementService','PermissionManagementService','AuditLogService','ReportService','RoomUsageReportService','BookingStatusReportService','ServiceSalesReportService','CustomerHistoryService','CalendarService','StaffReservationService')
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $buildPath 'MusicBoxManagement.Wpf.exe'))
function Contract-Type([Type]$t){
    if($t -eq [void]){return 'void'}
    if($names -contains $t.Name){return 'I'+$t.Name}
    if($t.Name -eq 'InvoiceService'){return 'IInvoiceService'}
    if($t.Name -in @('StaffBookingService','GuestBookingService')){return 'IBookingService'}
    if($t.IsArray){return (Contract-Type $t.GetElementType())+'[]'}
    if($t.IsGenericType){$base=$t.GetGenericTypeDefinition().FullName.Split('`')[0];return $base+'<'+(($t.GetGenericArguments()|ForEach-Object{Contract-Type $_}) -join ', ' )+'>'}
    return $t.FullName.Replace('+','.')
}
$text="// Shared contracts for the existing SQLite and MySQL providers. No database work here.`r`nnamespace MusicBoxManagement.Wpf.Services`r`n{`r`n"
foreach($name in $names){
    $type=$assembly.GetType('MusicBoxManagement.Wpf.Services.'+$name,$true)
    $text+='    public interface I'+$name+"`r`n    {`r`n"
    foreach($method in $type.GetMethods([Reflection.BindingFlags]'Public,NonPublic,Instance,DeclaredOnly')|Where-Object{!$_.IsSpecialName -and ($_.IsPublic -or $_.Name -like 'For*') -and !(@($_.GetParameters()|Where-Object{$_.ParameterType.FullName -like 'System.Data.SQLite*'}).Count)}){
        $parts=@();foreach($parameter in $method.GetParameters()){
            $part=(Contract-Type $parameter.ParameterType)+' @'+$parameter.Name
            if($parameter.IsOptional){$v=$parameter.DefaultValue;if($null -eq $v){$part+=' = null'}elseif($v -is [string]){$part+=' = "'+$v.Replace('\','\\').Replace('"','\"')+'"'}elseif($v -is [bool]){$part+=' = '+$v.ToString().ToLowerInvariant()}else{$part+=' = '+[Convert]::ToString($v,[Globalization.CultureInfo]::InvariantCulture)}}
            $parts+=$part
        }
        $text+='        '+(Contract-Type $method.ReturnType)+' '+$method.Name+'('+($parts -join ', ')+");`r`n"
    }
    $text+="    }`r`n"
}
$text+="}`r`n"
[IO.File]::WriteAllText((Join-Path $PSScriptRoot '..\MusicBoxManagement.Wpf\Services\ServiceContracts.cs'),$text,[Text.UTF8Encoding]::new($false))
