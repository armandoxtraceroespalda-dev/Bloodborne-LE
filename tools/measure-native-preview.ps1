param(
 [string]$Exe = "$PSScriptRoot\..\Bloodborne Launcher.exe",
 [int]$Seconds = 52,
 [switch]$CurrentLauncher,
 [string]$Output = "$PSScriptRoot\..\launcher\memory-comparison.csv"
)
$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path $Exe).Path
$directory = Split-Path $resolved
$Output = [IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory((Split-Path $Output)) | Out-Null
$arguments = if ($CurrentLauncher) { @() } else { @('--benchmark', '--report', ('"' + (Join-Path (Split-Path $Output) 'native-preview-metrics.json') + '"')) }
$process = if ($arguments.Count) { Start-Process -FilePath $resolved -WorkingDirectory $directory -ArgumentList $arguments -PassThru } else { Start-Process -FilePath $resolved -WorkingDirectory $directory -PassThru }
$samples = [Collections.Generic.List[object]]::new()
$clock = [Diagnostics.Stopwatch]::StartNew()
$lastCpu = 0.0
$lastTime = 0.0
Write-Host 'Keep the measured launcher in the foreground. Do not start the game.'
if ($CurrentLauncher) { Write-Host 'Navigate through the tabs and scroll, matching the native preview test.' }
while ($clock.Elapsed.TotalSeconds -lt $Seconds) {
 Start-Sleep -Milliseconds 1000
 $process.Refresh()
 if ($process.HasExited) { break }
 $now = $clock.Elapsed.TotalSeconds
 $cpu = $process.TotalProcessorTime.TotalSeconds
 $percent = 100 * ($cpu - $lastCpu) / [Math]::Max(0.001, $now - $lastTime) / [Environment]::ProcessorCount
 $samples.Add([pscustomobject]@{seconds=[Math]::Round($now,2);pid=$process.Id;privateMiB=[Math]::Round($process.PrivateMemorySize64/1MB,2);workingSetMiB=[Math]::Round($process.WorkingSet64/1MB,2);cpuPercent=[Math]::Round($percent,2)})
 $lastCpu = $cpu; $lastTime = $now
}
$samples | Export-Csv -LiteralPath $Output -NoTypeInformation -Encoding UTF8
Write-Host "Saved $Output"
if ($samples.Count) { $samples | Measure-Object -Property privateMiB,workingSetMiB,cpuPercent -Average -Maximum | Format-Table Property,Average,Maximum }
# A legacy launcher remains open; this script never terminates an existing app or game.
