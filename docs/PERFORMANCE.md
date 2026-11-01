# Performance

Measured 2026-11-01 with `tools/Measure-Budgets.ps1 -Assert`, which exits non-zero outside the budgets. Only the app's own process is counted: yt-dlp, FFmpeg and Deno are separate processes.

**Machine:** Intel Core i5-8350U (8 logical cores), Intel UHD Graphics 620, 32 GB RAM, Windows 11, 96 DPI.

**Budgets:** with no work running, 0% CPU over 30 seconds (the script fails at 0.05% or more). While animating or running a job, under 2% of total CPU and under 5% GPU. Private memory under 150 MB.

`cpuPct` is a share of all 8 cores. Each row is 10 samples over 30 seconds, after settling. `toolSamples` is how many samples had a tool running under the app.

## The real app

```
scenario  mode   wsMB   privateMB privateMaxMB cpuPct gpu3dPct toolSamples exited
idle      dark   172.90     69.90        71.00   0.01     0.00 0/10        False
idle      light  172.80     70.60        70.90   0.00     0.00 0/10        False
hidden    dark   168.20     66.60        67.80   0.01     0.00 0/10        False
job       dark   176.80     67.30        71.10   0.38     0.32 10/10       False
after job dark   205.40     94.90        94.90   0.00     0.00 0/10        False
```

The job is a real YouTube webinar. `after job` is measured once it had finished saving a 310 MB file and the file's audio had been measured.

## Every primitive at once

A stress page, more than any real screen shows.

```
scenario       mode  wsMB   privateMB privateMaxMB cpuPct gpu3dPct exited
all-primitives dark  135.50     54.50        54.60   0.44     1.18 False
all-primitives light 134.90     53.90        54.00   0.51     1.18 False
all-primitives dark  139.70     60.90        61.00   0.00     0.00 False   (held still)
```

## Limits of these numbers

- One machine, at 96 DPI. High-DPI displays are not measured.
- An earlier run reached 148 MB private memory just after a large 4K download. The 310 MB download here reached 95 MB. That larger case was not measured again, so it may be near the 150 MB budget.
- Several jobs at once were not measured. The job row is one download.

## Reduced motion

Checked with the Windows animation setting really switched off, by capturing the LED page 24 times:

```
animations on  : distinct frames of LED gallery in 24 captures = 2
animations off : distinct frames of LED gallery in 24 captures = 1
```

Windows' setting is watched live, so LEDs stop without a restart. The meter sweep and the knob overshoot are covered by unit tests (`MeterTimeline.Sweep`, `PressTravel.TurnEasing`), not by this capture.
