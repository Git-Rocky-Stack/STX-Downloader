param(
    # gallery: one held or live page. idle / hidden / job: the real app, with no work, in the tray, or with a real download.
    [ValidateSet('gallery', 'idle', 'hidden', 'job')] [string] $Scenario = 'gallery',
    [string] $Gallery = 'display',
    [ValidateSet('dark', 'light')] [string] $Mode = 'dark',
    [int] $SettleSeconds = 12,
    [int] $Samples = 10,
    [switch] $Hold,
    # job: what to download, where to put it, and whether to measure after it finishes rather than during it.
    [string] $VideoId = 'QBwwj5p25AM',
    [string] $OutputFolder,
    [switch] $AfterJob,
    # The only folder the app watches, in place of Videos and Music. Give it a large one to measure a big Library.
    [string] $WatchedFolder,
    # Copies the test database here before it's removed, to see how a job ended. It holds no secrets.
    [string] $KeepDatabaseIn,
    [switch] $Assert,
    # Spec budgets. Overridable so a check of -Assert itself can prove it fails.
    [double] $MemoryBudgetMB = 150,
    [double] $BusyCpuBudget = 2,
    [double] $GpuBudget = 5,
    [double] $RestCpuBudget = 0.05
)
# Measures the app's process only. yt-dlp and FFmpeg are separate processes and aren't counted, as the budgets say.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'src\Stx.App\bin\x64\Release\net10.0-windows10.0.22621.0\win-x64\STXDownloader.exe'
if (-not (Test-Path $exe)) { throw "Build first: $exe not found" }
$realApp = $Scenario -ne 'gallery'
$data = Join-Path $env:LOCALAPPDATA 'STXDownloader'
$kept = Join-Path ([IO.Path]::GetTempPath()) "stx-budgets-$PID"
$names = 'settings.json', 'stx.db', 'stx.db-wal', 'stx.db-shm'

function Tools-Running([int] $appId) {
    @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$appId" | Where-Object { $_.Name -in 'yt-dlp.exe', 'ffmpeg.exe', 'ffprobe.exe', 'deno.exe' }).Count
}

function Send-Capture([string] $json) {
    $name = "stx-downloader-$([Security.Principal.WindowsIdentity]::GetCurrent().User)"
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', $name, [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(30000)
        $body = [Text.Encoding]::UTF8.GetBytes($json)
        $pipe.Write([BitConverter]::GetBytes($body.Length), 0, 4)
        $pipe.Write($body, 0, $body.Length)
        $head = [byte[]]::new(4)
        [void]$pipe.Read($head, 0, 4)
        $reply = [byte[]]::new([BitConverter]::ToInt32($head, 0))
        [void]$pipe.Read($reply, 0, $reply.Length)
        [Text.Encoding]::UTF8.GetString($reply)
    }
    finally { $pipe.Dispose() }
}

# Your own settings and Library are moved aside while the measured app runs, and put back however this ends. One try
# covers everything from the first move (review 2026-11-07: a launch that failed, or a -WatchedFolder that wasn't
# there, left them in the temp folder). The restore touches only what this script moved, or made where you had none.
$moved = @(); $absent = @(); $p = $null
$failure = $null
try {
if ($realApp) {
    if (Get-Process STXDownloader -ErrorAction SilentlyContinue) { throw 'Close STX Downloader first: this replaces its settings while measuring.' }
    New-Item -ItemType Directory -Force $kept, $data | Out-Null
    foreach ($n in $names) {
        $f = Join-Path $data $n
        if (Test-Path -LiteralPath $f) { Move-Item -LiteralPath $f (Join-Path $kept $n); $moved += $n } else { $absent += $n }
    }
    # The app stores LookMode as a number, and it ignores a settings file it can't read, so this must match exactly.
    $settings = @{ LookMode = $(if ($Mode -eq 'light') { 1 } else { 0 }); TrayMode = ($Scenario -eq 'hidden') }
    if ($OutputFolder) { New-Item -ItemType Directory -Force $OutputFolder | Out-Null; $settings.OutputFolder = $OutputFolder }
    if ($WatchedFolder) { $settings.WatchedFolders = @((Resolve-Path -LiteralPath $WatchedFolder).Path) }
    $settings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $data 'settings.json') -Encoding utf8NoBOM
    $launch = @(); if ($Scenario -eq 'hidden') { $launch = @('--hidden') }
}
else {
    $launch = @("--gallery=$Gallery", "--mode=$Mode"); if ($Hold) { $launch += '--hold' }
}

$cores = [Environment]::ProcessorCount
$p = if ($launch.Count) { Start-Process $exe -ArgumentList $launch -PassThru } else { Start-Process $exe -PassThru }
    Start-Sleep -Seconds $SettleSeconds
    $p.Refresh()
    if ($Scenario -eq 'hidden' -and $p.MainWindowHandle -ne 0) { throw 'The window is showing, so this would not measure hidden.' }
    if ($Scenario -eq 'job') {
        if (-not $OutputFolder) { throw 'job needs -OutputFolder' }
        "capture reply: " + (Send-Capture (@{ Kind = 'youtube'; PageUrl = "https://www.youtube.com/watch?v=$VideoId"; VideoId = $VideoId; Title = 'budget check' } | ConvertTo-Json))
        $deadline = (Get-Date).AddSeconds(60)
        while ((Tools-Running $p.Id) -eq 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
        if ((Tools-Running $p.Id) -eq 0) { throw 'The job never started a tool.' }
        Start-Sleep -Seconds 10
        if ($AfterJob) {
            $deadline = (Get-Date).AddMinutes(20)
            while ((Tools-Running $p.Id) -gt 0 -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 2 }
            # Measuring the saved file starts its own tools just after the download ends.
            $quiet = 0
            while ($quiet -lt 10 -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 2; $quiet = if ((Tools-Running $p.Id) -gt 0) { 0 } else { $quiet + 1 } }
            $saved = @(Get-ChildItem -LiteralPath $OutputFolder -File | Where-Object { $_.Extension -notin '.part', '.ytdl', '.temp' })
            "saved: " + (($saved | ForEach-Object { "$($_.Name) $([math]::Round($_.Length / 1MB)) MB" }) -join '; ')
            if ($saved.Count -eq 0) { $failure = 'The job saved no file, so this did not measure after a job' }
        }
    }

    $p.Refresh()
    $cpuStart = $p.TotalProcessorTime
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $working = @(); $private = @(); $gpu = @(); $tools = 0
    for ($i = 0; $i -lt $Samples; $i++) {
        Start-Sleep -Seconds 3
        $p.Refresh()
        $working += $p.WorkingSet64
        $private += $p.PrivateMemorySize64
        if ((Tools-Running $p.Id) -gt 0) { $tools++ }
        try {
            $s = (Get-Counter "\GPU Engine(pid_$($p.Id)_*engtype_3D)\Utilization Percentage" -ErrorAction Stop).CounterSamples
            $gpu += ($s | Measure-Object CookedValue -Sum).Sum
        } catch { }
    }
    $p.Refresh()
    $cpu = ($p.TotalProcessorTime - $cpuStart).TotalSeconds / $clock.Elapsed.TotalSeconds / $cores * 100
    $atRest = $Scenario -in 'idle', 'hidden' -or ($Scenario -eq 'job' -and $AfterJob) -or ($Scenario -eq 'gallery' -and $Hold)
    $result = [pscustomobject]@{
        scenario     = $(if ($Scenario -eq 'gallery') { $Gallery } elseif ($AfterJob) { 'after job' } else { $Scenario })
        mode         = $Mode
        wsMB         = [math]::Round(($working | Measure-Object -Average).Average / 1MB, 1)
        privateMB    = [math]::Round(($private | Measure-Object -Average).Average / 1MB, 1)
        privateMaxMB = [math]::Round(($private | Measure-Object -Maximum).Maximum / 1MB, 1)
        cpuPct       = [math]::Round($cpu, 3)
        gpu3dPct     = $(if ($gpu.Count) { [math]::Round(($gpu | Measure-Object -Average).Average, 2) } else { 'n/a' })
        toolSamples  = "$tools/$Samples"
        exited       = $p.HasExited
    }
    $result | Format-Table -AutoSize | Out-String -Width 170

    if ($Assert -and -not $failure) {
        $cpuBudget = if ($atRest) { $RestCpuBudget } else { $BusyCpuBudget }
        if ($result.exited) { $failure = 'App exited during measurement' }
        elseif ($result.privateMaxMB -ge $MemoryBudgetMB) { $failure = "Private memory $($result.privateMaxMB) MB is over the $MemoryBudgetMB MB budget" }
        elseif ($result.cpuPct -ge $cpuBudget) { $failure = "CPU $($result.cpuPct)% is over the $cpuBudget% budget" }
        elseif ($result.gpu3dPct -ne 'n/a' -and $result.gpu3dPct -ge $GpuBudget) { $failure = "GPU $($result.gpu3dPct)% is over the $GpuBudget% budget" }
        elseif ($Scenario -eq 'job' -and -not $AfterJob -and $tools -eq 0) { $failure = 'No tool was running, so this did not measure a job' }
    }
}
finally {
    if ($p) {
        # Taking the whole tree, so no download carries on after the measurement.
        & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null
        Start-Sleep -Seconds 2
    }
    if ($realApp) {
        if ($p -and $KeepDatabaseIn) { New-Item -ItemType Directory -Force $KeepDatabaseIn | Out-Null; foreach ($n in 'stx.db', 'stx.db-wal') { $f = Join-Path $data $n; if (Test-Path -LiteralPath $f) { Copy-Item -LiteralPath $f $KeepDatabaseIn -Force } } }
        foreach ($n in $names) {
            $f = Join-Path $data $n
            if ($n -in $moved) {
                # Yours, moved aside: the measured run's copy goes and yours comes back.
                if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Confirm:$false }
                Move-Item -LiteralPath (Join-Path $kept $n) $f
            }
            elseif ($n -in $absent -and (Test-Path -LiteralPath $f)) {
                # You had none; the measured run made it.
                Remove-Item -LiteralPath $f -Confirm:$false
            }
        }
        Remove-Item -LiteralPath $kept -Recurse -Confirm:$false -ErrorAction SilentlyContinue
    }
}
if ($failure) { Write-Error $failure -ErrorAction Continue; exit 1 }
