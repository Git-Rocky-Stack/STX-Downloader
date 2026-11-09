# Performance

Measured 2026-11-01 with `tools/Measure-Budgets.ps1 -Assert`, every run exiting 0. The script exits non-zero outside the budgets. Only the app's own process is counted: yt-dlp, FFmpeg and Deno are separate processes.

**Machine:** Intel Core i5-8350U (8 logical cores), Intel UHD Graphics 620, 32 GB RAM, Windows 11, 96 DPI.

**Budgets:** with no work running, 0% CPU over 30 seconds (the script fails at 0.05% or more). While animating or running a job, under 2% of total CPU and under 5% GPU. Private memory under 150 MB.

**These budgets apply when nothing is playing.** Playback has no fixed limits: it uses what the file needs, within the PERFORMANCE setting (AUTO, ECO, BALANCED, MAX; `PerformanceProfile.For`). The PLAYER plate sits in the rack whether or not anything plays, so the idle budgets are measured with it there.

**Watched folders (Phase 2).** The first scan of Videos and Music is work, like a download: it runs on its own thread below normal priority (`CollectionWatcher.Watch`), so the idle budget applies once it has finished. Watching costs nothing until a file changes.

**Measured after Phases 2 to 7** (2026-11-05, this machine, `tools/Measure-Budgets.ps1 -Scenario idle -Assert -WatchedFolder <folder>`; `-WatchedFolder` makes that folder the only one watched). The 50,000 files are small files with media names in 500 folders: the scan reads only path, size and date (`FolderScan`), so they make 50,000 real Library rows, and the database held 50,000 `media_files` rows afterwards.

```
watched folder   wsMB   privateMB privateMaxMB cpuPct gpu3dPct exit
empty            228.90    117.30       117.50   0.01     0.00 0
50,000 files     302.70    185.30       185.50   0.00     0.00 1 (over the 150 MB budget)
```

**The idle memory budget fails with a very large collection.** With nothing watched, Phases 2 to 7 cost about 38 MB over Phase 1's 80 MB. Each Library row then costs about 1.4 KB: every row is held in memory so search answers as you type, and each kept a second, lower-case copy of its title, file name and folder for that search. That was the Library until 2026-11-07; the next measurement is after it changed. Rocky chose to rework row storage (plan `docs/superpowers/plans/2026-11-05-library-row-storage.md`); measured below. The idle and hidden rows below predate Phases 2 to 7.

**After the rows left memory** (2026-11-07). Not this machine: a Core Ultra 7 258V laptop, Arc 140V, a 2880x1800 panel at 175%, the same script and the same 50,000 files (500 folders of 100). All four runs in one sitting, each with the tools already downloaded, the code before the change built from the branch's last commit in a worktree:

```
code             watched folder   wsMB   privateMB privateMaxMB cpuPct gpu3dPct
before rows     empty            284.50    178.00       178.40   0.00     0.00
before rows     50,000 files     342.20    235.30       235.40   0.00     0.00
after rows      empty            266.60    165.40       165.50   0.01     0.00
after rows      50,000 files     271.00    163.60       163.80   0.00     0.00
```

At 50,000 rows the Library now costs nothing over an empty one: before, 57 MB (about 1.1 KB a row here). Two changes made that: the Library holds only the rows read so far, a page at first and more as the list scrolls (`LibraryViewModel.Page`, `IncrementalRows`), and the garbage a scan leaves, which an idle app never collects, is given back once the work has been quiet for 10 seconds (`MemoryReturn`; a heap dump of the idle app before that showed 48 MB of committed heap with 4 MB alive). **This laptop's own floor is over the budget**, 165 MB with an empty Library, against 117.5 MB on the reference PC; the 50,000-row run with `-Assert` on the reference PC is still to be done, and should land near its empty figure.

**Search at 50,000 rows**, on the same laptop: a keystroke (both counts and the first page) takes about 20 to 40 ms through the database, and under the 150 ms budget through the view-model on the real database (`tests/Stx.App.Tests/LibraryCollectionTests.cs`, `tests/Stx.Data.Tests/LibraryRowsTests.cs`). A page from row 49,000 comes as fast as the first.

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

**On the Core Ultra laptop (2026-11-07), the 4K60 test fails about half its runs.** Its panel runs at 60 Hz (2880x1800, Arc 140V, mains power, High performance). Each run either loses nothing after its first second, or loses 3 to 4 pictures a second all the way through (about 6%), in a window as in full screen, with GPU or software decoding, at any decoder thread count from 1 to 8 (each run's outcome is set when it starts, so single runs at different settings look like a pattern and aren't). With VLC told not to drop late pictures, none are lost but only about 55 a second reach the screen in those runs, the same as when they're dropped, and late pictures would carry the picture away from the sound; so that option wasn't kept. What limits those runs is VLC 3.0.24's Direct3D 11 output presenting into WinUI's swap chain on this GPU and panel, not decoding. Not fixed: it needs a newer VLC output or a measured change to how the swap chain presents.

## Shrink's encoder settings

SHRINK encodes video with SVT-AV1 preset 6 at CRF 30, or x265 medium at CRF 24, both 10-bit (`FfmpegArgs.ForConvert`). Checked on 2026-11-07 (Core Ultra laptop) against real footage: three clips cut from Blender's open films (CC BY), copied from their H.264 releases as a downloaded file would be, the first 10 seconds of each encoded as SHRINK encodes it at a ladder of CRFs and scored with VMAF (FFmpeg's libvmaf, default model) against its original. VMAF 95 and up is usually indistinguishable from the original, 90 and up good.

```
clip                          codec CRF  kbps  size   VMAF  worst 5%
Tears of Steel 720p (4.3 Mb/s) AV1   30   1133  0.26   95.7   93.1
  another scene                AV1   30   1275  0.30   97.3   94.2
Tears of Steel 720p            HEVC  24   1445  0.34   94.9   91.9
  another scene                HEVC  24   1198  0.28   96.1   92.2
Sintel 1080p (10.1 Mb/s, CG)   AV1   30    793  0.08   90.1   85.3
Sintel 1080p                   HEVC  24    937  0.09   90.2   85.8
Sintel 1080p                   AV1   24   1346  0.13   92.4   88.1
Sintel 1080p                   HEVC  20   1807  0.18   93.4   89.5
```

**The settings stay.** The two codecs are matched: on the CG clip they give the same quality, AV1's copy 15% smaller; on live action AV1's is better and smaller. Three CRF steps down buys about one VMAF point for a copy 25 to 40% larger. A detailed, high-bitrate source (the Sintel clip) stays near VMAF 90 at either codec's setting, a twelfth of its size; reaching 93 there takes CRF 20 to 24 and gives up most of the saving, so no single CRF suits both kinds of source. Making each copy aim at a quality instead (encoding samples to choose the CRF per file) would be a new feature, not a new setting. The full ladder (CRF 24 to 36 for AV1, 20 to 28 for HEVC) was recorded during the run; encode times aren't given, since other work shared the CPU.

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
