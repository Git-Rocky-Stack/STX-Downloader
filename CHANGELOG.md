# Changelog

## Unreleased

### The project page, rebuilt as a rack (2026-11-15)

- **The page is rebuilt from the ground up** as a piece of rack gear, in a black and an aluminum finish (`site/index.html`, `site/css/stx.css`, `site/js/stx.js`). Nothing is kept from the first page.
- **Its screws, LEDs, meter segments, digits, pads and brushed grain are drawn by the app's own art code** (`tools/site/SiteArt`, into `site/art/`). `build.yml` redraws them and fails when the committed images differ (`--check`).
- **Every screen is captured from 1.2.0 in both finishes** (`site/screens/`), and the README points there. The first page's `site/screenshots/` is gone.
- **`tools/site/check-site.mjs` is rewritten for this page.** It holds the page to the README's download table: the version, file names, sizes, SHA-256 hashes and links, in its attributes, the text people read, its structured data and its `llms` files. A release that forgets the page can't publish it.
- **The page serves its own fonts**, Cascadia Mono and Selawik, under the SIL Open Font License (`site/fonts/`, [third-party software](docs/THIRD_PARTY.md)).

### Public, with a project page (2026-11-15)

- **The repository is public**, under the MIT license it always had (`LICENSE`).
- **A project page** at https://git-rocky-stack.github.io/STX-Downloader/: what STX does, screens of every plate, both 1.2.0 downloads with their SHA-256, the release history, and what has and hasn't been tested. It's a single static page in `site/`, published by `.github/workflows/pages.yml` on every push to main that touches it, with a sitemap, `robots.txt`, `llms.txt`, `llms-full.txt`, structured data, and an IndexNow ping after each publish.
- **`tools/site/check-site.mjs` gates the publish.** It fails on a file the page points at that isn't there, an image without alt text or size, a missing search or social tag, structured data that doesn't parse, a sitemap that doesn't match `site/`, or an em dash or emoji in the page, its text files or the README. A missing image, a sitemap entry for a file that isn't there, an em dash and an emoji were each injected and caught; the alt text, tag and structured-data checks weren't tried that way.
- **The screens are new captures from 1.2.0**, drawn by the app's own panels on sample data (`tools/Capture-Gallery.ps1`), in `site/screenshots/` (since moved to `site/screens/`, above). The README shows five of them, and links both downloads with their hashes.
- **The README** gains a download table, the Screens section, and Contributing.

## 1.2.0 (2026-11-15)

A user's review of 1.1.0 asked for six things: a guide that isn't a wall of text, far more file types, a way to clear finished rows, real tutorials, meters with some life in them, and no dead space under the Library. All six are below, in two rounds.

### The release (2026-11-15)

- **The setup is signed**, as are STX's own files in it, through Strategia-X's Azure Artifact Signing account, with a timestamp. It is 254 MB, down from 278 MB in 1.1.0, and installs the app (384 MB) and its tools (432 MB): yt-dlp 2026.10.26, FFmpeg and FFprobe n9.0.2 (the 2026-11-14 build), and Deno 2.9.8, each checked against its publisher's checksum and run once offline before it went in. The zip is 171 MB, down from 191 MB. The .NET runtime is still 10.0.14.

- **Two tests that failed for reasons outside the app are fixed.** The tutorial video's playback tests were still in the suite after the video left the app, and failed on every PC; they're gone, and `GuideTests` still fails if the video comes back. The memory test's garbage stayed reachable in a Debug build, which keeps a local alive to the end of its method, so it failed in Debug and passed in Release; the garbage is now made in a frame of its own (`MemoryReturnTests.FillTheOldestGeneration`). Both failed before the fix.

### More file types: 120, up from 51 (2026-11-12)

- **STX plays and registers 120 file types, up from 51.** Each new one is played by a file in the parity corpus through STX's engine, VLC 3.0.24, and a test fails for a type without one (`MediaTypes`, `tests/corpus/manifest.json`, `CorpusManifestTests`). The installer's associations were made again from the same list (`tools/installer/associations.iss`).
  - Video: .264, .265, .3g2, .3gp2, .3gpp, .amv, .bik, .divx, .dvr-ms, .evo, .f4v, .gxf, .h264, .h265, .hevc, .ivf, .m2p, .m2t, .mjpeg, .mjpg, .mk3d, .mp2v, .mpe, .mpv, .mpv2, .nsv, .nuv, .ogx, .pva, .qt, .tod, .tp, .trp, .vro, .wm, .wtv, .xvid, .y4m.
  - Audio: .3ga, .669, .a52, .adts, .aifc, .amr, .au, .awb, .caf, .gbs, .gsm, .it, .kar, .m4b, .m4r, .mp1, .mpa, .mpga, .mtm, .qcp, .ra, .rmi, .s3m, .shn, .snd, .tak, .vgm, .vqf, .w64, .weba, .xm.
- Files no encoder writes are our own, written byte by byte by `tools/corpus/make-own-binaries.py`: tracker modules (.s3m, .xm, .it, .669, .mtm), karaoke and RIFF MIDI, and game music (.vgm, .gbs). The rest are made by FFmpeg or fetched from FFmpeg's sample archives with a pinned SHA-256. A make line can now read a corpus file made before it as `{files}/name` (`tools/corpus/Make-Corpus.ps1`); the raw .awb is copied out of a fetched 3GPP file that way.
- Left out: types the engine opened but didn't decode (DSD's .dsf and .dff, raw MPEG-4 video as .mp4v, gzipped .vgz), protected .m4p, and extensions other files use as often (.xa, .sid, .spc, .nsf, .stm, and .mod). .mod names tracker music, a camcorder's MPEG-2 recording and Go's go.mod alike: registered as either kind it would mislabel the other (a camcorder .mod would shrink to sound alone) and list go.mod files in a watched folder as music, so it's left out, though the corpus still plays one of each kind (`MediaTypesTests`, `CorpusManifestTests.Both_kinds_of_mod_file_are_played`).
- The parity corpus grows from 109 files to 178, all played through the engine (`CorpusTests`). The user guide's new *What STX plays* section lists the types from the registered list itself, so the guide and the engine can't disagree (`GuideContent.Formats`).
- This laptop's FFmpeg has no Speex encoder, so `Make-Corpus.ps1` can't make `speex.spx` here; the reference PC's FFmpeg can. Not a change to the manifest.

### The console, reworked from a user's review (2026-11-12)

Every change below has a test that failed without it, unless it says otherwise.

- **The user guide reads like a guide.** Three columns of every section at once read as a wall of text. The guide keeps its quick start across the top, numbered behind glass, and under it an index of sections beside one page at a time: a heading, a lead line, the details at 14/22 and about 75 characters a line, the console's names the section uses as chips, SHOW ME to run that part's tutorial, and PREVIOUS and NEXT. On a narrow plate the index moves above the page (`GuidePanel`). A new section, *What STX plays*, lists every type from the registered list itself, so it can't drift (`GuideContent.Formats`). The guide's own pads are now held to the guide like every other plate's (`GuideTests.Panels`).
- **Tutorials on the live console replace the video.** The 89-second recording showed stills of plates and taught nothing; it's gone from the app, the setup and the release check. TUTORIALS opens seven chapters, from your first save to the advanced settings. START dims the window and walks a spotlight over each real control, scrolling it into view, with a card that explains it; the lit control still works, so you can try it, and the dimmed rest can't be pressed by accident. BACK, NEXT, FINISH, END TOUR and Esc drive it, the arrow keys too while the card has focus, and a screen reader hears each step. A finished chapter's lamp stays green across launches, and the plate shows how many are done on a progress meter (`TutorialPanel`, `TourOverlay`, `TutorialContent`, `TourTargets`, `AppSettings.TutorialsDone`). Tests hold every step's words, controls, readouts and target to the console, as the guide's are, so a renamed or removed control fails the build (`GuideTests`). With REDUCE MOTION on, the spotlight jumps instead of gliding. The HyperFrames project in `tools/tutorial` is no longer built into anything.
- **Finished jobs can be cleared.** CLEAR takes the selected finished job off the Queue; CLEAR DONE takes every complete, canceled or failed one, including the older ones a launch doesn't show. A job still under way can't be cleared, nor one whose tool is still ending; what a cleared job saved stays in the Library and on disk (`QueueEngine.Remove`, `QueueEngine.RemoveFinished`, `JobRepo.Remove`, `QueueViewModel.Clear`).
- **Rows can leave the Library.** REMOVE takes the selected row off, and CLEAR MISSING every FILE MISSING row, scrolled to or not, in one write. Only rows go: every row for that path, in any case, with its measurements, and an item left with no file, so a removed download can be saved again. Nothing on disk is touched. A row with a job still in the Queue stays until the job ends, and a file still in a watched folder can't be removed, since the folder would list it again (`LibraryViewModel.RemoveAsync`, `ClearMissingAsync`, `MediaRepo.Forget`, `InMemoryLibrary.Forget`).
- **No dead space under the Library.** The Queue and Library column and the Player and Source truth column now always end level: the deck measures both and stretches each column's last plate to the taller one's height, so the black gap under the Library is gone, full or empty. The Library's list sits in a recessed tray that grows with it, so the room reads as list space, and its pads wrap onto a second line rather than run off a narrow plate (`DeckPanel`, `ListWell`, `ListTray`, `PadRow`). Not covered by a unit test: seen in the new `deck` gallery page, dark and light.
- **Meters like hi-fi gear.** A job's progress, and the Player's position bar, sweep from aqua through cyan and azure to violet instead of flat green. The LEFT and RIGHT level meters shade from green through lime, yellow and orange to red, each inside the same 72% and 90% zones as before, so a reading means what it did. Each ramp is cut into 16 shared colors, and a meter that never holds a peak no longer draws peak sprites (`BargraphLogic.ZoneHex`, `LedArt.SegmentLit`, `Bargraph`).
- **The default-player readout no longer reads as a limit.** STX OPENS 6 OF 51 TYPES counted only the types Windows hands to STX on a double-click, and read as "STX can only open 6 types". It now says PLAYS 120 TYPES · DEFAULT FOR 6, DEFAULT PLAYER FOR ALL 120 TYPES, or PLAYS 120 TYPES · NOT YET YOUR DEFAULT (`DefaultState.Text`).
- **Gallery pages** `deck`, `tour` and `guide-wide` show the console at the real window's size: the deck's columns ending level, a tour held on a step, and the guide with its index beside its page. The accessibility scan now covers `guide-wide`, `tutorial` and `tour`, dark and light: every control named and at least 44 px (`AccessibilityScanTests`).
- **Baselines to approve on the reference PC:** `bargraph`, `all-primitives`, `console`, `library`, `settings`, `sourcetruth` and `player` change by design (the meters' colors, the new pads, TUTORIALS). They're judged at 96 DPI, which this laptop can't produce.


## 1.1.0 (2026-11-09)

The first release since 1.0.0 (2026-11-01). STX plays what it saves now, and the Library is your whole collection: a media player on VLC's engine, STX as the default player, discs and streams, bit-perfect sound, ENHANCE, and Shrink. It was built in the rounds below, newest first.

### The tools ship inside the setup (2026-11-11)

- **The first launch has nothing to download.** The setup now installs yt-dlp, FFmpeg with FFprobe, and Deno with the app, where it keeps its tools (`%LOCALAPPDATA%\STXDownloader\tools`), so STX works from its first launch, offline or with GitHub down. Until now the first run fetched about 430 MB from GitHub before anything could be saved, and an outage ended it with TOOLS MISSING. `tools/Publish.ps1` takes each tool from the addresses the app's updater uses, read from `ToolVersions.cs` and `FfmpegSource.cs` so the two can't drift, and ships it only when it matches the SHA-256 its publisher posts and passes the same offline self-test the app applies. A copy already in place stays, so the setup never puts back a yt-dlp older than one STX has since updated itself to; uninstalling leaves the tools to the existing question about the app's data. The setup grows from 127 MB to 265 MB; on disk, the app takes 395 MB and its tools 432 MB, as before. `ToolsShipTests` fails without each of these; it failed with the setup line weakened.
- **The tools' licenses ship with them**, in `docs\licenses` in the install folder: FFmpeg's GPL text from the very archive the programs come from, yt-dlp's Unlicense and Deno's MIT license, and `BUNDLED-TOOLS.txt`, which names each build's version, checksum and source, including FFmpeg's exact commit (`docs/THIRD_PARTY.md`). FFmpeg is a separate program STX starts, so STX's own code stays MIT.
- The zip still carries the app alone, and its first launch downloads the tools as before. The user guide's first quick-start step now says the setup installs them (`GuideContent.QuickStart`). The tutorial's setup scene still captions "The first run downloads its tools, about 430 MB" until it's rendered again.

### .NET ships inside the app (2026-11-10)

- **Fixed: the setup didn't run on a PC without .NET.** The app was built to bundle the Windows App SDK but to look for .NET 10 installed, so on a PC without it the installed app stopped with "You must install .NET". The design always called for a self-contained build. The app now carries its own .NET runtime (10.0.14), so nothing has to be installed first and the setup still asks for no administrator; a separate .NET installer in the setup would have needed one. The setup grows from 103 MB to 127 MB, the installed app from 318 MB to 395 MB (`Stx.App.csproj`, `SelfContained`). `tools/Publish.ps1` refuses a release that leaves .NET out, and `RuntimeShipsTests` fails on a build without it; the test failed against the previous build. Checked on this laptop, which has no .NET installed for every user: the published app opened its window with the per-user SDK hidden from it.
- **Third-party notices** now list what ships with the app's own code: the .NET runtime, the Windows App SDK, CommunityToolkit.Mvvm and Microsoft.Data.Sqlite with SQLite (`docs/THIRD_PARTY.md`). Not covered by a test.

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
