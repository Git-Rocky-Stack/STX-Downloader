# Player, Phases 2 to 7: plan as built

**Spec:** `docs/superpowers/specs/2026-11-04-vlc-player-design.md`. **Before it:** `docs/superpowers/plans/2026-11-04-player-phase1.md` and its report. **After it:** `docs/superpowers/reports/2026-11-05-player-phases-2-7-report.md`.

Rocky: "continue with phases 2-7". This plan was written as the work went, in the spec's order, one commit (or more) per phase. It records the tasks and the calls made along the way; the report has the evidence.

## Where it was built, and what that rules out

A Linux cloud container: no Windows desktop, GPU, sound device or disc drive. So:

- **Runs here:** `Stx.Core`, `Stx.Data`, `Stx.Look`, `Stx.Art` and `Stx.Audio` tests, and most of `Stx.App.Tests`, `Stx.Tools.Tests` and `Stx.Bridge.Tests` (the parts that don't start `cmd.exe` or open Windows pipes). FFmpeg is installed here, so the real-FFmpeg tests run too.
- **Type-checked here:** the WinUI app's C#, through a scratch project that compiles it against the Windows App SDK without the XAML compiler (which is a Windows-only program).
- **Runs on CI's Windows runner:** everything above plus `Stx.Playback.Tests` with the real VLC engine, headless. CI installs FFmpeg from this phase on.
- **Only on the reference PC:** the Device, Corpus and screenshot tests, real discs, real Default Apps, real exclusive audio, and real GPU upscaling. These are written and listed in the report for Rocky to run.

## Decisions asked of Rocky during the work

| # | Question | Answer |
|---|---|---|
| D4 | Phase 4 DVDs: re-add VLC's dvdnav/dvdread (libdvdcss inside, as spec A5 allows) or play VOBs only | Re-add them, with STX's own encryption check in front |
| D5 | Spikes S5/S6 need hardware this container lacks | Build, test what CI can, and mark the rest unproven |
| D6 | S5 failed (see below): how should BIT-PERFECT work | FFmpeg decodes lossless music into STX's own exclusive renderer; films use VLC's passthrough |
| D7 | Mid-way: CI was failing and its actions used deprecated Node 20; "we shouldn't be using deprecated dependencies" | Actions moved to their Node 24 releases; xUnit 2 (Legacy on NuGet) moved to xunit.v3 |

## Phase 2: the Library as the whole collection

1. Schema step 3: `media_items.origin` (downloaded, opened, watched; old rows with no link become opened), `media_files.size` and `modified`, indexes on path and origin.
2. `Stx.Core.Library`: `MediaTypes` (the types STX plays), `FolderScan` (find and plan: add, note changes, pair moved files by size and date), `CollectionWatcher` (first scan on its own below-normal thread, batches of 500; FileSystemWatcher events gathered and applied after a quiet moment; folder renames carry their files; lost events rescan).
3. `LibraryViewModel`: origin in each row's status; search by title, file name and folder, every word matching; refresh builds the list off the UI thread and edits only the rows that changed, or resets once for a big change; the panel's ListView virtualizes.
4. Settings: watched folders (Videos and Music until changed), ADD FOLDER and REMOVE FOLDER. Removing takes rows out, never files.
5. Watched and opened files are hashed the first time they're chosen.

## Phase 3: default player

1. Ten more corpus files, so every type in `MediaTypes` is proven by a corpus file of that type; a test holds the two together.
2. `FileAssociations`: one plan of per-user registry values (two ProgIDs, Capabilities, RegisteredApplications, Open with), the installer's `associations.iss` generated from it and held equal by a test, the real state read from each type's user choice, and the Default Apps link (Windows 11 deep link, Windows 10 general page, per A6).
3. First run asks once (YES or NOT NOW). Settings: DEFAULT PLAYER pad and LED. STX never writes a user choice.

## Phase 4: discs and network streams

1. VLC's DVD plugins ship again (D4). `DiscCheck` reads CSS scrambling bits, AACS folders and copy-permission bits, failing closed; `PlaybackTarget` routes every open (file, `dvd:///`, `bluray:///`, stream) inside the engine, so nothing reaches VLC unchecked.
2. `StreamLink` and `StreamOpener`: streams, radio playlists, web pages to ANALYZE, UDP/RTP refused (no listening port), RTSP over TCP.
3. Source plate: PLAY STREAM and PLAY DISC.

## Phase 5: BIT-PERFECT (spike S5 first)

S5, read from VLC 3.0.24's source: `wasapi.c:546-548` opens PCM shared only; `amem.c:191-193` takes 16-bit only. The spec's planned route fails for 24-bit; the work stopped and Rocky chose D6.

1. `BitPerfect`: routes, device layouts that never cut a bit, FFmpeg's arguments (no resample, remix, filter or dither), the readout.
2. `ExclusiveRenderer` (NAudio's WASAPI layer, MIT) with format negotiation and buffer alignment; `MusicEngine`; `RoutingEngine`, which falls back to shared with the reason.
3. Films: `--mmdevice-passthrough=2`, read when the engine starts.
4. Settings: BIT-PERFECT, with its cost said beside it. VOLUME fixed while bit-perfect.

## Phase 6: ENHANCE (spike S6 first)

S6, read from VLC 3.0.24's source: `--d3d11-upscale-mode=super`; NVIDIA needs a 530+ driver, Intel is assumed, anything else silently falls back. The video output is made under the player, so per-file options don't reach it.

1. `Enhance`: availability (RTX with 530+, Intel Arc or Xe), the PERFORMANCE ceiling, engine options, the readout.
2. `HardwareProbe` reads the GPU through DXGI.
3. The engine restarts LibVLC in place on a change and resumes the file; the readout follows what VLC's log confirms.

## Phase 7: Shrink

1. Presets: Shrink AV1, Shrink HEVC (both with Opus, subtitles, chapters and attachments, 10-bit), Shrink Opus.
2. `CopyCheck`: decodes cleanly, has the streams, lasts as long. `FfmpegRunner` keeps a copy only after it passes, and checks the encoder exists first.
3. SHRINK, the Shrink to knob, SHRINK FOLDER; each copy's saving, and the total deleting the originals would free.
