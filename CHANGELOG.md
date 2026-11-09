# Changelog

## 1.1.0 (2026-11-09)

The first release since 1.0.0 (2026-11-01). STX plays what it saves now, and the Library is your whole collection: a media player on VLC's engine, STX as the default player, discs and streams, bit-perfect sound, ENHANCE, and Shrink. It was built in the rounds below, newest first.

### The Player in the tutorial, and the Player's knobs on a narrow plate (2026-11-09)

Every change below has a test that failed without it, unless it says otherwise.

- **The tutorial shows the Player.** A new scene, 06 · WATCH IT HERE, follows OPEN in the Library to the Player: the whole plate, its title, a press on FULL, then ENHANCE and its readout, ON SCREEN ONLY. Its three captions are the guide's own. The tutorial runs 89 seconds now, and the guide and README say so; a test fails if they, the project in `tools/tutorial` and the shipped `Assets/tutorial.mp4` disagree on its length (`GuideTests`). The plate is a capture of the real Player playing a sample clip FFmpeg made from a gradient, so no third-party picture is in it; `tools/tutorial/STORYBOARD.md` has the commands to make it again.
- **Fixed: with ENHANCE on, the Player pushed its own controls off the plate.** ENHANCE's readout sat in the knob's column, so "SHARPEN · SOFTWARE FILTER · ON SCREEN ONLY" widened the knob row past the plate: the time readout was squeezed to nothing and the FULL pad was cut off, in any window narrower than about 1,560 px, which includes the 1,360 px STX opens at (the knob row needed about 628 px of the 535 it had there), and wider still with AUDIO, SUBTITLES or CHAPTER knobs as well. The readout has a line of its own now, and wraps; when the knobs don't fit beside the time, the extra ones wrap under it, right-aligned (`KnobRowLayout.Arrange`, `KnobRow`, `KnobRowLayoutTests`). Where they fit, everything sits where it did: the `player`, `player-full` and `player-audio` captures are pixel-identical to before, so the approved baselines stand.
- **A `player-plate` gallery page**, the Player as the rack shows it, ENHANCE knob included, at the rack's right-column width, set from a fixed sample so it never shows a user's settings (`PlayerGallery`). The other player pages still leave ENHANCE out. Not covered by a test: it's what the tutorial's plate is captured from.

### The user guide, the docs, and the release (2026-11-08)

Every change below has a test that failed without it, unless it says otherwise.

#### The user guide

- **Rebuilt, and twice as deep.** It opens with four quick-start steps behind glass, then gives each plate a section with a one-line summary: the console itself, saving from a link and from a page, Settings, the Queue, the Library, Source truth, the Player, streams and discs, and what to do when something goes wrong (`src/Stx.ViewModels/GuideContent.cs`, `src/Stx.App/Panels/GuidePanel.cs`).
- **Every pad, switch, knob and readout it names is set in the console's accent**, semibold, so a line can be matched to the console at a glance. A new `Accent` colour meets WCAG AA on both plates, as does the dimmed ink the summaries use (`LookTokens`, `FaceplateLogicTests`).
- **It covers the whole console.** A test fails if the guide names a control the console doesn't have, and now also if the console has one the guide never names. LIGHT MODE, START WITH WINDOWS, TOOL UPDATES, REDUCE MOTION, BROWSE, GRAB IT, NO THANKS, CLOSE, and the VOLUME, AUDIO, SUBTITLES and CHAPTER knobs, among others, were missing (`tests/Stx.App.Tests/GuideTests.cs`). Every readout it quotes is held to the app's own words.
- **Corrected:** it never said how to open it again (SETTINGS, then USER GUIDE); it gave two of Source truth's five verdicts; it said STX never deletes media, when a new download identical to a saved file is removed; and it didn't say that with TOOL UPDATES off, a first run can't download its tools.

#### Docs

- **The README** leads with installing and a quick start, has a table of contents, and says what's been tested since the laptop runs (BIT-PERFECT, UPSCALE, high DPI, HDR, Default Apps), not what hadn't been on 2026-11-05.
- **References name the member, not the line.** Most of the README's line references had drifted onto blank lines or other code. The README and the living docs now name file and member, and a test fails on a line number, on a file that isn't there, or on a name the code doesn't have (`tests/Stx.App.Tests/DocsTests.cs`).

#### The app

- **A first run waits out a tool host that's briefly down.** GitHub's release downloads answered 504 for minutes at a time on 2026-11-08 and -09, and a single 504 ended a first run with TOOLS MISSING. A busy or briefly-down answer is asked again after 2, 5 and 10 seconds; a 404 isn't, and an unreachable network still shows at once (`ToolManager.TransientWaits`).

#### Release and build

- **1.1.0, set in one place.** `Directory.Build.props` holds the number; `tools/Publish.ps1` reads it, the installer won't build without being given it, and a test holds the extension's manifest to it (`tests/Stx.App.Tests/VersionTests.cs`). `Publish.ps1` also finds Inno Setup installed for one user. Built on this laptop with Inno Setup 6.7.4: the setup and the zip carry 1.1.0 in the exe and in the extension.
- **The GitHub Actions** moved to checkout 7.0.1, setup-dotnet 6.1.0, cache 6.1.0 and setup-node 7.1.0, each pinned to its release's commit (Dependabot, #4 to #7), and CI waits about five minutes for GitHub's release host before giving up.
- **Gallery captures on a scaled screen.** `tools/Capture-Gallery.ps1` measured the window in 96-DPI units when PowerShell wasn't DPI aware, and cut a 175% capture to its top-left corner. It measures in device pixels now; at 96 DPI nothing changes. Not covered by a test: seen in a capture.

### The Library's rows out of memory, Dirac streams, high-DPI and HDR screens, a 27 MB SoundFont (2026-11-07)

The rest of 2026-11-07, on the Core Ultra 7 258V laptop (Arc 140V, a 2880x1800 panel at 175%). Every change below has a test that failed without it, unless it says otherwise.

#### The Library holds only what's on screen (plan 2026-11-05)

- **Counted, paged and searched in the database.** The Library used to read every row into memory, and keep a lower-case copy of each for search. Now the database answers how many rows there are, a page of them in the Library's order, and a search (`src/Stx.Core/Queue/IMediaCatalog.cs:28`, `src/Stx.Data/MediaRepo.cs:136`); the list reads a page at first and the next as it scrolls (`src/Stx.App/Panels/IncrementalRows.cs:12`, `src/Stx.ViewModels/LibraryViewModel.cs:389`). Rows that didn't change stay the same objects, as before, and whether a file is still on disk is checked for the rows read, not all 50,000.
- **Search finds what it found before, every letter's case folded:** the database keeps each path and title with its case folded as Windows folds it (`CaseKey`, `src/Stx.Core/Queue/IMediaCatalog.cs:81`; migration step 5, `src/Stx.Data/Migrations.cs:89`). SQLite's NOCASE folds A to Z only, so "CAFÉ" and "café" were two paths to the one-row-per-file rule; now they're one. A test holds the database to the in-memory rules on 400 generated items and 27 searches (`tests/Stx.Data.Tests/LibraryRowsTests.cs`).
- **The plan said a trigram FTS5 index; it isn't one.** Measured at 50,000 rows: a common word took 150 ms through it, a word under three letters needed a second path, and it added 7 MB to the database. A walk of the folded keys with SQLite's own `instr` takes about 20 ms (`MediaRepo.cs:221`). A file is listed under the first row saved for its path through a stored flag the triggers keep: looking for an earlier row, row by row, cost 130 ms per count once the database outgrew SQLite's page cache, and 880 ms at 200,000 rows, against 20 and 85 ms (`MediaRepo.cs:126`).
- **Typing a search at 50,000 rows**, through the view-model on the real database, stays under the 150 ms budget per keystroke, and the search runs off the UI thread; an answer for a word already typed past never reaches the screen (`tests/Stx.App.Tests/LibraryCollectionTests.cs`).
- **PREV and NEXT walk the Library from the database**, past the rows read so far and past files gone from disk, off the UI thread; two quick presses step twice (`LibraryViewModel.cs:433`, `src/Stx.ViewModels/PlayerViewModel.cs:96`).
- **Memory a scan leaves behind goes back.** With the rows gone, what was left at 50,000 rows was 45 MB of dead objects from the first scan, which an idle app never collects. Once a scan or a job has been quiet for 10 seconds, one compacting collection returns it (`src/Stx.Core/Memory/MemoryReturn.cs:10`, `src/Stx.App/Composition.cs:95`).
- **Measured** (this laptop, idle, private MB): 50,000 rows **235.3 before, 163.6 after**; an empty Library 178.0 before, 165.4 after. At 50,000 rows the Library now costs nothing over an empty one. This laptop's empty app is over the 150 MB budget on its own; the reference PC's was 117.5, so its 50,000-row run should now pass there, which is still to be measured (`docs/PERFORMANCE.md`).

#### The player

- **A Dirac stream is refused, not played.** VLC 3.0.24 crashes on Dirac in FFmpeg's decoder (`Assertion frame->data[i] failed at src/libavcodec/decode.c:1628`, then abort), with each Dirac plugin taken out, with one decoding thread and with hardware decoding off, so no engine setting avoids it. LibVLC's own check reads local files only; an http(s) stream that may carry video is now read from its start, up to a megabyte or 2 seconds, before LibVLC is given it. Sound, playlists, manifests and pages aren't read, and an address naming a sound file isn't even asked (`src/Stx.Core/Playback/DiracSniff.cs:33`, `src/Stx.Playback/LibVlcEngine.cs:262`). Tested with a stream served from this PC; without the check, the test host crashed.
- **UPSCALE seen running** on the Arc 140V: VLC's log confirms its Super Resolution scaler, in a test that skips on a GPU without it (`tests/Stx.VisualTests/EnhanceSeenTests.cs`).
- **HDR10 reaches an HDR screen as HDR:** VLC's output chooses Rec.2020 with the PQ curve, 10 bits, through STX's swap chain; checked from VLC's own log on a laptop panel with Windows' HDR on (`tests/Stx.VisualTests/HdrTests.cs`; skipped on a screen without HDR).
- **The SoundFont is 27 MB, not 148.** FluidR3_GM ships as SoundFont 3, its samples in Ogg Vorbis, made at build time from the pinned SoundFont 2 by `tools/vlc/Make-Sf3.cs` with a pinned FFmpeg build; the result's sha256 is pinned too (`tools/vlc/engine.json:29`). Rendered through VLC, a ten-second MIDI file with held, looping notes matches the SoundFont 2's within 0.1 dB a second, correlation 0.9998 (`tests/Stx.Playback.Tests/MidiTests.cs`, which also held MIDI to working for the first time: nothing played MIDI in the tests before). The licence (MIT) is unchanged; MuseScore's SF3 font, whose licence was the question, isn't used.

#### The app

- **DEFAULT PLAYER reads Windows 24H2's choices as Windows does:** a test reads each type's chosen app as STX does and compares it with Windows' own answer (AssocQueryString), read only, on whatever Windows runs it; on build 26300 all agree (`src/Stx.Core/Library/FileAssociations.cs:97`, `tests/Stx.App.Tests/DefaultAppsLiveTests.cs`).
- **Sharp on high-DPI screens.** STX had no application manifest, so it ran DPI-unaware: on a screen scaled past 100% Windows drew it at 96 DPI and stretched it, so text and art were soft, and at 175% full screen missed the display by a pixel. It's per-monitor DPI aware now (`src/Stx.App/app.manifest:17`), and windows are sized in device-independent pixels, kept inside the display's work area (`src/Stx.App/Platform/WindowSize.cs:15`): a 1360x920 window at 150% was 2040x1380, more than a 1920x1080 screen holds.

#### Build and tests

- **Shrink's encoder settings, checked against real footage:** clips from Blender's open films, scored with VMAF against their originals. SVT-AV1 CRF 30 and x265 CRF 24 give the same quality, AV1's copy the smaller; live action keeps VMAF 95 to 97; a detailed 10 Mb/s source keeps about 90 at a twelfth of its size. The settings stay; the numbers are in `docs/PERFORMANCE.md`.
- **FFmpeg's builds moved** from BtbN/FFmpeg-Builds to FFmpeg-Builds/FFmpeg-Builds; STX and CI name the new owner, and a Network test fails if that address ever answers with a move again (`src/Stx.Tools/FfmpegSource.cs:12`).
- **The projects that aren't Windows-only run on Linux in CI** (Look, Art, Core, Data; `.github/workflows/build.yml`). Its first run passed all four once the job had FFmpeg, which Core's bit-perfect decode tests need; nothing else needed changing for Linux.
- **A test that failed since 2026-11-05:** the baseline size check still asked 944x601 of the settings and Library images after their taller baselines were approved (`tests/Stx.VisualTests/BaselineTests.cs:36`). The playback tests' picture regions now scale with the capture, for high-DPI screens (`tests/Stx.VisualTests/CaptureArea.cs`).

### The 2026-11-07 review, and the rest of the open findings (2026-11-07)

A whole-codebase review by four independent reviewers, each finding checked in the code before anything changed, and the four findings the 2026-11-05 handoff left unchecked. Every fix below has a test that failed without it (each break-checked by undoing the fix and running the test), unless it says otherwise. Run on a second PC (Core Ultra 7 258V, Arc 140V, Intel Smart Sound speakers); the report is `docs/superpowers/reports/2026-11-07-review-report.md`.

#### The Library

- **A file could be listed twice.** The first scan and the folder watcher each decided from their own reading of the index and both added a new file; an overflow rescan and the watcher's own timer could do the same. Now one row per file is the catalog's rule, in any case of the path (`src/Stx.Data/MediaRepo.cs:235`), the scan and the watcher take turns (`src/Stx.Core/Library/CollectionWatcher.cs:28`), and a migration merges the duplicates already made, moving any shrink copy or job to the row that stays and never removing a download (`src/Stx.Data/Migrations.cs:70`).
- **A busy database could close the app.** The watcher's timer caught only three kinds of error, not SQLite's; the changes are now kept and tried again (`CollectionWatcher.cs:257`).
- **One failed scan stopped every later one**, a removed folder's scan carried on adding rows, a folder missing at launch was never watched, and renaming onto a listed file left two rows (`CollectionWatcher.cs:98`, `:134`, `:307`).
- **Opening a file from Explorer froze the window** on a big Library or a sleeping NAS: the disk and database work, and a full rebuild of the Library, ran on the UI thread (`src/Stx.ViewModels/OpenedFiles.cs:14`).

#### The player

- **A stream that fails says why:** never reached, or cut off (`src/Stx.Playback/LibVlcEngine.cs:23`). It used to say "THIS FILE COULDN'T BE PLAYED", or nothing.
- **STOP during an open, a throw while opening, and `file:` addresses.** A file still being read started after STOP; a throw left the plate on OPENING; a `file:` address was handed to LibVLC unconverted (`LibVlcEngine.cs:333`, `:288`, `:250`).
- **The Dirac guard no longer fails open.** When LibVLC couldn't finish reading a file's tracks, the file was taken as safe and could reach the decoder that crashes the process; STX now reads Dirac's signature from the file itself (`LibVlcEngine.cs:304`, `src/Stx.Core/Playback/DiracSniff.cs:21`). Streams were sniffed later the same day (the section above).
- **A disc on a network share opens.** `\\nas\Films\My Film` reached VLC's DVD reader as `nas\Films\...`; checked against LibVLC 3.0.24 with a disc folder on `\\localhost\C$`, the new address reaches the reader as the share (`src/Stx.Core/Playback/PlaybackTarget.cs:55`). Blu-ray's address is made the same way but wasn't run; no real disc was played.
- **BIT-PERFECT:** disposing the renderer twice no longer throws, a failed open no longer leaves the device's COM objects behind, a device that stops asking for sound is reported instead of freezing the time, a pump stuck reading a stalled share can no longer write beside the next one (it wrote 173,760 frames before the fix), and a late end can't stop a seek (`src/Stx.Playback/ExclusiveRenderer.cs:284`, `:116`, `:215`, `:245`). A file FFmpeg can't read to its end fails with a message instead of reading as ENDED (`src/Stx.Playback/MusicEngine.cs:91`). PLAY on a finished track whose device another app took plays shared instead of throwing; STOP during the probe and closing during an open are safe (`src/Stx.Playback/RoutingEngine.cs:161`, `:174`, `:198`).
- **A failed measurement isn't "Measured"** (`src/Stx.Audio/AudioMeasurer.cs:39`), and one that finishes late no longer replaces the row you chose since (`src/Stx.App/Composition.cs:339`).

#### Downloads, Shrink and tools

- **SHRINK twice queued the same encode twice**, and RESUME straight after PAUSE ran two tools on one part file (`src/Stx.Core/Queue/QueueEngine.cs:34`, `:168`).
- **A long playlist no longer freezes ADD**: what's saved is read once, off the UI thread, not once per entry (640 file lookups became 20 in the test) (`src/Stx.ViewModels/SourceViewModel.cs:154`). ANALYZE gives up on a stalled site after 90 seconds (`:103`).
- **The yt-dlp update can't leave no yt-dlp**: one `File.Replace` instead of two moves, and a rollback that can't happen yet changes nothing instead of failing the job it rescued (`src/Stx.Tools/ToolManager.cs:164`, `:184`). Tool downloads now refuse any redirect off GitHub's hosts or HTTPS (`:64`); proven live against GitHub. The encoder probe no longer keeps an empty answer from before FFmpeg arrived (`src/Stx.Tools/EncoderProbe.cs:19`). Credential files can't take an address that isn't a web link, and their cleanup can't replace a job's outcome (`src/Stx.Tools/CredentialFile.cs:49`, `:69`).

#### The app

- **An error log**, `%LOCALAPPDATA%\STXDownloader\logs\errors.log`, scrubbed of tokens and kept under 1 MB (`src/Stx.Core/Logging/ErrorLog.cs:9`). There was nowhere for background failures to go, which is why so many were swallowed.
- **Nothing that escapes is lost:** the UI thread, a crash and unobserved tasks are all logged; the tray icon goes with a crash (`src/Stx.App/App.xaml.cs:13`). A failure at startup no longer leaves a console that looks dead (`src/Stx.App/Composition.cs:144`).
- **The tray icon comes back after Explorer restarts**, a hidden start shows the window if the icon couldn't be added, its icon handle isn't leaked, and its menu closes properly (`src/Stx.App/Platform/Tray.cs:38`).
- **The browser and "Open with" listeners survive a bad message** (`src/Stx.Bridge/PipeServer.cs:32`, `src/Stx.Bridge/OpenRequests.cs:74`); the extension always answers the popup (`extension/worker.js:93`).
- **Settings survive a power cut and a damaged file** is kept as `settings.json.damaged` (`src/Stx.Core/Settings/AppSettings.cs:88`, `:117`). A job row this version can't read is skipped, not fatal to the queue (`src/Stx.Data/JobRepo.cs:85`). A policy-locked registry no longer throws from DEFAULT PLAYER (`src/Stx.ViewModels/DefaultPlayerViewModel.cs:70`).

#### Build

- Warnings are errors, solution-wide (`Directory.Build.props`), and package versions live in one place (`Directory.Packages.props`). CI's token is read-only and its actions are pinned to commits, kept current by Dependabot (`.github/workflows/build.yml`, `.github/dependabot.yml`).
- `Measure-Budgets.ps1` can no longer strand your own Library in the temp folder, or delete it (`tools/Measure-Budgets.ps1:55`; proven with stand-in files). `Publish.ps1 -Version` now reaches the binaries, not only the file names (`tools/Publish.ps1:31`; not run here, which has no Inno Setup or signing).

### The whole collection, the default player, discs, streams, bit-perfect sound, ENHANCE and Shrink (2026-11-05)

Phases 2 to 7 of the player. The report, with what was and wasn't tested, is `docs/superpowers/reports/2026-11-05-player-phases-2-7-report.md`.

- **The Library is your whole collection.** Videos and Music are watched; add and remove folders in Settings. New files appear by themselves, renamed files keep their rows, deleted ones show FILE MISSING. Each row says ORIGINAL, OPENED or WATCHED. Search by title, file name or folder, on tens of thousands of rows (`src/Stx.Core/Library/CollectionWatcher.cs`, `src/Stx.ViewModels/LibraryViewModel.cs`). At 50,000 rows the idle app holds 185 MB, over the 150 MB budget; `docs/PERFORMANCE.md` has the measurement.
- **STX as your default player.** Registered for every type the corpus proves it plays; asked once at first run; a DEFAULT PLAYER pad and an LED that reads what Windows really does (`src/Stx.Core/Library/FileAssociations.cs`).
- **Discs and streams.** PLAY DISC for unencrypted DVDs and Blu-rays; encrypted discs are refused by STX before VLC's engine sees them. VLC's DVD plugins ship again for this. PLAY STREAM for stream addresses and internet radio; UDP and RTP are refused because they'd need a listening port (`src/Stx.Core/Playback/DiscCheck.cs`, `src/Stx.ViewModels/StreamOpener.cs`).
- **BIT-PERFECT.** Lossless music reaches the device exactly as it is, through FFmpeg and STX's own exclusive renderer, because VLC 3.0.24's output can't do it. Films pass their compressed soundtrack to the receiver. The Player says what reaches the device (`src/Stx.Core/Playback/BitPerfect.cs`, `src/Stx.Playback/ExclusiveRenderer.cs`).
- **ENHANCE.** SHARPEN, or UPSCALE on a GPU with super resolution, for the picture on screen only; UPSCALE UNAVAILABLE elsewhere (`src/Stx.Core/Playback/Enhance.cs`).
- **Shrink.** AV1, HEVC and Opus copies beside the originals, one file or a whole folder, each checked before it's kept. The Library shows what deleting the originals would free; STX never deletes them (`src/Stx.Tools/CopyCheck.cs`, `src/Stx.ViewModels/ShrinkFolder.cs`).
- **Build:** the test suites moved from xUnit 2, marked Legacy on NuGet, to xunit.v3 on Microsoft.Testing.Platform, and CI's actions to their Node 24 releases.

#### Fixed after the first independent review and the first run on the reference PC

- **Shrink failed on most surround films.** With mapping family 1, libopus takes only Vorbis's layouts and refused every other, 5.1(side) among them, which is how most AC3 and DTS soundtracks arrive. Side layouts are now renamed to Vorbis's, with no sample changed, and the rest are widened to the smallest Vorbis layout that holds every channel (`src/Stx.Tools/FfmpegArgs.cs:92`, `src/Stx.Tools/FfmpegRunner.cs:47`).
- **The player could stay on OPENING for good** when deciding a file's route threw; it now plays shared and says why. ffprobe's 10 second limit now holds, so a stalled share can't hang it (`src/Stx.Playback/RoutingEngine.cs:81`, `:143`).
- **BIT-PERFECT no longer brings the app down** when the device goes away mid-play: the renderer stops, gives the device back and the plate says why. A seek stops the renderer before its decoder is replaced, so seeking no longer ends the track; and stopping or moving on just as a track ends no longer waits 3 seconds (`src/Stx.Playback/ExclusiveRenderer.cs:166`, `src/Stx.Playback/MusicEngine.cs:73`, `:99`). The seek and the track's end are tested on the reference PC's device held exclusively (`tests/Stx.Playback.Tests/BitPerfectEngineTests.cs:155`, `:178`); a device going away mid-play isn't, since that needs one unplugged.
- **Only stream addresses STX knows reach VLC's engine.** An unknown scheme such as `dshow://` or `screen://` is refused, and a `file:` address plays as the file (`src/Stx.Core/Playback/PlaybackTarget.cs:32`).
- **An original with two Shrink copies is counted once** in what deleting originals would free (`src/Stx.ViewModels/LibraryViewModel.cs:255`).
- **The BIT-PERFECT note in Settings wraps** instead of running off the plate (`src/Stx.App/Panels/SettingsPanel.cs:216`).
- **Every STX window has the black title strip without an icon**, the screenshot gallery's included (`src/Stx.App/TitleStrip.cs:18`).
- **Build:** VLC's engine is fetched again whenever `tools/vlc/engine.json` changes. A machine that already had one kept it, so Phase 4's DVD plugins never arrived on it (`src/Stx.Playback/Stx.Playback.csproj:25`).

### The media player (2026-11-04)

Everything below is in the app built from this repository. `docs/PLAYBACK.md` has the details, and `docs/superpowers/reports/2026-11-04-player-phase1-report.md` the evidence.

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
