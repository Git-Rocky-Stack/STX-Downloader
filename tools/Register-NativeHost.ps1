param(
    [string] $HostPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'STXNativeHost.exe'),
    [switch] $Remove
)
# Lets the STX Downloader extension, and only it, start the native host. Writes for your account only.
$ErrorActionPreference = 'Stop'
$name = 'com.stx.downloader'
$browsers = @('Google\Chrome', 'Microsoft\Edge', 'Chromium')
$folder = Join-Path $env:LOCALAPPDATA 'STXDownloader\native-host'
$manifest = Join-Path $folder "$name.json"

if ($Remove) {
    foreach ($browser in $browsers) {
        $key = "HKCU:\Software\$browser\NativeMessagingHosts\$name"
        if (Test-Path -LiteralPath $key) { Remove-Item -LiteralPath $key -Confirm:$false; "removed $key" }
    }
    if (Test-Path -LiteralPath $manifest) { Remove-Item -LiteralPath $manifest -Confirm:$false; "removed $manifest" }
    return
}

if (-not (Test-Path -LiteralPath $HostPath)) { throw "Native host not found: $HostPath" }
$id = (Get-Content -Raw -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'extension\extension-id.txt')).Trim()
New-Item -ItemType Directory -Force $folder | Out-Null
@{
    name            = $name
    description     = 'STX Downloader hand-off'
    path            = (Resolve-Path -LiteralPath $HostPath).Path
    type            = 'stdio'
    allowed_origins = @("chrome-extension://$id/")
} | ConvertTo-Json | ForEach-Object { [IO.File]::WriteAllText($manifest, $_) }

foreach ($browser in $browsers) {
    $key = "HKCU:\Software\$browser\NativeMessagingHosts\$name"
    New-Item -Path $key -Force | Out-Null
    Set-Item -Path $key -Value $manifest
    "registered $key"
}
