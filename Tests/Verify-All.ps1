#requires -PSEdition Desktop
param(
    [string]$BuildDirectory = "$PSScriptRoot\..\MusicBoxManagement.Wpf\bin\Debug",
    [switch]$IncludeMySql,
    [ValidateRange(1,4)][int]$Parallelism = 3,
    [string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('MusicBoxRegression_' + [Guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference = 'Stop'
$buildPath = (Resolve-Path $BuildDirectory).Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($outputPath)
# Each suite runs in its own STA process: no assembly/dispatcher state leaks between suites.
# Existing suites create GUID databases; this runner never points them at application data.
$suites = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'Verify-*.ps1' |
    Where-Object { $_.BaseName -ne 'Verify-All' -and ($IncludeMySql -or $_.BaseName -notlike 'Verify-MySql*') } |
    Sort-Object Name)
$queue = New-Object 'System.Collections.Generic.Queue[object]'
foreach ($suite in $suites) { $queue.Enqueue($suite) }
$running = New-Object System.Collections.ArrayList
$results = New-Object System.Collections.ArrayList
Write-Output ("Running {0} suites; logs: {1}" -f $suites.Count, $outputPath)
while ($queue.Count -gt 0 -or $running.Count -gt 0) {
    while ($queue.Count -gt 0 -and $running.Count -lt $Parallelism) {
        $suite = $queue.Dequeue()
        $stdout = Join-Path $outputPath ($suite.BaseName + '.out.log')
        $stderr = Join-Path $outputPath ($suite.BaseName + '.err.log')
        $arguments = '-STA -NoProfile -ExecutionPolicy Bypass -File "{0}" -BuildDirectory "{1}"' -f $suite.FullName, $buildPath
        $process = Start-Process -FilePath powershell.exe -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        # Retain the native handle before exit; Windows PowerShell otherwise loses ExitCode.
        $null = $process.Handle
        [void]$running.Add([pscustomobject]@{ Name = $suite.BaseName; Process = $process; StartedAt = Get-Date; Stdout = $stdout; Stderr = $stderr })
    }
    foreach ($item in @($running.ToArray())) {
        if (!$item.Process.HasExited) { continue }
        $item.Process.WaitForExit()
        $result = [pscustomobject]@{ Suite = $item.Name; ExitCode = $item.Process.ExitCode; Seconds = [math]::Round(((Get-Date) - $item.StartedAt).TotalSeconds, 1); Stdout = $item.Stdout; Stderr = $item.Stderr }
        [void]$results.Add($result)
        Write-Output ("{0}: {1} ({2}s)" -f $result.Suite, $(if ($result.ExitCode -eq 0) { 'PASS' } else { 'FAIL' }), $result.Seconds)
        $item.Process.Dispose()
        $running.Remove($item)
        $results.ToArray() | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $outputPath 'results.json') -Encoding UTF8
    }
    if ($running.Count -gt 0) { Start-Sleep -Milliseconds 500 }
}
$failed = @($results | Where-Object { $_.ExitCode -ne 0 })
Write-Output ("Passed: {0}/{1}. Summary: {2}" -f ($results.Count - $failed.Count), $results.Count, (Join-Path $outputPath 'results.json'))
if ($failed.Count -gt 0) {
    foreach ($item in $failed) {
        Write-Output ("Failure log: " + $item.Stderr)
        Get-Content -LiteralPath $item.Stderr -Tail 16
    }
    exit 1
}
