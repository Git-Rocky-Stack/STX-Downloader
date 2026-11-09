param(
    [Parameter(Mandatory)] [string] $Name,
    [ValidateSet('dark', 'light')] [string] $Mode = 'dark',
    [Parameter(Mandatory)] [string] $Out,
    [int] $SettleSeconds = 3,
    [switch] $ReduceMotion,
    # Without --hold, so a playing picture keeps moving (the player's playback tests).
    [switch] $Live
)
# Asks the window to draw itself, rather than copying the screen. So the image holds only the app's content:
# no title bar, nothing from the desktop, nothing from windows on top, and it never takes focus from you.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'src\Stx.App\bin\x64\Release\net10.0-windows10.0.22621.0\win-x64\STXDownloader.exe'
if (-not (Test-Path $exe)) { throw "Build first: $exe not found" }

Add-Type -AssemblyName System.Drawing
if (-not ('StxCaptureWindow.Native' -as [type])) {
    Add-Type -Namespace StxCaptureWindow -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, int[] rect);
[DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, int[] rect);
[DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, int[] point);
[DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
'@
}
# STX is per-monitor DPI aware, so its window is drawn in device pixels. Measured from a thread that isn't, its rectangles
# come back in 96-DPI units: on a 175% screen the print was cut to its top-left 946x603 (2026-11-09). -4 is
# DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2. At 96 DPI both measures agree, so the baselines don't change.
[StxCaptureWindow.Native]::SetThreadDpiAwarenessContext([IntPtr]::new(-4)) | Out-Null

if (-not ('StxCaptureWindow.Pixels' -as [type])) {
    # Compiled, because a PowerShell loop over every pixel takes seconds, longer than the retry window below.
    Add-Type -Namespace StxCaptureWindow -Name Pixels -MemberDefinition @'
public static bool PureBlack(byte[] bgra)
{
    for (int i = 0; i < bgra.Length; i += 4)
        if (bgra[i] != 0 || bgra[i + 1] != 0 || bgra[i + 2] != 0) return false;
    return true;
}
'@
}

function Test-PureBlack([Drawing.Bitmap] $image) {
    $data = $image.LockBits((New-Object Drawing.Rectangle 0, 0, $image.Width, $image.Height), 'ReadOnly', 'Format32bppArgb')
    try {
        $bytes = New-Object byte[] ($data.Stride * $image.Height)
        [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    }
    finally { $image.UnlockBits($data) }
    [StxCaptureWindow.Pixels]::PureBlack($bytes)
}

$arguments = @("--gallery=$Name", "--mode=$Mode")
if (-not $Live) { $arguments += '--hold' }
if ($ReduceMotion) { $arguments += '--reduce-motion' }
$p = Start-Process $exe -ArgumentList $arguments -PassThru
try {
    $deadline = (Get-Date).AddSeconds(20)
    while ($p.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 100; $p.Refresh() }
    if ($p.MainWindowHandle -eq 0) { throw "No window for gallery '$Name'" }
    Start-Sleep -Seconds $SettleSeconds
    $h = $p.MainWindowHandle

    $window = New-Object int[] 4
    $client = New-Object int[] 4
    $origin = New-Object int[] 2
    [StxCaptureWindow.Native]::GetWindowRect($h, $window) | Out-Null
    [StxCaptureWindow.Native]::GetClientRect($h, $client) | Out-Null
    [StxCaptureWindow.Native]::ClientToScreen($h, $origin) | Out-Null
    $area = New-Object Drawing.Rectangle ($origin[0] - $window[0]), ($origin[1] - $window[1]), $client[2], $client[3]

    # A window that hasn't presented a frame yet prints as pure black, and as its content a few hundred milliseconds later.
    # Neither mode's background is pure black, so pure black means "not drawn yet": print again until it isn't, for up to 5 s
    # after the first print returns (a window still starting up can hold that first print for seconds).
    # A window that really draws black stays black, and its comparison fails.
    $content = $null
    $retryUntil = $null
    $retries = 0
    while ($true) {
        if ($content) { $content.Dispose() }
        $whole = New-Object Drawing.Bitmap ($window[2] - $window[0]), ($window[3] - $window[1])
        $g = [Drawing.Graphics]::FromImage($whole)
        $dc = $g.GetHdc()
        # 2 is PW_RENDERFULLCONTENT, which includes the composition layers this app is drawn with.
        $drawn = [StxCaptureWindow.Native]::PrintWindow($h, $dc, 2)
        $g.ReleaseHdc($dc)
        $g.Dispose()
        if (-not $drawn) { $whole.Dispose(); throw "Window would not draw itself for gallery '$Name'" }
        $content = $whole.Clone($area, $whole.PixelFormat)
        $whole.Dispose()
        if ($null -eq $retryUntil) { $retryUntil = (Get-Date).AddSeconds(5) }
        if (-not (Test-PureBlack $content) -or (Get-Date) -ge $retryUntil) { break }
        $retries++
        Start-Sleep -Milliseconds 100
    }
    New-Item -ItemType Directory -Force (Split-Path $Out -Parent) | Out-Null
    $content.Save($Out)
    $content.Dispose()
    "captured $Name $Mode $($client[2])x$($client[3]) -> $Out$(if ($retries) { " (printed $($retries + 1) times; the first prints were pure black)" })"
}
finally {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
}
