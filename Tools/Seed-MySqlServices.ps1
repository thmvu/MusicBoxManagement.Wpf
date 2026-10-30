#requires -PSEdition Desktop
param([switch]$Apply, [string]$DatabaseName='musicbox', [string]$BuildDirectory)
$ErrorActionPreference='Stop'
if (!$BuildDirectory) { $BuildDirectory=Join-Path $PSScriptRoot '..\MusicBoxManagement.Wpf\bin\Release' }
. "$PSScriptRoot\Load-AivenBuild.ps1" -BuildDirectory $BuildDirectory
# Administrative seed: no fabricated LoginSession, existing records are never updated.
$items=@(
    @{Name='Nước suối (Demo)';Category='Đồ uống';Price=[long]10000},
    @{Name='Coca-Cola (Demo)';Category='Đồ uống';Price=[long]20000},
    @{Name='Trà đào (Demo)';Category='Đồ uống';Price=[long]30000},
    @{Name='Nước cam (Demo)';Category='Đồ uống';Price=[long]35000},
    @{Name='Snack (Demo)';Category='Đồ ăn';Price=[long]20000},
    @{Name='Khoai tây chiên (Demo)';Category='Đồ ăn';Price=[long]35000},
    @{Name='Đĩa trái cây (Demo)';Category='Đồ ăn';Price=[long]60000},
    @{Name='Khăn lạnh (Demo)';Category='Khác';Price=[long]5000}
)
foreach($item in $items) {
    if($item.Name.Length -lt 1 -or $item.Name.Length -gt 100 -or $item.Price -le 0 -or
        ![MusicBoxManagement.Wpf.Services.MySqlServiceCatalogService]::Categories.Contains($item.Category)) {
        throw 'Dữ liệu seed không đúng hợp đồng danh mục dịch vụ.'
    }
}
$db=[MusicBoxManagement.Wpf.Data.MySqlDatabase]::new($DatabaseName)
$connection=$db.OpenConnection();$tx=$null
function SeedCommand([string]$Sql,[hashtable]$Values) {
    $q=$connection.CreateCommand();$q.Transaction=$tx;$q.CommandText=$Sql
    foreach($key in $Values.Keys){[void]$q.Parameters.AddWithValue('@'+$key,$Values[$key])}
    return $q
}
$result=[Collections.Generic.List[object]]::new()
try {
    $tx=if($Apply){[MusicBoxManagement.Wpf.Data.MySqlDatabase]::BeginWriteTransaction($connection)}else{[MusicBoxManagement.Wpf.Data.MySqlDatabase]::BeginReadTransaction($connection)}
    $q=SeedCommand 'SELECT COUNT(*) FROM AspNetUsers u JOIN AspNetUserRoles ur ON ur.UserId=u.Id WHERE u.IsActive=1 AND ur.RoleId=''Admin''' @{}
    try{if([int]$q.ExecuteScalar() -lt 1){throw 'Cần thiết lập Admin trước; script không tạo tài khoản.'}}finally{$q.Dispose()}
    $now=[DateTimeOffset]::UtcNow.ToString('O',[Globalization.CultureInfo]::InvariantCulture)
    foreach($item in $items) {
        # Name is not unique in the business schema. Any existing matching name is preserved.
        $q=SeedCommand 'SELECT ServiceId FROM Services WHERE Name=@name ORDER BY ServiceId LIMIT 1' @{name=$item.Name}
        try{$existing=$q.ExecuteScalar()}finally{$q.Dispose()}
        if($null -ne $existing){$id=[int]$existing;$state='SkippedExisting'}
        elseif(!$Apply){$id=$null;$state='WouldCreate'}
        else {
            $q=SeedCommand 'INSERT INTO Services(Name,Category,Price,Description,IsActive) VALUES(@name,@category,@price,@description,1); SELECT LAST_INSERT_ID();' @{name=$item.Name;category=$item.Category;price=$item.Price;description='Món/dịch vụ mẫu để thử gọi món Music Box; giá demo theo phần/đơn vị.'}
            try{$id=[int]$q.ExecuteScalar()}finally{$q.Dispose()}
            $q=SeedCommand 'INSERT INTO AuditLog(ActorType,UserId,Action,EntityName,EntityId,Description,CreatedAt) VALUES(''System'',NULL,''DemoSeed.Create'',''Service'',@id,@description,@now)' @{id=[string]$id;description=('Tạo '+$item.Name+' bằng script Seed-MySqlServices.');now=$now}
            try{if($q.ExecuteNonQuery() -ne 1){throw 'Không ghi đủ nhật ký seed dịch vụ.'}}finally{$q.Dispose()}
            $state='Created'
        }
        $result.Add([pscustomobject]@{Name=$item.Name;Category=$item.Category;SeedPrice=$item.Price;Id=$id;State=$state})
    }
    if($Apply){$tx.Commit()}
    $result
} finally {
    # Dispose rolls back any uncommitted transaction. No automatic retry after uncertain COMMIT.
    if($tx){$tx.Dispose()};$connection.Dispose()
}
