<#
.SYNOPSIS
  Prepares the VLC engine STX ships: the official VLC build and the FluidR3_GM SoundFont, both pinned by sha256 in
  tools/vlc/engine.json. Downloads once into .engine/ (git-ignored), checks the hashes, keeps libvlc, libvlccore and
  the plugins minus the excluded ones, rebuilds VLC's plugin cache, and writes ready.stamp when everything is in place.
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
        Invoke-WebRequest -Uri $url -OutFile $file -UserAgent 'STX-build/1.0'
        $actual = (Get-FileHash -Algorithm SHA256 $file).Hash.ToLowerInvariant()
        if ($actual -ne $sha) { Remove-Item $file; throw "$url has sha256 $actual; engine.json pins $sha. Nothing was kept." }
    }
    $file
}

$stamp = Join-Path $Cache 'ready.stamp'
Remove-Item $stamp -ErrorAction SilentlyContinue
$engine = Join-Path $Cache 'libvlc/win-x64'
if (Test-Path $engine) { Remove-Item -Recurse -Force $engine }

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

# SoundFont: unpack the Debian package with Windows' own tar and keep the .sf2 and its copyright.
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
$fonts = Join-Path $Cache 'libvlc/soundfonts'
New-Item -ItemType Directory -Force $fonts | Out-Null
Move-Item -Force (Join-Path $work $spec.soundFont.file) (Join-Path $fonts 'FluidR3_GM.sf2')
Move-Item -Force (Join-Path $work $spec.soundFont.copyright) (Join-Path $fonts 'FluidR3_GM-COPYRIGHT.txt')
Remove-Item -Recurse -Force $work

Set-Content $stamp "VLC $($spec.version) $($spec.vlc.sha256); SoundFont $($spec.soundFont.sha256)"
Write-Host "STX engine ready in $Cache"
