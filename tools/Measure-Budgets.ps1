param(
    [string] $Gallery = 'display',
    [ValidateSet('dark', 'light')] [string] $Mode = 'dark',
    [int] $SettleSeconds = 12,
    [int] $Samples = 10,
    [switch] $Assert
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'src\Stx.App\bin\x64\Release\net10.0-windows10.0.22621.0\win-x64\STXDownloader.exe'
if (-not (Test-Path $exe)) { throw "Build first: $exe not found" }

$cores = [Environment]::ProcessorCount
$p = Start-Process $exe -ArgumentList "--gallery=$Gallery", "--mode=$Mode" -PassThru
try {
    Start-Sleep -Seconds $SettleSeconds
    $p.Refresh()
    $cpuStart = $p.TotalProcessorTime
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $working = @(); $private = @(); $gpu = @()
    for ($i = 0; $i -lt $Samples; $i++) {
        Start-Sleep -Seconds 3
        $p.Refresh()
        $working += $p.WorkingSet64
        $private += $p.PrivateMemorySize64
        try {
            $s = (Get-Counter "\GPU Engine(pid_$($p.Id)_*engtype_3D)\Utilization Percentage" -ErrorAction Stop).CounterSamples
            $gpu += ($s | Measure-Object CookedValue -Sum).Sum
        } catch { }
    }
    $p.Refresh()
    $cpu = ($p.TotalProcessorTime - $cpuStart).TotalSeconds / $clock.Elapsed.TotalSeconds / $cores * 100
    $result = [pscustomobject]@{
        gallery   = $Gallery
        mode      = $Mode
        wsMB      = [math]::Round(($working | Measure-Object -Average).Average / 1MB, 1)
        privateMB = [math]::Round(($private | Measure-Object -Average).Average / 1MB, 1)
        cpuPct    = [math]::Round($cpu, 2)
        gpu3dPct  = $(if ($gpu.Count) { [math]::Round(($gpu | Measure-Object -Average).Average, 2) } else { 'n/a' })
        exited    = $p.HasExited
    }
    $result | Format-Table -AutoSize | Out-String -Width 160
    if ($Assert) {
        if ($result.exited) { throw 'App exited during measurement' }
        if ($result.privateMB -ge 150) { throw "Private memory $($result.privateMB) MB is over the 150 MB budget" }
        if ($result.cpuPct -ge 2) { throw "CPU $($result.cpuPct)% is over the 2% budget" }
        if ($result.gpu3dPct -ne 'n/a' -and $result.gpu3dPct -ge 5) { throw "GPU $($result.gpu3dPct)% is over the 5% budget" }
    }
}
finally {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
}
