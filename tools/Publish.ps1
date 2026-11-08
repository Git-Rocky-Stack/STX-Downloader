param(
    [string] $Version = '1.0.0',
    [string] $Out = (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist'),
    # Signs with the Strategia-X Azure Artifact Signing account, using your az login. Each file signed counts against its quota.
    [switch] $Sign,
    # The newest Windows SDK's signtool unless given; the machine that signed 1.0.0 had 10.0.26100.0.
    [string] $SignTool,
    [string] $InnoSetup = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
)
# Builds the release: the app, the native host beside it, the unpacked extension and its setup script, as a zip and an installer.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$name = "STXDownloader-$Version-win-x64"
$stage = Join-Path $Out $name
$zip = Join-Path $Out "$name.zip"
$setup = Join-Path $Out "STXDownloader-$Version-setup.exe"
foreach ($old in $stage, $zip, $setup) { if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Recurse -Confirm:$false } }

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

foreach ($needed in 'STXDownloader.exe', 'STXDownloader.pri', 'App.xbf', 'Themes\Generic.xbf', 'STXNativeHost.exe', 'Assets\badge.png', 'Assets\stx.ico', 'Assets\tutorial.mp4', 'Assets\tutorial-poster.png',
    'libvlc\win-x64\libvlc.dll', 'libvlc\win-x64\libvlccore.dll', 'libvlc\win-x64\plugins\plugins.dat', 'libvlc\soundfonts\FluidR3_GM.sf3', 'libvlc\soundfonts\FluidR3_GM-COPYRIGHT.txt',
    'extension\manifest.json', 'extension\icons\icon-128.png', 'tools\Register-NativeHost.ps1') {
    if (-not (Test-Path -LiteralPath (Join-Path $stage $needed))) { throw "Missing from the release: $needed" }
}
# The SoundFont ships as SF3 (2026-11-07); the 148 MB SF2 it's made from must never ride along.
if (Get-ChildItem -LiteralPath $stage -Filter *.sf2 -Recurse) { throw 'An .sf2 is in the release; STX ships FluidR3_GM.sf3' }

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

$iscc = $InnoSetup
if (-not (Test-Path -LiteralPath $iscc)) { throw "Inno Setup not found: $iscc (pass -InnoSetup)" }
$compile = @("/DStage=$stage", "/DAppVersion=$Version", "/DOutputFolder=$Out", '/Qp')
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
"$setup  $([math]::Round((Get-Item -LiteralPath $setup).Length / 1MB)) MB"
