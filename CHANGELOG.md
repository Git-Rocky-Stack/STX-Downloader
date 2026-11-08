# Changelog

## Unreleased: the 2026-11-07 review, and the rest of the open findings

**Not in a published release yet.** A whole-codebase review by four independent reviewers, each finding checked in the code before anything changed, and the four findings the 2026-11-05 handoff left unchecked. Every fix below has a test that failed without it (each break-checked by undoing the fix and running the test), unless it says otherwise. Run on a second PC (Core Ultra 7 258V, Arc 140V, Intel Smart Sound speakers); the report is `docs/superpowers/reports/2026-11-07-review-report.md`.

### The Library

- **A file could be listed twice.** The first scan and the folder watcher each decided from their own reading of the index and both added a new file; an overflow rescan and the watcher's own timer could do the same. Now one row per file is the catalog's rule, in any case of the path (`src/Stx.Data/MediaRepo.cs:235`), the scan and the watcher take turns (`src/Stx.Core/Library/CollectionWatcher.cs:28`), and a migration merges the duplicates already made, moving any shrink copy or job to the row that stays and never removing a download (`src/Stx.Data/Migrations.cs:70`).
- **A busy database could close the app.** The watcher's timer caught only three kinds of error, not SQLite's; the changes are now kept and tried again (`CollectionWatcher.cs:257`).
- **One failed scan stopped every later one**, a removed folder's scan carried on adding rows, a folder missing at launch was never watched, and renaming onto a listed file left two rows (`CollectionWatcher.cs:98`, `:134`, `:307`).
- **Opening a file from Explorer froze the window** on a big Library or a sleeping NAS: the disk and database work, and a full rebuild of the Library, ran on the UI thread (`src/Stx.ViewModels/OpenedFiles.cs:14`).

### The player

- **A stream that fails says why:** never reached, or cut off (`src/Stx.Playback/LibVlcEngine.cs:23`). It used to say "THIS FILE COULDN'T BE PLAYED", or nothing.
- **STOP during an open, a throw while opening, and `file:` addresses.** A file still being read started after STOP; a throw left the plate on OPENING; a `file:` address was handed to LibVLC unconverted (`LibVlcEngine.cs:333`, `:288`, `:250`).
- **The Dirac guard no longer fails open.** When LibVLC couldn't finish reading a file's tracks, the file was taken as safe and could reach the decoder that crashes the process; STX now reads Dirac's signature from the file itself (`LibVlcEngine.cs:304`, `src/Stx.Core/Playback/DiracSniff.cs:21`). Streams aren't sniffed; see the report.
- **A disc on a network share opens.** `\\nas\Films\My Film` reached VLC's DVD reader as `nas\Films\...`; checked against LibVLC 3.0.24 with a disc folder on `\\localhost\C$`, the new address reaches the reader as the share (`src/Stx.Core/Playback/PlaybackTarget.cs:55`). Blu-ray's address is made the same way but wasn't run; no real disc was played.
- **BIT-PERFECT:** disposing the renderer twice no longer throws, a failed open no longer leaves the device's COM objects behind, a device that stops asking for sound is reported instead of freezing the time, a pump stuck reading a stalled share can no longer write beside the next one (it wrote 173,760 frames before the fix), and a late end can't stop a seek (`src/Stx.Playback/ExclusiveRenderer.cs:284`, `:116`, `:215`, `:245`). A file FFmpeg can't read to its end fails with a message instead of reading as ENDED (`src/Stx.Playback/MusicEngine.cs:91`). PLAY on a finished track whose device another app took plays shared instead of throwing; STOP during the probe and closing during an open are safe (`src/Stx.Playback/RoutingEngine.cs:161`, `:174`, `:198`).
- **A failed measurement isn't "Measured"** (`src/Stx.Audio/AudioMeasurer.cs:39`), and one that finishes late no longer replaces the row you chose since (`src/Stx.App/Composition.cs:339`).

### Downloads, Shrink and tools

- **SHRINK twice queued the same encode twice**, and RESUME straight after PAUSE ran two tools on one part file (`src/Stx.Core/Queue/QueueEngine.cs:34`, `:168`).
- **A long playlist no longer freezes ADD**: what's saved is read once, off the UI thread, not once per entry (640 file lookups became 20 in the test) (`src/Stx.ViewModels/SourceViewModel.cs:154`). ANALYZE gives up on a stalled site after 90 seconds (`:103`).
- **The yt-dlp update can't leave no yt-dlp**: one `File.Replace` instead of two moves, and a rollback that can't happen yet changes nothing instead of failing the job it rescued (`src/Stx.Tools/ToolManager.cs:164`, `:184`). Tool downloads now refuse any redirect off GitHub's hosts or HTTPS (`:64`); proven live against GitHub. The encoder probe no longer keeps an empty answer from before FFmpeg arrived (`src/Stx.Tools/EncoderProbe.cs:19`). Credential files can't take an address that isn't a web link, and their cleanup can't replace a job's outcome (`src/Stx.Tools/CredentialFile.cs:49`, `:69`).

### The app

- **An error log**, `%LOCALAPPDATA%\STXDownloader\logs\errors.log`, scrubbed of tokens and kept under 1 MB (`src/Stx.Core/Logging/ErrorLog.cs:9`). There was nowhere for background failures to go, which is why so many were swallowed.
- **Nothing that escapes is lost:** the UI thread, a crash and unobserved tasks are all logged; the tray icon goes with a crash (`src/Stx.App/App.xaml.cs:13`). A failure at startup no longer leaves a console that looks dead (`src/Stx.App/Composition.cs:144`).
- **The tray icon comes back after Explorer restarts**, a hidden start shows the window if the icon couldn't be added, its icon handle isn't leaked, and its menu closes properly (`src/Stx.App/Platform/Tray.cs:38`).
- **The browser and "Open with" listeners survive a bad message** (`src/Stx.Bridge/PipeServer.cs:32`, `src/Stx.Bridge/OpenRequests.cs:74`); the extension always answers the popup (`extension/worker.js:93`).
- **Settings survive a power cut and a damaged file** is kept as `settings.json.damaged` (`src/Stx.Core/Settings/AppSettings.cs:88`, `:117`). A job row this version can't read is skipped, not fatal to the queue (`src/Stx.Data/JobRepo.cs:85`). A policy-locked registry no longer throws from DEFAULT PLAYER (`src/Stx.ViewModels/DefaultPlayerViewModel.cs:70`).

### Build

- Warnings are errors, solution-wide (`Directory.Build.props`), and package versions live in one place (`Directory.Packages.props`). CI's token is read-only and its actions are pinned to commits, kept current by Dependabot (`.github/workflows/build.yml`, `.github/dependabot.yml`).
- `Measure-Budgets.ps1` can no longer strand your own Library in the temp folder, or delete it (`tools/Measure-Budgets.ps1:55`; proven with stand-in files). `Publish.ps1 -Version` now reaches the binaries, not only the file names (`tools/Publish.ps1:31`; not run here, which has no Inno Setup or signing).

## Unreleased: the whole collection, the default player, discs, streams, bit-perfect sound, ENHANCE and Shrink

**Not in a published release yet.** Phases 2 to 7 of the player. The report, with what was and wasn't tested, is `docs/superpowers/reports/2026-11-05-player-phases-2-7-report.md`.

- **The Library is your whole collection.** Videos and Music are watched; add and remove folders in Settings. New files appear by themselves, renamed files keep their rows, deleted ones show FILE MISSING. Each row says ORIGINAL, OPENED or WATCHED. Search by title, file name or folder, on tens of thousands of rows (`src/Stx.Core/Library/CollectionWatcher.cs`, `src/Stx.ViewModels/LibraryViewModel.cs`). At 50,000 rows the idle app holds 185 MB, over the 150 MB budget; `docs/PERFORMANCE.md` has the measurement.
- **STX as your default player.** Registered for every type the corpus proves it plays; asked once at first run; a DEFAULT PLAYER pad and an LED that reads what Windows really does (`src/Stx.Core/Library/FileAssociations.cs`).
- **Discs and streams.** PLAY DISC for unencrypted DVDs and Blu-rays; encrypted discs are refused by STX before VLC's engine sees them. VLC's DVD plugins ship again for this. PLAY STREAM for stream addresses and internet radio; UDP and RTP are refused because they'd need a listening port (`src/Stx.Core/Playback/DiscCheck.cs`, `src/Stx.ViewModels/StreamOpener.cs`).
- **BIT-PERFECT.** Lossless music reaches the device exactly as it is, through FFmpeg and STX's own exclusive renderer, because VLC 3.0.24's output can't do it. Films pass their compressed soundtrack to the receiver. The Player says what reaches the device (`src/Stx.Core/Playback/BitPerfect.cs`, `src/Stx.Playback/ExclusiveRenderer.cs`).
- **ENHANCE.** SHARPEN, or UPSCALE on a GPU with super resolution, for the picture on screen only; UPSCALE UNAVAILABLE elsewhere (`src/Stx.Core/Playback/Enhance.cs`).
- **Shrink.** AV1, HEVC and Opus copies beside the originals, one file or a whole folder, each checked before it's kept. The Library shows what deleting the originals would free; STX never deletes them (`src/Stx.Tools/CopyCheck.cs`, `src/Stx.ViewModels/ShrinkFolder.cs`).
- **Build:** the test suites moved from xUnit 2, marked Legacy on NuGet, to xunit.v3 on Microsoft.Testing.Platform, and CI's actions to their Node 24 releases.

### Fixed after the first independent review and the first run on the reference PC

- **Shrink failed on most surround films.** With mapping family 1, libopus takes only Vorbis's layouts and refused every other, 5.1(side) among them, which is how most AC3 and DTS soundtracks arrive. Side layouts are now renamed to Vorbis's, with no sample changed, and the rest are widened to the smallest Vorbis layout that holds every channel (`src/Stx.Tools/FfmpegArgs.cs:92`, `src/Stx.Tools/FfmpegRunner.cs:47`).
- **The player could stay on OPENING for good** when deciding a file's route threw; it now plays shared and says why. ffprobe's 10 second limit now holds, so a stalled share can't hang it (`src/Stx.Playback/RoutingEngine.cs:81`, `:143`).
- **BIT-PERFECT no longer brings the app down** when the device goes away mid-play: the renderer stops, gives the device back and the plate says why. A seek stops the renderer before its decoder is replaced, so seeking no longer ends the track; and stopping or moving on just as a track ends no longer waits 3 seconds (`src/Stx.Playback/ExclusiveRenderer.cs:166`, `src/Stx.Playback/MusicEngine.cs:73`, `:99`). The seek and the track's end are tested on the reference PC's device held exclusively (`tests/Stx.Playback.Tests/BitPerfectEngineTests.cs:155`, `:178`); a device going away mid-play isn't, since that needs one unplugged.
- **Only stream addresses STX knows reach VLC's engine.** An unknown scheme such as `dshow://` or `screen://` is refused, and a `file:` address plays as the file (`src/Stx.Core/Playback/PlaybackTarget.cs:32`).
- **An original with two Shrink copies is counted once** in what deleting originals would free (`src/Stx.ViewModels/LibraryViewModel.cs:255`).
- **The BIT-PERFECT note in Settings wraps** instead of running off the plate (`src/Stx.App/Panels/SettingsPanel.cs:216`).
- **Every STX window has the black title strip without an icon**, the screenshot gallery's included (`src/Stx.App/TitleStrip.cs:18`).
- **Build:** VLC's engine is fetched again whenever `tools/vlc/engine.json` changes. A machine that already had one kept it, so Phase 4's DVD plugins never arrived on it (`src/Stx.Playback/Stx.Playback.csproj:25`).

## Unreleased: the media player

**Not in a published release yet.** Everything below is in the app built from this repository. `docs/PLAYBACK.md` has the details, and `docs/superpowers/reports/2026-11-04-player-phase1-report.md` the evidence.

- **A PLAYER plate**, beside the Library and above Source Truth. It has PREV, PLAY/PAUSE, NEXT, STOP, FULL, a position bar, the time, a VOLUME knob, and track pickers when a file has more than one choice (`src/Stx.App/Panels/PlayerPanel.cs`, `src/Stx.App/MainWindow.cs:58`).
- **OPEN plays inside STX.** OPEN in the Library plays the file on the PLAYER plate instead of handing it to another app (`src/Stx.ViewModels/LibraryViewModel.cs:109`).
- **Files from File Explorer.** A file opened with STX joins the Library and plays; a second launch hands its file to the copy already running (`src/Stx.App/App.xaml.cs:27`, `src/Stx.App/Platform/SingleInstance.cs:26`).
- **Full screen.** FULL fills the screen, and Esc or EXIT brings the console back without restarting the video (`src/Stx.App/Panels/PlayerPanel.cs:212`).
- **PERFORMANCE** in Settings: AUTO, ECO, BALANCED or MAX (`src/Stx.App/Panels/SettingsPanel.cs:46`).
- **VLC's engine.** The official VLC 3.0.24 build for Windows, pinned by sha256, without the parts that would open network ports, run site scripts or decrypt DVDs (`tools/vlc/engine.json`).
- **Dirac and VC-2 video are refused, not played.** VLC 3.0.24's engine crashes on them, the desktop VLC build included, so STX shows "DIRAC VIDEO CAN'T BE PLAYED SAFELY" instead of closing (`src/Stx.Playback/LibVlcEngine.cs:19`).
- **MIDI plays**, with the FluidR3_GM SoundFont (MIT), which VLC doesn't include (`src/Stx.Playback/LibVlcEngine.cs:22`).
- **Mono sound goes through WASAPI in stereo**, instead of falling back to DirectSound; surround is never downmixed (`src/Stx.Playback/LibVlcEngine.cs:417`).
- **A parity corpus.** 99 files covering 89 of the 91 formats and codecs on VideoLAN's list, plus HEVC Main10, HDR10, AV1 and VP9. The two left out are SVCD/CVD subtitles (no public sample) and CMML (no decoder in LibVLC 3.0.24) (`tests/corpus/manifest.json`).
