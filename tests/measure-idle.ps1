param([Parameter(Mandatory=$true)][string]$Exe, [int]$Seconds=10)
$ErrorActionPreference='Stop'
$p=Start-Process -FilePath $Exe -ArgumentList '--demo' -PassThru -WindowStyle Hidden
try {
    Start-Sleep -Seconds 3
    $p.Refresh()
    if ($p.HasExited) { throw 'Demo exited during startup' }
    $before=$p.TotalProcessorTime.TotalMilliseconds
    Start-Sleep -Seconds $Seconds
    $p.Refresh()
    [pscustomobject]@{
        Executable=$Exe
        IdleSeconds=$Seconds
        CpuMilliseconds=($p.TotalProcessorTime.TotalMilliseconds-$before)
        PrivateMB=[Math]::Round($p.PrivateMemorySize64/1MB,2)
        WorkingSetMB=[Math]::Round($p.WorkingSet64/1MB,2)
        Handles=$p.HandleCount
        Threads=$p.Threads.Count
    } | ConvertTo-Json
} finally {
    if (-not $p.HasExited) {
        $signal=[Threading.EventWaitHandle]::OpenExisting('Local\KinetikDockDemo.Shutdown')
        try { $signal.Set() | Out-Null } finally { $signal.Dispose() }
        if (-not $p.WaitForExit(15000)) { throw 'Demo did not shut down' }
    }
    $p.Dispose()
}
