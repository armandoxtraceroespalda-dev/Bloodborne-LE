param(
    [ValidateRange(30,900)][int]$Seconds = 120,
    [string]$LauncherPath = (Join-Path $PSScriptRoot '../Bloodborne Launcher.exe'),
    [string]$ReportFolder = (Join-Path (Get-Location) 'launcher-retention-report'),
    [switch]$CaptureHeap,
    [switch]$AutomaticGcOnly
)
$ErrorActionPreference = 'Stop'
if (@(Get-Process | Where-Object { $_.ProcessName -eq 'Bloodborne Launcher' }).Count -ne 0) {
    throw 'Close the launcher before starting this comparison.'
}
$LauncherPath = (Resolve-Path -LiteralPath $LauncherPath).Path
$null = New-Item -ItemType Directory -Force -Path $ReportFolder
$ReportFolder = (Resolve-Path -LiteralPath $ReportFolder).Path
$dumpTool = $null
if ($CaptureHeap) {
    $dumpTool = Get-Command dotnet-gcdump -ErrorAction SilentlyContinue
    if ($null -eq $dumpTool) {
        throw 'Optional heap capture requires: dotnet tool install --global dotnet-gcdump. Run without -CaptureHeap for observation only.'
    }
    Write-Host 'Heap capture is a separate diagnostic run: each requested dump induces a full GC.'
}
$launcherArgs = @('--retention-audit')
if ($AutomaticGcOnly) { $launcherArgs += '--automatic-gc-only' }
$launcher = Start-Process -FilePath $LauncherPath -ArgumentList $launcherArgs -PassThru
$rows = [System.Collections.Generic.List[object]]::new()
try {
    for ($sample = 0; $sample -lt $Seconds; $sample++) {
        $launcher.Refresh()
        if ($launcher.HasExited) { break }
        $phase = if ($sample -lt 15) { 'idle-start' } elseif ($sample -lt 45) { 'hover-start' } elseif ($sample -lt 90) { 'navigate-tabs' } else { 'settled-page' }
        if ($sample -eq 0) { Write-Host 'First 15 seconds: leave Start idle.' }
        if ($sample -eq 15) {
            if ($CaptureHeap) {
                & $dumpTool.Source collect -p $launcher.Id -o (Join-Path $ReportFolder 'before.gcdump')
                if ($LASTEXITCODE -ne 0) { throw 'Baseline heap capture failed.' }
            }
            Write-Host 'Next 30 seconds: hover Start and the sidebar without changing pages.'
        }
        if ($sample -eq 45) { Write-Host 'Next 45 seconds: visit all tabs, including Controls and More.' }
        if ($sample -eq 90) { Write-Host 'Stay on any page until sampling ends.' }
        $rows.Add([pscustomobject]@{
            Time = (Get-Date).ToString('o'); Phase = $phase
            WorkingSetMiB = [Math]::Round($launcher.WorkingSet64 / 1MB, 2)
            PrivateCommitMiB = [Math]::Round($launcher.PrivateMemorySize64 / 1MB, 2)
            CpuSeconds = [Math]::Round($launcher.TotalProcessorTime.TotalSeconds, 2)
            Handles = $launcher.HandleCount
        })
        Start-Sleep -Seconds 1
    }
    $rows | Export-Csv -LiteralPath (Join-Path $ReportFolder 'process-memory.csv') -NoTypeInformation -Encoding UTF8
    $launcher.Refresh()
    if ($CaptureHeap -and -not $launcher.HasExited) {
        & $dumpTool.Source collect -p $launcher.Id -o (Join-Path $ReportFolder 'after.gcdump')
        if ($LASTEXITCODE -ne 0) { throw 'Final heap capture failed.' }
    }
    $config = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'bbport-launcher'
    foreach ($name in @('launcher-retention.csv','launcher-diagnostics.log')) {
        $file = Join-Path $config $name
        if (Test-Path -LiteralPath $file) { Copy-Item -LiteralPath $file -Destination (Join-Path $ReportFolder $name) -Force }
    }
    Write-Host ('Report: ' + $ReportFolder)
    Write-Host 'The launcher stays open. The sampler never trims or collects; normal navigation cleanup is disabled only with -AutomaticGcOnly.'
} finally {
    $launcher.Dispose()
}
