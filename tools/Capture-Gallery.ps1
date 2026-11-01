param(
    [Parameter(Mandatory)] [string] $Name,
    [ValidateSet('dark', 'light')] [string] $Mode = 'dark',
    [Parameter(Mandatory)] [string] $Out,
    [int] $SettleSeconds = 3
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'src\Stx.App\bin\x64\Release\net10.0-windows10.0.22621.0\win-x64\STXDownloader.exe'
if (-not (Test-Path $exe)) { throw "Build first: $exe not found" }

Add-Type -AssemblyName System.Drawing
if (-not ('StxCapture.Native' -as [type])) {
    Add-Type -Namespace StxCapture -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, int[] rect);
[DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, int[] rect, int size);
'@
}

$p = Start-Process $exe -ArgumentList "--gallery=$Name", "--mode=$Mode", '--hold' -PassThru
try {
    $deadline = (Get-Date).AddSeconds(20)
    while ($p.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100; $p.Refresh() }
    if ($p.MainWindowHandle -eq 0) { throw "No window for gallery '$Name'" }
    Start-Sleep -Seconds $SettleSeconds
    [StxCapture.Native]::SetWindowPos($p.MainWindowHandle, [IntPtr]::new(-1), 40, 40, 0, 0, 0x41) | Out-Null
    [StxCapture.Native]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 800
    $r = New-Object int[] 4
    # Attribute 9 is the visible frame, without the invisible resize border.
    if ([StxCapture.Native]::DwmGetWindowAttribute($p.MainWindowHandle, 9, $r, 16) -ne 0) {
        [StxCapture.Native]::GetWindowRect($p.MainWindowHandle, $r) | Out-Null
    }
    $bmp = New-Object Drawing.Bitmap ($r[2] - $r[0]), ($r[3] - $r[1])
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r[0], $r[1], 0, 0, $bmp.Size)
    $g.Dispose()
    New-Item -ItemType Directory -Force (Split-Path $Out -Parent) | Out-Null
    $bmp.Save($Out)
    $bmp.Dispose()
    "captured $Name $Mode -> $Out"
}
finally {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
}
