<#
.SYNOPSIS
  Builds the VLC parity corpus in tests/corpus/files: generates what FFmpeg can make, copies our own files from
  tests/corpus/own, and downloads the rest, checking each download against the sha256 pinned in the manifest.
.EXAMPLE
  pwsh tools/corpus/Make-Corpus.ps1 -Ffmpeg (Get-Command ffmpeg).Source
#>
param(
    [Parameter(Mandatory)] [string] $Ffmpeg,
    [switch] $Force
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$corpus = Join-Path $root 'tests/corpus'
$files = Join-Path $corpus 'files'
$own = Join-Path $corpus 'own'
New-Item -ItemType Directory -Force $files | Out-Null
$manifest = Get-Content (Join-Path $corpus 'manifest.json') -Raw | ConvertFrom-Json

function Sha256([string] $path) { (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant() }

$made = 0; $kept = 0
foreach ($entry in $manifest.entries) {
    $out = Join-Path $files $entry.file
    if ($entry.source -eq 'fetched') {
        if (-not $Force -and (Test-Path $out) -and (Sha256 $out) -eq $entry.sha256) { $kept++; continue }
        Write-Host "fetch  $($entry.file)"
        Invoke-WebRequest -Uri $entry.url -OutFile $out -UserAgent 'STX-corpus/1.0'
        $actual = Sha256 $out
        if ($actual -ne $entry.sha256) {
            Remove-Item $out
            throw "$($entry.file): sha256 is $actual, the manifest pins $($entry.sha256). The download changed; nothing was kept."
        }
        $made++
        continue
    }
    if (-not $Force -and (Test-Path $out)) { $kept++; continue }
    $make = $entry.make
    if ($make -match '^copy own/(\S+)') {
        Write-Host "copy   $($entry.file)"
        Copy-Item (Join-Path $own $Matches[1]) $out -Force
    }
    elseif ($make.StartsWith('ffmpeg ')) {
        Write-Host "make   $($entry.file)"
        $arguments = '-hide_banner -loglevel error -y ' + ($make.Substring(7) -replace '\{own\}/(\S+)', "`"$own\`$1`"").Replace('{out}', "`"$out`"")
        $process = Start-Process -FilePath $Ffmpeg -ArgumentList $arguments -NoNewWindow -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "$($entry.file): ffmpeg exited $($process.ExitCode) for: $arguments" }
    }
    else { throw "$($entry.file): unknown make line '$make'" }
    $made++
}
Write-Host "corpus ready: $made made or fetched, $kept already there, in $files"
