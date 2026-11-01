# STX Downloader Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a light, MIT-licensed Windows app that saves YouTube and website media, tells the truth about source audio, and looks like premium solid-state hardware.

**Architecture:** Pure .NET libraries hold all behavior and are unit tested. A WinUI 3 app composes equipment primitives on top of them. yt-dlp and FFmpeg run as external processes that are downloaded on first run. A small Chrome extension detects media and hands it off through a native messaging host and a per-user named pipe.

**Tech Stack:** C#, .NET 10 (SDK 10.0.403), WinUI 3, Windows App SDK 2.6.4, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, xUnit, JavaScript for a Manifest V3 extension with `node --test`, Playwright's Chromium for extension checks.

**Spec:** `docs/superpowers/specs/2026-10-31-stx-downloader-design.md`. Visual reference: `docs/design/console-v2.html`. Executors read both.

## Global Constraints

- Windows 10 version 2004 (build 19041) or later, x64.
- MIT license. Clean-room: no code, assets, strings or names from the Store apps or plugin that were studied.
- `docs/analysis/` and `.superpowers/` never enter the repository.
- Tools are not bundled. yt-dlp and FFmpeg download on first run, with checksum verification where the source publishes checksums.
- No usage limits of any kind. No share-to-unlock, rate-to-unlock, cross-promotion or ads. No telemetry. The app contacts only the sites you ask it to and the tool update sources.
- DRM: if a stream is DRM-protected, the app says so and stops.
- Default is always to keep the original audio. Video is never re-encoded by default.
- Cookies, headers, stream tokens and tokenized links are never written to the database or logs.
- Black anodized is the default mode. Brushed aluminum is light mode. Opens in the last chosen mode and ignores the Windows setting.
- Screws: stainless socket-cap hex on the corners of every panel, even small ones.
- Meters and spectrum show real data only.
- Reduce-motion honored, keyboard navigation with visible focus, accessible names on all controls, WCAG AA text in both modes except decorative etching, hit targets at least 44 px, and loading, error, empty and disabled states.
- Budgets on an i5-8350U with integrated graphics: idle or hidden 0% CPU over 30 seconds; UI while animating under 2% of total CPU and 5% GPU, excluding yt-dlp and FFmpeg; app private memory under 150 MB.
- Rendering: static art rendered once into bitmaps, identical parts share bitmaps, and only brightness, position and rotation animate.
- TDD for all code, including styling. Failing test first.
- Every README claim traces to `path:line` of working, reachable code.
- Before each commit: run `/verify` for code changes (not for docs or tests). End commit messages with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Throwaway spike for reference only, not to be copied in as is: `%TEMP%\stxs\MainWindow.xaml.cs` (art and composition approach) and `%TEMP%\stxs\measure.ps1` (measurement). Results are in `docs/analysis/2026-10-31-spike-results.md`. Temp files can vanish; if they have, work from the results doc.
- Keep the repo path short enough for the build tools. The spike failed with `PRI175` under a very long temp path; the current project path has not been tested yet (Task 1 checks it).

## Review Focus

1. Titles with illegal filename characters, emoji, or extreme length, and a target file that already exists: saves under a safe name and never overwrites (Task 12).
2. Output folder gone or disk full mid-download, such as a pulled USB drive: job fails with a plain message, and the partial file is left for retry (Tasks 9, 11).
3. No internet on first run, or a tool download fails: the app still opens, shows the state plainly, and offers retry (Task 10).
4. App killed or machine asleep mid-job: on restart nothing is stuck in Running (Task 8).
5. Playlist with private or deleted entries: those are marked and the rest continue (Task 8).

---

## File Structure

```
STXDownloader.sln
src/Stx.Core/        Pure domain: job states, queue policy, error kinds, verdicts, presets, redaction, filenames
src/Stx.Tools/       ToolManager, YtDlpRunner, FfmpegRunner, ProcessRunner, CredentialFile
src/Stx.Data/        SQLite schema, migrations, repositories
src/Stx.Audio/       Facts from ffprobe, spectrum, loudness, meter timelines
src/Stx.Look/        Pure look logic: tokens, screw layout, strike timeline, detents, press travel
src/Stx.Art/         Procedural pixel renderers that return BGRA buffers. No WinUI references
src/Stx.Bridge/      Message schema and named pipe client and server
src/Stx.NativeHost/  Console exe launched by Chrome; relays stdio to the pipe
src/Stx.App/         WinUI 3 app: Primitives/, Panels/, ViewModels/, Gallery/
extension/           Manifest V3 extension: manifest.json, worker.js, detect.js, popup.*
tests/Stx.*.Tests/   xUnit for each pure library
tests/Stx.VisualTests/  Screenshot comparison, with baselines in tests/baselines/
tools/               Capture-Gallery.ps1, Measure-Budgets.ps1, Register-NativeHost.ps1
```

Pure libraries target `net10.0`. `Stx.App` uses the spike's settings, which are proven: `net10.0-windows10.0.22621.0`, `TargetPlatformMinVersion 10.0.19041.0`, `win-x64`, `UseWinUI`, `WindowsPackageType None`, `WindowsAppSDKSelfContained true`.

---

## Phase 1: Look system

### Task 1: Repo, solution and gallery shell

**Files:** Create `.gitignore`, `LICENSE`, `README.md`, `STXDownloader.sln`, `src/Stx.App/*`, `src/Stx.Look/LookMode.cs`, `tests/Stx.Look.Tests/*`, `tools/Capture-Gallery.ps1`, `tests/Stx.VisualTests/*`

**Interfaces:**
- Produces: `enum LookMode { Dark, Light }`; app args `--gallery=<name> --mode=dark|light --hold`; `ScreenshotDiff.Compare(string actualPng, string baselinePng) -> DiffResult(bool Pass, double ChangedRatio)`

- [ ] **Step 1:** `git init`, then work on branch `build/v1`, not the default branch. `.gitignore` must list `bin/`, `obj/`, `.superpowers/`, `.claude/`, `.playwright-mcp/`, `docs/analysis/`. Confirm with `git check-ignore docs/analysis/2026-10-31-feature-analysis.md`, which should print the path.
- [ ] **Step 2:** Add an MIT `LICENSE` and a README containing only the project name, "Under construction", the DRM-out-of-scope line, and the line that users are responsible for following each site's terms. No feature claims yet.
- [ ] **Step 3: Write failing test** `ScreenshotDiffTests`: identical images pass; one image with 1% of pixels changed by 40 per channel fails; per-channel differences of 4 or less are ignored.
- [ ] **Step 4:** Run `dotnet test tests/Stx.VisualTests`. Expected: FAIL, `ScreenshotDiff` not defined.
- [ ] **Step 5:** Implement `ScreenshotDiff` with tolerance 4 per channel and pass threshold of at most 0.5% changed pixels.
- [ ] **Step 6:** Run the test again. Expected: PASS.
- [ ] **Step 7:** Create the `Stx.App` shell that opens a gallery window for `--gallery=empty`, and `tools/Capture-Gallery.ps1 -Name -Mode -Out`, which launches it, waits 3 seconds, and captures the window to PNG (spike method: top-most, then `CopyFromScreen`).
- [ ] **Step 8:** Run `dotnet build -c Release -p:Platform=x64` from the project path. Expected: `Build succeeded`. If `PRI175` appears, shorten the path and report it instead of working around it silently.
- [ ] **Step 9:** Commit: `chore: scaffold solution, gallery shell, screenshot diff`.

### Task 2: Tokens, faceplate and screws

**Files:** Create `src/Stx.Look/LookTokens.cs`, `src/Stx.Look/ScrewLayout.cs`, `src/Stx.Art/PixelBuffer.cs`, `src/Stx.Art/PlateArt.cs`, `src/Stx.Art/ScrewArt.cs`, `src/Stx.App/Primitives/Faceplate.cs`, `src/Stx.App/Primitives/ArtCache.cs`, matching tests.

**Interfaces:**
- Produces: `LookTokens.For(LookMode) -> LookTokens`; `LookTokens.RequiredKeys`; `ScrewLayout.For(double w, double h) -> IReadOnlyList<ScrewSpot>`; `PlateArt.Render(int w, int h, LookMode) -> PixelBuffer`; `ScrewArt.Render(int size, int rotationDeg) -> PixelBuffer`; `ArtCache.Get(string key, Func<PixelBuffer>) -> WriteableBitmap`; XAML `Faceplate : ContentControl`

- [ ] **Step 1: Write failing tests:**
```csharp
[Fact] public void Screws_on_every_corner_even_small_panels() {
  var s = ScrewLayout.For(60, 40);
  Assert.Equal(4, s.Count);
  Assert.Contains(s, p => p.X == 6 && p.Y == 6);
  Assert.Contains(s, p => p.X == 60 - 24 && p.Y == 40 - 24);
}
[Theory, InlineData(LookMode.Dark), InlineData(LookMode.Light)]
public void Both_modes_supply_every_token(LookMode m) {
  var t = LookTokens.For(m);
  Assert.All(LookTokens.RequiredKeys, k => Assert.False(string.IsNullOrEmpty(t[k])));
}
[Fact] public void Plate_grain_runs_horizontally() {
  var p = PlateArt.Render(400, 200, LookMode.Dark);
  Assert.True(p.MeanAbsDeltaVertical() > p.MeanAbsDeltaHorizontal() * 2);
}
[Fact] public void Art_is_deterministic() =>
  Assert.Equal(PlateArt.Render(64, 64, LookMode.Light).Hash(), PlateArt.Render(64, 64, LookMode.Light).Hash());
```
- [ ] **Step 2:** Run `dotnet test tests/Stx.Look.Tests tests/Stx.Art.Tests`. Expected: FAIL, types not defined.
- [ ] **Step 3:** Implement. Screw is 18 px at offset 6; render at 4x. Plate uses seeded row noise blurred along x, per mockup v2. Rotation per screw comes from a stable hash of panel name, never `Random` unseeded, so baselines hold. Colors come from mockup v2's CSS variables.
- [ ] **Step 4:** Run the tests. Expected: PASS.
- [ ] **Step 5:** Build `Faceplate`, which adds its screws in code so a panel cannot exist without them. Add gallery `faceplate`.
- [ ] **Step 6:** Capture both modes and save as candidates in `tests/baselines/` (unapproved until Task 3's gate).
- [ ] **Step 7:** Commit: `feat: faceplate and screw primitives with tokens`.

### Task 3: Display with real text glow. **REVIEW GATE 1**

The text glow failed in the spike. Its smear is undiagnosed. This must be solved before the other primitives, because it may change the rendering approach.

**Files:** Create `src/Stx.Art/GlassArt.cs`, `src/Stx.App/Primitives/Display.cs`, `src/Stx.App/Primitives/GlowText.cs`, `tests/Stx.Art.Tests/GlassArtTests.cs`.

**Interfaces:**
- Produces: XAML `Display` with `Tone { Cyan, Amber }` and `Content`; `GlowText` with `Text`, `Tone`

- [ ] **Step 1:** Reproduce the smear in a gallery `glow-probe` using the spike's stacked `DropShadow` with `GetAlphaMask` on a 3-line `TextBlock`. Change one thing at a time and write down the actual cause.
- [ ] **Step 2:** Prototype in the gallery, in order, stopping at the first that passes: (a) fix the cause found; (b) Win2D `GaussianBlurEffect` into a cached render target, redrawn only when the text changes; (c) pre-rendered glyph atlas with baked glow.
- [ ] **Step 3: Write failing test** `Glow_text_redraws_only_on_text_change` against whichever draw counter the chosen approach exposes: setting the same text twice causes one draw; idle for 2 seconds causes none.
- [ ] **Step 4:** Run it. Expected: FAIL.
- [ ] **Step 5:** Implement `Display` (bezel, recessed screen, pixel grid, glass that sits above the text) and `GlowText`.
- [ ] **Step 6:** Run the test. Expected: PASS.
- [ ] **Step 7:** Run `tools/Measure-Budgets.ps1` (port from the spike's `measure.ps1`) on the `display` gallery. Expected: idle CPU 0.00.
- [ ] **Step 8:** Capture `display` in both modes beside mockup v2. **STOP. Show Rocky the screenshots and get approval.** Do not continue until the glow and the faceplate are approved. Save approved images as baselines.
- [ ] **Step 9:** Commit: `feat: glass display with glow text`.

### Task 4: LED, strike and bargraph

**Files:** Create `src/Stx.Look/StrikeTimeline.cs`, `src/Stx.Art/LedArt.cs`, `src/Stx.App/Primitives/Led.cs`, `src/Stx.App/Primitives/Bargraph.cs`, tests.

**Interfaces:**
- Produces: `StrikeTimeline.Default -> IReadOnlyList<(double T, double Brightness)>`; `Led` with `Color`, `State { Off, On, Strike, Busy }`; `Bargraph` with `Value`, `Peak`, `Zones`; `BargraphLogic.LitCount(double value, int segments)`; `BargraphLogic.PeakIndex(double peak, int segments)`

- [ ] **Step 1: Write failing tests:** strike timeline flashes at least 3 times before settling, ends within 1.2 seconds, and only uses brightness; `LitCount(50, 30) == 15`; `LitCount(-5, 30) == 0`; `LitCount(120, 30) == 30`; VU zones are green to 72%, amber to 90%, red above.
- [ ] **Step 2:** Run `dotnet test tests/Stx.Look.Tests`. Expected: FAIL.
- [ ] **Step 3:** Implement. Lit segment sprites overhang their segment so bloom spills onto neighbors, and they come from `ArtCache`, one bitmap per color. Strike runs as a composition animation, off the UI thread. `Bargraph` touches only changed segments.
- [ ] **Step 4:** Run the tests. Expected: PASS.
- [ ] **Step 5:** Add galleries with `--hold` for stable captures. Capture both modes as candidates.
- [ ] **Step 6:** Commit: `feat: LED strike and bargraph primitives`.

### Task 5: Seven-segment, pad, toggle and rotary

**Files:** Create `src/Stx.Look/SevenSegment.cs`, `src/Stx.Look/Detents.cs`, `src/Stx.Look/PressTravel.cs`, `src/Stx.Art/ControlArt.cs`, `src/Stx.App/Primitives/SevenSegmentReadout.cs`, `Pad.cs`, `Toggle.cs`, `Rotary.cs`, tests.

**Interfaces:**
- Produces: `SevenSegment.For(char) -> Segments`; `Detents.Angle(int index, int count) -> double`; `Detents.Next(int index, int count) -> int`; `PressTravel.Pixels == 3`; `Pad : Button` with `Lit`; `Toggle : ToggleButton`; `Rotary : Control` with `Items`, `SelectedIndex`

- [ ] **Step 1: Write failing tests:** `For('8')` lights all 7 segments; `For(' ')` lights none; `Detents.Angle(0,7) == -135`; `Detents.Angle(6,7) == 135`; `Next(6,7) == 0`; `PressTravel.Pixels == 3`.
- [ ] **Step 2:** Run them. Expected: FAIL.
- [ ] **Step 3:** Implement. On `Rotary`, the knurl and indicator rotate and the cap's highlights stay fixed. Rotary supports arrow keys. Pad and Toggle are real `Button` and `ToggleButton` templates, so keyboard and screen-reader support come with them. Respect reduce-motion by skipping overshoot.
- [ ] **Step 4:** Run the tests. Expected: PASS.
- [ ] **Step 5:** Capture both modes as candidates.
- [ ] **Step 6:** Commit: `feat: seven-segment, pad, toggle and rotary primitives`.

### Task 6: List, text entry, dialog and label primitives. **REVIEW GATE 2**

**Files:** Create `src/Stx.App/Primitives/EngravedLabel.cs`, `DisplayList.cs`, `DisplayTextBox.cs`, `EquipmentDialog.cs`, `StateViews.cs`.

**Interfaces:**
- Produces: `DisplayList : ListView` styled inside `Display`; `DisplayTextBox : TextBox`; `EquipmentDialog.ShowAsync(string title, string body, string primary, string? secondary)`; `StateViews` for loading, error, empty, disabled

- [ ] **Step 1: Write failing test:** `tests/Stx.VisualTests/AccessibilityScanTests`, which launches each gallery and fails if any interactive UI Automation element has an empty name or is smaller than 44 x 44 px.
- [ ] **Step 2:** Run it. Expected: FAIL for the new galleries.
- [ ] **Step 3:** Implement as restyled framework controls, so scrolling, text editing and selection remain native.
- [ ] **Step 4:** Run the scan on all galleries in both modes. Expected: PASS.
- [ ] **Step 5:** Run `tools/Measure-Budgets.ps1` on an `all-primitives` gallery. Expected: inside the budgets; report the numbers.
- [ ] **Step 6:** **STOP. Show Rocky every primitive in both modes and get approval.** Screens must not be built until this passes. Save approved images as baselines, then run `dotnet test tests/Stx.VisualTests`. Expected: PASS.
- [ ] **Step 7:** Commit: `feat: list, entry, dialog and state primitives`.

---

## Phase 2: Engine

### Task 7: Job states, errors, verdicts and redaction

**Files:** Create `src/Stx.Core/Jobs/JobState.cs`, `JobTransitions.cs`, `Errors/ErrorKind.cs`, `ErrorClassifier.cs`, `Audio/Verdict.cs`, `Logging/Redactor.cs`, tests.

**Interfaces:**
- Produces: `enum JobState { Queued, Running, Paused, Waiting, Analyzing, Completed, Failed, Canceled, NeedsRecapture }`; `JobTransitions.CanMove(JobState from, JobState to) -> bool`; `enum ErrorKind { UnsupportedSite, SignInNeeded, PrivateOrUnavailable, DrmDetected, LinkExpired, Network, ToolFailure, OutputFailure }`; `ErrorClassifier.Classify(string toolOutput, int exitCode) -> ErrorKind`; `ErrorKind.Message()` and `ErrorKind.NextStep()`; `enum OutputChoice { Original, Flac, Alac, Wav, Aiff, AacM4a, Mp3, Opus, OggVorbis, Mp4, Mkv }`; `Verdict.For(OutputChoice, bool sourceIsLossy) -> string`; `Redactor.Scrub(string) -> string`

- [ ] **Step 1: Write failing tests** with these exact values:
```csharp
[Fact] public void Unsupported_is_classified() =>
  Assert.Equal(ErrorKind.UnsupportedSite, ErrorClassifier.Classify(
    "ERROR: Unsupported URL: https://www.chevrolet.com/performance/corvette/experience", 1));
[Fact] public void Verdict_wording_is_exact() {
  Assert.Equal("Bit-perfect", Verdict.For(OutputChoice.Original, true));
  Assert.Equal("Lossless copy of a lossy source; no quality gained", Verdict.For(OutputChoice.Flac, true));
  Assert.Equal("Larger file, same sound", Verdict.For(OutputChoice.Wav, true));
  Assert.Equal("Re-encoded; slight quality loss", Verdict.For(OutputChoice.Mp3, true));
}
[Fact] public void Redactor_removes_tokens_cookies_and_auth() {
  var s = Redactor.Scrub("GET https://x/master.m3u8?fastly_token=ABC123 Cookie: a=b Authorization: Bearer q");
  Assert.DoesNotContain("ABC123", s); Assert.DoesNotContain("a=b", s); Assert.DoesNotContain("Bearer q", s);
}
[Fact] public void Completed_is_final() => Assert.False(JobTransitions.CanMove(JobState.Completed, JobState.Running));
[Fact] public void Expired_link_goes_to_recapture() => Assert.True(JobTransitions.CanMove(JobState.Running, JobState.NeedsRecapture));
```
Also a fixture-driven test that covers every `ErrorKind`, with real output captured in Task 9. Every `ErrorKind` must return a non-empty `Message()` and `NextStep()`.
- [ ] **Step 2:** Run `dotnet test tests/Stx.Core.Tests`. Expected: FAIL.
- [ ] **Step 3:** Implement. Unknown output maps to `ToolFailure`.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit: `feat: job states, error classification, verdicts, redaction`.

### Task 8: Database and durable queue

**Files:** Create `src/Stx.Data/Migrations.cs`, `Db.cs`, `MediaRepo.cs`, `JobRepo.cs`, `src/Stx.Core/Queue/QueuePolicy.cs`, `QueueEngine.cs`, `IJobRunner.cs`, tests.

**Interfaces:**
- Consumes: `JobState`, `JobTransitions`, `ErrorKind`
- Produces: tables `media_items(id, page_url, source_id, title)`, `media_files(id, media_id, path, hash, format)`, `jobs(id, media_id, kind, state, attempts, error_kind, preset, created_at)`, `playlists`, `playlist_items`, `audio_facts`; `QueueEngine(IJobRepo, IJobRunner, QueuePolicy, IClock)` with `Enqueue`, `Pause`, `Resume`, `Cancel`, `Retry`, `RecoverAfterRestart()`, event `JobChanged`; `QueuePolicy { int MaxParallel = 3; TimeSpan[] Backoff = {5s, 30s, 120s} }`; `QueuePolicy.NextToStart(IReadOnlyList<Job>, int running) -> Job?`; `Dedupe.Decide(sourceId, hash, format, existing)`

- [ ] **Step 1: Write failing tests:**
  - `Running_job_at_restart_becomes_Queued` for a link job.
  - `Running_captured_job_at_restart_becomes_NeedsRecapture`.
  - `Captured_jumps_front_and_may_run_one_over_limit`: 3 running and limit 3; a captured job may start, and a link job may not.
  - `Fails_after_three_retries_with_backoff` using a fake clock.
  - `Pause_stops_process_and_resume_continues`, and `Cancel_is_final_until_retried`.
  - `Parallel_limit_comes_from_settings`.
  - `Same_source_same_format_is_duplicate` and `Same_source_other_format_is_not_duplicate`.
  - `Playlist_private_entry_fails_alone_rest_continue` (Review Focus 5).
  - `Schema_has_no_secret_columns`: no column matching `cookie|header|token|stream_url`.
- [ ] **Step 2:** Run `dotnet test tests/Stx.Core.Tests tests/Stx.Data.Tests`. Expected: FAIL.
- [ ] **Step 3:** Implement with `Microsoft.Data.Sqlite` and hand-written migrations, not EF Core, to keep memory down. Captured job secrets live only in an in-memory dictionary keyed by job id. The database file lives in `%LOCALAPPDATA%\STXDownloader\`.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit: `feat: durable queue with retries and re-capture`.

### Task 9: Process runner and yt-dlp

**Files:** Create `src/Stx.Tools/ProcessRunner.cs`, `YtDlpArgs.cs`, `YtDlpProgress.cs`, `YtDlpRunner.cs`, `CredentialFile.cs`, tests and `tests/fixtures/ytdlp/*`.

**Interfaces:**
- Consumes: `IJobRunner`, `ErrorClassifier`, `Redactor`
- Produces: `IProcessRunner.RunAsync(path, args, onLine, ct) -> exit code`; `YtDlpArgs.ForDownload(DownloadRequest) -> IReadOnlyList<string>`; `YtDlpArgs.ForEnumerate(string url)` for playlists and format lists; `YtDlpProgress.TryParse(string line, out Progress)`; `YtDlpRunner : IJobRunner`; `CredentialFile.Create(cookies, headers) -> IDisposable` with `CookiePath`, `ConfigPath`; `CredentialFile.DeleteLeftovers()`

- [ ] **Step 1: Write failing tests:**
  - `Args_never_contain_cookie_or_header_values`.
  - `Default_keeps_original_with_no_recode`: no `--recode-video`, no `--audio-format`; format `bv*+ba/b`; `-N 8` (the spike used these).
  - `Mkv_when_streams_do_not_fit_mp4`: uses `--merge-output-format mp4/mkv`, or the equivalent verified against yt-dlp's docs.
  - `Credential_file_acl_is_current_user_only` and `Credential_file_deleted_on_dispose` and `Leftovers_deleted_at_startup`.
  - `Progress_parses_fixture_lines`.
  - `Chosen_resolution_audio_only_and_video_only_map_to_formats`.
  - `Playlist_enumeration_returns_entries_and_marks_unavailable_ones`.
  - `Disk_full_output_is_OutputFailure` (Review Focus 2).
- [ ] **Step 2:** Run `dotnet test tests/Stx.Tools.Tests`. Expected: FAIL.
- [ ] **Step 3:** Implement. Stream every output line through `Redactor` before it reaches logs.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5: Manual real check.** Download one real YouTube music video with the runner. This has never been tested end to end. Check yt-dlp's current docs for YouTube requirements, such as a JavaScript runtime. If one is required, add it as a managed tool in Task 10 and report that to Rocky. Save real failure output as fixtures for `ErrorClassifier`. Paste the `ffprobe` output of the result.
- [ ] **Step 6:** Commit: `feat: yt-dlp runner with protected credential handling`.

### Task 10: Tool manager with rollback

**Files:** Create `src/Stx.Tools/ToolManager.cs`, `ToolVersions.cs`, `FfmpegSource.cs`, `docs/THIRD_PARTY.md`, tests.

**Interfaces:**
- Produces: `ToolManager.EnsureAsync(IProgress<ToolStatus>, ct) -> ToolHealth`; `enum ToolHealth { Current, UpdatePending, Degraded, Missing }`; `ToolManager.UpdateYtDlpAsync()`; `ToolManager.RollbackAsync()`; `ToolManager.PreviousYtDlpPath`; `YtDlpRunner` retries one item on `PreviousYtDlpPath` after a post-update site-handling failure, and rolls back if that succeeds

- [ ] **Step 1: Write failing tests** with a fake HTTP handler: `Checksum_mismatch_rejects_and_keeps_current`; `Self_test_failure_rolls_back` where the fake tool can't report a version; `No_network_returns_Missing_without_throwing` (Review Focus 3); `Updates_off_makes_no_requests`; `Post_update_site_failure_retries_on_previous_then_rolls_back`; `Network_failure_after_update_does_not_roll_back`; `Update_never_contacts_media_sites`: the handler sees only the configured update hosts.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement. yt-dlp comes from `github.com/yt-dlp/yt-dlp/releases/latest`, verified against `SHA2-256SUMS`, as done in the spike. Tools live in `%LOCALAPPDATA%\STXDownloader\tools\`, with the prior yt-dlp kept alongside.
- [ ] **Step 4:** Check FFmpeg licensing and pick the build source. Record the source and license in `docs/THIRD_PARTY.md` before finishing.
- [ ] **Step 5:** Run the tests. Expected: PASS.
- [ ] **Step 6:** Commit: `feat: tool manager with checksum, self-test and rollback`.

### Task 11: Output handling and conversion

**Files:** Create `src/Stx.Core/Presets/PresetCatalog.cs`, `src/Stx.Tools/FfmpegRunner.cs`, `EncoderProbe.cs`, tests.

**Interfaces:**
- Consumes: `OutputChoice`, `Verdict`
- Produces: `FfmpegRunner.ConvertAsync(ConvertRequest, onProgress, ct)`; `PresetCatalog.All`; `Preset { Name, OutputChoice, Reencodes }`; `EncoderProbe.BestH264Async() -> string`

- [ ] **Step 1: Write failing tests:** `Keep_original_preset_never_reencodes`; `Car_usb_and_phone_reencode_to_h264_aac`; `Conversion_keeps_original_file`; `Levelling_off_by_default`; `Missing_output_folder_is_OutputFailure` (Review Focus 2); `Encoder_probe_falls_back_to_software`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement the four named presets from the spec: "Keep original", "Car USB", "Phone", "Archive". Hardware encoders count only after a real test encode succeeds. Tag, cover art and chapter options map to yt-dlp and FFmpeg flags, each with its own switch.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit: `feat: presets, conversion and encoder probe`.

### Task 12: Filenames

**Files:** Create `src/Stx.Core/Files/Filename.cs`, `FilenameTemplate.cs`, `tests/Stx.Core.Tests/FilenameTests.cs`.

**Interfaces:**
- Produces: `Filename.Safe(string title, string ext, string folder, Func<string,bool> exists) -> string`; `FilenameTemplate.Render(string template, MediaInfo) -> string`

- [ ] **Step 1: Write failing tests** (Review Focus 1): `CON` and `NUL` are made safe; `a<b>:c?.mp4` has no illegal characters; a 400-character title fits a 240-character full path; emoji titles survive; an existing target gets ` (2)` and never overwrites; an empty title falls back to the source ID; a template with an unknown token renders it literally without throwing.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit: `feat: safe filenames and templates`.

---

## Phase 3: Source Truth

### Task 13: Measured audio facts

**Files:** Create `src/Stx.Audio/AudioFacts.cs`, `ProbeParser.cs`, `Fft.cs`, `SpectrumAnalyzer.cs`, `LoudnessParser.cs`, tests and fixtures generated by FFmpeg.

**Interfaces:**
- Produces: `AudioFacts { Codec, BitrateKbps, SampleRate, Channels, RollOffHz, LoudnessLufs, TruePeakDb, Spectrum, Envelope, MeasuredFromFile }`; `SpectrumAnalyzer.Analyze(Stream pcm, int sampleRate) -> SpectrumResult`; `ReportedFacts.From(YtDlpFormat)` with `MeasuredFromFile == false`

- [ ] **Step 1: Write failing tests:** Using FFmpeg-generated test tones: `Rolloff_of_lowpassed_noise_at_16k_is_within_500Hz`; `Sine_at_1k_peaks_at_1k_within_one_bin`; `Silence_returns_no_rolloff_without_throwing`; `Probe_parses_chevrolet_fixture`: aac, 48000 Hz; `Video_with_no_audio_returns_null_facts` and the panel shows "no audio stream"; `Reported_facts_are_labeled_reported`.
- [ ] **Step 2:** Run `dotnet test tests/Stx.Audio.Tests`. Expected: FAIL.
- [ ] **Step 3:** Implement. Decode with FFmpeg to PCM over stdout. Keep a small own FFT, with no dependency. Use the FFmpeg `ebur128` filter for loudness. Analysis runs at below-normal priority.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit: `feat: measure audio facts, spectrum and loudness from the file`.

### Task 14: Meter behavior from real data

**Files:** Create `src/Stx.Audio/MeterTimeline.cs`, test.

**Interfaces:**
- Produces: `MeterTimeline.Sweep(AudioFacts) -> IReadOnlyList<MeterFrame>`; `MeterTimeline.Hold(AudioFacts) -> MeterFrame`; `MeterTimeline.Dark`

- [ ] **Step 1: Write failing tests:** `Sweep_is_derived_only_from_envelope`: the same facts give identical frames; `Sweep_lasts_4_seconds_at_20fps`; `Hold_equals_measured_peaks`; `Nothing_selected_is_dark`; `Reduce_motion_skips_sweep`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement. No random values anywhere in this file.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit: `feat: meter timelines driven by measured data`.

---

## Phase 4: Screens

Screens use only approved primitives. View models live in `src/Stx.App/ViewModels/` and hold no WinUI types, so `tests/Stx.App.Tests` can test them. Each task ends with a gallery capture and a baseline, and a check that the view model is actually reached from `MainWindow`.

### Task 15: Console, source panel and queue panel

**Files:** Create `src/Stx.App/MainWindow.xaml(.cs)`, `Panels/SourcePanel`, `Panels/QueuePanel`, `ViewModels/SourceViewModel.cs`, `QueueViewModel.cs`, `src/Stx.App/Composition.cs`, tests.

**Interfaces:**
- Consumes: `QueueEngine`, `YtDlpRunner`, `ToolManager`, `Led`, `Bargraph`, `Display`
- Produces: `SourceViewModel.AnalyzeCommand`; `QueueItemViewModel { Title, Status, Percent, LedState, ErrorMessage, NextStep, RawLog }`

- [ ] **Step 1: Write failing tests:** `Unsupported_link_suggests_browser_capture`; `Sign_in_needed_suggests_opening_in_chrome`; `Starting_job_sets_led_to_Strike_then_Busy`; `Finished_job_sets_led_On`; `Needs_recapture_shows_reopen_action`; `Raw_log_is_redacted`; `Playlist_link_asks_before_enqueuing_all`; `Engine_led_reflects_tool_health`; `Link_expired_moves_captured_job_to_recapture`.
- [ ] **Step 2:** Run `dotnet test tests/Stx.App.Tests`. Expected: FAIL.
- [ ] **Step 3:** Implement the panels per mockup v2. Replace the mockup's sample data with real bindings.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5: Manual check.** Paste a YouTube link in the running app, watch it finish, and paste the `ffprobe` output of the file.
- [ ] **Step 6:** Commit: `feat: console window with source and queue panels`.

### Task 16: Source Truth panel and library

**Files:** Create `Panels/SourceTruthPanel`, `Panels/LibraryPanel`, `ViewModels/SourceTruthViewModel.cs`, `LibraryViewModel.cs`, tests.

**Interfaces:**
- Consumes: `AudioFacts`, `MeterTimeline`, `Verdict`, `PresetCatalog`, `Rotary`, `Bargraph`, `SpectrumDisplay`
- Produces: `SourceTruthViewModel.Select(MediaItem)`; `LibraryViewModel.ConvertCommand`, `OpenCommand`, `RevealCommand`

- [ ] **Step 1: Write failing tests:** `Pointless_conversion_warns_but_is_allowed`; `Verdict_updates_with_rotary`; `Pre_download_values_labeled_reported`; `Post_download_values_labeled_measured`; `Convert_from_library_creates_job_and_keeps_original`; `Missing_file_shows_missing_state`; `Empty_library_shows_empty_state`; `Finished_analysis_triggers_sweep_then_hold`; `No_selection_is_dark`; `No_audio_stream_says_so`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement, adding `SpectrumDisplay` as a primitive that redraws only when its data changes, and that draws the measured roll-off marker.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit: `feat: Source Truth panel and library`.

### Task 17: Settings, first run, tray and clipboard

**Files:** Create `Panels/SettingsPanel`, `Panels/FirstRunPanel`, `src/Stx.App/Tray.cs`, `ClipboardWatcher.cs`, `SingleInstance.cs`, `src/Stx.Core/Settings/AppSettings.cs`, `LinkDetector.cs`, tests.

**Interfaces:**
- Produces: `AppSettings { LookMode, MaxParallel, ClipboardDetect=false, TrayMode=false, StartWithWindows=false, ToolUpdates=true, ReduceMotion, OutputFolder, FilenameTemplate }`; `LinkDetector.IsMediaLink(string) -> bool`; `App.SwitchMode(LookMode)`

- [ ] **Step 1: Write failing tests:** defaults exactly as above, with `LookMode.Dark`; `Mode_persists_across_restart`; `Mode_ignores_windows_theme`; `Clipboard_off_makes_no_offer`; `LinkDetector_ignores_plain_text`; `Hidden_state_stops_all_timers`; `First_run_offline_shows_retry` (Review Focus 3); `Corrupt_settings_file_falls_back_to_defaults`.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement. Start-with-Windows uses the `HKCU\...\Run` key. When hidden, stop timers and tear down window content if measurement shows it lowers memory. Report the numbers either way. A second launch signals the first instance and exits.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5:** Commit: `feat: settings, first run, tray and clipboard detection`.

---

## Phase 5: Browser capture

### Task 18: Extension detection

Check current Chrome Manifest V3 docs before writing this. The earlier detection test ran in Playwright, not a real extension.

**Files:** Create `extension/manifest.json`, `extension/detect.js`, `extension/worker.js`, `extension/popup.html`, `extension/popup.js`, `extension/package.json`, `extension/test/detect.test.js`.

**Interfaces:**
- Produces: `classify(url, contentType) -> 'hls'|'dash'|'file'|null`; `groupStreams(items) -> items`, which collapses renditions and segments under a master; `findEmbeds(iframeSrcs) -> [{kind:'youtube', id}]`; `labelFor(item) -> string`

- [ ] **Step 1: Write failing tests** in `extension/test/detect.test.js`, using the shapes seen in testing:
```js
test('Brightcove master is HLS', () =>
  assert.equal(classify('https://manifest.prod.boltdns.net/manifest/v1/hls/v4/clear/1/2/10s/master.m3u8?fastly_token=x', 'application/x-mpegURL'), 'hls'));
test('segments and tracker URLs are not listed', () =>
  assert.equal(classify('https://metrics.brightcove.com/v2/tracker?media_url=x.m3u8', ''), null));
test('renditions collapse under master', () =>
  assert.equal(groupStreams(fixtureChevrolet).filter(i => i.kind === 'hls').length, 2));
test('YouTube embed yields id', () =>
  assert.deepEqual(findEmbeds(['https://www.youtube.com/embed/QBwwj5p25AM?autoplay=0']), [{ kind: 'youtube', id: 'QBwwj5p25AM' }]));
```
Also: `DRM item offers no download` when the page reports `mediaKeys`.
- [ ] **Step 2:** Run `node --test extension/test/`. Expected: FAIL.
- [ ] **Step 3:** Implement. Permissions: `webRequest`, `tabs`, `cookies`, `nativeMessaging`, `<all_urls>`. Keep state in `chrome.storage.session`, because the worker can stop. Fix the extension ID with a `key` in the manifest.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5: Manual real check.** Load the extension unpacked in Playwright's Chromium, open the Chevrolet Corvette Experience page, and confirm the popup lists labeled streams. Paste the list. If the worker misses streams after going idle, stop and report.
- [ ] **Step 6:** Commit: `feat: extension detects and labels page media`.

### Task 19: Native host and hand-off

**Files:** Create `src/Stx.Bridge/CaptureMessage.cs`, `PipeServer.cs`, `PipeClient.cs`, `NativeMessaging.cs`, `src/Stx.NativeHost/Program.cs`, `tools/Register-NativeHost.ps1`, tests. Modify `extension/worker.js`, `src/Stx.App/Composition.cs`.

**Interfaces:**
- Consumes: `QueueEngine.Enqueue`, `CredentialFile`
- Produces: `CaptureMessage { Kind, PageUrl, Title, StreamUrl, DurationSec, Width, Height, Headers, Cookies, IsDrm }`; `NativeMessaging.Read(Stream) -> string`; `NativeMessaging.Write(Stream, string)`; pipe name `stx-downloader-<user SID>`

- [ ] **Step 1: Write failing tests:** `Framing_round_trips_4_byte_length`; `Oversize_message_rejected`; `Malformed_json_is_rejected_without_crash`; `Drm_message_creates_no_job_and_reports_DrmDetected`; `Capture_enqueues_at_front`; `Secrets_kept_in_memory_only`: the database has no trace after the job; `Recapture_from_same_page_resumes_job`; `Batch_cookies_cover_every_item_then_are_discarded`; `App_not_running_is_launched_by_host`; `Pipe_rejects_other_users` via ACL.
- [ ] **Step 2:** Run. Expected: FAIL.
- [ ] **Step 3:** Implement. Registration writes `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.stx.downloader` and the Edge equivalent, with `allowed_origins` limited to the fixed extension ID. No network port is opened. Cookies are sent only on a click.
- [ ] **Step 4:** Run. Expected: PASS.
- [ ] **Step 5: Manual end-to-end check.** A real click in the extension on the Chevrolet page downloads a real file through the app. Paste the `ffprobe` output. This is a spec success measure.
- [ ] **Step 6:** Commit: `feat: native messaging hand-off to the queue`.

---

## Phase 6: Finish

### Task 20: Budgets, pointer light and reduce motion

**Files:** Modify `tools/Measure-Budgets.ps1`, `src/Stx.App/Primitives/Faceplate.cs`, `GlowText.cs`; create `docs/PERFORMANCE.md`.

- [ ] **Step 1:** Add `Measure-Budgets.ps1 -Assert`, which exits non-zero outside the Global Constraints budgets. It must measure the app only, excluding yt-dlp and FFmpeg.
- [ ] **Step 2:** Run it on the real app: idle, hidden, and with a job running. Paste the raw table in `docs/PERFORMANCE.md` with the machine specs.
- [ ] **Step 3:** Prototype the pointer-following light with a composition sprite. Keep it only if the budgets still pass and Rocky agrees it looks better. Otherwise cut it and say so.
- [ ] **Step 4:** Verify reduce-motion by turning it on in Windows, then check that strike, sweep and overshoot stop. Run the accessibility scan on the real screens in both modes. Expected: PASS.
- [ ] **Step 5:** Commit: `perf: enforce budgets and verify reduced motion`.

### Task 21: Release

**Files:** Create `tools/Publish.ps1`, `.github/workflows/build.yml`, `docs/REAL-SITE-CHECKS.md`. Modify `README.md`.

- [ ] **Step 1:** Trial trimming on the self-contained publish. Report the before size against the spike's 157 MB, and run all tests against the trimmed build. Revert if trimming breaks the app.
- [ ] **Step 2:** Write `tools/Publish.ps1`, which produces the zip and the unpacked extension. Run it on a clean user profile or VM if available. Otherwise say this wasn't done.
- [ ] **Step 3:** Run the checklist in `docs/REAL-SITE-CHECKS.md` by hand and record results: YouTube music video with audio untouched, YouTube playlist with a private entry, Chevrolet capture, Ferrari capture, and a registered webinar. The webinar is untested so far: if Rocky has no link, record it as not tested.
- [ ] **Step 4:** Write the README. Every feature line carries a `path:line` anchor checked by hand, and includes installation with the SmartScreen note, the DRM-out-of-scope line and the user-responsibility line. Run `/verify`, then expect no unanchored claims.
- [ ] **Step 5:** Commit: `docs: release packaging and README`.
- [ ] **Step 6:** **STOP.** Rocky's final look check against the success measure, and his decision on whether and when to push to GitHub. Do not push without it. Worth a legal review before promoting it widely.
