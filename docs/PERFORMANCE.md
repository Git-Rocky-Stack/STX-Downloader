# Performance

Measured 2026-11-01 with `tools/Measure-Budgets.ps1 -Assert`, every run exiting 0. The script exits non-zero outside the budgets. Only the app's own process is counted: yt-dlp, FFmpeg and Deno are separate processes.

**Machine:** Intel Core i5-8350U (8 logical cores), Intel UHD Graphics 620, 32 GB RAM, Windows 11, 96 DPI.

**Budgets:** with no work running, 0% CPU over 30 seconds (the script fails at 0.05% or more). While animating or running a job, under 2% of total CPU and under 5% GPU. Private memory under 150 MB.

**These budgets apply when nothing is playing.** Playback has no fixed limits: it uses what the file needs, within the PERFORMANCE setting (AUTO, ECO, BALANCED, MAX; `src/Stx.Core/Playback/PerformanceProfile.cs:16`). The PLAYER plate sits in the rack whether or not anything plays, so the idle budgets are measured with it there.

**Watched folders (Phase 2).** The first scan of Videos and Music is work, like a download: it runs on its own thread below normal priority (`src/Stx.Core/Library/CollectionWatcher.cs:94`), so the idle budget applies once it has finished. Watching costs nothing until a file changes.

**Measured after Phases 2 to 7** (2026-11-05, this machine, `tools/Measure-Budgets.ps1 -Scenario idle -Assert -WatchedFolder <folder>`; `-WatchedFolder` makes that folder the only one watched). The 50,000 files are small files with media names in 500 folders: the scan reads only path, size and date (`src/Stx.Core/Library/FolderScan.cs:12`), so they make 50,000 real Library rows, and the database held 50,000 `media_files` rows afterwards.

```
watched folder   wsMB   privateMB privateMaxMB cpuPct gpu3dPct exit
empty            228.90    117.30       117.50   0.01     0.00 0
50,000 files     302.70    185.30       185.50   0.00     0.00 1 (over the 150 MB budget)
```

**The idle memory budget fails with a very large collection.** With nothing watched, Phases 2 to 7 cost about 38 MB over Phase 1's 80 MB. Each Library row then costs about 1.4 KB: every row is held in memory so search answers as you type, and each keeps a second, lower-case copy of its title, file name and folder for that search (`src/Stx.ViewModels/LibraryViewModel.cs:46`). Whether to shrink the rows or give the budget an allowance per row is open. The idle and hidden rows below predate Phases 2 to 7.

`cpuPct` is a share of all 8 cores. Each row is 10 samples over 30 seconds, after settling. `toolSamples` is how many samples had a tool running under the app.

## The real app

```
scenario  mode   wsMB   privateMB privateMaxMB cpuPct gpu3dPct toolSamples exited
idle      dark   179.30     79.10        80.00   0.01     0.00 0/10        False
idle      light  178.00     77.10        77.40   0.00     0.00 0/10        False
hidden    dark   174.40     74.90        75.50   0.01     0.00 0/10        False
job       dark   185.20     76.30        79.40   0.31     0.33 10/10       False
after job dark   211.10    102.30       102.30   0.00     0.00 0/10        False
```

The job is a real YouTube webinar. `after job` is measured once it had finished saving a 310 MB file and the file's audio had been measured.

**Identical-file check.** Each new file is hashed inside the app, on one core. That 310 MB file took 1208 ms (measured separately with the same hash), so for about a second it uses roughly 12% of this machine's total CPU, above the 2% moving budget. It's over within one sampling interval and doesn't show in the 30 second averages above.

**A scrolling queue title.** One row scrolling, measured with a separate 20 second sample, using the same counters, on the packaged app:

```
while scrolling: cpu 1.436% of total, gpu3d 3.35% over about 20 s
after coming to rest: cpu 0.000% of total, gpu3d 0.00% over about 20 s
```

## With the player in the rack, nothing playing

Measured 2026-11-04 with `tools/Measure-Budgets.ps1 -Assert`, every run exiting 0, after the PLAYER plate joined the rack. LibVLC is ready on the plate, which costs about 19 MB of private memory over the 79 MB measured on 2026-11-01. It uses no CPU or GPU while nothing plays.

```
scenario mode   wsMB   privateMB privateMaxMB cpuPct gpu3dPct toolSamples exited
idle     dark   207.60     97.90        99.00   0.01     0.00 0/10        False
idle     light  209.40     99.00        99.20   0.01     0.00 0/10        False
hidden   dark   208.70     98.70        99.00   0.01     0.00 0/10        False
```

## Playback

Playback has no budget, but it is measured. `tests/Stx.VisualTests/PlayerPlaybackTests.cs` plays a 4K60 HEVC file (39 Mb/s, made by FFmpeg) full screen for 60 seconds on this machine. LibVLC's lost pictures and the compositor's dropped and missed frames each have to stay under 1%. The run on 2026-11-04 passed: 3,891 pictures shown by the end, 12 lost in the whole run (all at the start, outside the measured minute), hardware decoding on the UHD 620 (D3D11VA).

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
- Several jobs at once were not measured. The job row is one download. Several long titles scrolling at once weren't measured either: one uses most of the 2% and 5% budgets, so a few together could pass them.

## Reduced motion

Checked with the Windows animation setting really switched off, by capturing the LED page 24 times:

```
animations on  : distinct frames of LED gallery in 24 captures = 2
animations off : distinct frames of LED gallery in 24 captures = 1
```

Windows' setting is watched live, so LEDs stop without a restart. The meter sweep and the knob overshoot are covered by unit tests (`MeterTimeline.Sweep`, `PressTravel.TurnEasing`), not by this capture.
