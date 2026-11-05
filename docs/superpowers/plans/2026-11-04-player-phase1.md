# Player, Phase 1 (with spike S1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** STX plays media in a PLAYER plate beside the Library, on VLC's engine, with full screen. Library OPEN plays inside STX. Files opened from Explorer play in the console, and a format corpus proves parity with VLC.

**Architecture:** A pure `IPlaybackEngine` interface in `Stx.Core` holds what the app needs from a player. A new `Stx.Playback` project implements it on LibVLCSharp. `PlayerViewModel` (`Stx.ViewModels`) drives it and is tested against a fake engine. `PlayerPanel` (`Stx.App`) is built from the console's existing primitives around a `VideoSurface` whose implementation spike S1 chooses.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK 2.6.4, unpackaged, self-contained), CommunityToolkit.Mvvm 8.4, LibVLCSharp 3.x with VideoLAN.LibVLC.Windows (exact versions pinned by S1), xUnit, FFmpeg (already downloaded by the app) for corpus generation.

**Spec:** `docs/superpowers/specs/2026-11-04-vlc-player-design.md` (approved, commit `7e612df`). Phases 2–7 get their own plans after this one ships.

## Global Constraints

- **Task 1 is a hard stop.** Its result goes to Rocky before Task 2 starts. If S1 fails, no fallback is chosen without him (spec, "Calls I made").
- Layout B: PLAYER plate in the deck's right column, above Source Truth (spec, Decisions table). Full screen in and out via double-click on the picture, or a FULL pad; Esc also exits.
- The PERFORMANCE setting defaults to AUTO, with ECO, BALANCED and MAX (spec). Playback has no fixed performance limits. The idle and capture budgets in `docs/PERFORMANCE.md:7` still apply when nothing is playing.
- LibVLC runs with its Lua site scripts off and no service discovery, so web-page links keep using the existing capture path and no network port is opened (`README.md:29`).
- The parity corpus is pinned to VideoLAN's published feature list for the exact LibVLC version shipped. It runs as a local gate; CI runs only the tests that need no GPU or audio device (`.github/workflows/build.yml:21-27`).
- Every existing rule holds: MIT for STX's own code (LibVLC is LGPL-2.1+, dynamically linked), secrets in memory only, reduce motion honored, 44 px targets, WCAG AA, dark and light looks, and README claims anchored with `path:line`.
- Opening a file from Explorer, or pressing OPEN, is an explicit request, so playback starts at once even with reduce motion on. Decorative motion on the plate still follows `LookState.ReduceMotion` (`src/Stx.App/Primitives/LookState.cs:19`).

## S1 findings folded into Phase 1

Spike S1 (`docs/superpowers/spikes/2026-11-04-s1-libvlc-winui.md`) passed on route (a): `LibVLCSharp.WinUI` 3.10.2 with `VideoLAN.LibVLC.Windows` 3.0.24. Rocky: "add the findings to phase one. Zero deferrals." Every finding and every item the report marked "Not proven" now has an owning task and a test:

| # | Finding | Owner | Pinned by |
|---|---|---|---|
| F1 | The `VideoView` is created once and never changes parent; a new parent re-creates the swap chain and restarts playback at 0:00 | Task 5 | `PlayerPlaybackTests.Full_screen_keeps_playing_without_a_restart` |
| F2 | LibVLC for video must be created from the view's `SwapChainOptions`, after its `Initialized` event | Task 2 | `LibVlcOptionsTests.Swap_chain_options_are_appended`, `LibVlcEngineTests.Open_without_a_surface_waits_for_one` |
| F3 | The package copies x86 and ARM64 LibVLC (202 MB) and the Lua plugin | Task 8 | `PlaybackShipsTests.Only_x64_libvlc_ships_and_no_lua` |
| F4 | `LibVLCSharp.WinUI` depends on SharpDX 4.2.0, unmaintained since 2019 | Task 8 | `docs/THIRD_PARTY.md` rows for LibVLCSharp and SharpDX, with the risk stated |
| F5 | `wasapi: cannot negotiate audio format (error 0x88890008)` at every start | Task 2 | root cause found and fixed; `LibVlcEngineTests.Audio_output_opens_without_a_format_error` (Device) |
| F6 | `main: Failed to set on top` at every start | Task 5 | root cause found and fixed; `PlayerPlaybackTests.The_video_output_logs_no_errors` |
| F7 | The faceplate's screws and border show in full screen | Task 5 | a full-screen look with no screws; baseline `player-full-dark.png`, approved by Rocky |
| N1 | A real mouse click on a pad over the video was not made | Task 5 | `PlayerPlaybackTests.A_real_click_on_a_pad_over_the_video_lands` (window made topmost; the test checks `WindowFromPoint` is the STX window before it clicks, so a click can never land elsewhere) |
| N2 | Lost pictures are LibVLC's count only; compositor drops were not measured | Task 5 | `PlayerPlaybackTests.Full_screen_4K60_loses_under_1_percent` (LibVLC lost pictures and DWM's dropped frames, both under 1% over 60 s) |
| N3 | Only synthetic 8-bit SDR was played | Task 3 | the parity corpus, which includes 10-bit HEVC, HDR10, AV1 and VP9 entries |
| N4 | Audio was counted, not heard | Task 5 | `PlayerPlaybackTests.Audio_reaches_the_output_device` (the default render endpoint's peak meter rises above zero while `player-audio` plays) |
| P1 | Task 7 rebuilt the engine when PERFORMANCE changes, but Task 5's panel holds the engine it was given | Task 2 | `LibVlcEngine.Profile` is settable and applies to the next `Open` as media options; nothing is rebuilt. `LibVlcOptionsTests.Media_options_carry_the_profile`, and `PlayerPlaybackTests.Eco_decodes_in_software_and_balanced_on_the_gpu` (Task 5) |

Tests marked Device or run in `Stx.VisualTests` need a desktop session with a GPU and an audio device; they are a local gate, like the screenshot tests.

## Review Focus

1. **Unicode, spaces and long paths** (for example `D:\Фильмы\My Movie (2019).mkv`) opened from Explorer play. Pinned by `LaunchArgsTests.Unicode_and_spaced_paths_survive` (Task 6) and `LibVlcEngineTests.Opens_a_unicode_path` (Task 2).
2. **A corrupt or truncated file** shows a plain problem line on the plate, and the rest of the console keeps working. Pinned by `LibVlcEngineTests.Truncated_file_ends_in_failed_state` (Task 2) and `PlayerViewModelTests.Engine_failure_shows_a_problem` (Task 4).
3. **An audio-only file** (most of the Music folder) shows its title and a still glass display, never a black void that looks broken. Pinned by `PlayerViewModelTests.Audio_only_media_is_marked_as_such` (Task 4) and the `player-audio` gallery baseline (Task 5).
4. **Opening a second file while one plays**, from Explorer or OPEN, replaces it with one decoder. Pinned by `PlayerViewModelTests.Playing_again_replaces_the_current_media` (Task 4).
5. **Closing the window or quitting while playing** releases the decoder, and a tray-hidden window doesn't keep decoding video. Pinned by `PlayerViewModelTests.Dispose_stops_the_engine` (Task 4) and the activity registration in Task 7.

---

### Task 1: Spike S1 — can LibVLC draw inside a WinUI 3 plate? (throwaway, HARD STOP)

**Files:** branch `spike/s1-libvlc-winui` only. Never merged. The one file that survives is the report, `docs/superpowers/spikes/2026-11-04-s1-libvlc-winui.md`, committed to main.

- [ ] **Step 1:** On the spike branch, add LibVLCSharp and VideoLAN.LibVLC.Windows to `src/Stx.App/Stx.App.csproj`. Find which rendering route works in an unpackaged, self-contained WinUI 3 app (`Stx.App.csproj:9-10`). Try in order, stopping at the first that passes: (a) VideoLAN's WinUI integration package, if one exists for LibVLCSharp 3.x; (b) a `SwapChainPanel` fed by LibVLC's D3D11 output callbacks; (c) a child HWND hosted in the plate.
- [ ] **Step 2:** Build a gallery page `--gallery=player-spike` (`src/Stx.App/Gallery/GalleryPages.cs`) that puts the video inside a `Faceplate` inside a `ScrollViewer`, as in the real rack (`src/Stx.App/MainWindow.cs:62`).
- [ ] **Step 3:** Record pass or fail, with evidence (screenshots and LibVLC statistics), for each check:
  1. the picture draws inside the plate, not in a floating window;
  2. it scrolls and clips with the rack;
  3. a Pad over or next to the picture stays visible and clickable;
  4. 1080p30 H.264 and 4K60 HEVC play on the reference i5-8350U / UHD 620 with lost pictures under 1% over 60 s;
  5. full screen in and out works;
  6. the installed size of LibVLC's x64 files is measured in MB (settles A2);
  7. the exact LibVLCSharp and LibVLC versions, and whether `--no-lua` is accepted.
- [ ] **Step 4:** Write the report with those results and the recommended route. Commit only the report, to main. **Stop and report to Rocky.** Task 2 starts only after he says go. If a check failed, the report lists the options and nothing else is built.

### Task 2: Playback engine interface and the LibVLC engine

**Files:**
- Create: `src/Stx.Core/Playback/IPlaybackEngine.cs`, `src/Stx.Core/Playback/PerformanceProfile.cs`
- Create: `src/Stx.Playback/Stx.Playback.csproj` (net10.0-windows, x64), `src/Stx.Playback/LibVlcEngine.cs`, `src/Stx.Playback/LibVlcOptions.cs`
- Create: `tests/Stx.Playback.Tests/` with `LibVlcEngineTests.cs` and `LibVlcOptionsTests.cs`, plus `tests/fixtures/playback/` holding `tiny.mp4` (2 s, 320x180, H.264+AAC), `tiny-audio.flac` (2 s, 16/44.1), `truncated.mp4` (first 40% of `tiny.mp4`) and `Фильм тест.mp4` (a copy of tiny.mp4), each under 100 KB and made by FFmpeg with the commands written in `tests/fixtures/playback/README.md`
- Modify: `STXDownloader.sln`, `.github/workflows/build.yml:24` (add `'Playback'` to the project list)

**Interfaces:**
- Produces, in `Stx.Core.Playback`:
  - `enum PlaybackState { Idle, Opening, Playing, Paused, Ended, Failed }`
  - `sealed record MediaTrack(int Id, string Name)`
  - `interface IPlaybackEngine : IDisposable` with:
    - `PlaybackState State`, `TimeSpan Position`, `TimeSpan Length`
    - `bool HasVideo`, `string? Error`
    - `int Volume { get; set; }` (0–100)
    - `IReadOnlyList<MediaTrack> AudioTracks`, `SubtitleTracks`, `Chapters`
    - `void Open(string path)` (starts playing), `void TogglePause()`, `void Stop()`, `void Seek(TimeSpan to)`
    - `void SelectAudio(int id)`, `void SelectSubtitle(int id)`, `void SelectChapter(int id)`
    - `event EventHandler? Changed` (may fire on any thread)
  - `enum PlaybackPerformance { Auto, Eco, Balanced, Max }`, `enum EnhanceLevel { Off, Sharpen, Upscale }`
  - `sealed record HardwareFacts(int LogicalCores, bool GpuSuperResolution)`
  - `sealed record PerformanceProfile(bool HardwareDecoding, int DecoderThreads, EnhanceLevel EnhanceCeiling)` with `static PerformanceProfile For(PlaybackPerformance level, HardwareFacts hw)`
- Produces, in `Stx.Playback`:
  - `static class LibVlcOptions { static string[] For(bool headless, IReadOnlyList<string>? swapChain = null); static string[] ForMedia(PerformanceProfile profile) }` (P1: the profile travels with each media, so it can change without a new LibVLC)
  - `sealed class LibVlcEngine(PerformanceProfile profile, bool headless = false) : IPlaybackEngine` with:
    - `PerformanceProfile Profile { get; set; }`, used from the next `Open` (P1)
    - `void UseSwapChain(IReadOnlyList<string> swapChainOptions)`, called by Task 5's `VideoSurface` from the view's `Initialized` event. A non-headless engine creates its LibVLC only then, and an `Open` that arrives first is held and played when the swap chain comes (F2)
    - `LibVLCSharp.Shared.MediaPlayer? Player` and `event EventHandler? PlayerCreated`, for the `VideoView`
    - `string Decoder` (`"d3d11va"`, `"software"`, or `""` before a picture is decoded), read from LibVLC's log

- [ ] **Step 1: Write the failing tests.**
  - `PerformanceProfileTests`, in `tests/Stx.Core.Tests/`. These values are my calls, recorded in the spec:
    - `For(Eco, new(8,false))` == `(true, 2, Off)`
    - `For(Balanced, new(8,false))` == `(true, 4, Sharpen)`
    - `For(Max, new(8,true))` == `(true, 8, Upscale)`
    - `For(Max, new(8,false)).EnhanceCeiling` == `Sharpen`, never Upscale without the GPU
    - `For(Auto, new(2,false))` == `For(Eco, …)`
    - `For(Auto, new(8,false))` == `For(Balanced, …)`
    - `For(Auto, new(8,true))` == `For(Max, …)`
    - Balanced uses at least 2 threads even when there's only 1 core.
  - `LibVlcOptionsTests`:
    - the result contains `--no-lua`, and contains nothing starting with `--sd=` or `--sout`;
    - `ForMedia`: `:avcodec-hw=` is `d3d11va` when `HardwareDecoding` and `none` otherwise, and `:avcodec-threads=` equals `DecoderThreads` (`Media_options_carry_the_profile`);
    - `headless: true` adds `--vout=dummy` and `--aout=dummy`;
    - `Swap_chain_options_are_appended`: the swap chain options appear in the result unchanged.
  - `LibVlcEngineTests` (headless):
    - `Plays_a_tiny_file`: within 5 s, State reaches Playing and Position moves past 0.5 s.
    - `Reports_audio_only`: `tiny-audio.flac` gives `HasVideo == false`.
    - `Truncated_file_ends_in_failed_state`: State becomes Failed or Ended within 5 s, `Error` is set if Failed, and nothing throws.
    - `Opens_a_unicode_path`: `Фильм тест.mp4` reaches Playing.
    - `Dispose_releases_the_player`: after Dispose, State is Idle and a second Dispose doesn't throw.
    - `Open_without_a_surface_waits_for_one`: a non-headless engine with no swap chain, after `Open`, has State Opening, `Player` null, and has opened no window of its own.
    - `Audio_output_opens_without_a_format_error` (`[Trait("Category","Device")]`, real audio output, dummy video): playing `tiny.mp4` and `tiny-audio.flac` logs no `cannot negotiate audio format` (F5).
- [ ] **Step 2:** Run `dotnet test tests/Stx.Core.Tests tests/Stx.Playback.Tests -c Release -p:Platform=x64`. Expected: FAIL to compile, because the types aren't defined yet.
- [ ] **Step 3:** Implement the types above. `LibVlcEngine` raises `Changed` from LibVLC's MediaPlayer events and maps LibVLC's states onto `PlaybackState`. For F5, find the root cause of `0x88890008` (AUDCLNT_E_UNSUPPORTED_FORMAT) from LibVLC's wasapi debug log before changing anything, and fix the cause; record it in the ledger.
- [ ] **Step 4:** Run the same command. Expected: PASS. Break-check each engine test (for example, make `Open` do nothing and confirm `Plays_a_tiny_file` fails), then restore.
- [ ] **Step 5:** Commit: `feat: playback engine interface and LibVLC engine`.

### Task 3: Parity corpus

**Files:**
- Create: `tests/corpus/vlc-features.json`, a transcription of VideoLAN's feature list for the LibVLC version from S1. It has one entry per listed input format, video codec, audio codec and subtitle format, plus the source URL and the date it was read.
- Create: `tests/corpus/manifest.json`. Each entry is `{ "file", "feature", "source": "generated"|"fetched", "url"?, "sha256", "license"? }`.
- Create: `tools/corpus/Make-Corpus.ps1 -Ffmpeg <path>`. It generates what FFmpeg can encode, and downloads fetched files checking each against its sha256. Everything goes into `tests/corpus/files/`, which is git-ignored.
- Create: `tests/Stx.Playback.Tests/CorpusManifestTests.cs` (runs in CI) and `tests/Stx.Playback.Tests/CorpusTests.cs` (`[Trait("Category","Corpus")]`, local gate).
- Modify: `.gitignore` (add `tests/corpus/files/`), `.github/workflows/build.yml` (run Playback tests with `--filter "Category!=Corpus"`)

**Interfaces:** consumes `LibVlcEngine(profile, headless: true)` from Task 2.

- [ ] **Step 1: Write the failing tests.**
  - `CorpusManifestTests.Every_feature_has_a_corpus_file`: every `vlc-features.json` entry is named by at least one manifest entry.
  - `CorpusManifestTests.Fetched_files_are_pinned`: every fetched entry has a url, a 64-hex sha256 and a license.
  - `CorpusManifestTests.Real_world_profiles_are_present` (N3): the manifest has at least one entry each for 10-bit HEVC (Main10), HDR10 (PQ, BT.2020), AV1 and VP9.
  - `CorpusTests.Every_corpus_file_plays`: for each manifest file, a headless `LibVlcEngine` reaches Playing and Position passes 0.5 s within 10 s. Every failure is listed in the assertion message. If `tests/corpus/files/` is missing, the test **fails** with "Run tools/corpus/Make-Corpus.ps1 first". It is never skipped.
- [ ] **Step 2:** Run `dotnet test tests/Stx.Playback.Tests -c Release -p:Platform=x64`. Expected: FAIL, because the manifest and feature list don't exist yet.
- [ ] **Step 3:** Transcribe the feature list and write the manifest and the script. Any format with no lawful public sample is recorded in `vlc-features.json` as `"untestable": "<reason>"`, which the manifest test accepts. Those reasons are listed for Rocky in Task 8's report.
- [ ] **Step 4:** Run `tools/corpus/Make-Corpus.ps1`, then the full Playback tests including the Corpus category. Expected: PASS. Every file that fails to play is a parity bug: stop and report it, never drop the file from the manifest.
- [ ] **Step 5:** Commit: `test: VLC parity corpus and manifest`.

### Task 4: PlayerViewModel and the PERFORMANCE setting

**Files:**
- Create: `src/Stx.ViewModels/PlayerViewModel.cs`, `tests/Stx.App.Tests/PlayerViewModelTests.cs`, and a `FakeEngine : IPlaybackEngine` in the same test file
- Modify: `src/Stx.Core/Settings/AppSettings.cs:11-31` (add `PlaybackPerformance Performance { get; init; } = PlaybackPerformance.Auto;`, and make `Sanitized()` reset undefined enum values to Auto)
- Modify: `src/Stx.ViewModels/SettingsViewModel.cs:20` area (a `Performance` property, same pattern as `Download`, plus `static IReadOnlyList<string> PerformanceLabels = ["AUTO","ECO","BALANCED","MAX"]`)

**Interfaces:**
- Consumes `IPlaybackEngine` and `PlaybackState` from Task 2.
- Produces `sealed class PlayerViewModel(IPlaybackEngine engine, Action<Action> onUi) : ObservableObject, IDisposable` with:
  - `void Play(string path, string title, IReadOnlyList<(string Path, string Title)> list)`
  - `string Title`, `bool HasMedia`, `bool IsPlaying`, `bool IsAudioOnly`
  - `string TimeText` (`HH:MM:SS` of Position), `double Progress` (0–1)
  - `int Volume { get; set; }`, `bool IsFullScreen { get; set; }`, `string Problem` (upper case, empty when fine)
  - `IRelayCommand PlayPauseCommand, StopCommand, PreviousCommand, NextCommand, FullScreenCommand`
  - `IRelayCommand<double> SeekCommand` (fraction 0–1)

- [ ] **Step 1: Write the failing tests** (FakeEngine records calls and lets a test set State, Position and Length):
  - `Play_opens_the_path_and_shows_its_title`
  - `Playing_again_replaces_the_current_media`: the second `Play` calls `engine.Open` once more with no `Dispose` in between.
  - `TimeText_reads_hours_minutes_seconds`: Position 1:02:03 gives `"01:02:03"`.
  - `Progress_is_position_over_length`, with Length 0 giving 0, never NaN.
  - `Previous_restarts_after_three_seconds`: at Position 5 s, Previous seeks to 0; at 2 s, Previous opens the previous list item. This is my call.
  - `Next_on_the_last_item_stops`
  - `SeekCommand_maps_fraction_to_time`, clamped to 0–1.
  - `Engine_failure_shows_a_problem`: State Failed gives `Problem == "THIS FILE COULDN'T BE PLAYED."`.
  - `Audio_only_media_is_marked_as_such`
  - `Dispose_stops_the_engine`
  - `Engine_events_reach_the_ui_through_onUi`: `Changed` raised on another thread updates properties only inside the `onUi` callback.
  - `SettingsTests.Performance_defaults_to_auto_and_survives_a_bad_value`: a file holding `"Performance": 99` loads as Auto.
- [ ] **Step 2:** Run `dotnet test tests/Stx.App.Tests -c Release -p:Platform=x64 --filter "PlayerViewModelTests|SettingsTests"`. Expected: FAIL to compile.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the same command. Expected: PASS. Break-check `Previous_restarts_after_three_seconds` and `Engine_failure_shows_a_problem`.
- [ ] **Step 5:** Commit: `feat: player view model and PERFORMANCE setting`.

### Task 5: PlayerPanel, VideoSurface and full screen (route chosen by S1)

**Files:**
- Create: `src/Stx.App/Primitives/VideoSurface.cs`, implementing the route S1's report recommends, with `void Attach(Stx.Playback.LibVlcEngine engine)` and `void Detach()`
- Create: `src/Stx.App/Panels/PlayerPanel.cs`: a `Faceplate` with `PanelName = "player"`, an EngravedLabel "PLAYER", the VideoSurface (16:9), pads `PREV`, `PLAY`/`PAUSE`, `NEXT`, `STOP`, `FULL`, a bargraph seek bar, a seven-seg `TimeText`, and a `Rotary` VOLUME, all `.Captioned()` per `GuideTests.Every_knob_on_a_plate_shows_its_caption`. Audio, subtitle and chapter pickers appear only when the engine lists more than one choice. Audio-only media shows the title on a cyan Display instead of the surface.
- Modify: `src/Stx.App/Gallery/GalleryPages.cs`, adding `player` (playing `tests/fixtures/playback/tiny.mp4`) and `player-audio` (playing `tiny-audio.flac`)
- Modify: `tests/Stx.VisualTests/AccessibilityScanTests.cs:15-21`, adding `("player", 6)`
- Create baselines: `tests/baselines/player-{dark,light}.png` and `player-audio-{dark,light}.png`, approved by Rocky before they're committed
- Create: `tests/Stx.VisualTests/PlayerPlaybackTests.cs`

**Interfaces:** consumes `PlayerViewModel` (Task 4) and `LibVlcEngine` (Task 2). Produces `sealed class PlayerPanel(PlayerViewModel player, LibVlcEngine engine) : ContentControl`.

- [ ] **Step 1: Write the failing tests.**
  - `PlayerPlaybackTests.The_player_shows_moving_video`: the gallery `player` is captured at 2 s and at 5 s, and the picture region differs by more than 5% (same method as `TutorialPlaybackTests.Changed`).
  - `PlayerPlaybackTests.Full_screen_fills_the_display_and_esc_returns`: through UI Automation, Invoke `FULL`; the window bounds equal the monitor bounds; send Esc; the bounds return.
  - `PlayerPlaybackTests.Full_screen_keeps_playing_without_a_restart` (F1): the played time read before FULL is not less than the time read after it, and the gallery's log shows the view initialized once.
  - `PlayerPlaybackTests.A_real_click_on_a_pad_over_the_video_lands` (N1): the window is made topmost; the test checks `WindowFromPoint` at the pad's centre is the STX window, fails without clicking if not, then clicks with real input; the pad's click count goes up.
  - `PlayerPlaybackTests.Full_screen_4K60_loses_under_1_percent` (N2): a 4K60 HEVC file made by FFmpeg plays full screen for 60 s; LibVLC's lost pictures and DWM's dropped frames (`DwmGetCompositionTimingInfo`) are each under 1%.
  - `PlayerPlaybackTests.Audio_reaches_the_output_device` (N4): while `player-audio` plays, the default render endpoint's `IAudioMeterInformation` peak rises above 0.
  - `PlayerPlaybackTests.The_video_output_logs_no_errors` (F6): a run of `player` logs no LibVLC error, `Failed to set on top` included.
  - `PlayerPlaybackTests.Eco_decodes_in_software_and_balanced_on_the_gpu` (P1): `engine.Decoder` is `software` under ECO's profile with hardware decoding off, and `d3d11va` under BALANCED.
  - The accessibility scan case for `player`.
- [ ] **Step 2:** Run `dotnet test tests/Stx.VisualTests -c Release -p:Platform=x64 --filter "PlayerPlaybackTests|AccessibilityScanTests"` after building the app. Expected: FAIL, because there's no gallery `player` yet.
- [ ] **Step 3:** Implement. Full screen uses `AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen)` and shows only the VideoSurface and a control strip. Esc or FULL restores the overlapped presenter and the rack. Full screen happens in place: the `VideoView` never changes parent; the rest of the rack folds away and the plate switches to a full-screen look with no screws or border (F1, F7). For F6, find the root cause of `Failed to set on top` from LibVLC's debug log and fix it; record it in the ledger.
- [ ] **Step 4:** Run the same command. Expected: PASS. Capture the five baselines (the four above and `player-full-dark.png`) with `tools/Capture-Gallery.ps1` and show them to Rocky. Commit them only after he approves.
- [ ] **Step 5:** Commit: `feat: PLAYER plate with full screen`.

### Task 6: Opening files from Explorer and from a second launch

**Files:**
- Create: `src/Stx.Core/Playback/LaunchArgs.cs` with `static string? FileToOpen(IReadOnlyList<string> args, Func<string,bool> exists)`
- Create: `src/Stx.Bridge/OpenRequests.cs` with `static Task<bool> SendAsync(string pipeName, string path, TimeSpan timeout)` and `sealed class OpenRequestServer(string pipeName, Action<string> onPath) : IDisposable`. It's a per-user named pipe with the same access rules as `src/Stx.Bridge/PipeServer.cs:17`. No network port.
- Create: `src/Stx.ViewModels/OpenedFiles.cs` with `sealed class OpenedFiles(IMediaCatalog media, LibraryViewModel library)` and `LibraryRowViewModel Adopt(string path)`. A path already in the Library returns that row (case-insensitive). Otherwise it calls `AddItem(null, null, <file name without extension>)` and `AddFile(id, path, null, SourceViewModel.OriginalFormat)`, refreshes, and returns the new row.
- Modify: `src/Stx.App/Platform/SingleInstance.cs:14-24`. A second launch with a file sends it through `OpenRequests` and exits; with no file it keeps today's "show" signal.
- Modify: `src/Stx.App/App.xaml.cs` to pass `LaunchArgs.FileToOpen` through.
- Test: `tests/Stx.Core.Tests/LaunchArgsTests.cs`, `tests/Stx.Bridge.Tests/OpenRequestsTests.cs`, `tests/Stx.App.Tests/OpenedFilesTests.cs`

**Interfaces:** produces `MainWindow.OpenFromOutside(string path)` for Task 7, which adopts the file, selects it, and plays it.

- [ ] **Step 1: Write the failing tests.**
  - `LaunchArgsTests`:
    - the first existing non-flag argument wins;
    - `--gallery=x` and `--hidden` are ignored;
    - a missing file gives null;
    - `Unicode_and_spaced_paths_survive`.
  - `OpenRequestsTests`:
    - a sent path arrives once, unchanged;
    - `SendAsync` returns false within its timeout when no server is listening;
    - a 64 KB message is rejected and the server keeps listening.
  - `OpenedFilesTests`:
    - adopting a new path adds exactly one item and file;
    - adopting the same path again, with different case, adds nothing;
    - the adopted row's title is the file name without extension.
- [ ] **Step 2:** Run the three test projects filtered to these classes. Expected: FAIL to compile.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run them. Expected: PASS. Break-check the case-insensitive dedupe.
- [ ] **Step 5:** Commit: `feat: open files from Explorer and second launches`.

### Task 7: Wire the player into the rack

**Files:**
- Modify: `src/Stx.App/MainWindow.cs:41-58`. The right column becomes a StackPanel of `PlayerPanel` then `truth`. Add `OpenFromOutside(string path)`. Register the player with `_app.Activity` (pattern at `MainWindow.cs:66`), so video decoding stops while the window is hidden to the tray and audio keeps playing.
- Modify: `src/Stx.App/Composition.cs:61-63`. Build `Player = new PlayerViewModel(new LibVlcEngine(PerformanceProfile.For(Settings.Current.Performance, HardwareProbe.Detect())), …)`, and when Performance changes, `Settings.Changed` sets the engine's `Profile`, which applies from the next `Open` (P1; the engine is never rebuilt). Add `Play(LibraryRowViewModel row)`, which calls `Select(row)` and then `Player.Play(row.Path, row.Title, <library rows in order>)`.
- Modify: `src/Stx.ViewModels/LibraryViewModel.cs:41-47`. Add a constructor parameter `Action<LibraryRowViewModel> play`. `OpenCommand` calls it instead of `_shell.OpenFile`.
- Create: `static class HardwareProbe { static HardwareFacts Detect() }` in `src/Stx.Playback/HardwareProbe.cs`. Logical cores from `Environment.ProcessorCount`; `GpuSuperResolution` is false until Phase 6 detects it.
- Modify: `src/Stx.App/Panels/SettingsPanel.cs`, adding a PERFORMANCE rotary with `PerformanceLabels`, `.Captioned()`.
- Modify: `src/Stx.ViewModels/GuideContent.cs`. Add a `"Player"` section whose lines name PLAY, STOP, FULL and PERFORMANCE, each with its code anchor in a comment like the existing sections. Add `"Player"` to `GuideTests.The_guide_covers_each_plate_on_the_console` (`tests/Stx.App.Tests/GuideTests.cs:45`).
- Test: `tests/Stx.App.Tests/SourceTruthTests.cs` (existing LibraryViewModel fixture at `:220-233`)

- [ ] **Step 1: Write the failing tests.**
  - `Open_plays_inside_STX_and_never_hands_off`: OPEN on a row calls `play` with that row, and `ShellSpy` records no OpenFile.
  - `Open_is_disabled_for_a_missing_file`: the existing `CanOpen` rule, now through `play`.
  - The guide test with "Player" added.
- [ ] **Step 2:** Run `dotnet test tests/Stx.App.Tests -c Release -p:Platform=x64`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run the full suite (`dotnet test STXDownloader.sln -c Release -p:Platform=x64`). Expected: all pass. Update the `console` and `settings` baselines only with Rocky's approval of the new captures. Launch the real app, OPEN a library row, and capture it playing.
- [ ] **Step 5:** Commit: `feat: player in the rack; OPEN plays inside STX`.

### Task 8: Ship it, document it, and the Phase 1 report

**Files:**
- Modify: `tools/Publish.ps1:44`, adding `libvlc.dll`, `libvlccore.dll` and `plugins\` to the required list. Modify `src/Stx.App/Stx.App.csproj` so only x64 LibVLC is copied (the package's `VlcWindowsX86IncludeFiles` / `VlcWindowsArm64IncludeFiles` emptied) and `plugins\lua` is excluded (`VlcWindowsX64ExcludeFiles`) (F3).
- Create: `tests/Stx.App.Tests/PlaybackShipsTests.cs`, following `GuideTests.The_tutorial_ships_beside_the_app…` (`tests/Stx.App.Tests/GuideTests.cs:109-114`)
- Modify: `docs/THIRD_PARTY.md`:
  - add a LibVLC row (version, LGPL-2.1+, dynamically linked, VideoLAN source link);
  - reword the opening "does not include any of the tools below";
  - add one sentence: the bundled VLC engine contains libdvdcss, and STX never uses it;
  - add LibVLCSharp (LGPL-2.1+) and SharpDX 4.2.0 (MIT) rows, stating that SharpDX has been unmaintained since 2019 and is used only through LibVLCSharp.WinUI's video view (F4).
- Modify: `docs/PERFORMANCE.md:7`. The budgets apply when nothing is playing; playback has no fixed limits and follows the PERFORMANCE setting.
- Modify: `README.md`, adding a **Playing** section. Each line is anchored: the PLAYER plate, full screen, OPEN plays inside, Explorer open, PERFORMANCE, and corpus parity (anchored to `tests/corpus/manifest.json`, stating the exact count of features covered and listing any untestable ones). No claim about discs, streams, bit-perfect audio, ENHANCE or shrink. Those are later phases.

- [ ] **Step 1: Write the failing tests** `PlaybackShipsTests.LibVlc_ships_beside_the_app`: `Publish.ps1` names each required LibVLC file, and the Release build output contains `libvlc.dll` and a non-empty `plugins` folder. `PlaybackShipsTests.Only_x64_libvlc_ships_and_no_lua` (F3): the build output has no `libvlc\win-x86` or `libvlc\win-arm64` folder and no `plugins\lua` folder.
- [ ] **Step 2:** Run it. Expected: FAIL.
- [ ] **Step 3:** Make the changes above.
- [ ] **Step 4:** Run the full .NET suite, the extension tests, the Corpus category locally, and `node C:/Users/User/.claude/hooks/verify-sweep.mjs`. Expected: all pass and the sweep is CLEAN. Paste every output into the Phase 1 report, along with the measured installer size change.
- [ ] **Step 5:** Commit: `docs: Phase 1 player shipped`. Then report to Rocky with the scope ledger (DONE / WIRED / TESTED / NOT DONE) before Phase 2's plan is written.
