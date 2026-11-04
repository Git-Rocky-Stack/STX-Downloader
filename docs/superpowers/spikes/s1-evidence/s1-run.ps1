param([string]$Media, [string]$Tag, [int]$Seconds = 70, [switch]$Script)
$ErrorActionPreference = 'Stop'
$exe = 'C:\Users\User\Desktop\Development Projects\Strategia-Enhanced-App\STX Downloader\src\Stx.App\bin\Release\net10.0-windows10.0.22621.0\win-x64\STXDownloader.exe'
$out = 'C:\Users\User\AppData\Local\Temp\claude\715dfb024409618a\fe940724-66c4-45aa-9dc9-057f1a5ae799\scratchpad\s1out'
New-Item -ItemType Directory -Force $out | Out-Null
$log = Join-Path $out "$Tag.log"

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Drawing;
namespace S1 { public static class N {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public struct RECT { public int L, T, R, B; }
  public struct POINT { public int X, Y; }
  public static void Shot(IntPtr h, string path) {
    RECT r; GetWindowRect(h, out r);
    using (var bmp = new Bitmap(r.R - r.L, r.B - r.T)) {
      using (var g = Graphics.FromImage(bmp)) { var dc = g.GetHdc(); PrintWindow(h, dc, 2); g.ReleaseHdc(dc); }
      bmp.Save(path);
    }
  }
  public static void ScreenShot(IntPtr h, string path) {
    RECT r; GetWindowRect(h, out r);
    using (var bmp = new Bitmap(r.R - r.L, r.B - r.T)) {
      using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.L, r.T, 0, 0, bmp.Size);
      bmp.Save(path);
    }
  }
  public static void Click(IntPtr h, double dipX, double dipY, double scale) {
    var p = new POINT { X = (int)(dipX * scale), Y = (int)(dipY * scale) };
    ClientToScreen(h, ref p); SetCursorPos(p.X, p.Y);
    mouse_event(2, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(60); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
  }
}}
'@
[S1.N]::SetProcessDPIAware() | Out-Null

$env:STX_SPIKE_MEDIA = $Media
$env:STX_SPIKE_LOG = $log
$env:STX_SPIKE_SECONDS = "$Seconds"
$env:STX_SPIKE_SCRIPT = if ($Script) { '1' } else { '0' }
$p = Start-Process $exe -ArgumentList '--gallery=player-spike' -PassThru
$t0 = Get-Date
while ($p.MainWindowHandle -eq 0 -and ((Get-Date) - $t0).TotalSeconds -lt 20) { Start-Sleep -Milliseconds 200; $p.Refresh() }
$h = $p.MainWindowHandle
"window $h"

function At([double]$s) { while (((Get-Date) - $t0).TotalSeconds -lt $s) { Start-Sleep -Milliseconds 100 } }
function Bounds {
  $line = (Get-Content $log | Where-Object { $_ -match ' bounds ' } | Select-Object -Last 1)
  if ($line -match 'scale=([\d.]+) over=\((\d+),(\d+),(\d+),(\d+)\) next=\((\d+),(\d+),(\d+),(\d+)\)') {
    return @{ s = [double]$Matches[1]; ox = [double]$Matches[2] + [double]$Matches[4] / 2; oy = [double]$Matches[3] + [double]$Matches[5] / 2; nx = [double]$Matches[6] + [double]$Matches[8] / 2; ny = [double]$Matches[7] + [double]$Matches[9] / 2 }
  }
}
if ($Script) {
  At 6;  [S1.N]::Shot($h, "$out\$Tag-1-inplate.png")
  At 9;  [S1.N]::Shot($h, "$out\$Tag-2-clicked.png")
  At 12; [S1.N]::Shot($h, "$out\$Tag-3-scrolled.png")
  At 17; [S1.N]::Shot($h, "$out\$Tag-4-back.png")
  At 23; [S1.N]::Shot($h, "$out\$Tag-5-fullscreen.png")
  At 28; [S1.N]::Shot($h, "$out\$Tag-6-out.png")
}
$p.WaitForExit(($Seconds + 30) * 1000) | Out-Null
"exited $($p.HasExited) code $(if ($p.HasExited) { $p.ExitCode })"


"cpu seconds $($p.TotalProcessorTime.TotalSeconds) over wall $(((Get-Date) - $t0).TotalSeconds) on $([Environment]::ProcessorCount) logical cores"
