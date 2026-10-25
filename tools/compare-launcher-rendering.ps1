param(
    [string]$Launcher = (Join-Path $PSScriptRoot '../Bloodborne Launcher.exe'),
    [string]$ReportDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'launcher-render-audit')
)
$ErrorActionPreference = 'Stop'
$exe = [IO.Path]::GetFullPath($Launcher)
if (-not (Test-Path -LiteralPath $exe)) { throw 'Build the launcher first: bash tools/build-launcher-only.sh' }
if (@(Get-Process | Where-Object { $_.ProcessName -eq 'Bloodborne Launcher' }).Count -gt 0) {
    throw 'Close Bloodborne Launcher before running this comparison.'
}
New-Item -ItemType Directory -Force -Path $ReportDirectory | Out-Null
$reportRoot = [IO.Path]::GetFullPath($ReportDirectory)
$configDir = Join-Path $env:APPDATA 'bbport-launcher'
$summary = [System.Collections.Generic.List[object]]::new()
$reportFiles = [System.Collections.Generic.List[string]]::new()
Write-Host 'Four automatic runs (about one minute). Leave the window size unchanged and do not navigate.'
Write-Host 'The launcher will close each test window automatically. The game is not started.'
$number = 0
# Balanced order reduces first-run warming bias; identical UI and dimensions in each process.
foreach ($mode in @('default', 'software', 'software', 'default')) {
    $number++
    $jsonPath = Join-Path $configDir ('memory-audit-' + $mode + '.json')
    if (Test-Path -LiteralPath $jsonPath) { Remove-Item -LiteralPath $jsonPath }
    $runArgs = @('--memory-audit')
    if ($mode -eq 'software') { $runArgs += '--software-rendering' }
    else { $runArgs += '--default-rendering' }
    Write-Host ("Run {0}/4: {1}" -f $number, $mode)
    $process = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe -Parent) -ArgumentList $runArgs -PassThru
    $null = $process.Handle
    try {
        if (-not $process.WaitForExit(60000)) {
            throw 'The audit did not finish. Close its window and inspect launcher-diagnostics.log; no process was forcibly terminated.'
        }
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $jsonPath)) {
            throw 'This launcher does not support the audit, or startup failed. Rebuild UI20 and check launcher-diagnostics.log.'
        }
    } finally { $process.Dispose() }
    $prefix = '{0:D2}-{1}' -f $number, $mode
    $copiedJson = Join-Path $reportRoot ($prefix + '.json')
    Copy-Item -LiteralPath $jsonPath -Destination $copiedJson
    $reportFiles.Add($copiedJson)
    $log = Join-Path $configDir 'launcher-diagnostics.log'
    if (Test-Path -LiteralPath $log) {
        $copiedLog = Join-Path $reportRoot ($prefix + '.log')
        Copy-Item -LiteralPath $log -Destination $copiedLog
        $reportFiles.Add($copiedLog)
    }
    $data = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
    $settled = @($data.samples | Where-Object { $_.seconds -ge 8 })
    if ($settled.Count -eq 0) { throw 'The audit report contains no settled samples.' }
    $cpuRate = ($settled[-1].cpuSeconds - $settled[0].cpuSeconds) / ($settled[-1].seconds - $settled[0].seconds)
    $summary.Add([pscustomobject]@{
        CpuOneCorePercent = [Math]::Round($cpuRate * 100, 2)
        Run = $number; Mode = $mode; RenderPreference = $data.renderPreference; RenderTier = $data.renderTier
        Width = $data.width; Height = $data.height; DpiScale = $data.dpi
        WorkingSetMiB = [Math]::Round(($settled | Measure-Object -Property workingSetMiB -Average).Average, 2)
        PrivateCommitMiB = [Math]::Round(($settled | Measure-Object -Property privateCommitMiB -Average).Average, 2)
        ManagedMiB = [Math]::Round(($settled | Measure-Object -Property managedMiB -Average).Average, 2)
        GcCommittedMiB = [Math]::Round(($settled | Measure-Object -Property gcCommittedMiB -Average).Average, 2)
    })
}
$csv = Join-Path $reportRoot 'summary.csv'
$summary | Export-Csv -NoTypeInformation -Encoding UTF8 -Path $csv
$reportFiles.Add($csv)
Compress-Archive -LiteralPath $reportFiles.ToArray() -DestinationPath ($reportRoot + '.zip') -Force
$summary | Format-Table -AutoSize
Write-Host ('Reports saved in: ' + $reportRoot)
Write-Host 'Rendering preferences apply only to these test processes; no registry or persistent render setting is changed.'
