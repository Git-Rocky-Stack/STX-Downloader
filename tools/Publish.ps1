param(
    [string] $Version = '1.0.0',
    [string] $Out = (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist')
)
# Builds the release zip: the app, the native host beside it, the unpacked extension and its setup script.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$name = "STXDownloader-$Version-win-x64"
$stage = Join-Path $Out $name
$zip = Join-Path $Out "$name.zip"
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Confirm:$false }
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Confirm:$false }

dotnet publish (Join-Path $root 'src\Stx.App') -c Release -p:Platform=x64 -o $stage --nologo
if ($LASTEXITCODE) { throw 'Publishing the app failed' }
$hostOut = Join-Path $Out 'host'
dotnet publish (Join-Path $root 'src\Stx.NativeHost') -c Release -o $hostOut --nologo
if ($LASTEXITCODE) { throw 'Publishing the native host failed' }
Copy-Item -LiteralPath (Join-Path $hostOut 'STXNativeHost.exe') $stage
Remove-Item -LiteralPath $hostOut -Recurse -Confirm:$false

$extension = New-Item -ItemType Directory -Force (Join-Path $stage 'extension')
foreach ($file in 'manifest.json', 'extension-id.txt', 'detect.js', 'worker.js', 'content.js', 'popup.html', 'popup.js') {
    Copy-Item -LiteralPath (Join-Path $root "extension\$file") $extension
}
$tools = New-Item -ItemType Directory -Force (Join-Path $stage 'tools')
Copy-Item -LiteralPath (Join-Path $root 'tools\Register-NativeHost.ps1') $tools
foreach ($doc in 'README.md', 'LICENSE') { Copy-Item -LiteralPath (Join-Path $root $doc) $stage }
$docs = New-Item -ItemType Directory -Force (Join-Path $stage 'docs')
foreach ($doc in 'THIRD_PARTY.md', 'PERFORMANCE.md', 'REAL-SITE-CHECKS.md') { Copy-Item -LiteralPath (Join-Path $root "docs\$doc") $docs }
Get-ChildItem -LiteralPath $stage -Filter *.pdb -Recurse | Remove-Item -Confirm:$false

foreach ($needed in 'STXDownloader.exe', 'STXNativeHost.exe', 'Assets\badge.png', 'Assets\stx.ico', 'extension\manifest.json', 'tools\Register-NativeHost.ps1') {
    if (-not (Test-Path -LiteralPath (Join-Path $stage $needed))) { throw "Missing from the release: $needed" }
}
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
$folderMB = [math]::Round((Get-ChildItem -LiteralPath $stage -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
"$zip  zip $([math]::Round((Get-Item -LiteralPath $zip).Length / 1MB)) MB, unpacked $folderMB MB"
