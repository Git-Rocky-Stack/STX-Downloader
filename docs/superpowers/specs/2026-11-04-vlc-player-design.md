# STX as the all-in-one media player — design

Date: 2026-11-04 · Owner: Rocky · Status: Rocky's answers in; ready for approval

## ASSUMPTIONS (unverified, each settled by a spike before the phase that needs it)

- ASSUMPTION A1: LibVLCSharp can draw into a WinUI 3 window in an unpackaged, self-contained Windows App SDK app (`src/Stx.App/Stx.App.csproj:9-10`). Not checked: which package does this (a WinUI-specific LibVLCSharp package, or a SwapChainPanel/HWND host), and whether it holds 4K60 smoothly. **Spike S1, before Phase 1.**
- ASSUMPTION A2: the VideoLAN LibVLC NuGet for Windows x64 carries VLC's full plugin set, around 80–100 MB on disk. The size is not measured.
- ASSUMPTION A3 (about VLC, unverified): VLC 3.0.19 or later is believed to hand video to the GPU's own super-resolution (NVIDIA RTX Video Super Resolution, and Intel's equivalent) through its Direct3D 11 output. It's also unverified whether that path is reachable through LibVLC when embedded. **Spike S6, before Phase 6.**
- ASSUMPTION A4: LibVLC's Windows audio output can't play PCM in WASAPI exclusive mode (its exclusive option is believed to cover only HDMI passthrough). If so, bit-perfect playback needs LibVLC's audio callbacks feeding STX's own WASAPI-exclusive renderer. **Spike S5, before Phase 5.**
- ASSUMPTION A5: VLC's Windows build bundles libdvdcss, the CSS decryption library, inside its DVD plugins. Rocky accepts shipping it as part of the stock VLC engine. STX still refuses encrypted discs itself, before handing them to LibVLC (see Phase 4), and `docs/THIRD_PARTY.md` says plainly that the engine contains it and STX doesn't use it. **Spike S4, before Phase 4**, confirms the detection works and that LibVLC never gets an encrypted disc.
- ASSUMPTION A6: Windows 11 accepts a deep link straight to an app's page in Default Apps (`ms-settings:defaultapps?registeredAppUser=…`), and Windows 10 doesn't. The fallback is the general Default Apps page.
- ASSUMPTION A7: the FFmpeg build STX already downloads (BtbN `win64-gpl`, `src/Stx.Tools/FfmpegSource.cs:9`) includes the SVT-AV1 and x265 encoders.

## Intent

STX Downloader already captures media from links and web pages, keeps a library, and tells you the truth about what you saved (README "What it does"). This makes it the **one desktop app for all of a person's media**. It plays everything VLC plays, on VLC's own engine, and becomes their default Windows player. Nothing else captures, plays, checks and converts in one place.

The thread through all of it: **STX tells the truth about quality.** It can enhance playback and save space, but it never claims a file got better. This is the stance already built into `src/Stx.Core/Audio/Verdict.cs:5-6`, and the tutorial says it out loud.

## Goals

1. Play every file, disc and stream VLC plays, except encrypted media (see Anti-goals).
2. Become the user's default player for video and audio, with the user choosing it once in Windows.
3. Turn the Library into the user's whole collection: downloads, files opened in STX, and the watched Videos and Music folders.
4. Sharper playback on screen, smaller files on disk, and bit-perfect hi-res audio, each labeled honestly.

## Non-goals

- Decrypting anything. STX never calls libdvdcss (shipped with the stock VLC engine, see A5), ships no AACS keys, and plays no store-bought encrypted DVDs or Blu-rays.
- A lean or separate player window. Opening a file opens the full console (Rocky).
- Deleting or replacing the user's files. STX never removes anything from disk.
- Streaming services, accounts, or cloud libraries.

## Anti-goals (failure even if it "works")

- **A file that VLC plays and STX doesn't** (Rocky: the thing that would send him back to VLC). Parity is tested, not claimed (Phase 1).
- Any wording or readout implying a re-encode or upscale improved the file.
- An original lost or overwritten by a shrink job.
- STX setting itself as default without the user's say.

## Decisions Rocky made

| Topic | Decision |
|---|---|
| Engine | VLC's engine (LibVLC), embedded |
| Where it plays | The full console opens, with a **PLAYER plate in the deck's right column, beside the Library**. Source Truth sits under it (mockup option B). |
| Full screen | Yes, the player can expand to full screen |
| Files opened from elsewhere | Join the Library, same as downloads |
| Watched folders | Videos and Music, watched automatically |
| Default player | Offered during first-run setup, and a Settings switch too |
| Scope of "everything" | Files, plus discs and network streams |
| DRM | Keep the existing no-DRM line: unencrypted discs only, say so on encrypted ones |
| Quality jobs | All three: sharper playback, smaller files, lossless audio |
| Shrinking | One file, or a whole folder |
| Originals after shrinking | Kept side by side |
| Bit-perfect audio | Exclusive device control for **music and movies alike**, with a user setting to share with Windows instead |
| Performance during playback | **No fixed limits.** The user's hardware is the only condition. A user setting, PERFORMANCE, defaults to AUTO and offers selectable levels |
| VLC's code in the installer | Yes, the stock VLC engine ships in the installer, including its DVD decryption library, which STX never uses |
| Test hardware | Phases are proven on the hardware available. Features the hardware lacks are tested as honestly unavailable, not faked |
| Library search | Yes, in Phase 2, with lighter indexing for watched files |
| Order | The seven phases below, in that order; code signing after Phase 7 |

## Calls I made (Rocky can overrule any of these)

- **LibVLCSharp** (VideoLAN's official .NET binding) over hand-written P/Invoke.
- **LibVLC ships inside the installer**, unlike yt-dlp/FFmpeg/Deno, which download on first run (`docs/THIRD_PARTY.md`). This is so the player works offline right after install. `docs/THIRD_PARTY.md` gets a LibVLC row (LGPL-2.1+, dynamically linked, so STX's own code stays MIT), and its "does not include any of the tools below" line gets reworded.
- **Full screen:** double-click the picture or press a FULL pad. Esc or FULL returns to the console.
- **Player controls in hardware style:** prev / PLAY-PAUSE / next / STOP pads, an LED seek bar, a seven-segment time readout, a VOLUME knob, an ENHANCE knob (OFF / SHARPEN / UPSCALE), and audio, subtitle and chapter track pickers.
- **Library OPEN plays inside STX.** Today it hands off to the shell (`src/Stx.ViewModels/LibraryViewModel.cs:47` → `src/Stx.App/Composition.cs:293`).
- **The Library labels each row's origin** (downloaded / opened / watched). Search lands in Phase 2; a filter by origin or type can come later.
- **Watched files are indexed lightly:** path, size, date and container facts on scan. Hashing and Source Truth measurement happen when a file is first selected or played, not for the whole collection up front. Downloads keep today's behavior.
- **First scan of watched folders** runs in the background at low priority, with progress on the Library plate.
- **PERFORMANCE levels** (my naming): AUTO (default; picks from the PC's GPU, CPU and the file), plus ECO, BALANCED and MAX. They decide hardware decoding, how far ENHANCE may go, and decoder threads. AUTO never chooses something the hardware can't keep smooth.
- **The published budgets** in `docs/PERFORMANCE.md:7` (150 MB, 2% CPU, 5% GPU) stay for everything except playback. `PERFORMANCE.md` is reworded to say so, and `tools/Measure-Budgets.ps1` measures only when nothing is playing. I read your "no perf limits" as being about playback, the question asked.
- **If a spike fails,** work stops and the result goes to Rocky before any fallback is chosen, especially for S1, which decides whether the plate layout he picked is possible.
- **"VLC's format list"** means VideoLAN's published feature list for the exact LibVLC version STX ships, frozen in the corpus manifest. Bumping VLC re-runs the corpus.
- **Web-page links keep using the existing capture path** (yt-dlp, with its DRM refusal). LibVLC plays only direct media and stream URLs. Its own site-scraping scripts and network service discovery are switched off, so the no-network-port rule (`README.md:29`) holds.
- **The parity corpus runs as a local gate**, like the screenshot tests. CI runs a small subset that needs no GPU or audio device.
- **Default-player LED** reads the real current default from Windows, never assumes it.
- **Shrink copies are checked** before they're marked done (plays, duration matches, no decode errors). Shrink jobs run through the existing Queue. The Library shows how much space deleting the originals would save. STX never deletes them.
- **Bit-perfect readout** shows what actually reaches the device (for example "24/192 BIT-PERFECT"). If exclusive mode can't be had, STX says so and plays shared.

## Existing code this builds on (read 2026-11-04)

- Rack layout, including the deck's two columns: `src/Stx.App/MainWindow.cs:41-58`. The PLAYER plate goes into the right column above `SourceTruthPanel` (`MainWindow.cs:52`, `:57`).
- Library rows, including FILE MISSING: `src/Stx.ViewModels/LibraryViewModel.cs:15-30`. Commands: `LibraryViewModel.cs:47-50`. Pads: `src/Stx.App/Panels/LibraryPanel.cs:19`.
- Library data: `media_items` and `media_files` (`src/Stx.Data/Migrations.cs:11-23`), through `MediaRepo` (`src/Stx.Data/MediaRepo.cs:7`) and `ILibraryCatalog` (`src/Stx.Core/Queue/IMediaCatalog.cs:20`). Watched and opened files need a migration (origin, and no page URL).
- Presets for the convert pads: `src/Stx.Core/Presets/PresetCatalog.cs:27-33`. Output choices already include Opus: `src/Stx.Core/Audio/Verdict.cs:3`.
- Queue engine, for shrink jobs: `src/Stx.Core/Queue/QueueEngine.cs:10`.
- Source Truth measurement: `src/Stx.Audio/AudioMeasurer.cs:8` (FFmpeg/FFprobe based). View model: `src/Stx.ViewModels/SourceTruthViewModel.cs:12`.
- First-run setup, where the default-player offer goes: `src/Stx.ViewModels/SettingsViewModel.cs:72`.
- Single instance. A second launch only signals the first, with no file path (`src/Stx.App/Platform/SingleInstance.cs:23`, `src/Stx.App/App.xaml.cs`). It must carry the opened file's path.
- Installer is per-user (`tools/installer/STXDownloader.iss:37`, `PrivilegesRequired=lowest`), with a `[Registry]` section (`:80`). File-type registration goes there under HKCU.
- Reduce motion: `src/Stx.App/Primitives/LookState.cs:19`. The player must respect it the way the tutorial does.

## The seven phases

Nothing below is implemented yet. Every phase is a requirement to build. Each phase ships working, tested and documented (README lines with anchors) before the next starts.

### Phase 1 — Player core
The PLAYER plate (layout B) with full screen and all controls above, and the PERFORMANCE setting (AUTO by default). Library OPEN plays in the plate. Source Truth reads the playing file. A file passed on the command line (Explorer "Open with", or a second launch) opens the console and plays it.
**Parity corpus:** a test set of sample files covering VLC's format list (containers, video and audio codecs, subtitles, legacy formats). The builder generates what FFmpeg can make and sources the rest from public-domain samples, all recorded in a manifest. An automated test plays each file through STX and checks frames or samples actually decode.
**Done when:** every corpus file plays, OPEN plays inside STX, Explorer-opened files play, and full screen works.

### Phase 2 — Library as the whole collection
Videos and Music are watched by default; folders can be added and removed in Settings. Opened files join the Library. New files appear live. Moved or deleted files show FILE MISSING (existing behavior). Every row gets OPEN, FIND FILE, convert pads and Source Truth.
**Search** across title, filename and folder, built for tens of thousands of rows. The Library list virtualizes instead of rebuilding every row (today `Refresh()` at `LibraryViewModel.cs:62-73` clears and rebuilds every row, and the list is capped at 300 px, `LibraryPanel.cs:17`).
**Done when:** a scan of a large folder doesn't block the UI, search returns results while typing on a large library, and adding, renaming and deleting files are reflected correctly.

### Phase 3 — Default player
The installer registers STX for every format the corpus proves it plays, per user (ProgIDs, Capabilities, RegisteredApplications). First-run setup asks once, "Make STX your player for videos and music?" Yes opens Windows Default Apps (A6); Not now never asks again. Settings gets a DEFAULT PLAYER switch and an LED showing the real state.
**Done when:** after choosing STX in Windows, double-clicking any registered type opens the console and plays it, including while STX is already running.

### Phase 4 — Discs and network streams
Unencrypted DVD and Blu-ray (discs, folders, ISO images), network stream URLs, and internet radio. **Encrypted discs are refused by STX itself before LibVLC sees them**, with the same plain wording as the capture side's DRM refusal (README: "When the app sees DRM, it says so and stops").
**Done when:** unencrypted samples play, and an encrypted sample is refused with no playback attempted. The refusal is tested on the detection logic, so no encrypted media goes in the repo.

### Phase 5 — Bit-perfect audio
To build (not yet implemented): a BIT-PERFECT setting for **music and movies alike**: exclusive device control at the file's own format, or shared with Windows. For movies this includes keeping sound in sync with LibVLC's picture, which is the hardest part of this phase. While exclusive mode is on, other apps can't make sound, and the setting says so. A readout shows what reaches the device. If exclusive mode can't be had, STX says so and falls back to shared. Approach per spike S5 (A4).
**Done when:** a 24/96 and a 16/44.1 FLAC each reach the device unaltered, and a movie's soundtrack does too while staying in sync. This is checked by the device format readout and, where the available hardware allows, a loopback bit comparison. Shared mode still works.

### Phase 6 — ENHANCE
The ENHANCE knob: OFF / SHARPEN / UPSCALE. UPSCALE uses the GPU's super-resolution where available (A3), SHARPEN is a software filter, and the knob says which is active. It only changes the picture on screen.
**Done when:** SHARPEN visibly applies, and UPSCALE either applies or, on hardware without GPU super-resolution (likely the reference UHD 620), is shown as unavailable, not faked. Both are proven on the hardware available.

### Phase 7 — Shrink
New presets for modern codecs (AV1 and HEVC for video, Opus for audio), available from the convert pads for one file and as a folder job. Copies are written next to originals and checked before being marked done. Source Truth labels them lossy and never "improved." Space saved is shown.
**Done when:** a folder job shrinks every file, originals are byte-identical before and after, and a failed encode leaves no half-written copy marked done.

## Constraints

- Every existing rule stays: MIT for STX's own code, no network port, secrets in memory only, reduce motion honored, 44 px targets, WCAG AA, and both look modes. The idle and capture performance budgets stay too. Playback has none (Rocky).
- Every new README claim carries a `path:line` anchor (project rule).
- The installer gets bigger by about the size of LibVLC (A2). This is accepted, because parity outranks size.

## Left to the builder

- Exact plate layout and pad labels, within the console's existing primitives.
- The schema migration's exact shape.
- How the watched-folder scanner batches and debounces changes.
- Corpus sourcing, as long as every file's license allows it in the repo or the corpus is fetched by a script.
