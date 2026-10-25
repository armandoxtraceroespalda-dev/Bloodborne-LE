param(
 [Parameter(Mandatory=$true)][string]$ReferenceExe,
 [string]$NativeExe="$PSScriptRoot\..\LauncherApp\Bloodborne Launcher.exe",
 [string]$OutputDirectory="$PSScriptRoot\..\LauncherApp\visual-reference"
)
$ErrorActionPreference='Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ReferenceWindows {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window,out Rect bounds);
 [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window,int command);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
}
'@
[ReferenceWindows]::SetProcessDpiAwarenessContext([IntPtr](-4))|Out-Null
Add-Type -AssemblyName System.Drawing
function Open-Launcher([string]$File) {
 $path=(Resolve-Path -LiteralPath $File).Path
 $process=Get-Process | Where-Object { try { $_.Path -eq $path } catch { $false } } | Select-Object -First 1
 if(!$process){$process=Start-Process -FilePath $path -WorkingDirectory (Split-Path $path) -PassThru}
 $deadline=[DateTime]::UtcNow.AddSeconds(20)
 do {Start-Sleep -Milliseconds 100;$process.Refresh();if($process.HasExited){throw "Launcher exited: $path"}} while($process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
 if($process.MainWindowHandle -eq 0){throw "No visible launcher window: $path"}
 return $process
}
$reference=Open-Launcher $ReferenceExe
$native=Open-Launcher $NativeExe
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($OutputDirectory)|Out-Null
foreach($process in @($reference,$native)) {
 $handle=$process.MainWindowHandle
 [ReferenceWindows]::ShowWindow($handle,9)|Out-Null
 $dpi=[ReferenceWindows]::GetDpiForWindow($handle)/96.0
 [ReferenceWindows]::SetWindowPos($handle,[IntPtr]::Zero,32,32,[int](1280*$dpi),[int](800*$dpi),4)|Out-Null
}
function Capture-Window($Process,[string]$Name,[bool]$Activate=$true) {
 $handle=$Process.MainWindowHandle
 if($Activate){[ReferenceWindows]::SetForegroundWindow($handle)|Out-Null}
 Start-Sleep -Milliseconds 800
 $bounds=New-Object ReferenceWindows+Rect
 [ReferenceWindows]::GetWindowRect($handle,[ref]$bounds)|Out-Null
 $bitmap=New-Object Drawing.Bitmap ($bounds.Right-$bounds.Left),($bounds.Bottom-$bounds.Top)
 try {
  $drawing=[Drawing.Graphics]::FromImage($bitmap)
  try{$drawing.CopyFromScreen($bounds.Left,$bounds.Top,0,0,$bitmap.Size)}finally{$drawing.Dispose()}
  $bitmap.Save((Join-Path $OutputDirectory "$Name.png"),[Drawing.Imaging.ImageFormat]::Png)
 } finally {$bitmap.Dispose()}
}
foreach($tab in @('Start','Game','Graphics','Controls','Tweaks','Online','Achievements','Settings','More')) {
 Read-Host "Open $tab in BOTH launchers, then press Enter. Do not start the game"|Out-Null
 Capture-Window $reference "$tab-reference"
 Capture-Window $native "$tab-native"
 # Keep the native window visible while giving focus to the reference. This
 # captures the reported inactive frame without an overlapping window hiding it.
 [ReferenceWindows]::SetWindowPos($native.MainWindowHandle,[IntPtr](-1),0,0,0,0,0x13)|Out-Null
 try {
  [ReferenceWindows]::SetForegroundWindow($reference.MainWindowHandle)|Out-Null
  Capture-Window $native "$tab-native-inactive" $false
 } finally {[ReferenceWindows]::SetWindowPos($native.MainWindowHandle,[IntPtr](-2),0,0,0,0,0x13)|Out-Null}
}
$html='<!doctype html><meta charset="utf-8"><title>Launcher visual reference</title><style>body{background:#141617;color:#e3ddcb;font:16px system-ui;margin:20px}section{display:flex;gap:16px;overflow:auto}figure{margin:0;flex:0 0 640px}img{width:640px}figcaption{padding:8px}</style><h1>UI59 reference / native / inactive native</h1><p>Captured at 1280 × 800 logical units on the same monitor. Pulsing glow phases can differ. This is a comparison aid, not an automatic pixel-parity certificate.</p>'
foreach($tab in @('Start','Game','Graphics','Controls','Tweaks','Online','Achievements','Settings','More')) {
 $html+="<h2>$tab</h2><section>"
 foreach($kind in @('reference','native','native-inactive')){$html+="<figure><figcaption>$kind</figcaption><img src='$tab-$kind.png'></figure>"}
 $html+='</section>'
}
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'comparison.html'),$html)
Write-Host "Saved local captures to $OutputDirectory"
Write-Host 'Nothing was uploaded. Both launchers remain open. Inspect comparison.html.'
