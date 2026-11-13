param(
    # The release number; Directory.Build.props sets it, so it's given here only to build something else.
    [string] $Version,
    [string] $Out = (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist'),
    # Signs with the Strategia-X Azure Artifact Signing account, using your az login. Each file signed counts against its quota.
    [switch] $Sign,
    # The newest Windows SDK's signtool unless given; the machine that signed 1.0.0 had 10.0.26100.0.
    [string] $SignTool,
    # Inno Setup 6's compiler, installed for every user or for yours alone (its /CURRENTUSER install), unless given.
    [string] $InnoSetup
)
# Builds the release: the app, the native host beside it, the unpacked extension and its setup script, as a zip and an installer.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Version) { $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
if (-not $Version) { throw 'Directory.Build.props sets no Version, and -Version was not given' }
$name = "STXDownloader-$Version-win-x64"
$stage = Join-Path $Out $name
$zip = Join-Path $Out "$name.zip"
$setup = Join-Path $Out "STXDownloader-$Version-setup.exe"
# The tools the setup installs beside the app's data (see "The tools" below). Not in the zip, which still fetches them.
$toolStage = Join-Path $Out "STXDownloader-$Version-tools"
foreach ($old in $stage, $zip, $setup, $toolStage) { if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Recurse -Confirm:$false } }

$signTool = if ($SignTool) { $SignTool } else {
    Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe' -ErrorAction SilentlyContinue |
        Sort-Object { [version]$_.Directory.Parent.Name } -Descending | Select-Object -First 1 -ExpandProperty FullName
}
$dlib = Join-Path $env:LOCALAPPDATA 'Microsoft\MicrosoftArtifactSigningClientTools\Azure.CodeSigning.Dlib.dll'
$metadata = Join-Path $PSScriptRoot 'signing\metadata.json'
$signArgs = @('sign', '/fd', 'SHA256', '/tr', 'http://timestamp.acs.microsoft.com', '/td', 'SHA256', '/d', 'STX Downloader', '/du', 'https://strategia-x.com', '/dlib', $dlib, '/dmdf', $metadata)
if ($Sign) {
    if (-not $signTool) { throw 'Signing needs signtool.exe: install the Windows SDK, or pass -SignTool' }
    foreach ($needed in $signTool, $dlib, $metadata) { if (-not (Test-Path -LiteralPath $needed)) { throw "Signing needs: $needed" } }
}

# -Version reaches the binaries too, not only the file names (review 2026-11-07: a 1.1.0 setup held a 1.0.0 exe).
dotnet publish (Join-Path $root 'src\Stx.App') -c Release -p:Platform=x64 "-p:Version=$Version" -o $stage --nologo
if ($LASTEXITCODE) { throw 'Publishing the app failed' }
$hostOut = Join-Path $Out 'host'
dotnet publish (Join-Path $root 'src\Stx.NativeHost') -c Release "-p:Version=$Version" -o $hostOut --nologo
if ($LASTEXITCODE) { throw 'Publishing the native host failed' }
Copy-Item -LiteralPath (Join-Path $hostOut 'STXNativeHost.exe') $stage
Remove-Item -LiteralPath $hostOut -Recurse -Confirm:$false

$extension = New-Item -ItemType Directory -Force (Join-Path $stage 'extension')
foreach ($file in 'manifest.json', 'extension-id.txt', 'detect.js', 'worker.js', 'content.js', 'popup.html', 'popup.js') {
    Copy-Item -LiteralPath (Join-Path $root "extension\$file") $extension
}
Copy-Item -LiteralPath (Join-Path $root 'extension\icons') $extension -Recurse
$tools = New-Item -ItemType Directory -Force (Join-Path $stage 'tools')
Copy-Item -LiteralPath (Join-Path $root 'tools\Register-NativeHost.ps1') $tools
foreach ($doc in 'README.md', 'LICENSE') { Copy-Item -LiteralPath (Join-Path $root $doc) $stage }
$docs = New-Item -ItemType Directory -Force (Join-Path $stage 'docs')
foreach ($doc in 'THIRD_PARTY.md', 'PERFORMANCE.md', 'REAL-SITE-CHECKS.md') { Copy-Item -LiteralPath (Join-Path $root "docs\$doc") $docs }
Get-ChildItem -LiteralPath $stage -Filter *.pdb -Recurse | Remove-Item -Confirm:$false

foreach ($needed in 'STXDownloader.exe', 'STXDownloader.pri', 'App.xbf', 'Themes\Generic.xbf', 'STXNativeHost.exe', 'Assets\badge.png', 'Assets\stx.ico',
    'libvlc\win-x64\libvlc.dll', 'libvlc\win-x64\libvlccore.dll', 'libvlc\win-x64\plugins\plugins.dat', 'libvlc\soundfonts\FluidR3_GM.sf3', 'libvlc\soundfonts\FluidR3_GM-COPYRIGHT.txt',
    'extension\manifest.json', 'extension\icons\icon-128.png', 'tools\Register-NativeHost.ps1',
    'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'System.Private.CoreLib.dll') {
    if (-not (Test-Path -LiteralPath (Join-Path $stage $needed))) { throw "Missing from the release: $needed" }
}
# .NET ships inside the app (2026-11-10: a setup that only asked for it stopped on a PC without it). A runtimeconfig
# that names a framework to look for means it was published without its own copy.
$runtime = (Get-Content -LiteralPath (Join-Path $stage 'STXDownloader.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions
if ($runtime.framework -or $runtime.frameworks -or -not $runtime.includedFrameworks) { throw 'STXDownloader.exe would need .NET installed: publish it self-contained' }
# The SoundFont ships as SF3 (2026-11-07); the 148 MB SF2 it's made from must never ride along.
if (Get-ChildItem -LiteralPath $stage -Filter *.sf2 -Recurse) { throw 'An .sf2 is in the release; STX ships FluidR3_GM.sf3' }

# --- The tools ---------------------------------------------------------------------------------------------------
# yt-dlp, FFmpeg with FFprobe, and Deno ship inside the setup, so the app works from its first launch with nothing to
# download (2026-11-11: testers shouldn't need a connection, or GitHub up, before STX works). They come from the very
# addresses the app's own updater uses, read from its source so the two can't drift, and pass the same gate the app
# holds them to: the SHA-256 their publisher posts, then an offline self-test (ToolManager.InstallFromZip).
$toolsSource = Join-Path $root 'src\Stx.Tools'
function Get-ToolConst([string] $file, [string] $const) {
    $text = Get-Content -LiteralPath (Join-Path $toolsSource $file) -Raw
    if ($text -notmatch "const string $const = `"([^`"]+)`"") { throw "$file no longer sets $const as a plain string" }
    $Matches[1]
}
$ffRelease = Get-ToolConst 'FfmpegSource.cs' 'Release'
$ffZipName = Get-ToolConst 'FfmpegSource.cs' 'ZipName'
if ((Get-Content -LiteralPath (Join-Path $toolsSource 'FfmpegSource.cs') -Raw) -notmatch 'Sums = Release \+ "([^"]+)"') { throw 'FfmpegSource.cs no longer builds Sums from Release' }
$ffSums = $ffRelease + $Matches[1]

# Reused between builds when the publisher's checksum still matches, so a rebuild doesn't fetch 300 MB again.
$toolCache = New-Item -ItemType Directory -Force (Join-Path $root 'dist\.tool-cache')

function Save-Url([string] $url, [string] $path) {
    # GitHub's release downloads answered 504 for minutes at a time on 2026-11-08 and -09 (ToolManager.TransientWaits).
    $waits = 2, 5, 10, 30
    for ($try = 0; ; $try++) {
        try {
            $ProgressPreference = 'SilentlyContinue'
            Invoke-WebRequest -Uri $url -OutFile $path -UserAgent 'STXDownloader-build' -MaximumRedirection 10
            return
        } catch {
            if ($try -ge $waits.Count) { throw "Downloading $url failed: $($_.Exception.Message)" }
            Write-Host "  $url failed ($($_.Exception.Message)); trying again in $($waits[$try]) s"
            Start-Sleep -Seconds $waits[$try]
        }
    }
}

# The same two readings as Checksums.Find and Checksums.FindOnly.
function Find-Hash([string] $sumsUrl, [string] $fileName) {
    $sumsFile = Join-Path $toolCache 'sums.tmp'
    Save-Url $sumsUrl $sumsFile
    $text = Get-Content -LiteralPath $sumsFile -Raw
    Remove-Item -LiteralPath $sumsFile
    if ($fileName) {
        foreach ($line in $text -split "`n") {
            if ($line.Trim() -match '^([0-9a-fA-F]{64})\s+\*?(.+)$' -and $Matches[2] -eq $fileName) { return $Matches[1].ToLowerInvariant() }
        }
    } elseif ($text -match '\b[0-9a-fA-F]{64}\b') { return $Matches[0].ToLowerInvariant() }
    throw "$sumsUrl has no SHA-256 for $(if ($fileName) { $fileName } else { 'its file' })"
}

# Fetched into the cache under its hash, and used only once the file's own hash matches the publisher's.
function Get-Verified([string] $url, [string] $sumsUrl, [string] $fileName) {
    $hash = Find-Hash $sumsUrl $fileName
    $leaf = [IO.Path]::GetFileName(([Uri]$url).AbsolutePath)
    $cached = Join-Path $toolCache "$hash-$leaf"
    if ((Test-Path -LiteralPath $cached) -and (Get-FileHash -LiteralPath $cached -Algorithm SHA256).Hash -eq $hash) {
        Write-Host "  $leaf  (cached, SHA-256 matches)"
    } else {
        Write-Host "  $leaf  downloading"
        Save-Url $url "$cached.part"
        $got = (Get-FileHash -LiteralPath "$cached.part" -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($got -ne $hash) { Remove-Item -LiteralPath "$cached.part"; throw "$leaf is $got, but its publisher posts ${hash}: not shipping it" }
        Move-Item -LiteralPath "$cached.part" $cached -Force
    }
    [pscustomobject]@{ Path = $cached; Hash = $hash; Url = $url; Name = $leaf }
}

# Only named entries come out, by their plain name, as ToolManager.Extract does.
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Expand-Named([string] $archive, [hashtable] $entries) {
    $zipFile = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($entryName in $entries.Keys) {
            $entry = $zipFile.Entries | Where-Object { $_.Name -ieq $entryName } | Select-Object -First 1
            if (-not $entry) { throw "$entryName isn't in $archive" }
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $entries[$entryName], $true)
        }
    } finally { $zipFile.Dispose() }
}

# The offline self-test: it starts, exits 0 and reports a version, within 30 seconds. Returns that version line.
function Test-Tool([string] $exe, [string] $flag) {
    $info = [Diagnostics.ProcessStartInfo]::new($exe, $flag)
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(30000)) { $process.Kill($true); throw "$exe $flag didn't finish in 30 seconds" }
    $text = ($stdout.Result + $stderr.Result).Trim()
    if ($process.ExitCode -or -not $text) { throw "$exe $flag failed its self-test (exit $($process.ExitCode))" }
    ($text -split "`r?`n")[0].Trim()
}

Write-Host 'Bundling the tools'
New-Item -ItemType Directory -Force $toolStage | Out-Null
$licenses = New-Item -ItemType Directory -Force (Join-Path $toolStage 'licenses')

$ytdlp = Get-Verified (Get-ToolConst 'ToolVersions.cs' 'YtDlpExe') (Get-ToolConst 'ToolVersions.cs' 'YtDlpSums') 'yt-dlp.exe'
Copy-Item -LiteralPath $ytdlp.Path (Join-Path $toolStage 'yt-dlp.exe')

$ffmpeg = Get-Verified ($ffRelease + $ffZipName) $ffSums $ffZipName
Expand-Named $ffmpeg.Path @{
    'ffmpeg.exe' = (Join-Path $toolStage 'ffmpeg.exe'); 'ffprobe.exe' = (Join-Path $toolStage 'ffprobe.exe')
    'LICENSE.txt' = (Join-Path $licenses 'FFmpeg-LICENSE.txt')
}

$deno = Get-Verified (Get-ToolConst 'ToolVersions.cs' 'DenoZip') (Get-ToolConst 'ToolVersions.cs' 'DenoSums') $null
Expand-Named $deno.Path @{ 'deno.exe' = (Join-Path $toolStage 'deno.exe') }

$versions = [ordered]@{
    'yt-dlp.exe'  = Test-Tool (Join-Path $toolStage 'yt-dlp.exe') '--version'
    'ffmpeg.exe'  = Test-Tool (Join-Path $toolStage 'ffmpeg.exe') '-version'
    'ffprobe.exe' = Test-Tool (Join-Path $toolStage 'ffprobe.exe') '-version'
    'deno.exe'    = Test-Tool (Join-Path $toolStage 'deno.exe') '--version'
}
foreach ($tool in $versions.Keys) { Write-Host "  $tool  $($versions[$tool])" }

foreach ($license in 'yt-dlp-LICENSE.txt', 'Deno-LICENSE.txt') { Copy-Item -LiteralPath (Join-Path $PSScriptRoot "installer\licenses\$license") $licenses }
# FFmpeg's GPL asks that whoever passes its program on says where its source is. This build's exact commit is in its
# version line; the scripts that configured it, and every library inside it, are in the builds' repository.
@"
The tools the STX Downloader $Version setup installs
=====================================================

The setup puts these in %LOCALAPPDATA%\STXDownloader\tools, where STX keeps its tools, unless a copy is already
there (STX updates yt-dlp on its own). Each one was checked against the SHA-256 its publisher posts, and started
once to report its version, when this setup was built.

yt-dlp.exe    $($versions['yt-dlp.exe'])
              $($ytdlp.Url)
              SHA-256 $($ytdlp.Hash)
              License: the Unlicense (yt-dlp-LICENSE.txt). Source: https://github.com/yt-dlp/yt-dlp

ffmpeg.exe    $($versions['ffmpeg.exe'])
ffprobe.exe   from $($ffmpeg.Url)
              SHA-256 $($ffmpeg.Hash) (the archive both come from)
              License: GNU GPL version 3 (FFmpeg-LICENSE.txt). This build enables GPL components, libx264 among them.
              Source: FFmpeg's source is at https://github.com/FFmpeg/FFmpeg, at the commit named in the version line
              above (the part after "-g"). The scripts that configure and compile this build, with the source of every
              library inside it, are at https://github.com/FFmpeg-Builds/FFmpeg-Builds (release branch 9.0, win64-gpl).

deno.exe      $($versions['deno.exe'])
              $($deno.Url)
              SHA-256 $($deno.Hash) (the archive it comes from)
              License: MIT (Deno-LICENSE.txt). Source: https://github.com/denoland/deno

These are separate programs that STX starts, not part of STX's own code, which stays under the MIT license.
"@ | Set-Content -LiteralPath (Join-Path $licenses 'BUNDLED-TOOLS.txt') -Encoding utf8

foreach ($needed in 'yt-dlp.exe', 'ffmpeg.exe', 'ffprobe.exe', 'deno.exe', 'licenses\FFmpeg-LICENSE.txt', 'licenses\yt-dlp-LICENSE.txt', 'licenses\Deno-LICENSE.txt', 'licenses\BUNDLED-TOOLS.txt') {
    if (-not (Test-Path -LiteralPath (Join-Path $toolStage $needed))) { throw "Missing from the setup's tools: $needed" }
}

if ($Sign) {
    # Only what this project built. Microsoft's own files already carry Microsoft's signature.
    $ours = @('STXDownloader.exe', 'STXDownloader.dll', 'STXNativeHost.exe') + (Get-ChildItem -LiteralPath $stage -Filter 'Stx.*.dll' | ForEach-Object Name)
    foreach ($file in $ours) {
        $path = Join-Path $stage $file
        if (-not (Test-Path -LiteralPath $path)) { throw "Nothing to sign at $path" }
        & $signTool @signArgs $path | Out-Null
        if ($LASTEXITCODE) { throw "Signing failed: $file" }
    }
}

$built = (Get-Item -LiteralPath (Join-Path $stage 'STXDownloader.exe')).VersionInfo.ProductVersion
if (-not $built.StartsWith($Version)) { throw "STXDownloader.exe says $built, not $Version" }

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip

$iscc = if ($InnoSetup) { $InnoSetup } else {
    @((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'), (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')) |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $iscc -or -not (Test-Path -LiteralPath $iscc)) { throw "Inno Setup 6 not found (install it from https://jrsoftware.org/isdl.php, or pass -InnoSetup)" }
$compile = @("/DStage=$stage", "/DTools=$toolStage", "/DAppVersion=$Version", "/DOutputFolder=$Out", '/Qp')
if ($Sign) {
    # Inno signs Setup and the uninstaller it writes into it, which is why the tool is handed over rather than run after.
    # Inno replaces `$q with a quote and `$f with the file. Anything with a space in it needs those quotes.
    $forInno = ($signArgs | ForEach-Object { if ($_ -match ' ') { "`$q$_`$q" } else { $_ } }) -join ' '
    $compile += "/Sstx=`$q$signTool`$q $forInno `$f"
    $compile += '/DSigned=1'
}
& $iscc @compile (Join-Path $PSScriptRoot 'installer\STXDownloader.iss')
if ($LASTEXITCODE) { throw 'Building the installer failed' }

$folderMB = [math]::Round((Get-ChildItem -LiteralPath $stage -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
"$zip  zip $([math]::Round((Get-Item -LiteralPath $zip).Length / 1MB)) MB, unpacked $folderMB MB"
$toolsMB = [math]::Round((Get-ChildItem -LiteralPath $toolStage -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
"$setup  $([math]::Round((Get-Item -LiteralPath $setup).Length / 1MB)) MB, installs the app ($folderMB MB) and its tools ($toolsMB MB)"
