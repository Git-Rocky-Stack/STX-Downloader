param(
    [Parameter(Mandatory)] [string] $Name,
    [ValidateSet('dark', 'light')] [string] $Mode = 'dark',
    [Parameter(Mandatory)] [string] $Out,
    [int] $SettleSeconds = 3
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
'@
}

$p = Start-Process $exe -ArgumentList "--gallery=$Name", "--mode=$Mode", '--hold' -PassThru
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

    $whole = New-Object Drawing.Bitmap ($window[2] - $window[0]), ($window[3] - $window[1])
    $g = [Drawing.Graphics]::FromImage($whole)
    $dc = $g.GetHdc()
    # 2 is PW_RENDERFULLCONTENT, which includes the composition layers this app is drawn with.
    $drawn = [StxCaptureWindow.Native]::PrintWindow($h, $dc, 2)
    $g.ReleaseHdc($dc)
    $g.Dispose()
    if (-not $drawn) { throw "Window would not draw itself for gallery '$Name'" }

    $area = New-Object Drawing.Rectangle ($origin[0] - $window[0]), ($origin[1] - $window[1]), $client[2], $client[3]
    $content = $whole.Clone($area, $whole.PixelFormat)
    New-Item -ItemType Directory -Force (Split-Path $Out -Parent) | Out-Null
    $content.Save($Out)
    $content.Dispose()
    $whole.Dispose()
    "captured $Name $Mode $($client[2])x$($client[3]) -> $Out"
}
finally {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
}
