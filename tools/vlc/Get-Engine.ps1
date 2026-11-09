<#
.SYNOPSIS
  Prepares the VLC engine STX ships: the official VLC build and the FluidR3_GM SoundFont, both pinned by sha256 in
  tools/vlc/engine.json. Downloads once into .engine/ (git-ignored), checks the hashes, keeps libvlc, libvlccore and
  the plugins minus the excluded ones, rebuilds VLC's plugin cache, converts the SoundFont to SF3 (tools/vlc/Make-Sf3.cs,
  with a pinned FFmpeg build; the result's sha256 is pinned too), and writes ready.stamp when everything is in place.
  The build (src/Stx.Playback/Stx.Playback.csproj) runs this when ready.stamp is missing.
#>
param([string] $Cache)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$spec = Get-Content (Join-Path $PSScriptRoot 'engine.json') -Raw | ConvertFrom-Json
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Cache) { $Cache = Join-Path $root ".engine/$($spec.version)" }
$downloads = Join-Path $root '.engine/downloads'
New-Item -ItemType Directory -Force $Cache, $downloads | Out-Null

function Fetch($url, $sha) {
    $file = Join-Path $downloads (Split-Path $url -Leaf)
    if (-not (Test-Path $file) -or (Get-FileHash -Algorithm SHA256 $file).Hash.ToLowerInvariant() -ne $sha) {
        Write-Host "STX engine: downloading $url"
        # GitHub's release downloads answer 504 now and then, once for over three minutes (2026-11-08 and -09); a failed
        # request is tried again, for about five minutes.
        Invoke-WebRequest -Uri $url -OutFile $file -UserAgent 'STX-build/1.0' -MaximumRetryCount 10 -RetryIntervalSec 30
        $actual = (Get-FileHash -Algorithm SHA256 $file).Hash.ToLowerInvariant()
        if ($actual -ne $sha) { Remove-Item $file; throw "$url has sha256 $actual; engine.json pins $sha. Nothing was kept." }
    }
    $file
}

$stamp = Join-Path $Cache 'ready.stamp'
Remove-Item $stamp -ErrorAction SilentlyContinue
$engine = Join-Path $Cache 'libvlc/win-x64'
if (Test-Path $engine) { Remove-Item -Recurse -Force $engine }
$fonts = Join-Path $Cache 'libvlc/soundfonts'
if (Test-Path $fonts) { Remove-Item -Recurse -Force $fonts }

# VLC: unzip, keep libvlc, libvlccore and the allowed plugins, then rebuild the plugin cache.
$zip = Fetch $spec.vlc.url $spec.vlc.sha256
$unpacked = Join-Path $downloads 'vlc-unpacked'
if (Test-Path $unpacked) { Remove-Item -Recurse -Force $unpacked }
Expand-Archive -Path $zip -DestinationPath $unpacked
$vlc = Join-Path $unpacked $spec.vlc.root
New-Item -ItemType Directory -Force $engine | Out-Null
Copy-Item (Join-Path $vlc 'libvlc.dll'), (Join-Path $vlc 'libvlccore.dll') $engine
Copy-Item -Recurse (Join-Path $vlc 'plugins') $engine
foreach ($excluded in $spec.excludePlugins.PSObject.Properties.Name) {
    $target = Join-Path $engine "plugins/$excluded"
    if (-not (Test-Path $target)) { throw "engine.json excludes plugins/$excluded, which VLC $($spec.version) doesn't have" }
    Remove-Item -Recurse -Force $target
}
& (Join-Path $vlc 'vlc-cache-gen.exe') (Join-Path $engine 'plugins')
if ($LASTEXITCODE) { throw "vlc-cache-gen exited $LASTEXITCODE" }
Remove-Item -Recurse -Force $unpacked

# SoundFont: unpack the Debian package with Windows' own tar, convert the .sf2 to SF3, and keep that and its copyright.
$deb = Fetch $spec.soundFont.url $spec.soundFont.sha256
$work = Join-Path $downloads 'soundfont-unpacked'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Force $work | Out-Null
# Windows' own tar (libarchive) reads .deb files; Git's GNU tar, often first on PATH, can't.
$tar = Join-Path $env:SystemRoot 'System32/tar.exe'
Push-Location $work
try {
    & $tar -xf $deb
    if ($LASTEXITCODE) { throw "tar could not read $deb" }
    $data = Get-ChildItem 'data.tar.*' | Select-Object -First 1
    & $tar -xf $data.Name $spec.soundFont.file $spec.soundFont.copyright
    if ($LASTEXITCODE) { throw "tar could not read $($data.Name)" }
} finally { Pop-Location }
New-Item -ItemType Directory -Force $fonts | Out-Null
$sf3 = $spec.soundFont.sf3
$ffmpegZip = Fetch $sf3.ffmpeg.url $sf3.ffmpeg.sha256
$ffmpegDir = Join-Path $downloads 'ffmpeg-sf3'
$ffmpeg = Join-Path $ffmpegDir "$($sf3.ffmpeg.root)/bin/ffmpeg.exe"
if (-not (Test-Path $ffmpeg)) {
    if (Test-Path $ffmpegDir) { Remove-Item -Recurse -Force $ffmpegDir }
    Expand-Archive -Path $ffmpegZip -DestinationPath $ffmpegDir
}
# The SDK that runs this build runs the converter (a .NET file-based app); MSBuild says where it is.
$dotnet = if ($env:DOTNET_HOST_PATH) { $env:DOTNET_HOST_PATH } else { 'dotnet' }
$converted = Join-Path $work 'FluidR3_GM.sf3'
& $dotnet run --file (Join-Path $PSScriptRoot 'Make-Sf3.cs') -- (Join-Path $work $spec.soundFont.file) $converted $ffmpeg $sf3.quality
if ($LASTEXITCODE) { throw "Make-Sf3 exited $LASTEXITCODE" }
$actual = (Get-FileHash -Algorithm SHA256 $converted).Hash.ToLowerInvariant()
if ($actual -ne $sf3.sha256) { throw "The SF3 made here has sha256 $actual; engine.json pins $($sf3.sha256). Nothing was kept." }
Move-Item -Force $converted (Join-Path $fonts 'FluidR3_GM.sf3')
Move-Item -Force (Join-Path $work $spec.soundFont.copyright) (Join-Path $fonts 'FluidR3_GM-COPYRIGHT.txt')
Remove-Item -Recurse -Force $work

Set-Content $stamp "VLC $($spec.version) $($spec.vlc.sha256); SoundFont $($spec.soundFont.sha256) as SF3 $($spec.soundFont.sf3.sha256)"
Write-Host "STX engine ready in $Cache"
